using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace UNMA.Domain;

public enum AlarmSeverity
{
    Notice = 0,
    Warning = 1,
    Critical = 2,
    Emergency = 3,
}

public enum AlarmLogic
{
    All = 0,
    Any = 1,
}

public enum ComparisonOperator
{
    Less = 0,
    LessOrEqual = 1,
    Equal = 2,
    NotEqual = 3,
    GreaterOrEqual = 4,
    Greater = 5,
}

[DataContract]
public sealed class ConditionDefinition
{
    [DataMember(Order = 1)] public int EntityId;
    [DataMember(Order = 2)] public string EntityTitle = "";
    [DataMember(Order = 3)] public string EntityType = "";
    [DataMember(Order = 4)] public string MetricPath = "";
    [DataMember(Order = 5)] public string MetricLabel = "";
    [DataMember(Order = 6)] public ComparisonOperator Comparison;
    [DataMember(Order = 7)] public double Threshold;
    [DataMember(Order = 8)] public string ExpectedProductId = "";
    [DataMember(Order = 9)] public string EntityPrototypeId = "";
}

[DataContract]
public sealed class AlarmRuleDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string PanelId = "main";
    [DataMember(Order = 3)] public string Name = "NEUE MELDUNG";
    [DataMember(Order = 4)] public AlarmSeverity Severity = AlarmSeverity.Warning;
    [DataMember(Order = 5)] public AlarmLogic Logic = AlarmLogic.All;
    [DataMember(Order = 6)] public List<ConditionDefinition> Conditions = new();
    [DataMember(Order = 7)] public string ActiveColor = "#F0C541";
    [DataMember(Order = 8)] public string SoundId = "auto";
    [DataMember(Order = 9)] public bool Enabled = true;
}

[DataContract]
public sealed class PanelDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string Name = "MELDETAFEL";
    [DataMember(Order = 3)] public int Columns = 3;
    [DataMember(Order = 4)] public bool IncludeVanilla = true;
    [DataMember(Order = 5)] public bool IncludeSystem = true;
    [DataMember(Order = 6)] public string NotificationFilter = "";
}

[DataContract]
public sealed class AlarmSoundOverride
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public string SoundId = "auto";
}

[DataContract]
public sealed class UnmaConfiguration
{
    [DataMember(Order = 1)] public int SchemaVersion = 3;
    [DataMember(Order = 2)] public List<PanelDefinition> Panels = new();
    [DataMember(Order = 3)] public List<AlarmRuleDefinition> Rules = new();
    [DataMember(Order = 4)] public string WarningColor = "#F0C541";
    [DataMember(Order = 5)] public string CriticalColor = "#F05A32";
    [DataMember(Order = 6)] public string EmergencyColor = "#E51B23";
    [DataMember(Order = 7)] public float WindowX = 120f;
    [DataMember(Order = 8)] public float WindowY = 80f;
    [DataMember(Order = 9)] public float WindowWidth = 980f;
    [DataMember(Order = 10)] public float WindowHeight = 720f;
    [DataMember(Order = 11)] public List<AlarmSoundOverride> SoundOverrides =
        new();
    [DataMember(Order = 12)] public float LauncherX = -1f;
    [DataMember(Order = 13)] public float LauncherY = -1f;

    public static UnmaConfiguration CreateDefault()
    {
        var config = new UnmaConfiguration();
        config.Panels.Add(new PanelDefinition
        {
            Id = "main",
            Name = "ALLE MELDUNGEN",
            Columns = 3,
            IncludeVanilla = true,
            IncludeSystem = true,
        });
        config.Panels.Add(new PanelDefinition
        {
            Id = "supply",
            Name = "VERSORGUNG",
            Columns = 3,
            IncludeVanilla = true,
            IncludeSystem = true,
            NotificationFilter = "food,nahrung,worker,arbeiter,health,gesund,maintenance,wartung,power,strom",
        });
        return config;
    }

