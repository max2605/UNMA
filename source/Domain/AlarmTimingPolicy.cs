using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UNMA.Domain;

public enum AlarmTimingTransition
{
    None = 0,
    Activated = 1,
    Cleared = 2,
}

/// <summary>
/// Tick-based alarm timing configuration. A default value deliberately has
/// the same immediate behaviour as alarms saved before timing was introduced.
/// </summary>
public readonly struct AlarmTimingSettings
{
    public int ActivationDelayTicks { get; }
    public int ResetDelayTicks { get; }
    public int MinimumActiveTicks { get; }
    public double Hysteresis { get; }

    public AlarmTimingSettings(
        int activationDelayTicks,
        int resetDelayTicks,
        int minimumActiveTicks,
        double hysteresis)
    {
        ActivationDelayTicks = activationDelayTicks;
        ResetDelayTicks = resetDelayTicks;
        MinimumActiveTicks = minimumActiveTicks;
        Hysteresis = hysteresis;
    }
}

/// <summary>
/// Immutable runtime memory for one timed alarm. Tick values refer only to
/// COI's game calendar; wall-clock and frame time must never be stored here.
/// </summary>
public readonly struct AlarmTimingState
{
    public const long NoTick = -1;

    public bool IsInitialized { get; }
    public bool IsActive { get; }
    public long ActivationPendingSinceTick { get; }
    public long ActiveSinceTick { get; }
    public long ResetPendingSinceTick { get; }
    public long LastObservedTick { get; }

    public static AlarmTimingState Inactive => new(
        false,
        NoTick,
        NoTick,
        NoTick,
        NoTick);

    public AlarmTimingState(
        bool isActive,
        long activationPendingSinceTick,
        long activeSinceTick,
        long resetPendingSinceTick,
        long lastObservedTick)
    {
        IsInitialized = true;
        IsActive = isActive;
        ActivationPendingSinceTick = activationPendingSinceTick;
        ActiveSinceTick = activeSinceTick;
        ResetPendingSinceTick = resetPendingSinceTick;
        LastObservedTick = lastObservedTick;
    }

    public static AlarmTimingState ActiveAt(long currentGameTick)
    {
        currentGameTick = Math.Max(0, currentGameTick);
        return new AlarmTimingState(
            true,
            NoTick,
            currentGameTick,
            NoTick,
            currentGameTick);
    }
}

public readonly struct AlarmTimingEvaluation
{
    public AlarmTimingState State { get; }
    public AlarmTimingTransition Transition { get; }
    public bool IsActive => State.IsActive;

    public AlarmTimingEvaluation(
        AlarmTimingState state,
        AlarmTimingTransition transition)
    {
        State = state;
        Transition = transition;
    }
}

/// <summary>
/// Pure Schmitt-trigger and debounce policy for alarms. Reset delay and
/// minimum active time run concurrently: an alarm clears only after both
/// limits have elapsed.
/// </summary>
public static class AlarmTimingPolicy
{
    public const int LegacyActivationDelayTicks = 0;
    public const int LegacyResetDelayTicks = 0;
    public const int LegacyMinimumActiveTicks = 0;
    public const double LegacyHysteresis = 0d;
    public const int MaximumTimingTicks =
        GameTimeWindowPolicy.MaximumWindowTicks;

    private const double EqualityTolerance = 0.000001d;

    /// <summary>
    /// Defaults to apply when migrating a definition which predates timing.
    /// They preserve the former immediate activation and clearing semantics.
    /// </summary>
    public static AlarmTimingSettings LegacyMigrationDefaults => new(
        LegacyActivationDelayTicks,
        LegacyResetDelayTicks,
        LegacyMinimumActiveTicks,
        LegacyHysteresis);

    public static AlarmTimingSettings Normalize(
        AlarmTimingSettings settings)
    {
        return new AlarmTimingSettings(
            NormalizeTicks(settings.ActivationDelayTicks),
            NormalizeTicks(settings.ResetDelayTicks),
            NormalizeTicks(settings.MinimumActiveTicks),
            IsFinite(settings.Hysteresis) && settings.Hysteresis > 0d
                ? settings.Hysteresis
                : 0d);
    }

