using System;
using System.Collections.Generic;
using System.Linq;

namespace UNMA.Domain;

public static class PanelTopologyPolicy
{
    private const string RulePrefix = "rule:";

    public static bool IsEntityPanel(PanelDefinition panel)
    {
        return panel != null && panel.OwnerEntityId > 0;
    }

    public static List<string> NormalizeLinkedPanelIds(
        string primaryPanelId,
        IEnumerable<string> linkedPanelIds,
        IEnumerable<PanelDefinition> panels)
    {
        primaryPanelId = primaryPanelId?.Trim() ?? "";
        var availableGlobalPanels = new HashSet<string>(
            (panels ?? Enumerable.Empty<PanelDefinition>())
            .Where(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !IsEntityPanel(panel) &&
                !string.IsNullOrWhiteSpace(panel.Id))
            .Select(panel => panel.Id.Trim()),
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (var candidate in linkedPanelIds ??
                     Enumerable.Empty<string>())
        {
            var panelId = candidate?.Trim() ?? "";
            if (panelId.Length == 0 ||
                string.Equals(
                    panelId,
                    primaryPanelId,
                    StringComparison.Ordinal) ||
                !availableGlobalPanels.Contains(panelId) ||
                !seen.Add(panelId))
            {
                continue;
            }
            normalized.Add(panelId);
        }
        return normalized;
    }

    public static IReadOnlyList<string> GetRulePanelIds(
        AlarmRuleDefinition rule,
        IEnumerable<PanelDefinition> panels)
    {
        if (rule == null)
        {
            return Array.Empty<string>();
        }

        var availablePrimaryPanels = new HashSet<string>(
            (panels ?? Enumerable.Empty<PanelDefinition>())
            .Where(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !string.IsNullOrWhiteSpace(panel.Id))
            .Select(panel => panel.Id.Trim()),
            StringComparer.Ordinal);
        var availableGlobalLinks = new HashSet<string>(
            (panels ?? Enumerable.Empty<PanelDefinition>())
            .Where(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !IsEntityPanel(panel) &&
                !string.IsNullOrWhiteSpace(panel.Id))
            .Select(panel => panel.Id.Trim()),
            StringComparer.Ordinal);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var primaryPanelId = rule.PanelId?.Trim() ?? "";
        if (availablePrimaryPanels.Contains(primaryPanelId) &&
            seen.Add(primaryPanelId))
        {
            result.Add(primaryPanelId);
        }
        foreach (var panelId in rule.LinkedPanelIds ??
                     Enumerable.Empty<string>())
        {
            var normalized = panelId?.Trim() ?? "";
            if (availableGlobalLinks.Contains(normalized) &&
                seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }
        return result;
    }

    public static bool IsRuleAssignedToPanel(
        AlarmRuleDefinition rule,
        PanelDefinition panel,
        IEnumerable<PanelDefinition> panels)
    {
        return panel != null &&
               GetRulePanelIds(rule, panels).Contains(
                   panel.Id,
                   StringComparer.Ordinal);
    }

    public static bool IsCustomMemoryEligibleForPanel(
        AlarmMemoryDefinition memory,
        PanelDefinition panel,
        IEnumerable<AlarmRuleDefinition> rules,
        IEnumerable<PanelDefinition> panels)
    {
        if (memory == null || panel == null)
        {
            return false;
        }
        if (TryGetRuleId(memory.SlotId, out var ruleId) ||
            TryGetRuleId(memory.Key, out ruleId))
        {
            var rule = (rules ?? Enumerable.Empty<AlarmRuleDefinition>())
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.Id,
                        ruleId,
                        StringComparison.Ordinal));
            if (rule != null)
            {
                return IsRuleAssignedToPanel(rule, panel, panels);
            }
        }
        return string.Equals(
            memory.PanelId,
            panel.Id,
            StringComparison.Ordinal);
    }

    public static bool TryGetRuleId(string alarmId, out string ruleId)
    {
        ruleId = "";
        alarmId = alarmId?.Trim() ?? "";
        if (!alarmId.StartsWith(RulePrefix, StringComparison.Ordinal) ||
            alarmId.Length <= RulePrefix.Length)
        {
            return false;
        }
        ruleId = alarmId.Substring(RulePrefix.Length).Trim();
        return ruleId.Length > 0;
    }
}
