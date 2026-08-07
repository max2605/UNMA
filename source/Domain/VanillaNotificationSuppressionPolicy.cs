using System;
using System.Collections.Generic;

namespace UNMA.Domain;

public static class VanillaNotificationSuppressionPolicy
{
    private const string VanillaPrefix = "vanilla:";
    private const string EntityMarker = ":entity:";
    private const string LegacyMarker = ":legacy:";

    public static bool IsVanillaOverrideId(string alarmId)
    {
        alarmId = alarmId?.Trim() ?? "";
        if (!alarmId.StartsWith(VanillaPrefix, StringComparison.Ordinal) ||
            alarmId.Length == VanillaPrefix.Length)
        {
            return false;
        }

        return alarmId.IndexOf(EntityMarker, StringComparison.Ordinal) < 0 &&
               alarmId.IndexOf(LegacyMarker, StringComparison.Ordinal) < 0;
    }

    public static string GetOverrideIdForSlotId(string slotId)
    {
        slotId = slotId?.Trim() ?? "";
        if (!slotId.StartsWith(VanillaPrefix, StringComparison.Ordinal) ||
            slotId.Length == VanillaPrefix.Length)
        {
            return "";
        }

        var entityMarkerIndex = slotId.IndexOf(
            EntityMarker,
            StringComparison.Ordinal);
        var legacyMarkerIndex = slotId.IndexOf(
            LegacyMarker,
            StringComparison.Ordinal);
        var markerIndex = FirstMarkerIndex(
            entityMarkerIndex,
            legacyMarkerIndex);
        if (markerIndex < 0)
        {
            return IsVanillaOverrideId(slotId) ? slotId : "";
        }

        var markerLength = markerIndex == entityMarkerIndex
            ? EntityMarker.Length
            : LegacyMarker.Length;
        if (markerIndex <= VanillaPrefix.Length ||
            markerIndex + markerLength >= slotId.Length)
        {
            return "";
        }

        var overrideId = slotId.Substring(0, markerIndex);
        return IsVanillaOverrideId(overrideId) ? overrideId : "";
    }

    public static bool IsSlotSuppressed(
        PanelSlotDefinition slot,
        IEnumerable<string> disabledOverrideIds)
    {
        if (slot == null || disabledOverrideIds == null)
        {
            return false;
        }

        var overrideId = GetOverrideIdForSlotId(slot.AlarmId);
        if (overrideId.Length == 0)
        {
            return false;
        }

        foreach (var disabledOverrideId in disabledOverrideIds)
        {
            if (string.Equals(
                    overrideId,
                    disabledOverrideId?.Trim(),
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static int FirstMarkerIndex(int first, int second)
    {
        if (first < 0)
        {
            return second;
        }
        if (second < 0)
        {
            return first;
        }
        return Math.Min(first, second);
    }
}
