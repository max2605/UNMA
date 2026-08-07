using System;

namespace UNMA.Domain;

public static class SustainedVanillaAlarmPolicy
{
    public const string HomelessLeftPrototypeId = "HomelessLeft";
    public const string PopulationDeltaMetricId =
        "population.last_diff";

    private const string VanillaPrefix = "vanilla:";
    private const string SustainedKeyPrefix = "vanilla:sustained:";

    public static bool IsSustainedPrototype(string prototypeId)
    {
        return string.Equals(
            prototypeId,
            HomelessLeftPrototypeId,
            StringComparison.Ordinal);
    }

    public static bool IsSustainedOverrideId(string overrideId)
    {
        return TryGetPrototypeId(overrideId, out var prototypeId) &&
               IsSustainedPrototype(prototypeId);
    }

    public static string AlarmKeyForNotification(
        string prototypeId,
        string occurrenceKey)
    {
        return IsSustainedPrototype(prototypeId)
            ? SustainedKeyPrefix + prototypeId
            : occurrenceKey;
    }

    public static string AlarmKeyForOverrideId(string overrideId)
    {
        return TryGetPrototypeId(overrideId, out var prototypeId) &&
               IsSustainedPrototype(prototypeId)
            ? SustainedKeyPrefix + prototypeId
            : "";
    }

    public static bool IgnoresNotificationRemoval(string prototypeId)
    {
        return IsSustainedPrototype(prototypeId);
    }

    public static bool MatchesHistory(
        string prototypeId,
        string alarmKey,
        string detail)
    {
        if (!IsSustainedPrototype(prototypeId))
        {
            return false;
        }
        return string.Equals(
                   alarmKey,
                   SustainedKeyPrefix + prototypeId,
                   StringComparison.Ordinal) ||
               string.Equals(detail, prototypeId, StringComparison.Ordinal) ||
               !string.IsNullOrWhiteSpace(detail) &&
               detail.StartsWith(
                   prototypeId + " ",
                   StringComparison.Ordinal);
    }

    public static bool ShouldClear(
        string prototypeId,
        double populationDelta)
    {
        return IsSustainedPrototype(prototypeId) &&
               populationDelta >= 0d;
    }

    public static bool ShouldProcessNotification(
        string prototypeId,
        double populationDelta)
    {
        return !IsSustainedPrototype(prototypeId) ||
               populationDelta < 0d;
    }

    private static bool TryGetPrototypeId(
        string overrideId,
        out string prototypeId)
    {
        prototypeId = "";
        if (string.IsNullOrWhiteSpace(overrideId) ||
            !overrideId.StartsWith(VanillaPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        prototypeId = overrideId.Substring(VanillaPrefix.Length);
        return prototypeId.Length > 0;
    }
}
