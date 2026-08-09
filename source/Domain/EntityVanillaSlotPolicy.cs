using System;
using System.Collections.Generic;
using System.Linq;

namespace UNMA.Domain;

public static class EntityVanillaSlotPolicy
{
    private const string EntityMarker = ":entity:";
    private const string VanillaPrefix = "vanilla:";

    public static bool IsForEntity(
        PanelSlotDefinition slot,
        int entityId)
    {
        if (slot == null || entityId <= 0 ||
            !string.Equals(slot.Source, "vanilla", StringComparison.Ordinal))
        {
            return false;
        }
        var alarmId = slot.AlarmId?.Trim() ?? "";
        return alarmId.EndsWith(
            EntityMarker + entityId,
            StringComparison.Ordinal) &&
            VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                VanillaNotificationSuppressionPolicy
                    .GetOverrideIdForSlotId(alarmId));
    }

    public static PanelSlotDefinition CreateForEntity(
        PanelSlotDefinition source,
        int entityId,
        string entityTitle)
    {
        if (source == null || entityId <= 0)
        {
            return null;
        }
        var overrideId = VanillaNotificationSuppressionPolicy
            .GetOverrideIdForSlotId(source.AlarmId);
        if (!VanillaNotificationSuppressionPolicy
                .IsVanillaOverrideId(overrideId))
        {
            return null;
        }
        var prototypeId = overrideId.StartsWith(
                VanillaPrefix,
                StringComparison.Ordinal)
            ? overrideId.Substring(VanillaPrefix.Length)
            : overrideId;
        entityTitle = entityTitle?.Trim() ?? "";
        return new PanelSlotDefinition
        {
            AlarmId = overrideId + EntityMarker + entityId,
            DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? prototypeId
                : source.DisplayName.Trim(),
            Detail = entityTitle.Length == 0
                ? prototypeId
                : prototypeId + " · " + entityTitle,
            Source = "vanilla",
            Severity = source.Severity,
            ActiveColor = string.IsNullOrWhiteSpace(source.ActiveColor)
                ? "#F0C541"
                : source.ActiveColor,
        };
    }

    public static bool Synchronize(
        PanelDefinition panel,
        IEnumerable<PanelSlotDefinition> knownSlots)
    {
        if (!PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return false;
        }
        panel.Slots ??= new List<PanelSlotDefinition>();
        var existingOverrideIds = new HashSet<string>(
            panel.Slots
                .Where(slot => IsForEntity(slot, panel.OwnerEntityId))
                .Select(slot => VanillaNotificationSuppressionPolicy
                    .GetOverrideIdForSlotId(slot.AlarmId)),
            StringComparer.Ordinal);
        var changed = false;
        foreach (var source in knownSlots ??
                     Enumerable.Empty<PanelSlotDefinition>())
        {
            var slot = CreateForEntity(
                source,
                panel.OwnerEntityId,
                panel.OwnerEntityTitle);
            if (slot == null)
            {
                continue;
            }
            var overrideId = VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(slot.AlarmId);
            if (!existingOverrideIds.Add(overrideId))
            {
                continue;
            }
            panel.Slots.Add(slot);
            changed = true;
        }
        return changed;
    }
}
