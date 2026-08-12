using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace UNMA.Domain;

public static class ConfigurationTransferPolicy
{
    private static readonly string[] s_recommendedSilentNotificationIds =
    {
        "UpgradeInProgress",
        "DowngradeInProgress",
        "VehicleGoalStruggling",
        "VehicleNoReachableDesignations",
        "NoTreesToHarvest",
        "ExcavatorHasNoValidTruck",
    };

    private static readonly string[] s_recommendedIgnoredNotificationIds =
    {
        "TruckCannotDeliver",
        "TruckCannotDeliverMixedCargo",
    };

    internal static bool ShouldInitializeRecommendedProfile(
        UnmaTransferProfile loadedProfile,
        string loadError,
        bool isWriteBlocked)
    {
        return loadedProfile == null &&
               string.IsNullOrWhiteSpace(loadError) &&
               !isWriteBlocked;
    }

    public static UnmaTransferProfile CreateRecommendedQuietProfile(
        string sourceVersion = "")
    {
        var rules = s_recommendedSilentNotificationIds
            .Select(notificationId => CreateRecommendedNotificationRule(
                notificationId,
                VanillaNotificationBehavior.Silent))
            .Concat(s_recommendedIgnoredNotificationIds.Select(
                notificationId => CreateRecommendedNotificationRule(
                    notificationId,
                    VanillaNotificationBehavior.Ignored)))
            .ToList();
        var profile = new UnmaTransferProfile
        {
            Metadata = new TransferProfileMetadata
            {
                Name = "UNMA Recommended Quiet",
                CreatedUtc = DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                SourceVersion = sourceVersion?.Trim() ?? "",
            },
            Selection = new TransferProfileSelection
            {
                NotificationBehaviors = true,
                SoundSettings = false,
                Appearance = false,
                SystemAlarms = false,
                WindowLayout = false,
                NotificationRuleIdentities = rules
                    .Select(RuleIdentity)
                    .ToList(),
            },
            NotificationRules = rules,
            SoundSettings = new List<TransferSoundSetting>(),
            Appearance = null,
            SystemAlarms = new List<SystemAlarmDefinition>(),
            WindowLayout = null,
        };
        profile.Normalize();
        return profile;
    }

    internal static bool TryRefreshPreviousRecommendedProfile(
        UnmaTransferProfile storedProfile,
        string sourceVersion,
        out UnmaTransferProfile upgradedProfile)
    {
        upgradedProfile = storedProfile;
        if (!IsPreviousRecommendedProfile(storedProfile))
        {
            return false;
        }

        upgradedProfile = CreateRecommendedQuietProfile(sourceVersion);
        upgradedProfile.Metadata.CreatedUtc =
            storedProfile.Metadata.CreatedUtc;
        return true;
    }

    private static bool IsPreviousRecommendedProfile(
        UnmaTransferProfile profile)
    {
        if (profile == null ||
            profile.ProfileSchemaVersion !=
                UnmaTransferProfile.CurrentProfileSchemaVersion ||
            profile.Metadata == null ||
            !string.Equals(
                profile.Metadata.SourceVersion,
                "0.10.2",
                StringComparison.Ordinal) ||
            profile.Metadata.SkippedItems != 0 ||
            (profile.Metadata.Diagnostics?.Count ?? 0) != 0 ||
            profile.Selection == null ||
            !profile.Selection.NotificationBehaviors ||
            profile.Selection.SoundSettings ||
            profile.Selection.Appearance ||
            profile.Selection.SystemAlarms ||
            profile.Selection.WindowLayout ||
            (profile.SoundSettings?.Count ?? 0) != 0 ||
            profile.Appearance != null ||
            (profile.SystemAlarms?.Count ?? 0) != 0 ||
            profile.WindowLayout != null)
        {
            return false;
        }

        var isLegacySilentName = string.Equals(
            profile.Metadata.Name,
            "UNMA Recommended Silent",
            StringComparison.Ordinal);
        var isPreviousQuietName = string.Equals(
            profile.Metadata.Name,
            "UNMA Recommended Quiet",
            StringComparison.Ordinal);
        if (!isLegacySilentName && !isPreviousQuietName)
        {
            return false;
        }
        var previousNoisyBehavior = isPreviousQuietName
            ? VanillaNotificationBehavior.Hidden
            : (VanillaNotificationBehavior?)null;
        var rules = profile.NotificationRules ??
            new List<TransferNotificationRule>();
        var expectedBehaviors = new Dictionary<
            string,
            VanillaNotificationBehavior>(StringComparer.Ordinal);
        foreach (var notificationId in s_recommendedSilentNotificationIds)
        {
            expectedBehaviors["vanilla:" + notificationId] =
                VanillaNotificationBehavior.Silent;
        }
        if (previousNoisyBehavior.HasValue)
        {
            foreach (var notificationId in
                     s_recommendedIgnoredNotificationIds)
            {
                expectedBehaviors["vanilla:" + notificationId] =
                    previousNoisyBehavior.Value;
            }
        }
        if (rules.Count != expectedBehaviors.Count ||
            rules.Any(rule =>
                rule == null ||
                rule.Scope != VanillaNotificationScope.NotificationType ||
                !expectedBehaviors.TryGetValue(
                    rule.AlarmId,
                    out var expectedBehavior) ||
                rule.Behavior != expectedBehavior ||
                !string.IsNullOrWhiteSpace(rule.EntityPrototypeId)))
        {
            return false;
        }

        var identities = profile.Selection.NotificationRuleIdentities;
        return expectedBehaviors.Keys.ToHashSet(StringComparer.Ordinal)
                   .SetEquals(rules.Select(rule => rule.AlarmId)) &&
               identities != null &&
               identities.Count == rules.Count &&
               new HashSet<string>(identities, StringComparer.Ordinal)
                   .SetEquals(rules.Select(RuleIdentity));
    }

