using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class SystemConditionDefinition
{
    [DataMember(Order = 1)] public string MetricId = "";
    [DataMember(Order = 2)] public ComparisonOperator Comparison;
    [DataMember(Order = 3)] public double Threshold;
}

[DataContract]
public sealed class SystemAlarmStageDefinition
{
    [DataMember(Order = 1)] public string Id = "";
    [DataMember(Order = 2)] public int Priority;
    [DataMember(Order = 3)] public bool Enabled = true;
    [DataMember(Order = 4)] public string Message = "MELDUNG";
    [DataMember(Order = 5)] public AlarmSeverity Severity = AlarmSeverity.Warning;
    [DataMember(Order = 6)] public AlarmLogic Logic = AlarmLogic.All;
    [DataMember(Order = 7)] public List<SystemConditionDefinition> Conditions =
        new();
    [DataMember(Order = 8)] public string ActiveColor = "auto";
    [DataMember(Order = 9)] public string SoundId = "auto";
}

[DataContract]
public sealed class SystemAlarmDefinition
{
    [DataMember(Order = 1)] public string Id = "";
    [DataMember(Order = 2)] public string DisplayName = "SYSTEMMELDUNG";
    [DataMember(Order = 3)] public bool Enabled = true;
    [DataMember(Order = 4)] public List<SystemAlarmStageDefinition> Stages =
        new();
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
    [DataMember(Order = 1)] public int SchemaVersion = 4;
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
    [DataMember(Order = 14)] public List<SystemAlarmDefinition> SystemAlarms =
        new();

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
        config.SystemAlarms.AddRange(CreateDefaultSystemAlarms());
        return config;
    }

    public static List<SystemAlarmDefinition> CreateDefaultSystemAlarms()
    {
        return new List<SystemAlarmDefinition>
        {
            new()
            {
                Id = "system:health",
                DisplayName = "GESUNDHEIT",
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        "GESUNDHEIT UNTER NORMALWERT",
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "health.value",
                            ComparisonOperator.Less,
                            10)),
                    CreateSystemStage(
                        "critical",
                        200,
                        "GESUNDHEIT KRITISCH",
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "health.value",
                            ComparisonOperator.LessOrEqual,
                            -10)),
                    CreateSystemStage(
                        "critical.pollution",
                        210,
                        "VERSCHMUTZUNG KRITISCH",
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "health.pollution_penalty",
                            ComparisonOperator.LessOrEqual,
                            -5)),
                    CreateSystemStage(
                        "emergency.worker_spiral",
                        300,
                        "NOTFALL: GESUNDHEITS-ARBEITERSPIRALE",
                        AlarmSeverity.Emergency,
                        CreateSystemCondition(
                            "health.disease_active",
                            ComparisonOperator.GreaterOrEqual,
                            1),
                        CreateSystemCondition(
                            "health.worker_spiral_margin",
                            ComparisonOperator.LessOrEqual,
                            0)),
                    CreateSystemStage(
                        "emergency.structural_spiral",
                        310,
                        "NOTFALL: STRUKTURELLE TODESSPIRALE",
                        AlarmSeverity.Emergency,
                        CreateSystemCondition(
                            "health.structural_value",
                            ComparisonOperator.Less,
                            0),
                        CreateSystemCondition(
                            "health.pollution_penalty",
                            ComparisonOperator.Less,
                            0)),
                },
            },
            new()
            {
                Id = "system:food",
                DisplayName = "NAHRUNGSVERSORGUNG",
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        "NAHRUNGSVORRAT NIEDRIG",
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "food.months",
                            ComparisonOperator.LessOrEqual,
                            12)),
                    CreateSystemStage(
                        "critical",
                        200,
                        "NAHRUNGSVORRAT KRITISCH",
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "food.months",
                            ComparisonOperator.LessOrEqual,
                            3)),
                    CreateSystemStage(
                        "emergency.starving",
                        300,
                        "NOTFALL: HUNGER-TODESSPIRALE",
                        AlarmSeverity.Emergency,
                        CreateSystemCondition(
                            "food.spiral",
                            ComparisonOperator.GreaterOrEqual,
                            1)),
                },
            },
            new()
            {
                Id = "system:workers",
                DisplayName = "ARBEITERRESERVE",
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        "ARBEITERRESERVE NIEDRIG",
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "workers.reserve_percent",
                            ComparisonOperator.Less,
                            5)),
                    CreateSystemStage(
                        "critical",
                        200,
                        "ARBEITER FEHLEN",
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "workers.reserve_percent",
                            ComparisonOperator.Less,
                            0)),
                },
            },
        };
    }

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        if (SchemaVersion < 3)
        {
            LauncherX = -1f;
            LauncherY = -1f;
        }
        Panels ??= new List<PanelDefinition>();
        Rules ??= new List<AlarmRuleDefinition>();
        SoundOverrides ??= new List<AlarmSoundOverride>();
        SystemAlarms ??= new List<SystemAlarmDefinition>();
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

        MergeDefaultSystemAlarms();
        if (loadedSchemaVersion < 4)
        {
            MigrateSystemSoundOverrides();
        }
        SchemaVersion = Math.Max(SchemaVersion, 4);
    }

    private void MigrateSystemSoundOverrides()
    {
        foreach (var soundOverride in SoundOverrides.Where(item =>
                     item.AlarmId.StartsWith(
                         "system:",
                         StringComparison.Ordinal) &&
                     !string.IsNullOrWhiteSpace(item.SoundId) &&
                     !string.Equals(
                         item.SoundId,
                         "auto",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var alarm = SystemAlarms.Find(candidate => string.Equals(
                candidate.Id,
                soundOverride.AlarmId,
                StringComparison.Ordinal));
            if (alarm == null)
            {
                continue;
            }
            foreach (var stage in alarm.Stages.Where(stage =>
                         string.IsNullOrWhiteSpace(stage.SoundId) ||
                         string.Equals(
                             stage.SoundId,
                             "auto",
                             StringComparison.OrdinalIgnoreCase)))
            {
                stage.SoundId = soundOverride.SoundId;
            }
        }
    }

    private void MergeDefaultSystemAlarms()
    {
        SystemAlarms.RemoveAll(item => item == null);
        foreach (var alarm in SystemAlarms)
        {
            alarm.Id ??= "";
            alarm.DisplayName ??= "";
            alarm.Stages ??= new List<SystemAlarmStageDefinition>();
            NormalizeSystemStages(alarm.Stages);
        }

        foreach (var defaultAlarm in CreateDefaultSystemAlarms())
        {
            var existingAlarm = SystemAlarms.Find(item => string.Equals(
                item.Id,
                defaultAlarm.Id,
                StringComparison.Ordinal));
            if (existingAlarm == null)
            {
                SystemAlarms.Add(CloneSystemAlarm(defaultAlarm));
                continue;
            }

            foreach (var defaultStage in defaultAlarm.Stages)
            {
                if (existingAlarm.Stages.Exists(stage => string.Equals(
                        stage.Id,
                        defaultStage.Id,
                        StringComparison.Ordinal)))
                {
                    continue;
                }
                existingAlarm.Stages.Add(CloneSystemStage(defaultStage));
            }
        }
    }

    private static void NormalizeSystemStages(
        List<SystemAlarmStageDefinition> stages)
    {
        stages.RemoveAll(item => item == null);
        foreach (var stage in stages)
        {
            stage.Id ??= "";
            stage.Message ??= "";
            stage.Conditions ??= new List<SystemConditionDefinition>();
            stage.Conditions.RemoveAll(item => item == null);
            foreach (var condition in stage.Conditions)
            {
                condition.MetricId ??= "";
            }
            stage.ActiveColor ??= "auto";
            stage.SoundId ??= "auto";
        }
    }

    private static SystemAlarmStageDefinition CreateSystemStage(
        string id,
        int priority,
        string message,
        AlarmSeverity severity,
        params SystemConditionDefinition[] conditions)
    {
        return new SystemAlarmStageDefinition
        {
            Id = id,
            Priority = priority,
            Enabled = true,
            Message = message,
            Severity = severity,
            Logic = AlarmLogic.All,
            Conditions = new List<SystemConditionDefinition>(conditions),
            ActiveColor = "auto",
            SoundId = "auto",
        };
    }

    private static SystemConditionDefinition CreateSystemCondition(
        string metricId,
        ComparisonOperator comparison,
        double threshold)
    {
        return new SystemConditionDefinition
        {
            MetricId = metricId,
            Comparison = comparison,
            Threshold = threshold,
        };
    }

    private static SystemAlarmDefinition CloneSystemAlarm(
        SystemAlarmDefinition source)
    {
        var clone = new SystemAlarmDefinition
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
        };
        foreach (var stage in source.Stages)
        {
            clone.Stages.Add(CloneSystemStage(stage));
        }
        return clone;
    }

    private static SystemAlarmStageDefinition CloneSystemStage(
        SystemAlarmStageDefinition source)
    {
        var clone = new SystemAlarmStageDefinition
        {
            Id = source.Id,
            Priority = source.Priority,
            Enabled = source.Enabled,
            Message = source.Message,
            Severity = source.Severity,
            Logic = source.Logic,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
        };
        foreach (var condition in source.Conditions)
        {
            clone.Conditions.Add(new SystemConditionDefinition
            {
                MetricId = condition.MetricId,
                Comparison = condition.Comparison,
                Threshold = condition.Threshold,
            });
        }
        return clone;
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

    public static SystemAlarmStageDefinition SelectSystemStage(
        SystemAlarmDefinition alarm,
        IReadOnlyDictionary<string, double> metrics)
    {
        if (alarm == null || !alarm.Enabled ||
            alarm.Stages == null || metrics == null)
        {
            return null;
        }

        SystemAlarmStageDefinition selected = null;
        foreach (var stage in alarm.Stages)
        {
            if (stage == null || !stage.Enabled ||
                stage.Conditions == null || stage.Conditions.Count == 0)
            {
                continue;
            }

            var results = new bool[stage.Conditions.Count];
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var condition = stage.Conditions[index];
                results[index] = condition != null &&
                                 metrics.TryGetValue(
                                     condition.MetricId ?? "",
                                     out var actual) &&
                                 Compare(
                                     actual,
                                     condition.Comparison,
                                     condition.Threshold);
            }

            if (!Combine(results, stage.Logic))
            {
                continue;
            }

            if (selected == null ||
                stage.Severity > selected.Severity ||
                stage.Severity == selected.Severity &&
                stage.Priority > selected.Priority)
            {
                selected = stage;
            }
        }
        return selected;
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

        var isNewOccurrence = !wasActive || severity > previousSeverity;
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
