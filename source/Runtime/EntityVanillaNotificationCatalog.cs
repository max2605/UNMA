using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Notifications;
using Mafi.Core.Prototypes;
using UNMA.Domain;
using UNMA.Localization;

namespace UNMA.Runtime;

public static class EntityVanillaNotificationCatalog
{
    private static readonly Dictionary<string, NotificationProto>
        s_prototypes = new(StringComparer.Ordinal);

    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    public static void Configure(ProtosDb protosDb)
    {
        s_prototypes.Clear();
        if (protosDb == null)
        {
            return;
        }
        foreach (var prototype in protosDb.All<NotificationProto>())
        {
            s_prototypes[prototype.Id.Value] = prototype;
        }
    }

    public static IReadOnlyList<PanelSlotDefinition> DiscoverSlots(
        IEntity entity,
        string entityTitle,
        Func<AlarmSeverity, string> colorForSeverity)
    {
        if (entity == null)
        {
            return Array.Empty<PanelSlotDefinition>();
        }

        var slots = new List<PanelSlotDefinition>();
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        for (var type = entity.GetType();
             type != null && type != typeof(object);
             type = type.BaseType)
        {
            foreach (var field in type.GetFields(DeclaredInstanceFields))
            {
                if (!TryReadNotificationPrototype(
                        field,
                        entity,
                        out var prototype))
                {
                    continue;
                }
                var prototypeId = prototype.Id.Value?.Trim() ?? "";
                if (prototypeId.Length == 0 || !knownIds.Add(prototypeId))
                {
                    continue;
                }
                var severity = SeverityFor(prototype);
                slots.Add(new PanelSlotDefinition
                {
                    AlarmId = "vanilla:" + prototypeId,
                    DisplayName = NotificationName(
                        prototype,
                        entityTitle),
                    Detail = prototypeId,
                    Source = "vanilla",
                    Severity = severity,
                    ActiveColor = colorForSeverity?.Invoke(severity) ??
                                  "#F0C541",
                });
            }
        }
        if (entity is IStaticEntity &&
            entity is not IEntityWithNoCollapse &&
            s_prototypes.TryGetValue(
                IdsCore.Notifications.EntityMayCollapseUnevenTerrain.Value,
                out var collapsePrototype) &&
            knownIds.Add(collapsePrototype.Id.Value))
        {
            var severity = SeverityFor(collapsePrototype);
            slots.Add(new PanelSlotDefinition
            {
                AlarmId = "vanilla:" + collapsePrototype.Id.Value,
                DisplayName = NotificationName(
                    collapsePrototype,
                    entityTitle),
                Detail = collapsePrototype.Id.Value,
                Source = "vanilla",
                Severity = severity,
                ActiveColor = colorForSeverity?.Invoke(severity) ??
                              "#F0C541",
            });
        }
        return slots;
    }

    private static bool TryReadNotificationPrototype(
        FieldInfo ownerField,
        object owner,
        out NotificationProto prototype)
    {
        prototype = null;
        var fieldType = ownerField.FieldType;
        if (!string.Equals(
                fieldType.Namespace,
                typeof(NotificationProto).Namespace,
                StringComparison.Ordinal) ||
            fieldType.Name.IndexOf(
                "Notificator",
                StringComparison.Ordinal) < 0)
        {
            return false;
        }

        object notificator;
        try
        {
            notificator = ownerField.GetValue(owner);
        }
        catch
        {
            return false;
        }
        if (notificator == null)
        {
            return false;
        }

        var prototypeField = fieldType.GetField(
            "Prototype",
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic) ??
            fieldType.GetField(
                "m_prototype",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        if (prototypeField == null ||
            !typeof(NotificationProto).IsAssignableFrom(
                prototypeField.FieldType))
        {
            return false;
        }
        try
        {
            prototype = prototypeField.GetValue(notificator) as
                NotificationProto;
            return prototype != null;
        }
        catch
        {
            prototype = null;
            return false;
        }
    }

    private static AlarmSeverity SeverityFor(NotificationProto prototype)
    {
        return prototype.Style switch
        {
            NotificationStyle.Critical => AlarmSeverity.Critical,
            NotificationStyle.Warning => AlarmSeverity.Warning,
            _ => AlarmSeverity.Notice,
        };
    }

    private static string NotificationName(
        NotificationProto prototype,
        string entityTitle)
    {
        var prototypeId = prototype.Id.Value;
        var name = prototype.Strings.Name.TranslatedString;
        if (string.IsNullOrWhiteSpace(name))
        {
            return prototypeId;
        }
        entityTitle = entityTitle?.Trim() ?? "";
        return name.Replace(
            "{entity}",
            entityTitle.Length == 0
                ? UnmaText.Get("entity.generic_object", "Object")
                : entityTitle);
    }
}
