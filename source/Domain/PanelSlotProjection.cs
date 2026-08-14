using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UNMA.Localization;

namespace UNMA.Domain;

public static class PanelSlotProjection
{
    private const string RulePrefix = "rule:";

    public static string StableAlarmId(AlarmView view)
    {
        if (view == null)
        {
            return "";
        }
        if (!string.IsNullOrWhiteSpace(view.SlotId))
        {
            return view.SlotId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(view.OverrideId))
        {
            return view.OverrideId.Trim();
        }
        if (string.Equals(
                view.Source,
                "vanilla",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(view.OccurrenceId))
        {
            return view.OccurrenceId.Trim();
        }
        return view.Key?.Trim() ?? "";
    }

    public static string StableViewIdentity(AlarmView view)
    {
        if (view == null)
        {
            return "";
        }
        return string.Join(
            "|",
            view.Source?.Trim() ?? "",
            view.OverrideId?.Trim() ?? "",
            view.EntityId.ToString(CultureInfo.InvariantCulture),
            view.EntityPrototypeId?.Trim() ?? "",
            StableAlarmId(view));
    }

    public static string LegacyVanillaSlotId(
        string overrideId,
        string detail)
    {
        overrideId = overrideId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(overrideId))
        {
            return "";
        }
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in detail ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return overrideId + ":legacy:" + hash.ToString("X8");
        }
    }

    public static bool IsLegacyVanillaSlotId(
        string slotId,
        string overrideId)
    {
        return !string.IsNullOrWhiteSpace(slotId) &&
               !string.IsNullOrWhiteSpace(overrideId) &&
               slotId.StartsWith(
                   overrideId.Trim() + ":legacy:",
                   StringComparison.Ordinal);
    }

    public static PanelSlotDefinition CreateSlot(AlarmView view)
    {
        var alarmId = StableAlarmId(view);
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return null;
        }
        return new PanelSlotDefinition
        {
            AlarmId = alarmId,
            DisplayName = string.IsNullOrWhiteSpace(view.Name)
                ? UnmaText.Get("default.notification", "NOTIFICATION")
                : view.Name.Trim(),
            Detail = view.Detail ?? "",
            Source = view.Source ?? "",
            Severity = view.Severity,
            ActiveColor = string.IsNullOrWhiteSpace(view.ActiveColor)
                ? "#F0C541"
                : view.ActiveColor,
        };
    }

    public static PanelSlotDefinition CloneSlot(PanelSlotDefinition source)
    {
        return source == null
            ? null
            : new PanelSlotDefinition
            {
                AlarmId = source.AlarmId,
                DisplayName = source.DisplayName,
                Detail = source.Detail,
                Source = source.Source,
                Severity = source.Severity,
                ActiveColor = source.ActiveColor,
            };
    }

    public static PanelSlotDefinition CreateRuleSlot(
        AlarmRuleDefinition rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.Id))
        {
            return null;
        }
        return new PanelSlotDefinition
        {
            AlarmId = RulePrefix + rule.Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(rule.Name)
                ? UnmaText.Get("default.notification", "NOTIFICATION")
                : rule.Name.Trim(),
            Detail = (rule.Conditions?.Count ?? 0) +
                     UnmaText.Get("auto.38bf168a03a3"),
            Source = "custom",
            Severity = rule.Severity,
            ActiveColor = string.IsNullOrWhiteSpace(rule.ActiveColor)
                ? "#F0C541"
                : rule.ActiveColor,
        };
    }

    public static bool InsertRuleSlot(
        PanelDefinition panel,
        AlarmRuleDefinition rule,
        int preferredIndex)
    {
        var slot = CreateRuleSlot(rule);
        if (panel == null || slot == null)
        {
            return false;
        }
        panel.Slots ??= new List<PanelSlotDefinition>();
        if (panel.Slots.Any(candidate => string.Equals(
                candidate?.AlarmId,
                slot.AlarmId,
                StringComparison.Ordinal)))
        {
            return false;
        }
        panel.Slots.Insert(
            Math.Max(0, Math.Min(preferredIndex, panel.Slots.Count)),
            slot);
        return true;
    }

    public static bool TryGetCustomRuleId(
        AlarmView view,
        out string ruleId)
    {
        ruleId = "";
        if (view == null ||
            !string.Equals(view.Source, "custom", StringComparison.Ordinal))
        {
            return false;
        }
        var stableId = StableAlarmId(view);
        if (!stableId.StartsWith(RulePrefix, StringComparison.Ordinal) ||
            stableId.Length <= RulePrefix.Length)
        {
            return false;
        }
        ruleId = stableId.Substring(RulePrefix.Length).Trim();
        return ruleId.Length > 0;
    }

    public static IReadOnlyList<AlarmView> Project(
        IReadOnlyList<PanelSlotDefinition> slots,
        IEnumerable<AlarmView> candidates)
    {
        if (slots == null || slots.Count == 0)
        {
            return Array.Empty<AlarmView>();
        }

        var byAlarmId = (candidates ?? Enumerable.Empty<AlarmView>())
            .Where(candidate => candidate != null)
            .Select(candidate => new
            {
                Candidate = candidate,
                AlarmId = StableAlarmId(candidate),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.AlarmId))
            .GroupBy(item => item.AlarmId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Candidate).ToArray(),
                StringComparer.Ordinal);

        var result = new List<AlarmView>(slots.Count);
        foreach (var slot in slots)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.AlarmId))
            {
                continue;
            }
            if (!byAlarmId.TryGetValue(slot.AlarmId, out var slotCandidates) ||
                slotCandidates.Length == 0)
            {
                result.Add(CreateNormalView(slot));
                continue;
            }

            var view = CreateAggregatedView(slotCandidates);
            view.SlotId = slot.AlarmId;
            view.Name = string.IsNullOrWhiteSpace(view.Name)
                ? slot.DisplayName
                : view.Name;
            view.Detail = string.IsNullOrWhiteSpace(view.Detail)
                ? slot.Detail
                : view.Detail;
            view.Source = string.IsNullOrWhiteSpace(view.Source)
                ? slot.Source
                : view.Source;
            view.ActiveColor = string.IsNullOrWhiteSpace(view.ActiveColor)
                ? slot.ActiveColor
                : view.ActiveColor;
            if (!view.IsLatched)
            {
                view.Key = slot.AlarmId;
                view.Name = slot.DisplayName;
                view.Detail = slot.Detail;
                view.Source = slot.Source;
                view.Severity = slot.Severity;
                view.ActiveColor = slot.ActiveColor;
                view.IsMissingSource = false;
            }
            result.Add(view);
        }
        return result;
    }

    public static IReadOnlyList<AlarmView> ProjectActive(
        IEnumerable<AlarmView> candidates)
    {
        return (candidates ?? Enumerable.Empty<AlarmView>())
            .Where(candidate => candidate != null && candidate.IsActive)
            .Select(candidate => new
            {
                Candidate = candidate,
                AlarmId = StableAlarmId(candidate),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.AlarmId))
            .GroupBy(item => item.AlarmId, StringComparer.Ordinal)
            .Select(group =>
            {
                var view = CreateAggregatedView(
                    group.Select(item => item.Candidate).ToArray());
                view.SlotId = group.Key;
                return view;
            })
            .Where(view => view.IsActive)
            .OrderByDescending(view => view.Severity)
            .ThenBy(
                StableAlarmId,
                StringComparer.Ordinal)
            .ThenBy(view => view.Name ?? "", StringComparer.Ordinal)
            .ToArray();
    }

    public static AlarmView SelectRepresentative(
        IEnumerable<AlarmView> candidates)
    {
        return (candidates ?? Enumerable.Empty<AlarmView>())
            .Where(candidate => candidate != null)
            .OrderByDescending(StateRank)
            .ThenByDescending(candidate => candidate.Severity)
            .ThenByDescending(candidate => candidate.Sequence)
            .FirstOrDefault();
    }

    private static int StateRank(AlarmView view)
    {
        if (view.IsActive && !view.IsAcknowledged)
        {
            return 4;
        }
        if (view.IsActive)
        {
            return 3;
        }
        if (view.IsGoneUnacknowledged)
        {
            return 2;
        }
        return 1;
    }

    private static AlarmView CreateAggregatedView(
        IReadOnlyCollection<AlarmView> candidates)
    {
        var representative = SelectRepresentative(candidates);
        var view = CloneView(representative);
        var active = candidates.Where(candidate => candidate.IsActive)
            .ToArray();
        var gone = candidates.Where(candidate =>
                candidate.IsGoneUnacknowledged)
            .ToArray();
        var requiresAcknowledgement = candidates.Any(candidate =>
            candidate.RequiresAcknowledgement);

        if (active.Length > 0)
        {
            view.IsActive = true;
            view.IsAcknowledged = !requiresAcknowledgement;
            view.IsGoneUnacknowledged = false;
            view.Severity = active.Max(candidate => candidate.Severity);
            view.IsMissingSource = active.Any(candidate =>
                candidate.IsMissingSource);
        }
        else if (gone.Length > 0)
        {
            view.IsActive = false;
            view.IsAcknowledged = false;
            view.IsGoneUnacknowledged = true;
            view.Severity = gone.Max(candidate => candidate.Severity);
            view.IsMissingSource = gone.Any(candidate =>
                candidate.IsMissingSource);
        }
        else
        {
            view.IsActive = false;
            view.IsAcknowledged = false;
            view.IsGoneUnacknowledged = false;
        }
        return view;
    }

    private static AlarmView CreateNormalView(PanelSlotDefinition slot)
    {
        return new AlarmView
        {
            Key = slot.AlarmId,
            SlotId = slot.AlarmId,
            OverrideId = slot.AlarmId,
            Name = slot.DisplayName,
            Detail = slot.Detail,
            Source = slot.Source,
            Severity = slot.Severity,
            ActiveColor = slot.ActiveColor,
        };
    }

    private static AlarmView CloneView(AlarmView source)
    {
        return new AlarmView
        {
            Key = source.Key,
            Name = source.Name,
            Detail = source.Detail,
            Source = source.Source,
            PanelId = source.PanelId,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            OverrideId = source.OverrideId,
            OccurrenceId = source.OccurrenceId,
            SlotId = source.SlotId,
            OccurrencePriority = source.OccurrencePriority,
            EntityId = source.EntityId,
            EntityPrototypeId = source.EntityPrototypeId,
            EntityTitle = source.EntityTitle,
            Sequence = source.Sequence,
            Severity = source.Severity,
            IsActive = source.IsActive,
            IsAcknowledged = source.IsAcknowledged,
            IsGoneUnacknowledged = source.IsGoneUnacknowledged,
            IsMissingSource = source.IsMissingSource,
            LastValue = source.LastValue,
        };
    }
}
