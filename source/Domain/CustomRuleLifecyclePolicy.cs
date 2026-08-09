using System;
using System.Collections.Generic;
using System.Linq;

namespace UNMA.Domain;

public static class CustomRuleLifecyclePolicy
{
    public static readonly TimeSpan StaticEntityMissingGracePeriod =
        TimeSpan.FromSeconds(10);

    public static bool ShouldDeleteForRemovedEntity(
        bool removedEntityIsDestroyed,
        bool hasLiveReplacement)
    {
        return removedEntityIsDestroyed && !hasLiveReplacement;
    }

    public static bool IsConfirmedMissingStaticEntity(
        long firstMissingTimestamp,
        long currentTimestamp,
        long timestampFrequency)
    {
        if (timestampFrequency <= 0 ||
            firstMissingTimestamp < 0 ||
            currentTimestamp < firstMissingTimestamp)
        {
            return false;
        }

        var elapsedSeconds =
            (currentTimestamp - firstMissingTimestamp) /
            (double)timestampFrequency;
        return elapsedSeconds >= StaticEntityMissingGracePeriod.TotalSeconds;
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

public sealed class StaticEntityMissingGraceTracker
{
    private readonly Dictionary<int, long> m_firstMissingTimestamps = new();

    public bool ObserveMissing(
        int entityId,
        long currentTimestamp,
        long timestampFrequency)
    {
        if (entityId < 0 || currentTimestamp < 0 || timestampFrequency <= 0)
        {
            return false;
        }

        if (!m_firstMissingTimestamps.TryGetValue(
                entityId,
                out var firstMissingTimestamp) ||
            currentTimestamp < firstMissingTimestamp)
        {
            m_firstMissingTimestamps[entityId] = currentTimestamp;
            return false;
        }

        return CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
            firstMissingTimestamp,
            currentTimestamp,
            timestampFrequency);
    }

    public void ObserveLive(int entityId)
    {
        m_firstMissingTimestamps.Remove(entityId);
    }

    public void Forget(int entityId)
    {
        m_firstMissingTimestamps.Remove(entityId);
    }

    public void RetainOnly(IEnumerable<int> watchedEntityIds)
    {
        var watched = new HashSet<int>(watchedEntityIds ??
            Enumerable.Empty<int>());
        foreach (var staleEntityId in m_firstMissingTimestamps.Keys
                     .Where(entityId => !watched.Contains(entityId))
                     .ToArray())
        {
            m_firstMissingTimestamps.Remove(staleEntityId);
        }
    }
}