    public static bool HasPersistentStateChanged(
        AlarmTimingState previous,
        AlarmTimingState current)
    {
        return previous.IsInitialized != current.IsInitialized ||
               previous.IsActive != current.IsActive ||
               previous.ActivationPendingSinceTick !=
               current.ActivationPendingSinceTick ||
               previous.ActiveSinceTick != current.ActiveSinceTick ||
               previous.ResetPendingSinceTick !=
               current.ResetPendingSinceTick;
    }

    /// <summary>
    /// Keeps an already annunciated alarm active across a semantic definition
    /// edit without carrying a pending clear from the old definition. Inactive
    /// and activation-pending state deliberately restart under the new
    /// semantics.
    /// </summary>
    public static AlarmTimingState PreserveActiveForDefinitionChange(
        AlarmTimingState state,
        long currentGameTick)
    {
        if (!state.IsInitialized || !state.IsActive)
        {
            return default;
        }

        currentGameTick = Math.Max(0, currentGameTick);
        var activeSinceTick = state.ActiveSinceTick >= 0 &&
                              state.ActiveSinceTick <= currentGameTick
            ? state.ActiveSinceTick
            : currentGameTick;
        return new AlarmTimingState(
            true,
            AlarmTimingState.NoTick,
            activeSinceTick,
            AlarmTimingState.NoTick,
            currentGameTick);
    }

    public static Dictionary<int, bool> CreateActiveConditionLatches(
        int conditionCount)
    {
        var latches = new Dictionary<int, bool>();
        for (var index = 0; index < Math.Max(0, conditionCount); index++)
        {
            latches[index] = true;
        }
        return latches;
    }

    /// <summary>
    /// Applies hysteresis to a numeric comparison. Directional operators keep
    /// their configured activation boundary and move only the release
    /// boundary. Equal/NotEqual use an inner equality band and a wider outer
    /// band so neither operator requires bit-exact equality to reset.
    /// </summary>
    public static bool CompareWithHysteresis(
        double actual,
        ComparisonOperator comparison,
        double threshold,
        double hysteresis,
        bool isCurrentlyActive)
    {
        if (!IsFinite(actual) || !IsFinite(threshold))
        {
            return false;
        }

        hysteresis = IsFinite(hysteresis) && hysteresis > 0d
            ? hysteresis
            : 0d;
        if (hysteresis <= 0d)
        {
            return AlarmEvaluation.Compare(actual, comparison, threshold);
        }

        var absoluteDifference = AbsoluteDifference(actual, threshold);
        var outerEqualityBand = AddSaturating(
            EqualityTolerance,
            hysteresis);
        if (!isCurrentlyActive)
        {
            // NotEqual activates outside the outer band and clears inside the
            // inner band. This is the inverse Schmitt trigger of Equal.
            return comparison == ComparisonOperator.NotEqual
                ? absoluteDifference > outerEqualityBand
                : AlarmEvaluation.Compare(actual, comparison, threshold);
        }

        return comparison switch
        {
            ComparisonOperator.Less =>
                actual < AddSaturating(threshold, hysteresis),
            ComparisonOperator.LessOrEqual =>
                actual <= AddSaturating(threshold, hysteresis),
            ComparisonOperator.Equal =>
                absoluteDifference <= outerEqualityBand,
            ComparisonOperator.NotEqual =>
                absoluteDifference > EqualityTolerance,
            ComparisonOperator.GreaterOrEqual =>
                actual >= SubtractSaturating(threshold, hysteresis),
            ComparisonOperator.Greater =>
                actual > SubtractSaturating(threshold, hysteresis),
            _ => false,
        };
    }

    internal static bool EvaluateConditionLatch(
        double actual,
        ComparisonOperator comparison,
        double threshold,
        double hysteresis,
        bool hasPreviousLatch,
        bool previousLatch)
    {
        return CompareWithHysteresis(
            actual,
            comparison,
            threshold,
            hysteresis,
            hasPreviousLatch && previousLatch);
    }