    private static TransferNotificationRule CreateRecommendedNotificationRule(
        string notificationId,
        VanillaNotificationBehavior behavior)
    {
        return new TransferNotificationRule
        {
            AlarmId = "vanilla:" + notificationId,
            Scope = VanillaNotificationScope.NotificationType,
            Behavior = behavior,
            EntityPrototypeId = "",
        };
    }

    public static UnmaTransferProfile CreateProfile(
        UnmaConfiguration source,
        TransferProfileSelection selection,
        string profileName = "Default",
        string sourceVersion = "")
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }
        selection ??= new TransferProfileSelection();

        var profile = new UnmaTransferProfile
        {
            Metadata = new TransferProfileMetadata
            {
                Name = string.IsNullOrWhiteSpace(profileName)
                    ? "Default"
                    : profileName.Trim(),
                CreatedUtc = DateTime.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                SourceVersion = sourceVersion?.Trim() ?? "",
            },
            Selection = CloneSelection(selection),
        };

        if (selection.NotificationBehaviors)
        {
            ExportNotificationRules(source, selection, profile);
        }
        else
        {
            profile.Selection.NotificationRuleIdentities =
                new List<string>();
        }

        if (selection.SoundSettings)
        {
            ExportSoundSettings(source, profile);
        }
        if (selection.Appearance)
        {
            profile.Appearance = new TransferAppearanceSettings
            {
                WarningColor = source.WarningColor,
                CriticalColor = source.CriticalColor,
                EmergencyColor = source.EmergencyColor,
                UiScalePercent = source.UiScalePercent,
                ReducedMotion = source.ReducedMotion,
            };
        }
        if (selection.SystemAlarms)
        {
            ExportSystemAlarms(source, profile);
        }
        if (selection.WindowLayout)
        {
            profile.WindowLayout = new TransferWindowLayout
            {
                WindowX = source.WindowX,
                WindowY = source.WindowY,
                WindowWidth = source.WindowWidth,
                WindowHeight = source.WindowHeight,
                LauncherX = source.LauncherX,
                LauncherY = source.LauncherY,
                EditorWindowX = source.EditorWindowX,
                EditorWindowY = source.EditorWindowY,
                EditorWindowWidth = source.EditorWindowWidth,
                EditorWindowHeight = source.EditorWindowHeight,
                DetachedPanels = (source.DetachedPanelLayouts ??
                        new List<DetachedPanelWindowLayout>())
                    .Where(layout => layout != null)
                    .Select(layout =>
                        new TransferDetachedPanelWindowLayout
                        {
                            PanelId = layout.PanelId,
                            X = layout.X,
                            Y = layout.Y,
                            Width = layout.Width,
                            Height = layout.Height,
                            IsOpen = layout.IsOpen,
                        })
                    .ToList(),
            };
        }

        profile.Normalize();
        return profile;
    }

    public static TransferImportPreview PreviewImport(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var preview = new TransferImportPreview();
        var safeProfile = PrepareProfile(profile, preview);
        if (safeProfile == null)
        {
            return preview;
        }

        AddExportDiagnostics(safeProfile, preview);
        var selection = safeProfile.Selection;
        if (selection.NotificationBehaviors)
        {
            PreviewNotificationRules(target, safeProfile, preview);
        }
        if (selection.SoundSettings)
        {
            PreviewSoundSettings(target, safeProfile, preview);
        }
        if (selection.Appearance)
        {
            PreviewAppearance(target, safeProfile, preview);
        }
        if (selection.SystemAlarms)
        {
            PreviewSystemAlarms(target, safeProfile, preview);
        }
        if (selection.WindowLayout)
        {
            PreviewWindowLayout(target, safeProfile, preview);
        }
        return preview;
    }

    public static TransferImportResult Merge(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var preview = PreviewImport(target, profile);
        var result = CloneDataContract(target);
        var safeProfile = PrepareProfile(profile, null);
        if (safeProfile == null)
        {
            return new TransferImportResult
            {
                Configuration = result,
                Preview = preview,
            };
        }

        var selection = safeProfile.Selection;
        if (selection.NotificationBehaviors)
        {
            MergeNotificationRules(result, safeProfile);
        }
        if (selection.SoundSettings)
        {
            MergeSoundSettings(result, safeProfile);
        }
        if (selection.Appearance)
        {
            MergeAppearance(result, safeProfile);
        }
        if (selection.SystemAlarms)
        {
            MergeSystemAlarms(result, safeProfile);
        }
        if (selection.WindowLayout)
        {
            MergeWindowLayout(result, safeProfile);
        }

        return new TransferImportResult
        {
            Configuration = result,
            Preview = preview,
        };
    }

    public static UnmaTransferProfile CloneProfile(
        UnmaTransferProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        return CloneDataContract(profile);
    }

    private static void ExportNotificationRules(
        UnmaConfiguration source,
        TransferProfileSelection selection,
        UnmaTransferProfile profile)
    {
        var requested = selection.NotificationRuleIdentities == null
            ? null
            : new HashSet<string>(
                selection.NotificationRuleIdentities
                    .Where(identity => !string.IsNullOrWhiteSpace(identity))
                    .Select(identity => identity.Trim()),
                StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sourceRule in source.VanillaNotificationRules ??
                 Enumerable.Empty<VanillaNotificationRule>())
        {
            var identity = VanillaNotificationSuppressionPolicy.RuleIdentity(
                sourceRule);
            if (requested != null && !requested.Contains(identity))
            {
                continue;
            }
            if (identity.Length > 0)
            {
                matched.Add(identity);
            }
            if (!TryValidateNotificationRule(
                    sourceRule,
                    out var diagnostic))
            {
                AddExportSkip(profile, diagnostic);
                continue;
            }

            var clone = CloneNotificationRule(sourceRule);
            identity = RuleIdentity(clone);
            if (indices.TryGetValue(identity, out var existingIndex))
            {
                profile.NotificationRules[existingIndex] = clone;
            }
            else
            {
                indices.Add(identity, profile.NotificationRules.Count);
                profile.NotificationRules.Add(clone);
            }
        }

        if (requested != null)
        {
            foreach (var identity in requested)
            {
                if (!matched.Contains(identity))
                {
                    AddExportSkip(
                        profile,
                        "A selected notification rule was not found and " +
                        "was skipped.");
                }
            }
        }

        profile.Selection.NotificationRuleIdentities =
            profile.NotificationRules
                .Select(RuleIdentity)
                .ToList();
    }

    private static void ExportSoundSettings(
        UnmaConfiguration source,
        UnmaTransferProfile profile)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in source.SoundOverrides ??
                 Enumerable.Empty<AlarmSoundOverride>())
        {
            var alarmId = item?.AlarmId?.Trim() ?? "";
            if (!IsTransferableSoundAlarmId(alarmId))
            {
                AddExportSkip(
                    profile,
                    "Sound setting '" + alarmId +
                    "' was skipped because its alarm id is not stable " +
                    "across savegames.");
                continue;
            }
            var setting = new TransferSoundSetting
            {
                AlarmId = alarmId,
                SoundId = NormalizeSoundId(item.SoundId),
                AutoAcknowledgeOnClear = item.AutoAcknowledgeOnClear,
            };
            if (indices.TryGetValue(alarmId, out var existingIndex))
            {
                profile.SoundSettings[existingIndex] = setting;
            }
            else
            {
                indices.Add(alarmId, profile.SoundSettings.Count);
                profile.SoundSettings.Add(setting);
            }
        }
    }

    private static void ExportSystemAlarms(
        UnmaConfiguration source,
        UnmaTransferProfile profile)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var alarm in source.SystemAlarms ??
                 Enumerable.Empty<SystemAlarmDefinition>())
        {
            var alarmId = alarm?.Id?.Trim() ?? "";
            if (!TryValidateSystemAlarm(alarm, out var diagnostic))
            {
                AddExportSkip(
                    profile,
                    diagnostic);
                continue;
            }
            var clone = CloneDataContract(alarm);
            clone.Id = alarmId;
            if (indices.TryGetValue(alarmId, out var existingIndex))
            {
                profile.SystemAlarms[existingIndex] = clone;
            }
            else
            {
                indices.Add(alarmId, profile.SystemAlarms.Count);
                profile.SystemAlarms.Add(clone);
            }
        }
    }

    private static void PreviewNotificationRules(
        UnmaConfiguration target,
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var targetByIdentity = (target.VanillaNotificationRules ??
                new List<VanillaNotificationRule>())
            .Where(rule => rule != null)
            .GroupBy(
                VanillaNotificationSuppressionPolicy.RuleIdentity,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(),
                StringComparer.Ordinal);
        foreach (var rule in GetEffectiveNotificationRules(profile, preview))
        {
            var identity = RuleIdentity(rule);
            if (!targetByIdentity.TryGetValue(identity, out var current))
            {
                preview.Add(
                    TransferProfileCategory.NotificationBehaviors,
                    identity,
                    TransferImportChangeKind.Added);
            }
            else
            {
                preview.Add(
                    TransferProfileCategory.NotificationBehaviors,
                    identity,
                    current.Behavior == rule.Behavior
                        ? TransferImportChangeKind.Unchanged
                        : TransferImportChangeKind.Changed);
            }
        }
    }

    private static void PreviewSoundSettings(
        UnmaConfiguration target,
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var targetById = (target.SoundOverrides ??
                new List<AlarmSoundOverride>())
            .Where(item => item != null &&
                           !string.IsNullOrWhiteSpace(item.AlarmId))
            .GroupBy(item => item.AlarmId.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(),
                StringComparer.Ordinal);
        foreach (var setting in GetEffectiveSoundSettings(profile, preview))
        {
            if (!targetById.TryGetValue(setting.AlarmId, out var current))
            {
                preview.Add(
                    TransferProfileCategory.SoundSettings,
                    setting.AlarmId,
                    TransferImportChangeKind.Added);
            }
            else
            {
                var unchanged = string.Equals(
                                    NormalizeSoundId(current.SoundId),
                                    setting.SoundId,
                                    StringComparison.Ordinal) &&
                                current.AutoAcknowledgeOnClear ==
                                setting.AutoAcknowledgeOnClear;
                preview.Add(
                    TransferProfileCategory.SoundSettings,
                    setting.AlarmId,
                    unchanged
                        ? TransferImportChangeKind.Unchanged
                        : TransferImportChangeKind.Changed);
            }
        }
    }

    private static void PreviewAppearance(
        UnmaConfiguration target,
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        if (profile.Appearance == null)
        {
            preview.Add(
                TransferProfileCategory.Appearance,
                "appearance",
                TransferImportChangeKind.Skipped,
                "The profile selected appearance settings but contains none.");
            return;
        }

        PreviewColorValue(preview, "warning-color", target.WarningColor,
            profile.Appearance.WarningColor);
        PreviewColorValue(preview, "critical-color", target.CriticalColor,
            profile.Appearance.CriticalColor);
        PreviewColorValue(preview, "emergency-color", target.EmergencyColor,
            profile.Appearance.EmergencyColor);
        if (IsValidUiScale(profile.Appearance.UiScalePercent))
        {
            PreviewValue(
                preview,
                TransferProfileCategory.Appearance,
                "ui-scale-percent",
                target.UiScalePercent,
                profile.Appearance.UiScalePercent);
        }
        else
        {
            AddSkipped(
                preview,
                TransferProfileCategory.Appearance,
                "ui-scale-percent",
                "UI scale must be between 75 and 200 percent; the profile " +
                "value was skipped.");
        }
        PreviewValue(
            preview,
            TransferProfileCategory.Appearance,
            "reduced-motion",
            target.ReducedMotion,
            profile.Appearance.ReducedMotion);
    }

    private static void PreviewSystemAlarms(
        UnmaConfiguration target,
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var targetById = (target.SystemAlarms ??
                new List<SystemAlarmDefinition>())
            .Where(alarm => alarm != null &&
                            !string.IsNullOrWhiteSpace(alarm.Id))
            .GroupBy(alarm => alarm.Id.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(),
                StringComparer.Ordinal);
        foreach (var alarm in GetEffectiveSystemAlarms(profile, preview))
        {
            if (!targetById.TryGetValue(alarm.Id, out var current))
            {
                preview.Add(
                    TransferProfileCategory.SystemAlarms,
                    alarm.Id,
                    TransferImportChangeKind.Added);
            }
            else
            {
                preview.Add(
                    TransferProfileCategory.SystemAlarms,
                    alarm.Id,
                    SystemAlarmEquals(current, alarm)
                        ? TransferImportChangeKind.Unchanged
                        : TransferImportChangeKind.Changed);
            }
        }
    }

    private static void PreviewWindowLayout(
        UnmaConfiguration target,
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        if (profile.WindowLayout == null)
        {
            preview.Add(
                TransferProfileCategory.WindowLayout,
                "window-layout",
                TransferImportChangeKind.Skipped,
                "The profile selected window layout but contains none.");
            return;
        }
        var layout = profile.WindowLayout;
        PreviewWindowValue(preview, "window-x", target.WindowX,
            layout.WindowX, float.NegativeInfinity);
        PreviewWindowValue(preview, "window-y", target.WindowY,
            layout.WindowY, float.NegativeInfinity);
        PreviewWindowValue(preview, "window-width", target.WindowWidth,
            layout.WindowWidth, 700f);
        PreviewWindowValue(preview, "window-height", target.WindowHeight,
            layout.WindowHeight, 520f);
        PreviewWindowValue(preview, "launcher-x", target.LauncherX,
            layout.LauncherX, float.NegativeInfinity);
        PreviewWindowValue(preview, "launcher-y", target.LauncherY,
            layout.LauncherY, float.NegativeInfinity);
        PreviewWindowValue(preview, "editor-window-x", target.EditorWindowX,
            layout.EditorWindowX, float.NegativeInfinity);
        PreviewWindowValue(preview, "editor-window-y", target.EditorWindowY,
            layout.EditorWindowY, float.NegativeInfinity);
        PreviewWindowValue(preview, "editor-window-width",
            target.EditorWindowWidth, layout.EditorWindowWidth, 700f);
        PreviewWindowValue(preview, "editor-window-height",
            target.EditorWindowHeight, layout.EditorWindowHeight, 520f);
        PreviewDetachedPanelLayouts(target, layout, preview);
    }

    private static void MergeNotificationRules(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        target.VanillaNotificationRules ??= new List<VanillaNotificationRule>();
        foreach (var rule in GetEffectiveNotificationRules(profile, null))
        {
            var identity = RuleIdentity(rule);
            target.VanillaNotificationRules.RemoveAll(candidate =>
                string.Equals(
                    VanillaNotificationSuppressionPolicy.RuleIdentity(
                        candidate),
                    identity,
                    StringComparison.Ordinal));
            target.VanillaNotificationRules.Add(
                ToVanillaNotificationRule(rule));
        }
    }

    private static void MergeSoundSettings(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        target.SoundOverrides ??= new List<AlarmSoundOverride>();
        foreach (var setting in GetEffectiveSoundSettings(profile, null))
        {
            var current = target.SoundOverrides.LastOrDefault(item =>
                item != null && string.Equals(
                    item.AlarmId?.Trim(),
                    setting.AlarmId,
                    StringComparison.Ordinal));
            var isGloballyDisabled = current?.IsGloballyDisabled ?? false;
            target.SoundOverrides.RemoveAll(item =>
                item != null && string.Equals(
                    item.AlarmId?.Trim(),
                    setting.AlarmId,
                    StringComparison.Ordinal));
            target.SoundOverrides.Add(new AlarmSoundOverride
            {
                AlarmId = setting.AlarmId,
                SoundId = setting.SoundId,
                AutoAcknowledgeOnClear = setting.AutoAcknowledgeOnClear,
                IsGloballyDisabled = isGloballyDisabled,
            });
        }
    }

    private static void MergeSystemAlarms(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        target.SystemAlarms ??= new List<SystemAlarmDefinition>();
        foreach (var alarm in GetEffectiveSystemAlarms(profile, null))
        {
            target.SystemAlarms.RemoveAll(candidate =>
                candidate != null && string.Equals(
                    candidate.Id?.Trim(),
                    alarm.Id,
                    StringComparison.Ordinal));
            target.SystemAlarms.Add(CloneDataContract(alarm));
        }
    }

    private static void MergeAppearance(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        var appearance = profile.Appearance;
        if (appearance == null)
        {
            return;
        }
        if (IsValidColor(appearance.WarningColor, allowAuto: false))
        {
            target.WarningColor = appearance.WarningColor;
        }
        if (IsValidColor(appearance.CriticalColor, allowAuto: false))
        {
            target.CriticalColor = appearance.CriticalColor;
        }
        if (IsValidColor(appearance.EmergencyColor, allowAuto: false))
        {
            target.EmergencyColor = appearance.EmergencyColor;
        }
        if (IsValidUiScale(appearance.UiScalePercent))
        {
            target.UiScalePercent = appearance.UiScalePercent;
        }
        target.ReducedMotion = appearance.ReducedMotion;
    }

    private static void MergeWindowLayout(
        UnmaConfiguration target,
        UnmaTransferProfile profile)
    {
        var layout = profile.WindowLayout;
        if (layout == null)
        {
            return;
        }
        ApplyFiniteWindowValue(layout.WindowX, value => target.WindowX = value);
        ApplyFiniteWindowValue(layout.WindowY, value => target.WindowY = value);
        ApplySizedWindowValue(
            layout.WindowWidth,
            700f,
            value => target.WindowWidth = value);
        ApplySizedWindowValue(
            layout.WindowHeight,
            520f,
            value => target.WindowHeight = value);
        ApplyFiniteWindowValue(
            layout.LauncherX,
            value => target.LauncherX = value);
        ApplyFiniteWindowValue(
            layout.LauncherY,
            value => target.LauncherY = value);
        ApplyFiniteWindowValue(
            layout.EditorWindowX,
            value => target.EditorWindowX = value);
        ApplyFiniteWindowValue(
            layout.EditorWindowY,
            value => target.EditorWindowY = value);
        ApplySizedWindowValue(
            layout.EditorWindowWidth,
            700f,
            value => target.EditorWindowWidth = value);
        ApplySizedWindowValue(
            layout.EditorWindowHeight,
            520f,
            value => target.EditorWindowHeight = value);
        target.DetachedPanelLayouts = (layout.DetachedPanels ??
                new List<TransferDetachedPanelWindowLayout>())
            .Where(item => item != null &&
                           target.Panels.Any(panel => panel != null &&
                               string.Equals(
                                   panel.Id,
                                   item.PanelId?.Trim(),
                                   StringComparison.Ordinal)) &&
                           IsFinite(item.X) && IsFinite(item.Y) &&
                           IsValidSizedWindowValue(item.Width, 420f) &&
                           IsValidSizedWindowValue(item.Height, 320f))
            .GroupBy(
                item => item.PanelId.Trim(),
                StringComparer.Ordinal)
            .Select(group =>
            {
                var item = group.First();
                return new DetachedPanelWindowLayout
                {
                    PanelId = item.PanelId.Trim(),
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    IsOpen = item.IsOpen,
                };
            })
            .ToList();
    }

    private static void PreviewDetachedPanelLayouts(
        UnmaConfiguration target,
        TransferWindowLayout layout,
        TransferImportPreview preview)
    {
        var targetLayouts = (target.DetachedPanelLayouts ??
                new List<DetachedPanelWindowLayout>())
            .Where(item => item != null)
            .ToDictionary(item => item.PanelId, StringComparer.Ordinal);
        foreach (var item in layout.DetachedPanels ??
                     new List<TransferDetachedPanelWindowLayout>())
        {
            var panelId = item?.PanelId?.Trim() ?? "";
            var valid = panelId.Length > 0 &&
                        target.Panels.Any(panel => panel != null &&
                            string.Equals(
                                panel.Id,
                                panelId,
                                StringComparison.Ordinal)) &&
                        IsFinite(item.X) && IsFinite(item.Y) &&
                        IsValidSizedWindowValue(item.Width, 420f) &&
                        IsValidSizedWindowValue(item.Height, 320f);
            if (!valid)
            {
                AddSkipped(
                    preview,
                    TransferProfileCategory.WindowLayout,
                    "detached-panel:" + panelId,
                    "");
                continue;
            }
            var unchanged = targetLayouts.TryGetValue(panelId, out var current) &&
                            current.X.Equals(item.X) &&
                            current.Y.Equals(item.Y) &&
                            current.Width.Equals(item.Width) &&
                            current.Height.Equals(item.Height) &&
                            current.IsOpen == item.IsOpen;
            preview.Add(
                TransferProfileCategory.WindowLayout,
                "detached-panel:" + panelId,
                targetLayouts.ContainsKey(panelId)
                    ? unchanged
                        ? TransferImportChangeKind.Unchanged
                        : TransferImportChangeKind.Changed
                    : TransferImportChangeKind.Added);
        }
    }

    private static List<TransferNotificationRule>
        GetEffectiveNotificationRules(
            UnmaTransferProfile profile,
            TransferImportPreview preview)
    {
        var allowed = profile.Selection.NotificationRuleIdentities == null
            ? null
            : new HashSet<string>(
                profile.Selection.NotificationRuleIdentities,
                StringComparer.Ordinal);
        var result = new List<TransferNotificationRule>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in profile.NotificationRules)
        {
            var identity = RuleIdentity(rule);
            if (allowed != null && !allowed.Contains(identity))
            {
                AddSkipped(
                    preview,
                    TransferProfileCategory.NotificationBehaviors,
                    identity,
                    "Notification rule '" + identity +
                    "' is not selected by the profile and was skipped.");
                continue;
            }
            if (!TryValidateNotificationRule(rule, out var diagnostic))
            {
                AddSkipped(
                    preview,
                    TransferProfileCategory.NotificationBehaviors,
                    identity,
                    diagnostic);
                continue;
            }

            var clone = CloneTransferNotificationRule(rule);
            identity = RuleIdentity(clone);
            if (indices.TryGetValue(identity, out var index))
            {
                result[index] = clone;
                AddSkipped(
                    preview,
                    TransferProfileCategory.NotificationBehaviors,
                    identity,
                    "Duplicate notification rule '" + identity +
                    "' was reduced to its last value.");
            }
            else
            {
                indices.Add(identity, result.Count);
                result.Add(clone);
            }
        }
        return result;
    }

    private static List<TransferSoundSetting> GetEffectiveSoundSettings(
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var result = new List<TransferSoundSetting>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in profile.SoundSettings)
        {
            var alarmId = source?.AlarmId?.Trim() ?? "";
            if (!IsTransferableSoundAlarmId(alarmId))
            {
                AddSkipped(
                    preview,
                    TransferProfileCategory.SoundSettings,
                    alarmId,
                    "Sound setting '" + alarmId +
                    "' was skipped because its alarm id is not stable " +
                    "across savegames.");
                continue;
            }
            var setting = new TransferSoundSetting
            {
                AlarmId = alarmId,
                SoundId = NormalizeSoundId(source.SoundId),
                AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
            };
            if (indices.TryGetValue(alarmId, out var index))
            {
                result[index] = setting;
                AddSkipped(
                    preview,
                    TransferProfileCategory.SoundSettings,
                    alarmId,
                    "Duplicate sound setting '" + alarmId +
                    "' was reduced to its last value.");
            }
            else
            {
                indices.Add(alarmId, result.Count);
                result.Add(setting);
            }
        }
        return result;
    }

    private static List<SystemAlarmDefinition> GetEffectiveSystemAlarms(
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var result = new List<SystemAlarmDefinition>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in profile.SystemAlarms)
        {
            var alarmId = source?.Id?.Trim() ?? "";
            if (!TryValidateSystemAlarm(source, out var diagnostic))
            {
                AddSkipped(
                    preview,
                    TransferProfileCategory.SystemAlarms,
                    alarmId,
                    diagnostic);
                continue;
            }
            var alarm = CloneDataContract(source);
            alarm.Id = alarmId;
            if (indices.TryGetValue(alarmId, out var index))
            {
                result[index] = alarm;
                AddSkipped(
                    preview,
                    TransferProfileCategory.SystemAlarms,
                    alarmId,
                    "Duplicate system alarm '" + alarmId +
                    "' was reduced to its last value.");
            }
            else
            {
                indices.Add(alarmId, result.Count);
                result.Add(alarm);
            }
        }
        return result;
    }

    private static UnmaTransferProfile PrepareProfile(
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        if (profile == null)
        {
            AddSkipped(
                preview,
                TransferProfileCategory.NotificationBehaviors,
                "profile",
                "The transfer profile is missing.");
            return null;
        }
        if (profile.ProfileSchemaVersion >
            UnmaTransferProfile.CurrentProfileSchemaVersion)
        {
            AddSkipped(
                preview,
                TransferProfileCategory.NotificationBehaviors,
                "profile-schema",
                "Transfer profile schema " +
                profile.ProfileSchemaVersion +
                " is newer than supported schema " +
                UnmaTransferProfile.CurrentProfileSchemaVersion + ".");
            return null;
        }

        try
        {
            var clone = CloneDataContract(profile);
            clone.Normalize();
            return clone;
        }
        catch (Exception exception)
        {
            AddSkipped(
                preview,
                TransferProfileCategory.NotificationBehaviors,
                "profile",
                "The transfer profile is invalid: " + exception.Message);
            return null;
        }
    }

    private static void AddExportDiagnostics(
        UnmaTransferProfile profile,
        TransferImportPreview preview)
    {
        var diagnostics = profile.Metadata.Diagnostics ?? new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            preview.Add(
                TransferProfileCategory.NotificationBehaviors,
                "export",
                TransferImportChangeKind.Skipped,
                diagnostic);
        }
        for (var index = diagnostics.Count;
             index < profile.Metadata.SkippedItems;
             index++)
        {
            preview.Add(
                TransferProfileCategory.NotificationBehaviors,
                "export",
                TransferImportChangeKind.Skipped,
                "An item was skipped while the profile was created.");
        }
    }

    private static void AddExportSkip(
        UnmaTransferProfile profile,
        string diagnostic)
    {
        profile.Metadata.SkippedItems++;
        profile.Metadata.Diagnostics.Add(diagnostic);
    }

    private static void AddSkipped(
        TransferImportPreview preview,
        TransferProfileCategory category,
        string key,
        string diagnostic)
    {
        preview?.Add(
            category,
            key,
            TransferImportChangeKind.Skipped,
            diagnostic);
    }

    private static bool TryValidateNotificationRule(
        VanillaNotificationRule rule,
        out string diagnostic)
    {
        if (rule == null)
        {
            diagnostic = "A null notification rule was skipped.";
            return false;
        }
        if (!VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                rule.AlarmId))
        {
            diagnostic = "Notification rule '" +
                (rule.AlarmId?.Trim() ?? "") +
                "' has no transferable vanilla notification id.";
            return false;
        }
        if (!Enum.IsDefined(typeof(VanillaNotificationScope), rule.Scope) ||
            !Enum.IsDefined(
                typeof(VanillaNotificationBehavior),
                rule.Behavior))
        {
            diagnostic = "Notification rule '" +
                VanillaNotificationSuppressionPolicy.RuleIdentity(rule) +
                "' contains an unsupported value.";
            return false;
        }
        if (rule.Scope == VanillaNotificationScope.Entity)
        {
            diagnostic = "Entity-scoped notification rule '" +
                (rule.AlarmId?.Trim() ?? "") +
                "' was skipped because entity ids are world-specific.";
            return false;
        }
        if (rule.Scope == VanillaNotificationScope.EntityPrototype &&
            string.IsNullOrWhiteSpace(rule.EntityPrototypeId))
        {
            diagnostic = "Prototype-scoped notification rule '" +
                (rule.AlarmId?.Trim() ?? "") +
                "' has no prototype id and was skipped.";
            return false;
        }
        diagnostic = "";
        return true;
    }

    private static bool TryValidateNotificationRule(
        TransferNotificationRule rule,
        out string diagnostic)
    {
        if (rule == null)
        {
            diagnostic = "A null notification rule was skipped.";
            return false;
        }
        return TryValidateNotificationRule(
            ToVanillaNotificationRule(rule),
            out diagnostic);
    }

    private static TransferNotificationRule CloneNotificationRule(
        VanillaNotificationRule source)
    {
        return new TransferNotificationRule
        {
            AlarmId = source.AlarmId?.Trim() ?? "",
            Scope = source.Scope,
            Behavior = source.Behavior,
            EntityPrototypeId =
                source.Scope == VanillaNotificationScope.EntityPrototype
                    ? source.EntityPrototypeId?.Trim() ?? ""
                    : "",
        };
    }

    private static TransferNotificationRule CloneTransferNotificationRule(
        TransferNotificationRule source)
    {
        return new TransferNotificationRule
        {
            AlarmId = source.AlarmId?.Trim() ?? "",
            Scope = source.Scope,
            Behavior = source.Behavior,
            EntityPrototypeId =
                source.Scope == VanillaNotificationScope.EntityPrototype
                    ? source.EntityPrototypeId?.Trim() ?? ""
                    : "",
        };
    }

    private static VanillaNotificationRule ToVanillaNotificationRule(
        TransferNotificationRule source)
    {
        return new VanillaNotificationRule
        {
            AlarmId = source.AlarmId?.Trim() ?? "",
            Scope = source.Scope,
            Behavior = source.Behavior,
            EntityId = -1,
            EntityPrototypeId =
                source.Scope == VanillaNotificationScope.EntityPrototype
                    ? source.EntityPrototypeId?.Trim() ?? ""
                    : "",
        };
    }

    public static string RuleIdentity(TransferNotificationRule rule)
    {
        return rule == null
            ? ""
            : VanillaNotificationSuppressionPolicy.RuleIdentity(
                ToVanillaNotificationRule(rule));
    }

    private static TransferProfileSelection CloneSelection(
        TransferProfileSelection source)
    {
        return new TransferProfileSelection
        {
            NotificationBehaviors = source.NotificationBehaviors,
            SoundSettings = source.SoundSettings,
            Appearance = source.Appearance,
            SystemAlarms = source.SystemAlarms,
            WindowLayout = source.WindowLayout,
            NotificationRuleIdentities =
                source.NotificationRuleIdentities == null
                    ? null
                    : new List<string>(source.NotificationRuleIdentities),
        };
    }

    private static void PreviewColorValue(
        TransferImportPreview preview,
        string key,
        string current,
        string imported)
    {
        if (IsValidColor(imported, allowAuto: false))
        {
            PreviewValue(
                preview,
                TransferProfileCategory.Appearance,
                key,
                current,
                imported);
            return;
        }
        AddSkipped(
            preview,
            TransferProfileCategory.Appearance,
            key,
            "Color '" + (imported ?? "<null>") +
            "' is not a supported HTML color; the profile value was " +
            "skipped.");
    }

    private static void PreviewWindowValue(
        TransferImportPreview preview,
        string key,
        float current,
        float imported,
        float minimum)
    {
        if (IsFinite(imported) && imported >= minimum)
        {
            PreviewValue(
                preview,
                TransferProfileCategory.WindowLayout,
                key,
                current,
                imported);
            return;
        }
        var constraint = float.IsNegativeInfinity(minimum)
            ? "finite"
            : "finite and at least " +
              minimum.ToString(CultureInfo.InvariantCulture);
        AddSkipped(
            preview,
            TransferProfileCategory.WindowLayout,
            key,
            "Window layout value '" + key + "' must be " + constraint +
            "; the profile value was skipped.");
    }

    private static void ApplyFiniteWindowValue(
        float value,
        Action<float> apply)
    {
        if (IsFinite(value))
        {
            apply(value);
        }
    }

    private static void ApplySizedWindowValue(
        float value,
        float minimum,
        Action<float> apply)
    {
        if (IsFinite(value) && value >= minimum)
        {
            apply(value);
        }
    }

    private static bool IsValidSizedWindowValue(float value, float minimum)
    {
        return IsFinite(value) && value >= minimum;
    }

    private static bool IsValidUiScale(int value)
    {
        return value >= 75 && value <= 200;
    }

    private static bool IsValidColor(string value, bool allowAuto)
    {
        if (allowAuto && string.Equals(
                value,
                "auto",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (value == null)
        {
            return false;
        }
        if (IsNamedColor(value))
        {
            return true;
        }
        if ((value.Length != 4 && value.Length != 5 &&
             value.Length != 7 && value.Length != 9) || value[0] != '#')
        {
            return false;
        }
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNamedColor(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "red" or "cyan" or "blue" or "darkblue" or "lightblue" or
            "purple" or "yellow" or "lime" or "fuchsia" or "white" or
            "silver" or "grey" or "black" or "orange" or "brown" or
            "maroon" or "green" or "olive" or "navy" or "teal" or
            "aqua" or "magenta" => true,
            _ => false,
        };
    }

    private static bool TryValidateSystemAlarm(
        SystemAlarmDefinition alarm,
        out string diagnostic)
    {
        var alarmId = alarm?.Id?.Trim() ?? "";
        if (!HasNonEmptyPrefix(alarmId, "system:"))
        {
            diagnostic = "System alarm '" + alarmId +
                "' has no stable system id and was skipped.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(alarm.DisplayName))
        {
            diagnostic = "System alarm '" + alarmId +
                "' has no display name and was skipped.";
            return false;
        }
        if (alarm.Stages == null || alarm.Stages.Count == 0)
        {
            diagnostic = "System alarm '" + alarmId +
                "' contains no stages and was skipped.";
            return false;
        }

        var stageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in alarm.Stages)
        {
            if (!TryValidateSystemStage(
                    alarmId,
                    stage,
                    stageIds,
                    out diagnostic))
            {
                return false;
            }
        }

        var defaultAlarm = UnmaConfiguration.CreateDefaultSystemAlarms()
            .FirstOrDefault(item => string.Equals(
                item.Id,
                alarmId,
                StringComparison.Ordinal));
        if (defaultAlarm != null)
        {
            foreach (var defaultStage in defaultAlarm.Stages)
            {
                if (!stageIds.Contains(defaultStage.Id))
                {
                    diagnostic = "System alarm '" + alarmId +
                        "' is missing required stage '" + defaultStage.Id +
                        "' and was skipped.";
                    return false;
                }
            }
        }

        diagnostic = "";
        return true;
    }

    private static bool TryValidateSystemStage(
        string alarmId,
        SystemAlarmStageDefinition stage,
        HashSet<string> stageIds,
        out string diagnostic)
    {
        var stageId = stage?.Id?.Trim() ?? "";
        var key = alarmId + "/" + stageId;
        if (stage == null || stageId.Length == 0 ||
            !string.Equals(stage.Id, stageId, StringComparison.Ordinal))
        {
            diagnostic = "System alarm '" + alarmId +
                "' contains a stage without an id and was skipped.";
            return false;
        }
        if (!stageIds.Add(stageId))
        {
            diagnostic = "System alarm '" + alarmId +
                "' contains duplicate stage '" + stageId +
                "' and was skipped.";
            return false;
        }
        if (stage.Priority < 0 ||
            string.IsNullOrWhiteSpace(stage.Message) ||
            !Enum.IsDefined(typeof(AlarmSeverity), stage.Severity) ||
            !Enum.IsDefined(typeof(AlarmLogic), stage.Logic) ||
            !Enum.IsDefined(
                typeof(AlarmOperatorAction),
                stage.OperatorAction) ||
            !IsValidColor(stage.ActiveColor, allowAuto: true) ||
            !IsValidSoundId(stage.SoundId) ||
            !IsValidTiming(stage.ActivationDelayTicks) ||
            !IsValidTiming(stage.ResetDelayTicks) ||
            !IsValidTiming(stage.MinimumActiveTicks))
        {
            diagnostic = "System alarm stage '" + key +
                "' contains an invalid priority, message, enum, color, " +
                "sound, or timing value and was skipped.";
            return false;
        }
        if (stage.Conditions == null || stage.Conditions.Count == 0)
        {
            diagnostic = "System alarm stage '" + key +
                "' contains no conditions and was skipped.";
            return false;
        }
        for (var index = 0; index < stage.Conditions.Count; index++)
        {
            var condition = stage.Conditions[index];
            if (condition == null ||
                string.IsNullOrWhiteSpace(condition.MetricId) ||
                !string.Equals(
                    condition.MetricId,
                    condition.MetricId.Trim(),
                    StringComparison.Ordinal) ||
                !Enum.IsDefined(
                    typeof(ComparisonOperator),
                    condition.Comparison) ||
                !IsFinite(condition.Threshold) ||
                !IsFinite(condition.Hysteresis) ||
                condition.Hysteresis < 0d)
            {
                diagnostic = "System alarm stage '" + key +
                    "' contains an invalid condition at index " + index +
                    " and was skipped.";
                return false;
            }
        }
        diagnostic = "";
        return true;
    }

    private static bool IsValidTiming(int ticks)
    {
        return ticks >= 0 && ticks <= AlarmTimingPolicy.MaximumTimingTicks;
    }

    private static bool IsValidSoundId(string soundId)
    {
        if (string.IsNullOrWhiteSpace(soundId) || soundId.Length > 256 ||
            !string.Equals(soundId, soundId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }
        for (var index = 0; index < soundId.Length; index++)
        {
            if (char.IsControl(soundId[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static void PreviewValue<T>(
        TransferImportPreview preview,
        TransferProfileCategory category,
        string key,
        T current,
        T imported)
    {
        preview.Add(
            category,
            key,
            EqualityComparer<T>.Default.Equals(current, imported)
                ? TransferImportChangeKind.Unchanged
                : TransferImportChangeKind.Changed);
    }

    private static bool SystemAlarmEquals(
        SystemAlarmDefinition first,
        SystemAlarmDefinition second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }
        if (first == null || second == null ||
            !string.Equals(first.Id, second.Id, StringComparison.Ordinal) ||
            !string.Equals(
                first.DisplayName,
                second.DisplayName,
                StringComparison.Ordinal) ||
            first.Enabled != second.Enabled ||
            first.AutoAcknowledgeOnClear !=
            second.AutoAcknowledgeOnClear)
        {
            return false;
        }
        var firstStages = first.Stages ?? new List<SystemAlarmStageDefinition>();
        var secondStages = second.Stages ?? new List<SystemAlarmStageDefinition>();
        if (firstStages.Count != secondStages.Count)
        {
            return false;
        }
        for (var index = 0; index < firstStages.Count; index++)
        {
            if (!SystemStageEquals(firstStages[index], secondStages[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool SystemStageEquals(
        SystemAlarmStageDefinition first,
        SystemAlarmStageDefinition second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }
        if (first == null || second == null ||
            !string.Equals(first.Id, second.Id, StringComparison.Ordinal) ||
            first.Priority != second.Priority ||
            first.Enabled != second.Enabled ||
            !string.Equals(
                first.Message,
                second.Message,
                StringComparison.Ordinal) ||
            first.Severity != second.Severity ||
            first.Logic != second.Logic ||
            !string.Equals(
                first.ActiveColor,
                second.ActiveColor,
                StringComparison.Ordinal) ||
            !string.Equals(
                first.SoundId,
                second.SoundId,
                StringComparison.Ordinal) ||
            first.ActivationDelayTicks != second.ActivationDelayTicks ||
            first.ResetDelayTicks != second.ResetDelayTicks ||
            first.MinimumActiveTicks != second.MinimumActiveTicks ||
            first.OperatorAction != second.OperatorAction)
        {
            return false;
        }
        var firstConditions = first.Conditions ??
                              new List<SystemConditionDefinition>();
        var secondConditions = second.Conditions ??
                               new List<SystemConditionDefinition>();
        if (firstConditions.Count != secondConditions.Count)
        {
            return false;
        }
        for (var index = 0; index < firstConditions.Count; index++)
        {
            var firstCondition = firstConditions[index];
            var secondCondition = secondConditions[index];
            if (ReferenceEquals(firstCondition, secondCondition))
            {
                continue;
            }
            if (firstCondition == null || secondCondition == null ||
                !string.Equals(
                    firstCondition.MetricId,
                    secondCondition.MetricId,
                    StringComparison.Ordinal) ||
                firstCondition.Comparison != secondCondition.Comparison ||
                !firstCondition.Threshold.Equals(secondCondition.Threshold) ||
                !firstCondition.Hysteresis.Equals(secondCondition.Hysteresis))
            {
                return false;
            }
        }
        return true;
    }

    private static string NormalizeSoundId(string soundId)
    {
        return string.IsNullOrWhiteSpace(soundId)
            ? "auto"
            : soundId.Trim();
    }

    private static bool IsTransferableSoundAlarmId(string alarmId)
    {
        alarmId = alarmId?.Trim() ?? "";
        return HasNonEmptyPrefix(alarmId, "vanilla:") ||
               HasNonEmptyPrefix(alarmId, "system:") ||
               HasNonEmptyPrefix(alarmId, "external:");
    }

    private static bool HasNonEmptyPrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length > prefix.Length;
    }

    private static T CloneDataContract<T>(T source)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(typeof(T));
        serializer.WriteObject(stream, source);
        stream.Position = 0;
        return (T)serializer.ReadObject(stream);
    }
}
