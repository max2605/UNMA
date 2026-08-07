using System;
using System.Collections.Generic;
using System.Linq;

namespace UNMA.Domain;

public static class CustomRuleLifecyclePolicy
{
    public static bool ShouldDeleteForRemovedEntity(
        bool removedEntityIsDestroyed,
        bool hasLiveReplacement)
    {
        return removedEntityIsDestroyed && !hasLiveReplacement;
    }

    public static bool IsConfirmedMissingStaticEntity(
        int consecutiveMissingObservations)
    {
        return consecutiveMissingObservations >= 2;
    }

    public static IReadOnlyList<string> FindRulesReferencingEntities(
        IEnumerable<AlarmRuleDefinition> rules,
        IEnumerable<int> entityIds)
    {
        var removedEntityIds = new HashSet<int>(entityIds ??
            Enumerable.Empty<int>());
        if (removedEntityIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        return (rules ?? Enumerable.Empty<AlarmRuleDefinition>())
            .Where(rule =>
                rule != null &&
                !string.IsNullOrWhiteSpace(rule.Id) &&
                (rule.Conditions ?? new List<ConditionDefinition>()).Any(
                    condition =>
                        condition != null &&
                        removedEntityIds.Contains(condition.EntityId)))
            .Select(rule => rule.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
