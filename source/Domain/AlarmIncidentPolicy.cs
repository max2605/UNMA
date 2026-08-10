using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UNMA.Domain;

public enum AlarmStormLevel
{
    Normal = 0,
    Elevated = 1,
    Storm = 2,
    Severe = 3,
}

/// <summary>
/// Immutable input captured from one active, already scoped alarm tile.
/// RaisedAtTicks is expressed in simulation ticks.
/// </summary>
public sealed class AlarmIncidentActiveSample
{
    public string Key { get; }
    public string StableAlarmId { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Source { get; }
    public string PanelId { get; }
    public string SlotId { get; }
    public int EntityId { get; }
    public string EntityPrototypeId { get; }
    public string EntityTitle { get; }
    public AlarmSeverity Severity { get; }
    public long Sequence { get; }
    public double RaisedAtTicks { get; }
    public bool IsAcknowledged { get; }

    public AlarmIncidentActiveSample(
        string key,
        string stableAlarmId,
        string name,
        string detail,
        string source,
        string panelId,
        string slotId,
        int entityId,
        string entityPrototypeId,
        string entityTitle,
        AlarmSeverity severity,
        long sequence,
        double raisedAtTicks,
        bool isAcknowledged)
    {
        Key = key;
        StableAlarmId = stableAlarmId;
        Name = name;
        Detail = detail;
        Source = source;
        PanelId = panelId;
        SlotId = slotId;
        EntityId = entityId;
        EntityPrototypeId = entityPrototypeId;
        EntityTitle = entityTitle;
        Severity = severity;
        Sequence = sequence;
        RaisedAtTicks = raisedAtTicks;
        IsAcknowledged = isAcknowledged;
    }
}

/// <summary>
/// Immutable occurrence signal used only for bounded global alarm pressure.
/// It does not mutate or acknowledge the corresponding alarm occurrence.
/// </summary>
public sealed class AlarmOccurrenceSignal
{
    public string Key { get; }
    public AlarmSeverity Severity { get; }
    public long Sequence { get; }
    public double RaisedAtTicks { get; }

    public AlarmOccurrenceSignal(
        string key,
        AlarmSeverity severity,
        long sequence,
        double raisedAtTicks)
    {
        Key = key;
        Severity = severity;
        Sequence = sequence;
        RaisedAtTicks = raisedAtTicks;
    }
}

/// <summary>
/// Normalized immutable member of a derived temporal incident.
/// </summary>
public sealed class AlarmIncidentMember
{
    public string Key { get; }
    public string StableAlarmId { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Source { get; }
    public string PanelId { get; }
    public string SlotId { get; }
    public int EntityId { get; }
    public string EntityPrototypeId { get; }
    public string EntityTitle { get; }
    public AlarmSeverity Severity { get; }
    public long Sequence { get; }
    public double RaisedAtTicks { get; }
    public bool IsAcknowledged { get; }
    public bool RequiresAcknowledgement => !IsAcknowledged;

    internal AlarmIncidentMember(
        string key,
        string stableAlarmId,
        string name,
        string detail,
        string source,
        string panelId,
        string slotId,
        int entityId,
        string entityPrototypeId,
        string entityTitle,
        AlarmSeverity severity,
        long sequence,
        double raisedAtTicks,
        bool isAcknowledged)
    {
        Key = key;
        StableAlarmId = stableAlarmId;
        Name = name;
        Detail = detail;
        Source = source;
        PanelId = panelId;
        SlotId = slotId;
        EntityId = entityId;
        EntityPrototypeId = entityPrototypeId;
        EntityTitle = entityTitle;
        Severity = severity;
        Sequence = sequence;
        RaisedAtTicks = raisedAtTicks;
        IsAcknowledged = isAcknowledged;
    }
}

/// <summary>
/// A deterministic temporal burst of active alarms. FirstSignal means only
/// the earliest observed member; no causal or root-cause claim is made.
/// </summary>
public sealed class AlarmIncident
{
    public string IncidentId { get; }
    public AlarmSeverity Severity { get; }
    public int MemberCount => Members.Count;
    public int UnacknowledgedCount { get; }
    public double FirstRaisedAtTicks { get; }
    public double LastRaisedAtTicks { get; }
    public AlarmIncidentMember FirstSignal { get; }
    public IReadOnlyList<AlarmIncidentMember> Members { get; }

