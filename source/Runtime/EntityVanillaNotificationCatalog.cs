using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi.Core.Entities;
using Mafi.Core.Notifications;
using UNMA.Domain;

namespace UNMA.Runtime;

public static class EntityVanillaNotificationCatalog
{
    private const BindingFlags DeclaredInstanceFields =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

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
            entityTitle.Length == 0 ? "Object" : entityTitle);
    }
}
