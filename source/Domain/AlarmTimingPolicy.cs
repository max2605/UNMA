using System;
using System.Collections.Generic;

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