    internal AlarmIncident(
        string incidentId,
        AlarmSeverity severity,
        int unacknowledgedCount,
        double firstRaisedAtTicks,
        double lastRaisedAtTicks,
        AlarmIncidentMember firstSignal,
        IReadOnlyList<AlarmIncidentMember> members)
    {
        IncidentId = incidentId;
        Severity = severity;
        UnacknowledgedCount = unacknowledgedCount;
        FirstRaisedAtTicks = firstRaisedAtTicks;
        LastRaisedAtTicks = lastRaisedAtTicks;
        FirstSignal = firstSignal;
        Members = members;
    }
}

/// <summary>
/// Immutable, transient Incident Lens result. It contains no persisted state
/// and derives all values from the supplied snapshots.
/// </summary>
public sealed class AlarmIncidentSnapshot
{
    public bool IsTimeValid { get; }
    public double CurrentGameTick { get; }
    public int BurstGapTicks { get; }
    public int PressureWindowTicks { get; }
    public int ActiveAlarmCount { get; }
    public int ActiveUnacknowledgedCount { get; }
    public int RecentOccurrenceCount { get; }
    public int RecentDistinctAlarmCount { get; }
    public int AlarmPressure { get; }
    public AlarmStormLevel StormLevel { get; }
    public IReadOnlyList<AlarmIncident> Incidents { get; }

    internal AlarmIncidentSnapshot(
        bool isTimeValid,
        double currentGameTick,
        int burstGapTicks,
        int pressureWindowTicks,
        int activeAlarmCount,
        int activeUnacknowledgedCount,
        int recentOccurrenceCount,
        int recentDistinctAlarmCount,
        int alarmPressure,
        AlarmStormLevel stormLevel,
        IReadOnlyList<AlarmIncident> incidents)
    {
        IsTimeValid = isTimeValid;
        CurrentGameTick = currentGameTick;
        BurstGapTicks = burstGapTicks;
        PressureWindowTicks = pressureWindowTicks;
        ActiveAlarmCount = activeAlarmCount;
        ActiveUnacknowledgedCount = activeUnacknowledgedCount;
        RecentOccurrenceCount = recentOccurrenceCount;
        RecentDistinctAlarmCount = recentDistinctAlarmCount;
        AlarmPressure = alarmPressure;
        StormLevel = stormLevel;
        Incidents = incidents;
    }
}

/// <summary>
/// Pure derivation of temporal alarm incidents and bounded global alarm
/// pressure. Pressure weights are NOTICE=1, WARNING=2, CRITICAL=4 and
/// EMERGENCY=8. Thresholds are deliberately conservative and public so UI
/// wording never has to infer hidden semantics.
/// </summary>
public static class AlarmIncidentPolicy
{
    public const int DefaultBurstGapTicks =
        GameTimeWindowPolicy.SimTicksPerDay * 2;
    public const int DefaultPressureWindowTicks =
        GameTimeWindowPolicy.SimTicksPerDay * 10;
    public const int MaximumActiveSamples = 4096;
    public const int MaximumOccurrenceSignals = 8192;
    public const int MaximumActiveInputScan = MaximumActiveSamples * 2;
    public const int MaximumOccurrenceInputScan =
        MaximumOccurrenceSignals * 2;
    public const int MaximumTextLength = 1024;
    public const int ElevatedPressureThreshold = 8;
    public const int StormPressureThreshold = 16;
    public const int SeverePressureThreshold = 32;

