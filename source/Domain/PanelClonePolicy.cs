using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UNMA.Domain;

public enum PanelCloneFailure
{
    None,
    InvalidSource,
    DashboardNotSupported,
    EntityPanelNotSupported,
    InvalidSourceData,
    IdGenerationFailed,
}

public sealed class PanelClonePlan
{
    internal PanelClonePlan(
        PanelDefinition panel,
        IEnumerable<AlarmRuleDefinition> rules,
        IDictionary<string, string> ruleIdMap,
        int skippedRuleSlotCount)
    {
        Panel = panel;
        Rules = new ReadOnlyCollection<AlarmRuleDefinition>(
            (rules ?? Enumerable.Empty<AlarmRuleDefinition>()).ToList());
        RuleIdMap = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                ruleIdMap ?? new Dictionary<string, string>(),
                StringComparer.Ordinal));
        SkippedRuleSlotCount = Math.Max(0, skippedRuleSlotCount);
    }

    public PanelDefinition Panel { get; }

    public IReadOnlyList<AlarmRuleDefinition> Rules { get; }

    public IReadOnlyDictionary<string, string> RuleIdMap { get; }

    public int SkippedRuleSlotCount { get; }

    // Compatibility alias for the runtime integration introduced together
    // with this policy. The count now also includes malformed and duplicate
    // custom-rule slots, not only orphaned rule references.
    public int OrphanRuleSlotCount => SkippedRuleSlotCount;
}

public static class PanelClonePolicy
{
    private const int MaxIdGenerationAttempts = 128;

    public static bool CanClone(PanelDefinition source)
    {
        return source != null &&
               !source.IsDashboard &&
               !PanelTopologyPolicy.IsEntityPanel(source);
    }

    public static string CreateCopyName(
        PanelDefinition source,
        IEnumerable<PanelDefinition> panels)
    {
        return CreateCopyName(source?.Name, panels);
    }

    public static string CreateCopyName(
        string sourceName,
        IEnumerable<PanelDefinition> panels)
    {
        var baseName = sourceName?.Trim() ?? "";
        if (baseName.Length == 0)
        {
            baseName = "PANEL";
        }

        var existingNames = new HashSet<string>(
            (panels ?? Enumerable.Empty<PanelDefinition>())
            .Where(panel => panel != null)
            .Select(panel => panel.Name?.Trim() ?? "")
            .Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var candidate = baseName + " COPY";
        if (!existingNames.Contains(candidate))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            candidate = baseName + " COPY " + suffix;
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No copy name is available.");
    }

    public static bool TryCreatePlan(
        PanelDefinition source,
        IEnumerable<PanelDefinition> panels,
        IEnumerable<AlarmRuleDefinition> rules,
        Func<string> createId,
        out PanelClonePlan plan)
    {
        return TryCreatePlan(
            source,
            panels,
            rules,
            createId,
            out plan,
            out _);
    }

    public static bool TryCreatePlan(
        PanelDefinition source,
        IEnumerable<PanelDefinition> panels,
        IEnumerable<AlarmRuleDefinition> rules,
        Func<string> createId,
        out PanelClonePlan plan,
        out PanelCloneFailure failure)
    {
        plan = null;
        failure = PanelCloneFailure.None;
        if (source == null || string.IsNullOrWhiteSpace(source.Id))
        {
            failure = PanelCloneFailure.InvalidSource;
            return false;
        }
        if (source.IsDashboard)
        {
            failure = PanelCloneFailure.DashboardNotSupported;
            return false;
        }
        if (PanelTopologyPolicy.IsEntityPanel(source))
        {
            failure = PanelCloneFailure.EntityPanelNotSupported;
            return false;
        }
        if (createId == null)
        {
            failure = PanelCloneFailure.IdGenerationFailed;
            return false;
        }

        var sourceId = source.Id.Trim();
        var panelList = (panels ?? Enumerable.Empty<PanelDefinition>())
            .Where(panel => panel != null)
            .ToList();
        if (!panelList.Any(panel => string.Equals(
                panel.Id?.Trim(),
                sourceId,
                StringComparison.Ordinal)))
        {
            panelList.Add(source);
        }

        var ruleList = (rules ?? Enumerable.Empty<AlarmRuleDefinition>())
            .Where(rule => rule != null)
            .ToList();
        var assignedRules = ruleList
            .Where(rule => IsAssignedToSource(rule, sourceId))
            .ToList();
        var assignedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in assignedRules)
        {
            var ruleId = rule.Id?.Trim() ?? "";
            if (ruleId.Length == 0 || !assignedRuleIds.Add(ruleId))
            {
                failure = PanelCloneFailure.InvalidSourceData;
                return false;
            }
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in panelList)
        {
            AddExistingId(usedIds, panel.Id);
        }
        foreach (var rule in ruleList)
        {
            AddExistingId(usedIds, rule.Id);
        }

        if (!TryReserveFreshId(createId, usedIds, out var clonedPanelId))
        {
            failure = PanelCloneFailure.IdGenerationFailed;
            return false;
        }

        var clonedRules = new List<AlarmRuleDefinition>(assignedRules.Count);
        var ruleIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourceRule in assignedRules)
        {
            if (!TryReserveFreshId(createId, usedIds, out var clonedRuleId))
            {
                failure = PanelCloneFailure.IdGenerationFailed;
                return false;
            }

            var sourceRuleId = sourceRule.Id.Trim();
            ruleIdMap.Add(sourceRuleId, clonedRuleId);
            clonedRules.Add(CloneRule(
                sourceRule,
                clonedRuleId,
                clonedPanelId));
        }

