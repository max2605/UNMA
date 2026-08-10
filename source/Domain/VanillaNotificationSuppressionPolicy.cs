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

    public static VanillaNotificationBehavior ResolveBehavior(
        IEnumerable<VanillaNotificationRule> rules,
        string alarmId,
        int entityId = -1,
        string entityPrototypeId = "")
    {
        if (rules == null || !IsVanillaOverrideId(alarmId))
        {
            return VanillaNotificationBehavior.Normal;
        }

        alarmId = alarmId.Trim();
        entityPrototypeId = entityPrototypeId?.Trim() ?? "";
        VanillaNotificationRule best = null;
        var bestRank = -1;
        foreach (var rule in rules)
        {
            if (rule == null ||
                !string.Equals(rule.AlarmId?.Trim(), alarmId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var rank = rule.Scope switch
            {
                VanillaNotificationScope.Entity
                    when entityId >= 0 && rule.EntityId == entityId => 3,
                VanillaNotificationScope.EntityPrototype
                    when entityPrototypeId.Length > 0 && string.Equals(
                        rule.EntityPrototypeId?.Trim(),
                        entityPrototypeId,
                        StringComparison.Ordinal) => 2,
                VanillaNotificationScope.NotificationType => 1,
                _ => -1,
            };
            if (rank > bestRank)
            {
                best = rule;
                bestRank = rank;
            }
        }
        return best?.Behavior ?? VanillaNotificationBehavior.Normal;
    }

    public static bool IsHiddenFromPanel(
        VanillaNotificationBehavior behavior,
        bool isEntityPanel,
        bool belongsToEntityPanel)
    {
        if (behavior == VanillaNotificationBehavior.Ignored)
        {
            return true;
        }
        if (behavior != VanillaNotificationBehavior.Hidden)
        {
            return false;
        }
        return !isEntityPanel || !belongsToEntityPanel;
    }

    public static string RuleIdentity(VanillaNotificationRule rule)
    {
        if (rule == null)
        {
            return "";
        }
        return (rule.AlarmId?.Trim() ?? "") + "|" +
               (int)rule.Scope + "|" +
               (rule.Scope == VanillaNotificationScope.Entity
                   ? rule.EntityId.ToString(
                       System.Globalization.CultureInfo.InvariantCulture)
                   : rule.Scope == VanillaNotificationScope.EntityPrototype
                       ? rule.EntityPrototypeId?.Trim() ?? ""
                       : "");
    }

    public static bool MatchesScope(
        VanillaNotificationRule rule,
        string alarmId,
        VanillaNotificationScope scope,
        int entityId,
        string entityPrototypeId)
    {
        if (rule == null ||
            rule.Scope != scope ||
            !string.Equals(rule.AlarmId?.Trim(), alarmId?.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }
        return scope switch
        {
            VanillaNotificationScope.Entity => rule.EntityId == entityId,
            VanillaNotificationScope.EntityPrototype => string.Equals(
                rule.EntityPrototypeId?.Trim(),
                entityPrototypeId?.Trim(),
                StringComparison.Ordinal),
            _ => true,
        };
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