    public static AlarmIncidentSnapshot Analyze(
        IReadOnlyList<AlarmIncidentActiveSample> activeSamples,
        IReadOnlyList<AlarmOccurrenceSignal> recentSignals,
        double currentGameTick,
        int burstGapTicks = DefaultBurstGapTicks,
        int pressureWindowTicks = DefaultPressureWindowTicks)
    {
        burstGapTicks = ClampWindow(burstGapTicks, DefaultBurstGapTicks);
        pressureWindowTicks = ClampWindow(
            pressureWindowTicks,
            DefaultPressureWindowTicks);
        if (!IsFinite(currentGameTick) || currentGameTick < 0d)
        {
            return CreateEmpty(
                false,
                0d,
                burstGapTicks,
                pressureWindowTicks);
        }

        var members = NormalizeActiveSamples(activeSamples, currentGameTick);
        var incidents = CreateIncidents(members, burstGapTicks);
        var signals = NormalizeOccurrenceSignals(
            recentSignals,
            currentGameTick,
            pressureWindowTicks);
        var pressure = CalculatePressure(signals);
        var distinctAlarmCount = signals
            .Select(signal => signal.Key)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new AlarmIncidentSnapshot(
            true,
            currentGameTick,
            burstGapTicks,
            pressureWindowTicks,
            members.Count,
            members.Count(member => member.RequiresAcknowledgement),
            signals.Count,
            distinctAlarmCount,
            pressure,
            ResolveStormLevel(pressure),
            ReadOnly(incidents));
    }

    public static AlarmStormLevel ResolveStormLevel(int alarmPressure)
    {
        alarmPressure = Math.Max(0, alarmPressure);
        if (alarmPressure >= SeverePressureThreshold)
        {
            return AlarmStormLevel.Severe;
        }
        if (alarmPressure >= StormPressureThreshold)
        {
            return AlarmStormLevel.Storm;
        }
        if (alarmPressure >= ElevatedPressureThreshold)
        {
            return AlarmStormLevel.Elevated;
        }
        return AlarmStormLevel.Normal;
    }

    private static List<AlarmIncidentMember> NormalizeActiveSamples(
        IReadOnlyList<AlarmIncidentActiveSample> samples,
        double currentGameTick)
    {
        var byStableId = new Dictionary<string, AlarmIncidentMember>(
            StringComparer.Ordinal);
        if (samples == null)
        {
            return new List<AlarmIncidentMember>();
        }

        // The capture boundary is intentionally hard: hostile callers cannot
        // force unbounded allocations or work. Runtime captures are already
        // smaller than this ceiling. Invalid/future entries inside the scanned
        // prefix are dropped; entries beyond the prefix are never inspected.
        var scanCount = Math.Min(samples.Count, MaximumActiveInputScan);
        for (var index = 0; index < scanCount; index++)
        {
            if (!TryNormalize(samples[index], currentGameTick, out var member))
            {
                continue;
            }
            if (byStableId.TryGetValue(member.StableAlarmId, out var existing))
            {
                byStableId[member.StableAlarmId] = MergeDuplicate(
                    existing,
                    member);
                continue;
            }
            byStableId.Add(member.StableAlarmId, member);
        }

        return byStableId.Values
            .OrderByDescending(member => member.RaisedAtTicks)
            .ThenByDescending(member => member.Severity)
            .ThenBy(member => member.StableAlarmId, StringComparer.Ordinal)
            .ThenByDescending(member => member.Sequence)
            .Take(MaximumActiveSamples)
            .OrderBy(member => member.RaisedAtTicks)
            .ThenByDescending(member => member.Severity)
            .ThenBy(member => member.StableAlarmId, StringComparer.Ordinal)
            .ThenBy(member => member.Key, StringComparer.Ordinal)
            .ThenBy(member => member.Sequence)
            .ToList();
    }

    private static bool TryNormalize(
        AlarmIncidentActiveSample sample,
        double currentGameTick,
        out AlarmIncidentMember member)
    {
        member = null;
        if (sample == null ||
            !IsFinite(sample.RaisedAtTicks) ||
            sample.RaisedAtTicks < 0d ||
            sample.RaisedAtTicks > currentGameTick)
        {
            return false;
        }
        var stableId = FirstNonEmpty(
            sample.StableAlarmId,
            sample.SlotId,
            sample.Key);
        if (stableId.Length == 0)
        {
            return false;
        }
        var key = NormalizeText(sample.Key);
        if (key.Length == 0)
        {
            key = stableId;
        }
        member = new AlarmIncidentMember(
            key,
            stableId,
            NormalizeText(sample.Name),
            NormalizeText(sample.Detail),
            NormalizeText(sample.Source),
            NormalizeText(sample.PanelId),
            NormalizeText(sample.SlotId),
            sample.EntityId > 0 ? sample.EntityId : -1,
            NormalizeText(sample.EntityPrototypeId),
            NormalizeText(sample.EntityTitle),
            NormalizeSeverity(sample.Severity),
            Math.Max(0L, sample.Sequence),
            sample.RaisedAtTicks,
            sample.IsAcknowledged);
        return true;
    }