        var clonedRulesById = clonedRules.ToDictionary(
            rule => rule.Id,
            StringComparer.Ordinal);
        var clonedSlots = new List<PanelSlotDefinition>();
        var emittedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var skippedRuleSlotCount = 0;
        foreach (var sourceSlot in source.Slots ??
                     new List<PanelSlotDefinition>())
        {
            if (sourceSlot == null)
            {
                continue;
            }
            if (!PanelTopologyPolicy.TryGetRuleId(
                    sourceSlot.AlarmId,
                    out var sourceRuleId))
            {
                if (LooksLikeRuleSlot(sourceSlot.AlarmId))
                {
                    skippedRuleSlotCount++;
                    continue;
                }
                clonedSlots.Add(PanelSlotProjection.CloneSlot(sourceSlot));
                continue;
            }
            if (!ruleIdMap.TryGetValue(sourceRuleId, out var clonedRuleId))
            {
                skippedRuleSlotCount++;
                continue;
            }
            if (!emittedRuleIds.Add(clonedRuleId))
            {
                skippedRuleSlotCount++;
                continue;
            }

            clonedSlots.Add(PanelSlotProjection.CreateRuleSlot(
                clonedRulesById[clonedRuleId]));
        }

        foreach (var clonedRule in clonedRules)
        {
            if (emittedRuleIds.Add(clonedRule.Id))
            {
                clonedSlots.Add(PanelSlotProjection.CreateRuleSlot(
                    clonedRule));
            }
        }