    public void Normalize()
    {
        if (SchemaVersion < 3)
        {
            LauncherX = -1f;
            LauncherY = -1f;
            SchemaVersion = 3;
        }
        Panels ??= new List<PanelDefinition>();
        Rules ??= new List<AlarmRuleDefinition>();
        SoundOverrides ??= new List<AlarmSoundOverride>();
        if (Panels.Count == 0)
        {
            Panels.Add(CreateDefault().Panels[0]);
        }

        foreach (var panel in Panels)
        {
            panel.Id = string.IsNullOrWhiteSpace(panel.Id)
                ? Guid.NewGuid().ToString("N")
                : panel.Id;
            panel.Name = string.IsNullOrWhiteSpace(panel.Name)
                ? "MELDETAFEL"
                : panel.Name.Trim();
            panel.Columns = Math.Max(1, Math.Min(8, panel.Columns));
            panel.NotificationFilter ??= "";
        }

        foreach (var rule in Rules)
        {
            rule.Id = string.IsNullOrWhiteSpace(rule.Id)
                ? Guid.NewGuid().ToString("N")
                : rule.Id;
            rule.PanelId ??= Panels[0].Id;
            rule.Name = string.IsNullOrWhiteSpace(rule.Name)
                ? "MELDUNG"
                : rule.Name.Trim();
            rule.Conditions ??= new List<ConditionDefinition>();
            rule.ActiveColor ??= "#F0C541";
            rule.SoundId ??= "auto";
        }

        SoundOverrides.RemoveAll(item =>
            item == null || string.IsNullOrWhiteSpace(item.AlarmId));
        foreach (var item in SoundOverrides)
        {
            item.AlarmId = item.AlarmId.Trim();
            item.SoundId = string.IsNullOrWhiteSpace(item.SoundId)
                ? "auto"
                : item.SoundId;
        }
    }
}

public sealed class AlarmView
{
    public string Key = "";
    public string Name = "";
    public string Detail = "";
    public string Source = "";
    public string PanelId = "";
    public string ActiveColor = "";
    public string SoundId = "auto";
    public string OverrideId = "";
    public AlarmSeverity Severity;
    public bool IsActive;
    public bool IsAcknowledged;
    public bool IsMissingSource;
    public double LastValue;
}

public static class AlarmEvaluation
{
    private const double EqualityTolerance = 0.000001d;

    public static bool Compare(
        double actual,
        ComparisonOperator comparison,
        double threshold)
    {
        return comparison switch
        {
            ComparisonOperator.Less => actual < threshold,
            ComparisonOperator.LessOrEqual => actual <= threshold,
            ComparisonOperator.Equal =>
                Math.Abs(actual - threshold) <= EqualityTolerance,
            ComparisonOperator.NotEqual =>
                Math.Abs(actual - threshold) > EqualityTolerance,
            ComparisonOperator.GreaterOrEqual => actual >= threshold,
            ComparisonOperator.Greater => actual > threshold,
            _ => false,
        };
    }

    public static bool Combine(
        IReadOnlyList<bool> values,
        AlarmLogic logic)
    {
        if (values == null || values.Count == 0)
        {
            return false;
        }

        if (logic == AlarmLogic.Any)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i])
                {
                    return true;
                }
            }
            return false;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (!values[i])
            {
                return false;
            }
        }
        return true;
    }

    public static AlarmTransition Transition(
        bool wasActive,
        bool wasAcknowledged,
        AlarmSeverity previousSeverity,
        bool isActive,
        AlarmSeverity severity,
        bool initiallyAcknowledged = false)
    {
        if (!isActive)
        {
            return new AlarmTransition(false, false, false);
        }

        var isNewOccurrence = !wasActive || previousSeverity != severity;
        return new AlarmTransition(
            true,
            isNewOccurrence ? initiallyAcknowledged : wasAcknowledged,
            isNewOccurrence);
    }
}

public readonly struct AlarmTransition
{
    public bool IsActive { get; }
    public bool IsAcknowledged { get; }
    public bool IsNewOccurrence { get; }

    public AlarmTransition(
        bool isActive,
        bool isAcknowledged,
        bool isNewOccurrence)
    {
        IsActive = isActive;
        IsAcknowledged = isAcknowledged;
        IsNewOccurrence = isNewOccurrence;
    }
}