    private static AlarmIncidentMember MergeDuplicate(
        AlarmIncidentMember left,
        AlarmIncidentMember right)
    {
        var first = CompareChronology(left, right) <= 0 ? left : right;
        return new AlarmIncidentMember(
            first.Key,
            first.StableAlarmId,
            first.Name,
            first.Detail,
            first.Source,
            first.PanelId,
            first.SlotId,
            first.EntityId,
            first.EntityPrototypeId,
            first.EntityTitle,
            (AlarmSeverity)Math.Max((int)left.Severity, (int)right.Severity),
            first.Sequence,
            Math.Min(left.RaisedAtTicks, right.RaisedAtTicks),
            left.IsAcknowledged && right.IsAcknowledged);
    }

    private static List<AlarmIncident> CreateIncidents(
        IReadOnlyList<AlarmIncidentMember> members,
        int burstGapTicks)
    {
        var incidents = new List<AlarmIncident>();
        var cluster = new List<AlarmIncidentMember>();
        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            if (cluster.Count > 0 &&
                member.RaisedAtTicks -
                cluster[cluster.Count - 1].RaisedAtTicks > burstGapTicks)
            {
                incidents.Add(CreateIncident(cluster));
                cluster.Clear();
            }
            cluster.Add(member);
        }
        if (cluster.Count > 0)
        {
            incidents.Add(CreateIncident(cluster));
        }
        return incidents
            .OrderByDescending(incident => incident.FirstRaisedAtTicks)
            .ThenByDescending(incident => incident.Severity)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .ToList();
    }

    private static AlarmIncident CreateIncident(
        IReadOnlyList<AlarmIncidentMember> cluster)
    {
        var members = cluster.ToArray();
        var first = members[0];
        var id = first.StableAlarmId + ":" +
                 first.Sequence.ToString(
                     System.Globalization.CultureInfo.InvariantCulture);
        return new AlarmIncident(
            id,
            members.Max(member => member.Severity),
            members.Count(member => member.RequiresAcknowledgement),
            first.RaisedAtTicks,
            members[members.Length - 1].RaisedAtTicks,
            first,
            Array.AsReadOnly(members));
    }

    private static List<AlarmOccurrenceSignal> NormalizeOccurrenceSignals(
        IReadOnlyList<AlarmOccurrenceSignal> signals,
        double currentGameTick,
        int pressureWindowTicks)
    {
        var byOccurrence = new Dictionary<string, AlarmOccurrenceSignal>(
            StringComparer.Ordinal);
        if (signals == null)
        {
            return new List<AlarmOccurrenceSignal>();
        }
        var windowStart = Math.Max(0d, currentGameTick - pressureWindowTicks);
        var scanCount = Math.Min(signals.Count, MaximumOccurrenceInputScan);
        for (var index = 0; index < scanCount; index++)
        {
            var source = signals[index];
            if (source == null ||
                !IsFinite(source.RaisedAtTicks) ||
                source.RaisedAtTicks < windowStart ||
                source.RaisedAtTicks > currentGameTick)
            {
                continue;
            }
            var key = NormalizeText(source.Key);
            if (key.Length == 0)
            {
                continue;
            }
            var normalized = new AlarmOccurrenceSignal(
                key,
                NormalizeSeverity(source.Severity),
                Math.Max(0L, source.Sequence),
                source.RaisedAtTicks);
            var identity = key + "\u001f" + normalized.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (byOccurrence.TryGetValue(identity, out var existing))
            {
                byOccurrence[identity] = PreferOccurrence(existing, normalized);
                continue;
            }
            byOccurrence.Add(identity, normalized);
        }
        return byOccurrence.Values
            .OrderByDescending(signal => signal.RaisedAtTicks)
            .ThenByDescending(signal => signal.Severity)
            .ThenBy(signal => signal.Key, StringComparer.Ordinal)
            .ThenByDescending(signal => signal.Sequence)
            .Take(MaximumOccurrenceSignals)
            .OrderBy(signal => signal.RaisedAtTicks)
            .ThenByDescending(signal => signal.Severity)
            .ThenBy(signal => signal.Key, StringComparer.Ordinal)
            .ThenBy(signal => signal.Sequence)
            .ToList();
    }