    /// <summary>
    /// Advances timing for a condition which the caller has already
    /// evaluated and, for compound rules, combined with AND/OR logic.
    /// </summary>
    public static AlarmTimingEvaluation Advance(
        AlarmTimingState state,
        bool conditionMet,
        long currentGameTick,
        AlarmTimingSettings settings)
    {
        return AdvanceNormalized(
            state,
            conditionMet,
            Math.Max(0, currentGameTick),
            Normalize(settings));
    }

    /// <summary>
    /// Convenience path for a single numeric threshold. Compound rules should
    /// call <see cref="CompareWithHysteresis"/> per condition, combine the
    /// results, and pass the combined value to <see cref="Advance"/>.
    /// </summary>
    public static AlarmTimingEvaluation AdvanceComparison(
        AlarmTimingState state,
        double actual,
        ComparisonOperator comparison,
        double threshold,
        long currentGameTick,
        AlarmTimingSettings settings)
    {
        var normalized = Normalize(settings);
        var conditionMet = CompareWithHysteresis(
            actual,
            comparison,
            threshold,
            normalized.Hysteresis,
            state.IsActive);
        return AdvanceNormalized(
            state,
            conditionMet,
            Math.Max(0, currentGameTick),
            normalized);
    }

    private static AlarmTimingEvaluation AdvanceNormalized(
        AlarmTimingState state,
        bool conditionMet,
        long currentGameTick,
        AlarmTimingSettings settings)
    {
        state = NormalizeStateForTick(state, currentGameTick);
        return state.IsActive
            ? AdvanceActive(
                state,
                conditionMet,
                currentGameTick,
                settings)
            : AdvanceInactive(
                state,
                conditionMet,
                currentGameTick,
                settings);
    }

    private static AlarmTimingEvaluation AdvanceInactive(
        AlarmTimingState state,
        bool conditionMet,
        long currentGameTick,
        AlarmTimingSettings settings)
    {
        if (!conditionMet)
        {
            return Unchanged(new AlarmTimingState(
                false,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                currentGameTick));
        }

        var pendingSince = ValidPastTick(
            state.ActivationPendingSinceTick,
            currentGameTick)
            ? state.ActivationPendingSinceTick
            : currentGameTick;
        if (!HasElapsed(
                pendingSince,
                currentGameTick,
                settings.ActivationDelayTicks))
        {
            return Unchanged(new AlarmTimingState(
                false,
                pendingSince,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                currentGameTick));
        }

        return new AlarmTimingEvaluation(
            AlarmTimingState.ActiveAt(currentGameTick),
            AlarmTimingTransition.Activated);
    }

    private static AlarmTimingEvaluation AdvanceActive(
        AlarmTimingState state,
        bool conditionMet,
        long currentGameTick,
        AlarmTimingSettings settings)
    {
        var activeSince = ValidPastTick(
            state.ActiveSinceTick,
            currentGameTick)
            ? state.ActiveSinceTick
            : currentGameTick;
        if (conditionMet)
        {
            return Unchanged(new AlarmTimingState(
                true,
                AlarmTimingState.NoTick,
                activeSince,
                AlarmTimingState.NoTick,
                currentGameTick));
        }

        var resetPendingSince = ValidPastTick(
            state.ResetPendingSinceTick,
            currentGameTick)
            ? state.ResetPendingSinceTick
            : currentGameTick;
        var minimumActiveElapsed = HasElapsed(
            activeSince,
            currentGameTick,
            settings.MinimumActiveTicks);
        var resetDelayElapsed = HasElapsed(
            resetPendingSince,
            currentGameTick,
            settings.ResetDelayTicks);
        if (!minimumActiveElapsed || !resetDelayElapsed)
        {
            return Unchanged(new AlarmTimingState(
                true,
                AlarmTimingState.NoTick,
                activeSince,
                resetPendingSince,
                currentGameTick));
        }

        return new AlarmTimingEvaluation(
            new AlarmTimingState(
                false,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                currentGameTick),
            AlarmTimingTransition.Cleared);
    }

