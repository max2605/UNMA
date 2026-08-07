using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace UNMA.Api;

/// <summary>
/// Describes a metric that a provider mod exposes to UNMA.
/// </summary>
[DataContract]
public sealed class ExternalMetricDefinition
{
    [DataMember(Name = "id", Order = 1)]
    public string Id { get; set; } = "";

    [DataMember(Name = "prototype_id", Order = 2)]
    public string PrototypeId { get; set; } = "*";

    [DataMember(Name = "label_key", Order = 3)]
    public string LabelKey { get; set; } = "";

    [DataMember(Name = "label_fallback", Order = 4)]
    public string LabelFallback { get; set; } = "";

    [DataMember(Name = "unit", Order = 5)]
    public string Unit { get; set; } = "";

    [DataMember(Name = "suggested_reference_metric", Order = 6)]
    public string SuggestedReferenceMetric { get; set; } = "";

    /// <summary>
    /// Reads the metric from the game entity supplied by UNMA. Provider mods
    /// can cast the object to one of their own public entity types.
    /// Returning null means that no value is available for that entity.
    /// </summary>
    [IgnoreDataMember]
    public Func<object, double?> Reader { get; set; }
}

/// <summary>
/// One condition of an external alarm template.
/// String values deliberately keep the public API independent of UNMA's
/// internal domain enums.
/// </summary>
[DataContract]
public sealed class ExternalAlarmConditionDefinition
{
    [DataMember(Name = "metric", Order = 1, IsRequired = true)]
    public string Metric { get; set; } = "";

    [DataMember(Name = "operator", Order = 2, IsRequired = true)]
    public string Operator { get; set; } = "<";

    [DataMember(Name = "threshold", Order = 3, IsRequired = true)]
    public double Threshold { get; set; }

    [DataMember(Name = "value_mode", Order = 4)]
    public string ValueMode { get; set; } = "absolute";

    [DataMember(Name = "reference_metric", Order = 5)]
    public string ReferenceMetric { get; set; } = "";

    [DataMember(Name = "label_key", Order = 6)]
    public string LabelKey { get; set; } = "";

    [DataMember(Name = "label_fallback", Order = 7)]
    public string LabelFallback { get; set; } = "";

    [DataMember(Name = "reference_label_key", Order = 8)]
    public string ReferenceLabelKey { get; set; } = "";

    [DataMember(Name = "reference_label_fallback", Order = 9)]
    public string ReferenceLabelFallback { get; set; } = "";
}

/// <summary>
/// Declarative alarm template shared by the C# registration API and JSON
/// extension files.
/// </summary>
[DataContract]
public sealed class ExternalAlarmTemplateDefinition
{
    [DataMember(Name = "id", Order = 1, IsRequired = true)]
    public string Id { get; set; } = "";

    [DataMember(Name = "prototype_ids", Order = 2, IsRequired = true)]
    public List<string> PrototypeIds { get; set; } = new();

    [DataMember(Name = "scope", Order = 3)]
    public string Scope { get; set; } = "aggregate";

    [DataMember(Name = "panel_id", Order = 4)]
    public string PanelId { get; set; } = "main";

    [DataMember(Name = "localization_namespace", Order = 5)]
    public string LocalizationNamespace { get; set; } = "";

    [DataMember(Name = "message_key", Order = 6)]
    public string MessageKey { get; set; } = "";

    [DataMember(Name = "message_fallback", Order = 7)]
    public string MessageFallback { get; set; } = "";

    [DataMember(Name = "detail_key", Order = 8)]
    public string DetailKey { get; set; } = "";

    [DataMember(Name = "detail_fallback", Order = 9)]
    public string DetailFallback { get; set; } = "";

    [DataMember(Name = "severity", Order = 10)]
    public string Severity { get; set; } = "warning";

    [DataMember(Name = "sound_id", Order = 11)]
    public string SoundId { get; set; } = "auto";

    [DataMember(Name = "active_color", Order = 12)]
    public string ActiveColor { get; set; } = "auto";

    [DataMember(Name = "auto_acknowledge_on_clear", Order = 13)]
    public bool AutoAcknowledgeOnClear { get; set; }

    [DataMember(Name = "logic", Order = 14)]
    public string Logic { get; set; } = "all";

    [DataMember(Name = "conditions", Order = 15, IsRequired = true)]
    public List<ExternalAlarmConditionDefinition> Conditions { get; set; } =
        new();
}

/// <summary>
/// A current alarm state pushed directly by a provider mod.
/// </summary>
[DataContract]
public sealed class ExternalAlarmState
{
    [DataMember(Name = "id", Order = 1)]
    public string Id { get; set; } = "";

    [DataMember(Name = "instance_id", Order = 2)]
    public string InstanceId { get; set; } = "default";

    [DataMember(Name = "active", Order = 3)]
    public bool Active { get; set; }

    [DataMember(Name = "panel_id", Order = 4)]
    public string PanelId { get; set; } = "main";

    [DataMember(Name = "prototype_id", Order = 5)]
    public string PrototypeId { get; set; } = "";