        var clonedPanel = new PanelDefinition
        {
            Id = clonedPanelId,
            Name = CreateCopyName(source, panelList),
            Columns = source.Columns,
            IncludeVanilla = source.IncludeVanilla,
            IncludeSystem = source.IncludeSystem,
            NotificationFilter = source.NotificationFilter,
            Slots = clonedSlots,
            ExcludedAlarmIds = CloneNormalExclusions(
                source.ExcludedAlarmIds),
            IsDashboard = false,
            OwnerEntityId = -1,
            OwnerEntityTitle = "",
            OwnerEntityPrototypeId = "",
            OwnerEntityType = "",
        };
        plan = new PanelClonePlan(
            clonedPanel,
            clonedRules,
            ruleIdMap,
            skippedRuleSlotCount);
        return true;
    }

    private static bool LooksLikeRuleSlot(string alarmId)
    {
        return (alarmId?.Trim() ?? "").StartsWith(
            "rule:",
            StringComparison.Ordinal);
    }

    private static bool IsAssignedToSource(
        AlarmRuleDefinition rule,
        string sourcePanelId)
    {
        if (rule == null)
        {
            return false;
        }
        if (string.Equals(
                rule.PanelId?.Trim(),
                sourcePanelId,
                StringComparison.Ordinal))
        {
            return true;
        }
        return (rule.LinkedPanelIds ?? new List<string>()).Any(
            panelId => string.Equals(
                panelId?.Trim(),
                sourcePanelId,
                StringComparison.Ordinal));
    }

    private static void AddExistingId(
        ISet<string> usedIds,
        string candidate)
    {
        candidate = candidate?.Trim() ?? "";
        if (candidate.Length > 0)
        {
            usedIds.Add(candidate);
        }
    }

    private static bool TryReserveFreshId(
        Func<string> createId,
        ISet<string> usedIds,
        out string freshId)
    {
        freshId = "";
        for (var attempt = 0;
             attempt < MaxIdGenerationAttempts;
             attempt++)
        {
            string candidate;
            try
            {
                candidate = createId()?.Trim() ?? "";
            }
            catch (Exception)
            {
                return false;
            }
            if (candidate.Length > 0 && usedIds.Add(candidate))
            {
                freshId = candidate;
                return true;
            }
        }
        return false;
    }

    private static List<string> CloneNormalExclusions(
        IEnumerable<string> sourceExclusions)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceExclusion in sourceExclusions ??
                     Enumerable.Empty<string>())
        {
            var alarmId = sourceExclusion?.Trim() ?? "";
            if (alarmId.Length == 0 ||
                PanelTopologyPolicy.TryGetRuleId(alarmId, out _) ||
                !seen.Add(alarmId))
            {
                continue;
            }
            result.Add(alarmId);
        }
        return result;
    }

    private static AlarmRuleDefinition CloneRule(
        AlarmRuleDefinition source,
        string clonedRuleId,
        string clonedPanelId)
    {
        return new AlarmRuleDefinition
        {
            Id = clonedRuleId,
            PanelId = clonedPanelId,
            Name = source.Name,
            Severity = source.Severity,
            Logic = source.Logic,
            Conditions = (source.Conditions ??
                          new List<ConditionDefinition>())
                .Where(condition => condition != null)
                .Select(CloneCondition)
                .ToList(),
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            Enabled = false,
            AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
            LinkedPanelIds = new List<string>(),
            ActivationDelayTicks = source.ActivationDelayTicks,
            ResetDelayTicks = source.ResetDelayTicks,
            MinimumActiveTicks = source.MinimumActiveTicks,
        };
    }

    private static ConditionDefinition CloneCondition(
        ConditionDefinition source)
    {
        return new ConditionDefinition
        {
            EntityId = source.EntityId,
            EntityTitle = source.EntityTitle,
            EntityType = source.EntityType,
            MetricPath = source.MetricPath,
            MetricLabel = source.MetricLabel,
            Comparison = source.Comparison,
            Threshold = source.Threshold,
            Hysteresis = source.Hysteresis,
            ExpectedProductId = source.ExpectedProductId,
            EntityPrototypeId = source.EntityPrototypeId,
            ValueMode = source.ValueMode,
            ReferenceMetricPath = source.ReferenceMetricPath,
            ReferenceMetricLabel = source.ReferenceMetricLabel,
            InstrumentId = source.InstrumentId,
            TrendMode = source.TrendMode,
            WindowSeconds = source.WindowSeconds,
            DeltaThreshold = source.DeltaThreshold,
            WindowAmount = source.WindowAmount,
            WindowUnit = source.WindowUnit,
        };
    }
}