    private static AlarmTimingState NormalizeStateForTick(
        AlarmTimingState state,
        long currentGameTick)
    {
        if (!state.IsInitialized)
        {
            // default(AlarmTimingState) is a natural dictionary fallback. Its
            // zero-valued tick fields must not masquerade as timers begun at
            // the start of the game.
            return new AlarmTimingState(
                false,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                currentGameTick);
        }

        if (state.LastObservedTick != AlarmTimingState.NoTick &&
            (!ValidTick(state.LastObservedTick) ||
             currentGameTick < state.LastObservedTick))
        {
            // A loaded save or changed calendar epoch must not inherit elapsed
            // time from the previous epoch. Preserve active state, but restart
            // all relevant timers at the new tick.
            return state.IsActive
                ? AlarmTimingState.ActiveAt(currentGameTick)
                : new AlarmTimingState(
                    false,
                    AlarmTimingState.NoTick,
                    AlarmTimingState.NoTick,
                    AlarmTimingState.NoTick,
                    currentGameTick);
        }

        return state;
    }

    private static AlarmTimingEvaluation Unchanged(AlarmTimingState state)
    {
        return new AlarmTimingEvaluation(
            state,
            AlarmTimingTransition.None);
    }

    private static bool HasElapsed(
        long startedAtTick,
        long currentGameTick,
        int requiredTicks)
    {
        return requiredTicks <= 0 ||
               ValidPastTick(startedAtTick, currentGameTick) &&
               currentGameTick - startedAtTick >= requiredTicks;
    }

    private static bool ValidPastTick(long tick, long currentGameTick)
    {
        return ValidTick(tick) && tick <= currentGameTick;
    }

    private static bool ValidTick(long tick)
    {
        return tick >= 0;
    }

    private static int NormalizeTicks(int ticks)
    {
        return Math.Max(0, Math.Min(MaximumTimingTicks, ticks));
    }

    private static double AbsoluteDifference(double left, double right)
    {
        var difference = Math.Abs(left - right);
        return double.IsInfinity(difference)
            ? double.MaxValue
            : difference;
    }

    private static double AddSaturating(double value, double amount)
    {
        return value > double.MaxValue - amount
            ? double.MaxValue
            : value + amount;
    }