    [DataMember(Name = "entity_key", Order = 6)]
    public string EntityKey { get; set; } = "";

    [DataMember(Name = "localization_namespace", Order = 7)]
    public string LocalizationNamespace { get; set; } = "";

    [DataMember(Name = "message_key", Order = 8)]
    public string MessageKey { get; set; } = "";

    [DataMember(Name = "message_fallback", Order = 9)]
    public string MessageFallback { get; set; } = "";

    [DataMember(Name = "detail_key", Order = 10)]
    public string DetailKey { get; set; } = "";

    [DataMember(Name = "detail_fallback", Order = 11)]
    public string DetailFallback { get; set; } = "";

    [DataMember(Name = "severity", Order = 12)]
    public string Severity { get; set; } = "warning";

    [DataMember(Name = "sound_id", Order = 13)]
    public string SoundId { get; set; } = "auto";

    [DataMember(Name = "active_color", Order = 14)]
    public string ActiveColor { get; set; } = "auto";

    [DataMember(Name = "auto_acknowledge_on_clear", Order = 15)]
    public bool AutoAcknowledgeOnClear { get; set; }

    [DataMember(Name = "current_value", Order = 16,
        EmitDefaultValue = false)]
    public double? CurrentValue { get; set; }
}

/// <summary>
/// Immutable registered metric exposed by an API snapshot.
/// </summary>
public sealed class ExternalMetricSnapshot
{
    public string OwnerModId { get; }
    public string Id { get; }
    public string PrototypeId { get; }
    public string LabelKey { get; }
    public string LabelFallback { get; }
    public string Unit { get; }
    public string SuggestedReferenceMetric { get; }

    private readonly Func<object, double?> m_reader;

    internal ExternalMetricSnapshot(
        string ownerModId,
        string id,
        string prototypeId,
        string labelKey,
        string labelFallback,
        string unit,
        string suggestedReferenceMetric,
        Func<object, double?> reader)
    {
        OwnerModId = ownerModId;
        Id = id;
        PrototypeId = prototypeId;
        LabelKey = labelKey;
        LabelFallback = labelFallback;
        Unit = unit;
        SuggestedReferenceMetric = suggestedReferenceMetric;
        m_reader = reader;
    }

