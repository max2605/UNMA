using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UNMA.Domain;

/// <summary>
/// Immutable input describing one concrete alarm occurrence considered for an
/// operator-silence reminder. EffectiveBehavior is resolved before the sample
/// reaches this pure policy.
/// </summary>
public sealed class OperatorSilenceReminderSample
{
    public string StableGroupId { get; }
    public string HumanLabel { get; }
    public bool IsActive { get; }
    public bool IsOperatorSilenced { get; }
    public VanillaNotificationBehavior EffectiveBehavior { get; }
    public string SoundId { get; }
    public long OperatorSilencedAtGameTick { get; }

    public OperatorSilenceReminderSample(
        string stableGroupId,
        string humanLabel,
        bool isActive,
        bool isOperatorSilenced,
        VanillaNotificationBehavior effectiveBehavior,
        string soundId,
        long operatorSilencedAtGameTick)
    {
        StableGroupId = stableGroupId;
        HumanLabel = humanLabel;
        IsActive = isActive;
        IsOperatorSilenced = isOperatorSilenced;
        EffectiveBehavior = effectiveBehavior;
        SoundId = soundId;
        OperatorSilencedAtGameTick = operatorSilencedAtGameTick;
    }
}

/// <summary>
/// Deterministic aggregate for one stable alarm group.
/// </summary>
public sealed class OperatorSilenceReminderGroup
{
    public string GroupId { get; }
    public string Label { get; }
    public int Count { get; }

    public string StableGroupId => GroupId;
    public string HumanLabel => Label;
    public int AlarmCount => Count;

    internal OperatorSilenceReminderGroup(
        string groupId,
        string label,
        int count)
    {
        GroupId = groupId;
        Label = label;
        Count = count;
    }
}

/// <summary>
/// Immutable reminder projection. Scheduling remains a runtime concern; this
/// snapshot only identifies alarm occurrences old enough to be reminded.
/// </summary>
public sealed class OperatorSilenceReminderSnapshot
{
    public long CurrentGameTick { get; }
    public long MinimumAgeTicks { get; }
    public int GroupCount => Groups.Count;
    public int AlarmCount { get; }
    public IReadOnlyList<OperatorSilenceReminderGroup> Groups { get; }

    internal OperatorSilenceReminderSnapshot(
        long currentGameTick,
        long minimumAgeTicks,
        int alarmCount,
        IReadOnlyList<OperatorSilenceReminderGroup> groups)
    {
        CurrentGameTick = currentGameTick;
        MinimumAgeTicks = minimumAgeTicks;
        AlarmCount = alarmCount;
        Groups = groups;
    }
}

/// <summary>
/// Pure monthly-reminder projection for active alarms explicitly silenced by
/// the operator. Configuration-silent and soundless alarms are never included.
/// </summary>
public static class OperatorSilenceReminderPolicy
{
    public const string UnknownGroupId = "operator-silenced:unknown";
    public const string UnknownLabel = "UNKNOWN ALARM";

    public static OperatorSilenceReminderSnapshot Build(
        IEnumerable<OperatorSilenceReminderSample> samples,
        long currentGameTick,
        long minimumAgeTicks)
    {
        if (currentGameTick < 0 || minimumAgeTicks <= 0)
        {
            return Empty(currentGameTick, minimumAgeTicks);
        }

        var eligible = (samples ??
                Enumerable.Empty<OperatorSilenceReminderSample>())
            .Where(sample => IsEligible(
                sample,
                currentGameTick,
                minimumAgeTicks))
            .Select(sample => new
            {
                GroupId = NormalizeOrFallback(
                    sample.StableGroupId,
                    UnknownGroupId),
                Label = NormalizeOrFallback(
                    sample.HumanLabel,
                    UnknownLabel),
            })
            .ToArray();

        var groups = eligible
            .GroupBy(sample => sample.GroupId, StringComparer.Ordinal)
            .Select(group => new OperatorSilenceReminderGroup(
                group.Key,
                group.Select(sample => sample.Label)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(label => label, StringComparer.Ordinal)
                    .First(),
                group.Count()))
            .OrderBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Label, StringComparer.Ordinal)
            .ThenBy(group => group.GroupId, StringComparer.Ordinal)
            .ToList();

        return new OperatorSilenceReminderSnapshot(
            currentGameTick,
            minimumAgeTicks,
            eligible.Length,
            new ReadOnlyCollection<OperatorSilenceReminderGroup>(groups));
    }

    private static bool IsEligible(
        OperatorSilenceReminderSample sample,
        long currentGameTick,
        long minimumAgeTicks)
    {
        if (sample == null ||
            !sample.IsActive ||
            !sample.IsOperatorSilenced ||
            sample.EffectiveBehavior != VanillaNotificationBehavior.Normal ||
            string.Equals(
                sample.SoundId?.Trim(),
                "none",
                StringComparison.OrdinalIgnoreCase) ||
            sample.OperatorSilencedAtGameTick < 0 ||
            currentGameTick < sample.OperatorSilencedAtGameTick)
        {
            return false;
        }

        return currentGameTick - sample.OperatorSilencedAtGameTick >=
               minimumAgeTicks;
    }

    private static string NormalizeOrFallback(string value, string fallback)
    {
        var normalized = value?.Trim() ?? "";
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static OperatorSilenceReminderSnapshot Empty(
        long currentGameTick,
        long minimumAgeTicks)
    {
        return new OperatorSilenceReminderSnapshot(
            currentGameTick,
            minimumAgeTicks,
            0,
            new ReadOnlyCollection<OperatorSilenceReminderGroup>(
                new List<OperatorSilenceReminderGroup>()));
    }
}
