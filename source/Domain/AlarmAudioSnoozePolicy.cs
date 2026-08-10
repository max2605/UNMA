using System;

namespace UNMA.Domain;

/// <summary>
/// Immutable audio-snooze state for one concrete alarm occurrence. The
/// sequence is part of the identity so a later occurrence is never silenced
/// by an older one.
/// </summary>
public readonly struct AlarmAudioSnoozeState
{
    public const long NoGameTick = -1;

    public bool IsInitialized { get; }
    public string AlarmKey { get; }
    public long Sequence { get; }
    public long StartedAtGameTick { get; }
    public long MutedUntilGameTick { get; }
    public bool EndWhenGone { get; }

    public bool HasEndTick => MutedUntilGameTick != NoGameTick;

    internal AlarmAudioSnoozeState(
        string alarmKey,
        long sequence,
        long startedAtGameTick,
        long mutedUntilGameTick,
        bool endWhenGone)
    {
        IsInitialized = true;
        AlarmKey = alarmKey;
        Sequence = sequence;
        StartedAtGameTick = startedAtGameTick;
        MutedUntilGameTick = mutedUntilGameTick;
        EndWhenGone = endWhenGone;
    }
}

/// <summary>
/// Pure game-tick policy for temporarily suppressing alarm audio. It does not
/// alter alarm visibility, acknowledgement or history state.
/// </summary>
public static class AlarmAudioSnoozePolicy
{
    public const int MaximumDurationTicks =
        GameTimeWindowPolicy.MaximumWindowTicks;

    public static bool TryCreateUntilTick(
        string alarmKey,
        long sequence,
        long currentGameTick,
        long requestedUntilGameTick,
        out AlarmAudioSnoozeState state)
    {
        return TryCreateUntilTick(
            alarmKey,
            sequence,
            currentGameTick,
            requestedUntilGameTick,
            endWhenGone: false,
            out state);
    }

    /// <summary>
    /// Creates a timed snooze. Excessively distant deadlines are clamped to
    /// one maximum supported game-time window from the current tick.
    /// </summary>
    public static bool TryCreateUntilTick(
        string alarmKey,
        long sequence,
        long currentGameTick,
        long requestedUntilGameTick,
        bool endWhenGone,
        out AlarmAudioSnoozeState state)
    {
        state = default;
        if (!TryNormalizeIdentity(alarmKey, sequence, out var normalizedKey) ||
            currentGameTick < 0 ||
            requestedUntilGameTick <= currentGameTick)
        {
            return false;
        }

        var maximumEndTick = AddSaturating(
            currentGameTick,
            MaximumDurationTicks);
        var normalizedEndTick = Math.Min(
            requestedUntilGameTick,
            maximumEndTick);
        if (normalizedEndTick <= currentGameTick)
        {
            return false;
        }

        state = new AlarmAudioSnoozeState(
            normalizedKey,
            sequence,
            currentGameTick,
            normalizedEndTick,
            endWhenGone);
        return true;
    }

    /// <summary>
    /// Creates an open-ended snooze which remains valid only while this exact
    /// alarm occurrence is active.
    /// </summary>
    public static bool TryCreateUntilGone(
        string alarmKey,
        long sequence,
        long currentGameTick,
        out AlarmAudioSnoozeState state)
    {
        state = default;
        if (!TryNormalizeIdentity(alarmKey, sequence, out var normalizedKey) ||
            currentGameTick < 0)
        {
            return false;
        }

        state = new AlarmAudioSnoozeState(
            normalizedKey,
            sequence,
            currentGameTick,
            AlarmAudioSnoozeState.NoGameTick,
            endWhenGone: true);
        return true;
    }

    /// <summary>
    /// Returns whether audio for the supplied occurrence is still snoozed.
    /// A changed sequence, a gone alarm when requested, an elapsed deadline,
    /// or a game-clock rollback all release the snooze safely.
    /// </summary>
    public static bool IsSnoozed(
        AlarmAudioSnoozeState state,
        string alarmKey,
        long sequence,
        long currentGameTick,
        bool isActive)
    {
        if (!state.IsInitialized ||
            !TryNormalizeIdentity(alarmKey, sequence, out var normalizedKey) ||
            !TryNormalizeIdentity(
                state.AlarmKey,
                state.Sequence,
                out var stateKey) ||
            currentGameTick < 0 ||
            state.StartedAtGameTick < 0 ||
            currentGameTick < state.StartedAtGameTick ||
            !string.Equals(
                normalizedKey,
                stateKey,
                StringComparison.Ordinal) ||
            sequence != state.Sequence ||
            state.EndWhenGone && !isActive)
        {
            return false;
        }

        if (!state.HasEndTick)
        {
            return state.EndWhenGone;
        }

        return state.MutedUntilGameTick > state.StartedAtGameTick &&
               currentGameTick < state.MutedUntilGameTick;
    }

    private static bool TryNormalizeIdentity(
        string alarmKey,
        long sequence,
        out string normalizedKey)
    {
        normalizedKey = alarmKey?.Trim() ?? "";
        return normalizedKey.Length > 0 && sequence > 0;
    }

    private static long AddSaturating(long value, int amount)
    {
        return value > long.MaxValue - amount
            ? long.MaxValue
            : value + amount;
    }
}
