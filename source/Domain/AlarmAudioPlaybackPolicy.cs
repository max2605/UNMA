using System;

namespace UNMA.Domain;

/// <summary>
/// Pure eligibility, priority, and occurrence-identity rules for alarm audio.
/// Cleared-but-unacknowledged alarms remain available for acknowledgement and
/// history, but they no longer keep sounding after the condition has gone.
/// </summary>
public static class AlarmAudioPlaybackPolicy
{
    public static bool CanPlay(
        AlarmView alarm,
        bool isSnoozed,
        bool isSuppressed,
        bool usesNormalSoundBehavior)
    {
        return alarm != null &&
               alarm.IsActive &&
               !alarm.IsAcknowledged &&
               !isSnoozed &&
               !isSuppressed &&
               usesNormalSoundBehavior &&
               !string.Equals(
                   alarm.SoundId,
                   "none",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasHigherPriority(
        AlarmView candidate,
        AlarmView current)
    {
        if (candidate == null)
        {
            return false;
        }
        if (current == null)
        {
            return true;
        }
        if (candidate.Severity != current.Severity)
        {
            return candidate.Severity > current.Severity;
        }
        if (candidate.Sequence != current.Sequence)
        {
            return candidate.Sequence > current.Sequence;
        }
        return string.CompareOrdinal(
                   candidate.Key ?? "",
                   current.Key ?? "") > 0;
    }

    public static bool IsSameOccurrence(
        AlarmView alarm,
        string alarmKey,
        long sequence)
    {
        return alarm != null &&
               sequence > 0 &&
               alarm.Sequence == sequence &&
               string.Equals(
                   alarm.Key ?? "",
                   alarmKey ?? "",
                   StringComparison.Ordinal);
    }
}