    private static AlarmOccurrenceSignal PreferOccurrence(
        AlarmOccurrenceSignal left,
        AlarmOccurrenceSignal right)
    {
        var raisedAt = Math.Min(left.RaisedAtTicks, right.RaisedAtTicks);
        var severity = (AlarmSeverity)Math.Max(
            (int)left.Severity,
            (int)right.Severity);
        return new AlarmOccurrenceSignal(
            left.Key,
            severity,
            left.Sequence,
            raisedAt);
    }

    private static int CalculatePressure(
        IReadOnlyList<AlarmOccurrenceSignal> signals)
    {
        long pressure = 0;
        for (var index = 0; index < signals.Count; index++)
        {
            pressure += SeverityWeight(signals[index].Severity);
        }
        return (int)Math.Min(int.MaxValue, pressure);
    }

    private static int SeverityWeight(AlarmSeverity severity)
    {
        return severity switch
        {
            AlarmSeverity.Emergency => 8,
            AlarmSeverity.Critical => 4,
            AlarmSeverity.Warning => 2,
            _ => 1,
        };
    }

    private static int CompareChronology(
        AlarmIncidentMember left,
        AlarmIncidentMember right)
    {
        var comparison = left.RaisedAtTicks.CompareTo(right.RaisedAtTicks);
        if (comparison != 0)
        {
            return comparison;
        }
        comparison = ((int)right.Severity).CompareTo((int)left.Severity);
        if (comparison != 0)
        {
            return comparison;
        }
        comparison = string.Compare(
            left.StableAlarmId,
            right.StableAlarmId,
            StringComparison.Ordinal);
        if (comparison != 0)
        {
            return comparison;
        }
        comparison = string.Compare(left.Key, right.Key, StringComparison.Ordinal);
        return comparison != 0
            ? comparison
            : left.Sequence.CompareTo(right.Sequence);
    }

    private static AlarmSeverity NormalizeSeverity(AlarmSeverity severity)
    {
        return (AlarmSeverity)Math.Max(
            (int)AlarmSeverity.Notice,
            Math.Min((int)AlarmSeverity.Emergency, (int)severity));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var normalized = NormalizeText(values[index]);
            if (normalized.Length > 0)
            {
                return normalized;
            }
        }
        return "";
    }

    private static string NormalizeText(string value)
    {
        value = value?.Trim() ?? "";
        return value.Length <= MaximumTextLength
            ? value
            : value.Substring(0, MaximumTextLength);
    }

    private static int ClampWindow(int requested, int fallback)
    {
        if (requested <= 0)
        {
            requested = fallback;
        }
        return Math.Max(
            1,
            Math.Min(GameTimeWindowPolicy.MaximumWindowTicks, requested));
    }

    private static AlarmIncidentSnapshot CreateEmpty(
        bool isTimeValid,
        double currentGameTick,
        int burstGapTicks,
        int pressureWindowTicks)
    {
        return new AlarmIncidentSnapshot(
            isTimeValid,
            currentGameTick,
            burstGapTicks,
            pressureWindowTicks,
            0,
            0,
            0,
            0,
            0,
            AlarmStormLevel.Normal,
            ReadOnly(new List<AlarmIncident>()));
    }

    private static IReadOnlyList<T> ReadOnly<T>(IList<T> source)
    {
        return new ReadOnlyCollection<T>(source ?? new List<T>());
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