    private static double SubtractSaturating(double value, double amount)
    {
        return value < double.MinValue + amount
            ? double.MinValue
            : value - amount;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

/// <summary>
/// Stable persistence contract for the runtime-only timing state. Definition
/// signatures deliberately exclude presentation and routing fields, while all
/// values which can change condition or timer semantics are included.
/// </summary>
public static class AlarmTimingMemoryPolicy
{
    private const char OwnerSeparator = '\u001f';

    private sealed class ExpectedDefinition
    {
        public string Signature = "";
        public int ConditionCount;
    }

    public static string RuleOwnerKey(string ruleId)
    {
        return "rule:" + (ruleId ?? "");
    }

    public static string SystemStageOwnerKey(
        string alarmId,
        string stageId,
        int stageIndex)
    {
        return SystemAlarmOwnerPrefix(alarmId) +
               (stageId ?? "") +
               OwnerSeparator +
               Math.Max(0, stageIndex).ToString(
                   CultureInfo.InvariantCulture);
    }

    public static string SystemAlarmOwnerPrefix(string alarmId)
    {
        return "system-stage:" + (alarmId ?? "") + OwnerSeparator;
    }

    /// <summary>
    /// Resolves the stage represented by a restored active system alarm.
    /// Modern memories carry a stable occurrence ID and must never be
    /// reassigned to another stage when that stage was removed or disabled.
    /// Priority/severity fallbacks remain available only for legacy memories
    /// which predate occurrence IDs.
    /// </summary>
    public static int FindRestoredSystemStageIndex(
        IReadOnlyList<SystemAlarmStageDefinition> stages,
        string occurrenceId,
        int occurrencePriority,
        AlarmSeverity severity)
    {
        if (stages == null)
        {
            return -1;
        }

        if (!string.IsNullOrWhiteSpace(occurrenceId))
        {
            for (var index = 0; index < stages.Count; index++)
            {
                var stage = stages[index];
                if (stage != null &&
                    stage.Enabled &&
                    string.Equals(
                        stage.Id,
                        occurrenceId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        var candidates = stages
            .Select((stage, index) => new { Stage = stage, Index = index })
            .Where(item => item.Stage != null && item.Stage.Enabled)
            .ToArray();
        var restored = candidates.FirstOrDefault(item =>
                           item.Stage.Priority == occurrencePriority &&
                           item.Stage.Severity == severity) ??
                       candidates.FirstOrDefault(item =>
                           item.Stage.Severity == severity) ??
                       candidates
                           .OrderByDescending(item => item.Stage.Severity)
                           .ThenByDescending(item => item.Stage.Priority)
                           .FirstOrDefault();
        return restored?.Index ?? -1;
    }

    public static string RuleDefinitionSignature(AlarmRuleDefinition rule)
    {
        if (rule == null)
        {
            return "";
        }

        var signature = new SignatureBuilder("unma-rule-timing-v1");
        signature.Add((int)rule.Logic);
        AddTiming(signature, new AlarmTimingSettings(
            rule.ActivationDelayTicks,
            rule.ResetDelayTicks,
            rule.MinimumActiveTicks,
            0d));
        var conditions = rule.Conditions ?? new List<ConditionDefinition>();
        signature.Add(conditions.Count);
        foreach (var condition in conditions)
        {
            if (condition == null)
            {
                signature.AddNull();
                continue;
            }
            signature.Add(condition.EntityId);
            signature.Add(condition.EntityType);
            signature.Add(condition.MetricPath);
            signature.Add((int)condition.Comparison);
            signature.Add(condition.Threshold);
            signature.Add(AlarmTimingPolicy.Normalize(
                new AlarmTimingSettings(0, 0, 0, condition.Hysteresis))
                .Hysteresis);
            signature.Add(condition.ExpectedProductId);
            signature.Add(condition.EntityPrototypeId);
            signature.Add((int)condition.ValueMode);
            signature.Add(condition.ReferenceMetricPath);
            signature.Add(condition.InstrumentId);
            signature.Add((int)condition.TrendMode);
            signature.Add(condition.DeltaThreshold);
            signature.Add(condition.WindowAmount);
            signature.Add((int)condition.WindowUnit);
        }
        return signature.Finish();
    }

    public static string SystemStageDefinitionSignature(
        SystemAlarmStageDefinition stage)
    {
        if (stage == null)
        {
            return "";
        }

        var signature = new SignatureBuilder("unma-system-stage-timing-v1");
        signature.Add((int)stage.Logic);
        AddTiming(signature, new AlarmTimingSettings(
            stage.ActivationDelayTicks,
            stage.ResetDelayTicks,
            stage.MinimumActiveTicks,
            0d));
        var conditions = stage.Conditions ??
                         new List<SystemConditionDefinition>();
        signature.Add(conditions.Count);
        foreach (var condition in conditions)
        {
            if (condition == null)
            {
                signature.AddNull();
                continue;
            }
            signature.Add(condition.MetricId);
            signature.Add((int)condition.Comparison);
            signature.Add(condition.Threshold);
            signature.Add(AlarmTimingPolicy.Normalize(
                new AlarmTimingSettings(0, 0, 0, condition.Hysteresis))
                .Hysteresis);
        }
        return signature.Finish();
    }

    public static AlarmTimingMemoryDefinition CreateMemory(
        string ownerKey,
        string definitionSignature,
        AlarmTimingState state,
        IReadOnlyDictionary<int, bool> conditionLatches)
    {
        ownerKey ??= "";
        definitionSignature ??= "";
        if (ownerKey.Length == 0 ||
            definitionSignature.Length == 0 ||
            !state.IsInitialized)
        {
            return null;
        }

        var memory = new AlarmTimingMemoryDefinition
        {
            OwnerKey = ownerKey,
            DefinitionSignature = definitionSignature,
            IsActive = state.IsActive,
            ActivationPendingSinceTick = state.ActivationPendingSinceTick,
            ActiveSinceTick = state.ActiveSinceTick,
            ResetPendingSinceTick = state.ResetPendingSinceTick,
            LastObservedTick = state.LastObservedTick,
        };
        if (conditionLatches != null)
        {
            foreach (var latch in conditionLatches
                         .Where(item => item.Key >= 0)
                         .OrderBy(item => item.Key))
            {
                memory.ConditionLatches.Add(
                    new AlarmConditionLatchMemoryDefinition
                    {
                        ConditionIndex = latch.Key,
                        IsLatched = latch.Value,
                    });
            }
        }
        return memory;
    }

    public static bool TryRestore(
        AlarmTimingMemoryDefinition memory,
        string expectedOwnerKey,
        string expectedSignature,
        int conditionCount,
        out AlarmTimingState state,
        out Dictionary<int, bool> conditionLatches)
    {
        state = default;
        conditionLatches = new Dictionary<int, bool>();
        expectedOwnerKey ??= "";
        expectedSignature ??= "";
        if (memory == null ||
            expectedOwnerKey.Length == 0 ||
            expectedSignature.Length == 0 ||
            !string.Equals(
                memory.OwnerKey ?? "",
                expectedOwnerKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                memory.DefinitionSignature ?? "",
                expectedSignature,
                StringComparison.Ordinal) ||
            memory.LastObservedTick < 0)
        {
            return false;
        }

        var lastObservedTick = memory.LastObservedTick;
        if (memory.IsActive)
        {
            var activeSinceTick = IsPastTick(
                memory.ActiveSinceTick,
                lastObservedTick)
                ? memory.ActiveSinceTick
                : lastObservedTick;
            var resetPendingSinceTick = IsPastTick(
                memory.ResetPendingSinceTick,
                lastObservedTick)
                ? memory.ResetPendingSinceTick
                : AlarmTimingState.NoTick;
            state = new AlarmTimingState(
                true,
                AlarmTimingState.NoTick,
                activeSinceTick,
                resetPendingSinceTick,
                lastObservedTick);
        }
        else
        {
            var activationPendingSinceTick = IsPastTick(
                memory.ActivationPendingSinceTick,
                lastObservedTick)
                ? memory.ActivationPendingSinceTick
                : AlarmTimingState.NoTick;
            state = new AlarmTimingState(
                false,
                activationPendingSinceTick,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                lastObservedTick);
        }

        conditionCount = Math.Max(0, conditionCount);
        foreach (var latch in memory.ConditionLatches ??
                     new List<AlarmConditionLatchMemoryDefinition>())
        {
            if (latch == null ||
                latch.ConditionIndex < 0 ||
                latch.ConditionIndex >= conditionCount)
            {
                continue;
            }
            conditionLatches[latch.ConditionIndex] = latch.IsLatched;
        }
        return true;
    }

    public static AlarmTimingMemoryDefinition CloneMemory(
        AlarmTimingMemoryDefinition source)
    {
        if (source == null)
        {
            return null;
        }
        return new AlarmTimingMemoryDefinition
        {
            OwnerKey = source.OwnerKey,
            DefinitionSignature = source.DefinitionSignature,
            IsActive = source.IsActive,
            ActivationPendingSinceTick = source.ActivationPendingSinceTick,
            ActiveSinceTick = source.ActiveSinceTick,
            ResetPendingSinceTick = source.ResetPendingSinceTick,
            LastObservedTick = source.LastObservedTick,
            ConditionLatches = (source.ConditionLatches ??
                    new List<AlarmConditionLatchMemoryDefinition>())
                .Where(item => item != null)
                .Select(item => new AlarmConditionLatchMemoryDefinition
                {
                    ConditionIndex = item.ConditionIndex,
                    IsLatched = item.IsLatched,
                })
                .ToList(),
        };
    }

    public static void NormalizeMemories(
        List<AlarmTimingMemoryDefinition> memories,
        IReadOnlyList<AlarmRuleDefinition> rules,
        IReadOnlyList<SystemAlarmDefinition> systemAlarms,
        bool discardExisting)
    {
        if (memories == null)
        {
            throw new ArgumentNullException(nameof(memories));
        }
        if (discardExisting)
        {
            memories.Clear();
            return;
        }

        var expected = BuildExpectedDefinitions(rules, systemAlarms);
        var normalized = new Dictionary<string, AlarmTimingMemoryDefinition>(
            StringComparer.Ordinal);
        foreach (var memory in memories)
        {
            var ownerKey = memory?.OwnerKey ?? "";
            if (!expected.TryGetValue(ownerKey, out var definition) ||
                !TryRestore(
                    memory,
                    ownerKey,
                    definition.Signature,
                    definition.ConditionCount,
                    out var state,
                    out var latches))
            {
                continue;
            }
            var restored = CreateMemory(
                ownerKey,
                definition.Signature,
                state,
                latches);
            if (restored != null)
            {
                normalized[ownerKey] = restored;
            }
        }

        memories.Clear();
        memories.AddRange(normalized.Values.OrderBy(
            item => item.OwnerKey,
            StringComparer.Ordinal));
    }

    private static Dictionary<string, ExpectedDefinition>
        BuildExpectedDefinitions(
            IReadOnlyList<AlarmRuleDefinition> rules,
            IReadOnlyList<SystemAlarmDefinition> systemAlarms)
    {
        var expected = new Dictionary<string, ExpectedDefinition>(
            StringComparer.Ordinal);
        foreach (var rule in rules ?? Array.Empty<AlarmRuleDefinition>())
        {
            if (rule == null ||
                !rule.Enabled ||
                string.IsNullOrWhiteSpace(rule.Id))
            {
                continue;
            }
            expected[RuleOwnerKey(rule.Id)] = new ExpectedDefinition
            {
                Signature = RuleDefinitionSignature(rule),
                ConditionCount = rule.Conditions?.Count ?? 0,
            };
        }

        foreach (var alarm in systemAlarms ??
                     Array.Empty<SystemAlarmDefinition>())
        {
            if (alarm == null ||
                !alarm.Enabled ||
                string.IsNullOrWhiteSpace(alarm.Id))
            {
                continue;
            }
            var stages = alarm.Stages ??
                         new List<SystemAlarmStageDefinition>();
            for (var stageIndex = 0;
                 stageIndex < stages.Count;
                 stageIndex++)
            {
                var stage = stages[stageIndex];
                if (stage == null || !stage.Enabled)
                {
                    continue;
                }
                expected[SystemStageOwnerKey(
                    alarm.Id,
                    stage.Id,
                    stageIndex)] = new ExpectedDefinition
                {
                    Signature = SystemStageDefinitionSignature(stage),
                    ConditionCount = stage.Conditions?.Count ?? 0,
                };
            }
        }
        return expected;
    }

    private static void AddTiming(
        SignatureBuilder signature,
        AlarmTimingSettings settings)
    {
        settings = AlarmTimingPolicy.Normalize(settings);
        signature.Add(settings.ActivationDelayTicks);
        signature.Add(settings.ResetDelayTicks);
        signature.Add(settings.MinimumActiveTicks);
    }

    private static bool IsPastTick(long tick, long lastObservedTick)
    {
        return tick >= 0 && tick <= lastObservedTick;
    }

    private sealed class SignatureBuilder
    {
        private readonly StringBuilder m_value = new();

        public SignatureBuilder(string version)
        {
            Add(version);
        }

        public void AddNull()
        {
            m_value.Append("n;");
        }

        public void Add(string value)
        {
            value ??= "";
            m_value.Append('s');
            m_value.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            m_value.Append(':');
            m_value.Append(value);
            m_value.Append(';');
        }

        public void Add(int value)
        {
            m_value.Append('i');
            m_value.Append(value.ToString(CultureInfo.InvariantCulture));
            m_value.Append(';');
        }

        public void Add(double value)
        {
            m_value.Append('d');
            if (double.IsNaN(value))
            {
                m_value.Append("nan");
            }
            else if (double.IsPositiveInfinity(value))
            {
                m_value.Append("+inf");
            }
            else if (double.IsNegativeInfinity(value))
            {
                m_value.Append("-inf");
            }
            else
            {
                m_value.Append(BitConverter.DoubleToInt64Bits(value).ToString(
                    "X16",
                    CultureInfo.InvariantCulture));
            }
            m_value.Append(';');
        }

        public string Finish()
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(
                m_value.ToString()));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                result.Append(value.ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }
    }
}