    /// <summary>
    /// Invokes provider code behind an exception boundary. Invalid, missing,
    /// NaN, and infinite values are reported as unavailable.
    /// </summary>
    public bool TryRead(object entity, out double value)
    {
        value = 0d;
        if (entity == null || m_reader == null)
        {
            return false;
        }

        try
        {
            var result = m_reader(entity);
            if (!result.HasValue || double.IsNaN(result.Value) ||
                double.IsInfinity(result.Value))
            {
                return false;
            }

            value = result.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class ExternalAlarmConditionSnapshot
{
    public string Metric { get; }
    public string Operator { get; }
    public double Threshold { get; }
    public string ValueMode { get; }
    public string ReferenceMetric { get; }
    public string LabelKey { get; }
    public string LabelFallback { get; }
    public string ReferenceLabelKey { get; }
    public string ReferenceLabelFallback { get; }

    internal ExternalAlarmConditionSnapshot(
        string metric,
        string comparisonOperator,
        double threshold,
        string valueMode,
        string referenceMetric,
        string labelKey,
        string labelFallback,
        string referenceLabelKey,
        string referenceLabelFallback)
    {
        Metric = metric;
        Operator = comparisonOperator;
        Threshold = threshold;
        ValueMode = valueMode;
        ReferenceMetric = referenceMetric;
        LabelKey = labelKey;
        LabelFallback = labelFallback;
        ReferenceLabelKey = referenceLabelKey;
        ReferenceLabelFallback = referenceLabelFallback;
    }
}

/// <summary>
/// Immutable, owner-bound external alarm template.
/// </summary>
public sealed class ExternalAlarmTemplateSnapshot
{
    public string OwnerModId { get; }
    public string Id { get; }
    public IReadOnlyList<string> PrototypeIds { get; }
    public string Scope { get; }
    public string PanelId { get; }
    public string LocalizationNamespace { get; }
    public string MessageKey { get; }
    public string MessageFallback { get; }
    public string DetailKey { get; }
    public string DetailFallback { get; }
    public string Severity { get; }
    public string SoundId { get; }
    public string ActiveColor { get; }
    public bool AutoAcknowledgeOnClear { get; }
    public string Logic { get; }
    public IReadOnlyList<ExternalAlarmConditionSnapshot> Conditions { get; }

    internal ExternalAlarmTemplateSnapshot(
        string ownerModId,
        string id,
        IList<string> prototypeIds,
        string scope,
        string panelId,
        string localizationNamespace,
        string messageKey,
        string messageFallback,
        string detailKey,
        string detailFallback,
        string severity,
        string soundId,
        string activeColor,
        bool autoAcknowledgeOnClear,
        string logic,
        IList<ExternalAlarmConditionSnapshot> conditions)
    {
        OwnerModId = ownerModId;
        Id = id;
        PrototypeIds = new ReadOnlyCollection<string>(
            new List<string>(prototypeIds));
        Scope = scope;
        PanelId = panelId;
        LocalizationNamespace = localizationNamespace;
        MessageKey = messageKey;
        MessageFallback = messageFallback;
        DetailKey = detailKey;
        DetailFallback = detailFallback;
        Severity = severity;
        SoundId = soundId;
        ActiveColor = activeColor;
        AutoAcknowledgeOnClear = autoAcknowledgeOnClear;
        Logic = logic;
        Conditions = new ReadOnlyCollection<ExternalAlarmConditionSnapshot>(
            new List<ExternalAlarmConditionSnapshot>(conditions));
    }
}

/// <summary>
/// Immutable current state of a pushed alarm.
/// </summary>
public sealed class ExternalAlarmStateSnapshot
{
    public string OwnerModId { get; }
    public string Id { get; }
    public string InstanceId { get; }
    public bool Active { get; }
    public string PanelId { get; }
    public string PrototypeId { get; }
    public string EntityKey { get; }
    public string LocalizationNamespace { get; }
    public string MessageKey { get; }
    public string MessageFallback { get; }
    public string DetailKey { get; }
    public string DetailFallback { get; }
    public string Severity { get; }
    public string SoundId { get; }
    public string ActiveColor { get; }
    public bool AutoAcknowledgeOnClear { get; }
    public double? CurrentValue { get; }

    internal ExternalAlarmStateSnapshot(
        string ownerModId,
        string id,
        string instanceId,
        bool active,
        string panelId,
        string prototypeId,
        string entityKey,
        string localizationNamespace,
        string messageKey,
        string messageFallback,
        string detailKey,
        string detailFallback,
        string severity,
        string soundId,
        string activeColor,
        bool autoAcknowledgeOnClear,
        double? currentValue)
    {
        OwnerModId = ownerModId;
        Id = id;
        InstanceId = instanceId;
        Active = active;
        PanelId = panelId;
        PrototypeId = prototypeId;
        EntityKey = entityKey;
        LocalizationNamespace = localizationNamespace;
        MessageKey = messageKey;
        MessageFallback = messageFallback;
        DetailKey = detailKey;
        DetailFallback = detailFallback;
        Severity = severity;
        SoundId = soundId;
        ActiveColor = activeColor;
        AutoAcknowledgeOnClear = autoAcknowledgeOnClear;
        CurrentValue = currentValue;
    }
}

/// <summary>
/// Atomic immutable view of all C# extension registrations.
/// </summary>
public sealed class ExternalRegistrySnapshot
{
    public long Revision { get; }
    public IReadOnlyList<ExternalMetricSnapshot> Metrics { get; }
    public IReadOnlyList<ExternalAlarmTemplateSnapshot> AlarmTemplates { get; }
    public IReadOnlyList<ExternalAlarmStateSnapshot> AlarmStates { get; }

    private readonly IReadOnlyDictionary<string, ExternalMetricSnapshot>
        m_metricsByKey;

    internal ExternalRegistrySnapshot(
        long revision,
        IList<ExternalMetricSnapshot> metrics,
        IList<ExternalAlarmTemplateSnapshot> templates,
        IList<ExternalAlarmStateSnapshot> states)
    {
        Revision = revision;
        Metrics = new ReadOnlyCollection<ExternalMetricSnapshot>(
            new List<ExternalMetricSnapshot>(metrics));
        AlarmTemplates =
            new ReadOnlyCollection<ExternalAlarmTemplateSnapshot>(
                new List<ExternalAlarmTemplateSnapshot>(templates));
        AlarmStates = new ReadOnlyCollection<ExternalAlarmStateSnapshot>(
            new List<ExternalAlarmStateSnapshot>(states));

        var byKey = new Dictionary<string, ExternalMetricSnapshot>(
            StringComparer.Ordinal);
        foreach (var metric in metrics)
        {
            byKey[CreateMetricKey(
                metric.OwnerModId,
                metric.PrototypeId,
                metric.Id)] = metric;
        }

        m_metricsByKey = new ReadOnlyDictionary<string,
            ExternalMetricSnapshot>(byKey);
    }

    /// <summary>
    /// Reads an owner-scoped metric. A prototype-specific reader takes
    /// precedence over the provider's wildcard reader.
    /// </summary>
    public bool TryReadMetric(
        string ownerModId,
        string prototypeId,
        string metricId,
        object entity,
        out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(ownerModId) ||
            string.IsNullOrWhiteSpace(prototypeId) ||
            string.IsNullOrWhiteSpace(metricId))
        {
            return false;
        }

        if (m_metricsByKey.TryGetValue(
                CreateMetricKey(ownerModId, prototypeId, metricId),
                out var exact) && exact.TryRead(entity, out value))
        {
            return true;
        }

        return m_metricsByKey.TryGetValue(
                   CreateMetricKey(ownerModId, "*", metricId),
                   out var wildcard) &&
               wildcard.TryRead(entity, out value);
    }

    internal static string CreateMetricKey(
        string ownerModId,
        string prototypeId,
        string metricId)
    {
        return ownerModId + "\u001f" + prototypeId + "\u001f" + metricId;
    }
}
