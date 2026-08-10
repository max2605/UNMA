using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using UNMA.Localization;

namespace UNMA.Domain;

public enum AlarmSeverity
{
    Notice = 0,
    Warning = 1,
    Critical = 2,
    Emergency = 3,
}

public enum AlarmOperatorAction
{
    None = 0,
    OpenPanel = 1,
    OpenPanelAndCancelTemporaryMute = 2,
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

public enum ConditionValueMode
{
    Absolute = 0,
    PercentOfReference = 1,
}

public enum VanillaNotificationBehavior
{
    Normal = 0,
    Silent = 1,
    Hidden = 2,
    Ignored = 3,
}

public enum VanillaNotificationScope
{
    NotificationType = 0,
    EntityPrototype = 1,
    Entity = 2,
}

public enum InstrumentDisplayType
{
    EdgewiseVertical = 0,
    EdgewiseHorizontal = 1,
    RoundGauge = 2,
    SevenSegmentRed = 3,
    SevenSegmentGreen = 4,
    NixieTube = 5,
    CrtAmber = 6,
    CrtGreen = 7,
    PaperRecorder = 8,
}

public enum InstrumentAggregationMode
{
    Single = 0,
    Sum = 1,
    Average = 2,
    Minimum = 3,
    Maximum = 4,
}

public enum InstrumentTrendMode
{
    None = 0,
    DecreaseAbsolute = 1,
    DecreasePercent = 2,
    IncreaseAbsolute = 3,
    IncreasePercent = 4,
    SustainComparison = 5,
}

[DataContract]
public sealed class InstrumentSourceDefinition
{
    [DataMember(Order = 1)] public int EntityId = -1;
    [DataMember(Order = 2)] public string EntityTitle = "";
    [DataMember(Order = 3)] public string EntityPrototypeId = "";
}

[DataContract]
public sealed class InstrumentPanelDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string Name = UnmaText.Get(
        "default.instrument_panel",
        "INSTRUMENT PANEL");
}

[DataContract]
public sealed class InstrumentDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string Title = UnmaText.Get(
        "default.measurement",
        "MEASUREMENT");
    [DataMember(Order = 3)] public InstrumentDisplayType DisplayType =
        InstrumentDisplayType.RoundGauge;
    [DataMember(Order = 4)] public int EntityId = -1;
    [DataMember(Order = 5)] public string EntityTitle = "";
    [DataMember(Order = 6)] public string EntityPrototypeId = "";
    [DataMember(Order = 7)] public string MetricPath = "";
    [DataMember(Order = 8)] public string MetricLabel = "";
    [DataMember(Order = 9)] public string Unit = "";
    [DataMember(Order = 10)] public double Minimum;
    [DataMember(Order = 11)] public double Maximum = 100d;
    [DataMember(Order = 12)] public string PanelId = "instruments-main";
    [DataMember(Order = 13)] public List<InstrumentSourceDefinition> Sources =
        new();
    [DataMember(Order = 14)] public InstrumentAggregationMode Aggregation;
    [DataMember(Order = 15)] public int HistoryDurationSeconds = 3600;
    [DataMember(Order = 16)] public int HistoryDurationAmount = 100;
    [DataMember(Order = 17)] public GameTimeUnit HistoryDurationUnit =
        GameTimeUnit.Year;
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
    [DataMember(Order = 10)] public ConditionValueMode ValueMode;
    [DataMember(Order = 11)] public string ReferenceMetricPath = "";
    [DataMember(Order = 12)] public string ReferenceMetricLabel = "";
    [DataMember(Order = 13)] public string InstrumentId = "";
    [DataMember(Order = 14)] public InstrumentTrendMode TrendMode;
    [DataMember(Order = 15)] public int WindowSeconds = 60;
    [DataMember(Order = 16)] public double DeltaThreshold = 1d;
    [DataMember(Order = 17)] public int WindowAmount = 1;
    [DataMember(Order = 18)] public GameTimeUnit WindowUnit =
        GameTimeUnit.Month;
    [DataMember(Order = 19)] public double Hysteresis;
}

[DataContract]
public sealed class SystemConditionDefinition
{
    [DataMember(Order = 1)] public string MetricId = "";
    [DataMember(Order = 2)] public ComparisonOperator Comparison;
    [DataMember(Order = 3)] public double Threshold;
    [DataMember(Order = 4)] public double Hysteresis;
}

[DataContract]
public sealed class AlarmEscalationDefinition
{
    [DataMember(Order = 1)] public bool Enabled;
    [DataMember(Order = 2)] public int AfterTicks;
    [DataMember(Order = 3)] public AlarmSeverity Severity =
        AlarmSeverity.Critical;
    [DataMember(Order = 4)] public string SoundId = "";
    [DataMember(Order = 5)] public AlarmOperatorAction OperatorAction;
}

[DataContract]
public sealed class SystemAlarmStageDefinition
{
    [DataMember(Order = 1)] public string Id = "";
    [DataMember(Order = 2)] public int Priority;
    [DataMember(Order = 3)] public bool Enabled = true;
    [DataMember(Order = 4)] public string Message = UnmaText.Get(
        "default.notification",
        "NOTIFICATION");
    [DataMember(Order = 5)] public AlarmSeverity Severity = AlarmSeverity.Warning;
    [DataMember(Order = 6)] public AlarmLogic Logic = AlarmLogic.All;
    [DataMember(Order = 7)] public List<SystemConditionDefinition> Conditions =
        new();
    [DataMember(Order = 8)] public string ActiveColor = "auto";
    [DataMember(Order = 9)] public string SoundId = "auto";
    [DataMember(Order = 10)] public int ActivationDelayTicks;
    [DataMember(Order = 11)] public int ResetDelayTicks;
    [DataMember(Order = 12)] public int MinimumActiveTicks;
    [DataMember(Order = 13)] public AlarmOperatorAction OperatorAction;
}

[DataContract]
public sealed class SystemAlarmDefinition
{
    [DataMember(Order = 1)] public string Id = "";
    [DataMember(Order = 2)] public string DisplayName = UnmaText.Get(
        "default.system_notification",
        "SYSTEM NOTIFICATION");
    [DataMember(Order = 3)] public bool Enabled = true;
    [DataMember(Order = 4)] public List<SystemAlarmStageDefinition> Stages =
        new();
    [DataMember(Order = 5)] public bool AutoAcknowledgeOnClear;
}

[DataContract]
public sealed class AlarmRuleDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string PanelId = "main";
    [DataMember(Order = 3)] public string Name = UnmaText.Get("auto.fe04a9d0e58c");
    [DataMember(Order = 4)] public AlarmSeverity Severity = AlarmSeverity.Warning;
    [DataMember(Order = 5)] public AlarmLogic Logic = AlarmLogic.All;
    [DataMember(Order = 6)] public List<ConditionDefinition> Conditions = new();
    [DataMember(Order = 7)] public string ActiveColor = "#F0C541";
    [DataMember(Order = 8)] public string SoundId = "auto";
    [DataMember(Order = 9)] public bool Enabled = true;
    [DataMember(Order = 10)] public bool AutoAcknowledgeOnClear;
    [DataMember(Order = 11)] public List<string> LinkedPanelIds = new();
    [DataMember(Order = 12)] public int ActivationDelayTicks;
    [DataMember(Order = 13)] public int ResetDelayTicks;
    [DataMember(Order = 14)] public int MinimumActiveTicks;
    [DataMember(Order = 15)] public AlarmEscalationDefinition Escalation =
        new();
}

[DataContract]
public sealed class PanelSlotDefinition
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public string DisplayName = UnmaText.Get(
        "default.notification",
        "NOTIFICATION");
    [DataMember(Order = 3)] public string Detail = "";
    [DataMember(Order = 4)] public string Source = "";
    [DataMember(Order = 5)] public AlarmSeverity Severity =
        AlarmSeverity.Warning;
    [DataMember(Order = 6)] public string ActiveColor = "#F0C541";
}

[DataContract]
public sealed class AlarmAreaDefinition
{
    [DataMember(Order = 1)] public string Id =
        Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string Name = "AREA";
}

[DataContract]
public sealed class PanelDefinition
{
    [DataMember(Order = 1)] public string Id = Guid.NewGuid().ToString("N");
    [DataMember(Order = 2)] public string Name = UnmaText.Get(
        "default.panel",
        "PANEL");
    [DataMember(Order = 3)] public int Columns = 3;
    [DataMember(Order = 4)] public bool IncludeVanilla = true;
    [DataMember(Order = 5)] public bool IncludeSystem = true;
    [DataMember(Order = 6)] public string NotificationFilter = "";
    [DataMember(Order = 7)] public List<PanelSlotDefinition> Slots = new();
    [DataMember(Order = 8)] public List<string> ExcludedAlarmIds = new();
    [DataMember(Order = 9)] public bool IsDashboard;
    [DataMember(Order = 10)] public int OwnerEntityId = -1;
    [DataMember(Order = 11)] public string OwnerEntityTitle = "";
    [DataMember(Order = 12)] public string OwnerEntityPrototypeId = "";
    [DataMember(Order = 13)] public string OwnerEntityType = "";
    [DataMember(Order = 14)] public string AreaId = "";
}

[DataContract]
public sealed class AlarmSoundOverride
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public string SoundId = "auto";
    [DataMember(Order = 3)] public bool AutoAcknowledgeOnClear;
    [DataMember(Order = 4)] public bool IsGloballyDisabled;
}

[DataContract]
public sealed class VanillaNotificationRule
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public VanillaNotificationScope Scope;
    [DataMember(Order = 3)] public VanillaNotificationBehavior Behavior;
    [DataMember(Order = 4)] public int EntityId = -1;
    [DataMember(Order = 5)] public string EntityPrototypeId = "";
}

[DataContract]
public sealed class AlarmMemoryDefinition
{
    [DataMember(Order = 1)] public string Key = "";
    [DataMember(Order = 2)] public string Name = "";
    [DataMember(Order = 3)] public string Detail = "";
    [DataMember(Order = 4)] public string Source = "";
    [DataMember(Order = 5)] public string PanelId = "";
    [DataMember(Order = 6)] public string ActiveColor = "";
    [DataMember(Order = 7)] public string SoundId = "auto";
    [DataMember(Order = 8)] public string OverrideId = "";
    [DataMember(Order = 9)] public AlarmSeverity Severity;
    [DataMember(Order = 10)] public bool IsActive;
    [DataMember(Order = 11)] public bool IsAcknowledged;
    [DataMember(Order = 12)] public bool IsGoneUnacknowledged;
    [DataMember(Order = 13)] public bool IsMissingSource;
    [DataMember(Order = 14)] public double LastValue;
    [DataMember(Order = 15)] public long Sequence;
    [DataMember(Order = 16)] public string OccurrenceId = "";
    [DataMember(Order = 17)] public int OccurrencePriority;
    [DataMember(Order = 18)] public string SlotId = "";
    [DataMember(Order = 19)] public bool AutoAcknowledgeOnClear;
    [DataMember(Order = 20)] public int EntityId = -1;
    [DataMember(Order = 21)] public string EntityPrototypeId = "";
    [DataMember(Order = 22)] public string EntityTitle = "";
}

[DataContract]
public sealed class AlarmConditionLatchMemoryDefinition
{
    [DataMember(Order = 1)] public int ConditionIndex;
    [DataMember(Order = 2)] public bool IsLatched;
}

[DataContract]
public sealed class AlarmTimingMemoryDefinition
{
    [DataMember(Order = 1)] public string OwnerKey = "";
    [DataMember(Order = 2)] public string DefinitionSignature = "";
    [DataMember(Order = 3)] public bool IsActive;
    [DataMember(Order = 4)] public long ActivationPendingSinceTick =
        AlarmTimingState.NoTick;
    [DataMember(Order = 5)] public long ActiveSinceTick =
        AlarmTimingState.NoTick;
    [DataMember(Order = 6)] public long ResetPendingSinceTick =
        AlarmTimingState.NoTick;
    [DataMember(Order = 7)] public long LastObservedTick =
        AlarmTimingState.NoTick;
    [DataMember(Order = 8)]
    public List<AlarmConditionLatchMemoryDefinition> ConditionLatches = new();
}

[DataContract]
public sealed class AlarmHistoryDefinition
{
    [DataMember(Order = 1)] public long Sequence;
    [DataMember(Order = 2)] public string AlarmKey = "";
    [DataMember(Order = 3)] public string Message = "";
    [DataMember(Order = 4)] public string Detail = "";
    [DataMember(Order = 5)] public string Source = "";
    [DataMember(Order = 6)] public string PanelId = "";
    [DataMember(Order = 7)] public AlarmSeverity Severity;
    [DataMember(Order = 8)] public bool IsGone;
    [DataMember(Order = 9)] public bool IsAcknowledged;
    [DataMember(Order = 10)] public double RaisedAtTicks;
    [DataMember(Order = 11)] public double ClearedAtTicks;
    [DataMember(Order = 12)] public double AcknowledgedAtTicks;

    public string StateCode => IsGone
        ? IsAcknowledged ? "KGQ" : "KG"
        : IsAcknowledged ? "KQ" : "K";

    public bool CanDelete => IsGone && IsAcknowledged;

    public bool SetState(bool isGone, bool isAcknowledged)
    {
        var nextAcknowledged = IsAcknowledged || isAcknowledged;
        var changed = IsGone != isGone ||
                      IsAcknowledged != nextAcknowledged;
        IsGone = isGone;
        IsAcknowledged = nextAcknowledged;
        return changed;
    }

    public bool SetState(
        bool isGone,
        bool isAcknowledged,
        double currentGameTicks)
    {
        var changed = SetState(isGone, isAcknowledged);
        if (isGone)
        {
            if (ClearedAtTicks <= 0d && currentGameTicks > 0d)
            {
                ClearedAtTicks = currentGameTicks;
                changed = true;
            }
        }
        else if (ClearedAtTicks > 0d)
        {
            ClearedAtTicks = 0d;
            changed = true;
        }
        if (isAcknowledged &&
            AcknowledgedAtTicks <= 0d &&
            currentGameTicks > 0d)
        {
            AcknowledgedAtTicks = currentGameTicks;
            changed = true;
        }
        return changed;
    }
}

[DataContract]
public sealed class UnmaConfiguration
{
    [DataMember(Order = 1)] public int SchemaVersion = 20;
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
    [DataMember(Order = 15)] public List<AlarmMemoryDefinition> AlarmMemories =
        new();
    [DataMember(Order = 16)] public List<AlarmHistoryDefinition> AlarmHistory =
        new();
    [DataMember(Order = 17)]
    public bool LegacySustainedAlarmReconciliationPending;
    [DataMember(Order = 18)] public int UiScalePercent = 100;
    [DataMember(Order = 19)] public float EditorWindowX = 180f;
    [DataMember(Order = 20)] public float EditorWindowY = 110f;
    [DataMember(Order = 21)] public float EditorWindowWidth = 1080f;
    [DataMember(Order = 22)] public float EditorWindowHeight = 720f;
    [DataMember(Order = 23)]
    public List<VanillaNotificationRule> VanillaNotificationRules = new();
    [DataMember(Order = 24)]
    public List<InstrumentDefinition> Instruments = new();
    [DataMember(Order = 25)]
    public List<InstrumentPanelDefinition> InstrumentPanels = new();
    [DataMember(Order = 26)]
    public List<AlarmTimingMemoryDefinition> AlarmTimingMemories = new();
    [DataMember(Order = 27)]
    public List<AlarmAreaDefinition> AlarmAreas = new();

    public static UnmaConfiguration CreateDefault()
    {
        var config = new UnmaConfiguration();
        config.InstrumentPanels.Add(new InstrumentPanelDefinition
        {
            Id = "instruments-main",
            Name = UnmaText.Get(
                "default.main_instrument_panel",
                "MAIN INSTRUMENT PANEL"),
        });
        config.Panels.Add(new PanelDefinition
        {
            Id = "main",
            Name = UnmaText.Get("auto.778a27cbcdf2"),
            Columns = 3,
            IncludeVanilla = true,
            IncludeSystem = true,
            IsDashboard = true,
        });
        config.Panels.Add(new PanelDefinition
        {
            Id = "supply",
            Name = UnmaText.Get("default.supply_panel", "SUPPLY"),
            Columns = 3,
            IncludeVanilla = true,
            IncludeSystem = true,
            NotificationFilter = "food,nahrung,worker,arbeiter,health,gesund,maintenance,wartung,power,strom",
        });
        config.SystemAlarms.AddRange(CreateDefaultSystemAlarms());
        config.SeedPanelSlots(includeMemories: false);
        return config;
    }

    public static List<SystemAlarmDefinition> CreateDefaultSystemAlarms()
    {
        return new List<SystemAlarmDefinition>
        {
            new()
            {
                Id = "system:health",
                DisplayName = UnmaText.Get(
                    "default.health_alarm",
                    "HEALTH"),
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        UnmaText.Get("auto.d11c28379225"),
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "health.value",
                            ComparisonOperator.Less,
                            10)),
                    CreateSystemStage(
                        "critical",
                        200,
                        UnmaText.Get("auto.80517e373fe8"),
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "health.value",
                            ComparisonOperator.LessOrEqual,
                            -10)),
                    CreateSystemStage(
                        "critical.pollution",
                        210,
                        UnmaText.Get("auto.288803ad208e"),
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "health.pollution_penalty",
                            ComparisonOperator.LessOrEqual,
                            -5)),
                    CreateSystemStage(
                        "emergency.worker_spiral",
                        300,
                        UnmaText.Get("auto.a6f12e4f8a8d"),
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
                        UnmaText.Get("auto.16eef1f4c097"),
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
                DisplayName = UnmaText.Get(
                    "default.food_supply_alarm",
                    "FOOD SUPPLY"),
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        UnmaText.Get("auto.ec076a9bd367"),
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "food.months",
                            ComparisonOperator.LessOrEqual,
                            12)),
                    CreateSystemStage(
                        "critical",
                        200,
                        UnmaText.Get("auto.c5629fd15dcf"),
                        AlarmSeverity.Critical,
                        CreateSystemCondition(
                            "food.months",
                            ComparisonOperator.LessOrEqual,
                            3)),
                    CreateSystemStage(
                        "emergency.starving",
                        300,
                        UnmaText.Get("auto.dae3282f0df2"),
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
                DisplayName = UnmaText.Get(
                    "default.worker_reserve_alarm",
                    "WORKER RESERVE"),
                Enabled = true,
                Stages = new List<SystemAlarmStageDefinition>
                {
                    CreateSystemStage(
                        "warning",
                        100,
                        UnmaText.Get("auto.95bd3959b728"),
                        AlarmSeverity.Warning,
                        CreateSystemCondition(
                            "workers.reserve_percent",
                            ComparisonOperator.Less,
                            5)),
                    CreateSystemStage(
                        "critical",
                        200,
                        UnmaText.Get("auto.78780c9a06e6"),
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
        if (loadedSchemaVersion < 12)
        {
            UiScalePercent = 100;
            EditorWindowX = 180f;
            EditorWindowY = 110f;
            EditorWindowWidth = 1080f;
            EditorWindowHeight = 720f;
        }
        UiScalePercent = UiScalePercent <= 0
            ? 100
            : Math.Max(75, Math.Min(200, UiScalePercent));
        EditorWindowX = NormalizeFinite(EditorWindowX, 180f);
        EditorWindowY = NormalizeFinite(EditorWindowY, 110f);
        EditorWindowWidth = Math.Max(
            700f,
            NormalizeFinite(EditorWindowWidth, 1080f));
        EditorWindowHeight = Math.Max(
            520f,
            NormalizeFinite(EditorWindowHeight, 720f));
        Panels ??= new List<PanelDefinition>();
        Rules ??= new List<AlarmRuleDefinition>();
        SoundOverrides ??= new List<AlarmSoundOverride>();
        SystemAlarms ??= new List<SystemAlarmDefinition>();
        AlarmMemories ??= new List<AlarmMemoryDefinition>();
        AlarmHistory ??= new List<AlarmHistoryDefinition>();
        VanillaNotificationRules ??= new List<VanillaNotificationRule>();
        Instruments ??= new List<InstrumentDefinition>();
        InstrumentPanels ??= new List<InstrumentPanelDefinition>();
        AlarmTimingMemories ??= new List<AlarmTimingMemoryDefinition>();
        AlarmAreas ??= new List<AlarmAreaDefinition>();
        if (Panels.Count == 0)
        {
            Panels.Add(CreateDefault().Panels[0]);
        }

        foreach (var panel in Panels)
        {
            panel.Id = string.IsNullOrWhiteSpace(panel.Id)
                ? Guid.NewGuid().ToString("N")
                : panel.Id.Trim();
            panel.Name = string.IsNullOrWhiteSpace(panel.Name)
                ? UnmaText.Get("default.panel", "PANEL")
                : panel.Name.Trim();
            panel.Columns = Math.Max(1, Math.Min(8, panel.Columns));
            panel.NotificationFilter ??= "";
            panel.Slots ??= new List<PanelSlotDefinition>();
            panel.ExcludedAlarmIds ??= new List<string>();
            panel.ExcludedAlarmIds = panel.ExcludedAlarmIds
                .Where(alarmId => !string.IsNullOrWhiteSpace(alarmId))
                .Select(alarmId => alarmId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (loadedSchemaVersion < 12 || panel.OwnerEntityId <= 0)
            {
                panel.OwnerEntityId = -1;
                panel.OwnerEntityTitle = "";
                panel.OwnerEntityPrototypeId = "";
                panel.OwnerEntityType = "";
            }
            else
            {
                panel.OwnerEntityTitle =
                    panel.OwnerEntityTitle?.Trim() ?? "";
                panel.OwnerEntityPrototypeId =
                    panel.OwnerEntityPrototypeId?.Trim() ?? "";
                panel.OwnerEntityType = panel.OwnerEntityType?.Trim() ?? "";
            }
            NormalizePanelSlots(panel.Slots);
        }

        var dashboardPanel = loadedSchemaVersion >= 10
            ? Panels.FirstOrDefault(panel => panel.IsDashboard)
            : null;
        dashboardPanel ??= Panels.FirstOrDefault(panel => string.Equals(
                              panel.Id,
                              "main",
                              StringComparison.Ordinal)) ??
                          Panels[0];
        foreach (var panel in Panels)
        {
            panel.IsDashboard = ReferenceEquals(panel, dashboardPanel);
        }
        if (!Panels.Any(panel => panel != null && !panel.IsDashboard))
        {
            var fallbackId = Panels.Any(panel => string.Equals(
                panel?.Id,
                "supply",
                StringComparison.Ordinal))
                ? Guid.NewGuid().ToString("N")
                : "supply";
            Panels.Add(new PanelDefinition
            {
                Id = fallbackId,
                Name = UnmaText.Get("default.supply_panel", "SUPPLY"),
                Columns = 3,
                IncludeVanilla = true,
                IncludeSystem = true,
                IsDashboard = false,
            });
        }
        if (loadedSchemaVersion < 20)
        {
            AlarmAreas = new List<AlarmAreaDefinition>();
            AlarmAreaPolicy.NormalizePanelAssignments(
                Panels,
                AlarmAreas,
                discardAssignments: true);
        }
        else
        {
            AlarmAreas = AlarmAreaPolicy.Normalize(AlarmAreas);
            AlarmAreaPolicy.NormalizePanelAssignments(Panels, AlarmAreas);
        }
        // Keep legacy dashboard slots serialized for lossless downgrade and
        // recovery. Dashboard projection and editing deliberately ignore them.

        var fallbackRulePanelId = Panels.First(panel =>
            panel != null && !panel.IsDashboard).Id;
        foreach (var rule in Rules)
        {
            rule.Escalation = loadedSchemaVersion < 19
                ? AlarmEscalationPolicy.LegacyMigrationDefaults
                : AlarmEscalationPolicy.Normalize(
                    rule.Escalation,
                    rule.Severity);
            var timing = loadedSchemaVersion < 18
                ? AlarmTimingPolicy.LegacyMigrationDefaults
                : AlarmTimingPolicy.Normalize(new AlarmTimingSettings(
                    rule.ActivationDelayTicks,
                    rule.ResetDelayTicks,
                    rule.MinimumActiveTicks,
                    AlarmTimingPolicy.LegacyHysteresis));
            rule.ActivationDelayTicks = timing.ActivationDelayTicks;
            rule.ResetDelayTicks = timing.ResetDelayTicks;
            rule.MinimumActiveTicks = timing.MinimumActiveTicks;
            rule.Id = string.IsNullOrWhiteSpace(rule.Id)
                ? Guid.NewGuid().ToString("N")
                : rule.Id.Trim();
            rule.PanelId = rule.PanelId?.Trim() ?? "";
            if (!Panels.Any(panel =>
                    panel != null &&
                    !panel.IsDashboard &&
                    string.Equals(
                        panel.Id,
                        rule.PanelId,
                        StringComparison.Ordinal)))
            {
                rule.PanelId = fallbackRulePanelId;
            }
            rule.LinkedPanelIds =
                PanelTopologyPolicy.NormalizeLinkedPanelIds(
                    rule.PanelId,
                    rule.LinkedPanelIds,
                    Panels);
            rule.Name = string.IsNullOrWhiteSpace(rule.Name)
                ? UnmaText.Get("default.notification", "NOTIFICATION")
                : rule.Name.Trim();
            rule.Conditions ??= new List<ConditionDefinition>();
            rule.Conditions.RemoveAll(condition => condition == null);
            foreach (var condition in rule.Conditions)
            {
                condition.Hysteresis = loadedSchemaVersion < 18
                    ? AlarmTimingPolicy.LegacyHysteresis
                    : NormalizeHysteresis(condition.Hysteresis);
                condition.EntityTitle ??= "";
                condition.EntityType ??= "";
                condition.MetricPath = condition.MetricPath?.Trim() ?? "";
                condition.MetricLabel ??= "";
                condition.ExpectedProductId ??= "";
                condition.EntityPrototypeId ??= "";
                if (condition.ValueMode != ConditionValueMode.Absolute &&
                    condition.ValueMode !=
                    ConditionValueMode.PercentOfReference)
                {
                    condition.ValueMode = ConditionValueMode.Absolute;
                }
                condition.ReferenceMetricPath =
                    condition.ReferenceMetricPath?.Trim() ?? "";
                condition.ReferenceMetricLabel ??= "";
                condition.InstrumentId = condition.InstrumentId?.Trim() ?? "";
                if (!Enum.IsDefined(
                        typeof(InstrumentTrendMode),
                        condition.TrendMode))
                {
                    condition.TrendMode = InstrumentTrendMode.None;
                }
                condition.WindowSeconds = Math.Max(
                    1,
                    Math.Min(86400, condition.WindowSeconds <= 0
                        ? 60
                        : condition.WindowSeconds));
                if (loadedSchemaVersion < 17)
                {
                    GameTimeWindowPolicy.FromLegacyRealSeconds(
                        condition.WindowSeconds,
                        out condition.WindowAmount,
                        out condition.WindowUnit);
                }
                if (!Enum.IsDefined(
                        typeof(GameTimeUnit),
                        condition.WindowUnit))
                {
                    condition.WindowUnit = GameTimeUnit.Month;
                }
                condition.WindowAmount = GameTimeWindowPolicy.ClampAmount(
                    condition.WindowAmount,
                    condition.WindowUnit);
                if (double.IsNaN(condition.DeltaThreshold) ||
                    double.IsInfinity(condition.DeltaThreshold) ||
                    condition.DeltaThreshold < 0d)
                {
                    condition.DeltaThreshold = 1d;
                }
            }
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

        VanillaNotificationRules.RemoveAll(rule =>
            rule == null ||
            !VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                rule.AlarmId) ||
            !Enum.IsDefined(
                typeof(VanillaNotificationScope),
                rule.Scope) ||
            !Enum.IsDefined(
                typeof(VanillaNotificationBehavior),
                rule.Behavior) ||
            rule.Scope == VanillaNotificationScope.Entity &&
            rule.EntityId < 0 ||
            rule.Scope == VanillaNotificationScope.EntityPrototype &&
            string.IsNullOrWhiteSpace(rule.EntityPrototypeId));
        foreach (var rule in VanillaNotificationRules)
        {
            rule.AlarmId = rule.AlarmId.Trim();
            rule.EntityPrototypeId = rule.EntityPrototypeId?.Trim() ?? "";
        }
        VanillaNotificationRules = VanillaNotificationRules
            .GroupBy(
                VanillaNotificationSuppressionPolicy.RuleIdentity,
                StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();

        InstrumentPanels.RemoveAll(item => item == null);
        foreach (var panel in InstrumentPanels)
        {
            panel.Id = string.IsNullOrWhiteSpace(panel.Id)
                ? Guid.NewGuid().ToString("N")
                : panel.Id.Trim();
            panel.Name = string.IsNullOrWhiteSpace(panel.Name)
                ? UnmaText.Get(
                    "default.instrument_panel",
                    "INSTRUMENT PANEL")
                : panel.Name.Trim();
        }
        InstrumentPanels = InstrumentPanels
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        if (InstrumentPanels.Count == 0)
        {
            InstrumentPanels.Add(new InstrumentPanelDefinition
            {
                Id = "instruments-main",
                Name = UnmaText.Get(
                    "default.main_instrument_panel",
                    "MAIN INSTRUMENT PANEL"),
            });
        }
        var defaultInstrumentPanelId = InstrumentPanels[0].Id;

        Instruments.RemoveAll(item =>
            item == null || string.IsNullOrWhiteSpace(item.MetricPath));
        foreach (var instrument in Instruments)
        {
            instrument.Id = string.IsNullOrWhiteSpace(instrument.Id)
                ? Guid.NewGuid().ToString("N")
                : instrument.Id.Trim();
            instrument.Title = string.IsNullOrWhiteSpace(instrument.Title)
                ? UnmaText.Get("default.measurement", "MEASUREMENT")
                : instrument.Title.Trim();
            instrument.EntityTitle = instrument.EntityTitle?.Trim() ?? "";
            instrument.EntityPrototypeId =
                instrument.EntityPrototypeId?.Trim() ?? "";
            instrument.MetricPath = instrument.MetricPath.Trim();
            instrument.MetricLabel = instrument.MetricLabel?.Trim() ?? "";
            instrument.Unit = instrument.Unit?.Trim() ?? "";
            instrument.PanelId = instrument.PanelId?.Trim() ?? "";
            instrument.Sources ??= new List<InstrumentSourceDefinition>();
            var matchingLegacySource = instrument.Sources.FirstOrDefault(
                source => source != null &&
                          source.EntityId == instrument.EntityId);
            if (loadedSchemaVersion < 16 &&
                instrument.Sources.Count == 0 &&
                instrument.EntityId > 0)
            {
                instrument.Sources.Insert(0, new InstrumentSourceDefinition
                {
                    EntityId = instrument.EntityId,
                    EntityTitle = instrument.EntityTitle,
                    EntityPrototypeId = instrument.EntityPrototypeId,
                });
            }
            else if (matchingLegacySource != null)
            {
                if (string.IsNullOrWhiteSpace(matchingLegacySource.EntityTitle))
                {
                    matchingLegacySource.EntityTitle = instrument.EntityTitle;
                }
                if (string.IsNullOrWhiteSpace(
                        matchingLegacySource.EntityPrototypeId))
                {
                    matchingLegacySource.EntityPrototypeId =
                        instrument.EntityPrototypeId;
                }
            }
            instrument.Sources.RemoveAll(source =>
                source == null || source.EntityId <= 0);
            foreach (var source in instrument.Sources)
            {
                source.EntityTitle = source.EntityTitle?.Trim() ?? "";
                source.EntityPrototypeId =
                    source.EntityPrototypeId?.Trim() ?? "";
            }
            instrument.Sources = instrument.Sources
                .GroupBy(source => source.EntityId)
                .Select(group => group.First())
                .ToList();
            if (instrument.Sources.Count > 0)
            {
                var primarySource = instrument.Sources[0];
                instrument.EntityId = primarySource.EntityId;
                instrument.EntityPrototypeId =
                    primarySource.EntityPrototypeId;
                if (instrument.Sources.Count == 1)
                {
                    instrument.EntityTitle = primarySource.EntityTitle;
                }
                else if (string.IsNullOrWhiteSpace(instrument.EntityTitle))
                {
                    instrument.EntityTitle =
                        UnmaText.Format(
                            "default.source_count",
                            "{0} SOURCES",
                            instrument.Sources.Count);
                }
            }
            if (!Enum.IsDefined(
                    typeof(InstrumentAggregationMode),
                    instrument.Aggregation))
            {
                instrument.Aggregation = InstrumentAggregationMode.Single;
            }
            instrument.HistoryDurationSeconds = Math.Max(
                60,
                Math.Min(86400, instrument.HistoryDurationSeconds <= 0
                    ? 3600
                    : instrument.HistoryDurationSeconds));
            if (loadedSchemaVersion < 17)
            {
                GameTimeWindowPolicy.FromLegacyRealSeconds(
                    instrument.HistoryDurationSeconds,
                    out instrument.HistoryDurationAmount,
                    out instrument.HistoryDurationUnit);
            }
            if (!Enum.IsDefined(
                    typeof(GameTimeUnit),
                    instrument.HistoryDurationUnit))
            {
                instrument.HistoryDurationUnit = GameTimeUnit.Year;
            }
            // Recorder archives are deliberately retained for the complete
            // century range offered by the archive UI.
            instrument.HistoryDurationAmount = 100;
            instrument.HistoryDurationUnit = GameTimeUnit.Year;
            if (!InstrumentPanels.Any(panel => string.Equals(
                    panel.Id,
                    instrument.PanelId,
                    StringComparison.Ordinal)))
            {
                instrument.PanelId = defaultInstrumentPanelId;
            }
            if (!Enum.IsDefined(
                    typeof(InstrumentDisplayType),
                    instrument.DisplayType))
            {
                instrument.DisplayType = InstrumentDisplayType.RoundGauge;
            }
            if (double.IsNaN(instrument.Minimum) ||
                double.IsInfinity(instrument.Minimum))
            {
                instrument.Minimum = 0d;
            }
            if (double.IsNaN(instrument.Maximum) ||
                double.IsInfinity(instrument.Maximum) ||
                instrument.Maximum <= instrument.Minimum)
            {
                instrument.Maximum = instrument.Minimum + 100d;
            }
        }
        Instruments.RemoveAll(item => item.Sources.Count == 0);
        Instruments = Instruments
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();

        AlarmMemories.RemoveAll(item =>
            item == null ||
            string.IsNullOrWhiteSpace(item.Key) ||
            !item.IsActive && !item.IsGoneUnacknowledged);
        foreach (var memory in AlarmMemories)
        {
            memory.Key = memory.Key.Trim();
            memory.Name ??= "";
            memory.Detail ??= "";
            memory.Source ??= "";
            memory.PanelId ??= "";
            memory.ActiveColor ??= "";
            memory.SoundId = string.IsNullOrWhiteSpace(memory.SoundId)
                ? "auto"
                : memory.SoundId;
            memory.OverrideId ??= "";
            memory.OccurrenceId ??= "";
            memory.SlotId ??= "";
            if (memory.IsActive)
            {
                memory.IsGoneUnacknowledged = false;
            }
            else
            {
                memory.IsAcknowledged = false;
            }
        }

        if (loadedSchemaVersion < 9)
        {
            MigrateSustainedVanillaAlarmMemories();
            LegacySustainedAlarmReconciliationPending = AlarmHistory.Any(
                item =>
                    item != null &&
                    string.Equals(
                        item.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    SustainedVanillaAlarmPolicy.MatchesHistory(
                        SustainedVanillaAlarmPolicy
                            .HomelessLeftPrototypeId,
                        item.AlarmKey,
                        item.Detail));
        }

        AlarmHistory.RemoveAll(item =>
            item == null ||
            item.Sequence <= 0 ||
            string.IsNullOrWhiteSpace(item.AlarmKey));
        var historySequences = new HashSet<long>();
        AlarmHistory.RemoveAll(item => !historySequences.Add(item.Sequence));
        foreach (var item in AlarmHistory)
        {
            item.AlarmKey = item.AlarmKey.Trim();
            item.Message ??= "";
            item.Detail ??= "";
            item.Source ??= "";
            item.PanelId ??= "";
            if (double.IsNaN(item.RaisedAtTicks) ||
                double.IsInfinity(item.RaisedAtTicks) ||
                item.RaisedAtTicks < 0d)
            {
                item.RaisedAtTicks = 0d;
            }
            if (double.IsNaN(item.ClearedAtTicks) ||
                double.IsInfinity(item.ClearedAtTicks) ||
                item.ClearedAtTicks < 0d)
            {
                item.ClearedAtTicks = 0d;
            }
            if (double.IsNaN(item.AcknowledgedAtTicks) ||
                double.IsInfinity(item.AcknowledgedAtTicks) ||
                item.AcknowledgedAtTicks < 0d)
            {
                item.AcknowledgedAtTicks = 0d;
            }
        }

        if (loadedSchemaVersion < 6)
        {
            MigrateAlarmHistory();
        }

        MergeDefaultSystemAlarms(loadedSchemaVersion);
        AlarmTimingMemoryPolicy.NormalizeMemories(
            AlarmTimingMemories,
            Rules,
            SystemAlarms,
            discardExisting: loadedSchemaVersion < 18);
        SynchronizeAutomaticSystemSlots();
        if (loadedSchemaVersion < 4)
        {
            MigrateSystemSoundOverrides();
        }
        if (loadedSchemaVersion < 8)
        {
            SeedPanelSlots(includeMemories: true);
        }
        SynchronizeRuleSlots();
        if (loadedSchemaVersion < 13)
        {
            foreach (var legacyOverride in SoundOverrides.Where(item =>
                         item != null &&
                         item.IsGloballyDisabled &&
                         VanillaNotificationSuppressionPolicy
                             .IsVanillaOverrideId(item.AlarmId)))
            {
                if (!VanillaNotificationRules.Any(rule =>
                        rule.Scope ==
                            VanillaNotificationScope.NotificationType &&
                        string.Equals(
                            rule.AlarmId,
                            legacyOverride.AlarmId,
                            StringComparison.Ordinal)))
                {
                    VanillaNotificationRules.Add(new VanillaNotificationRule
                    {
                        AlarmId = legacyOverride.AlarmId,
                        Scope = VanillaNotificationScope.NotificationType,
                        Behavior = VanillaNotificationBehavior.Hidden,
                    });
                }
                legacyOverride.IsGloballyDisabled = false;
            }
        }
        SchemaVersion = Math.Max(SchemaVersion, 20);
    }

    private static float NormalizeFinite(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : value;
    }

    private static double NormalizeHysteresis(double hysteresis)
    {
        return AlarmTimingPolicy.Normalize(new AlarmTimingSettings(
            AlarmTimingPolicy.LegacyActivationDelayTicks,
            AlarmTimingPolicy.LegacyResetDelayTicks,
            AlarmTimingPolicy.LegacyMinimumActiveTicks,
            hysteresis)).Hysteresis;
    }

    private void MigrateSustainedVanillaAlarmMemories()
    {
        var groups = AlarmMemories
            .Where(memory =>
                SustainedVanillaAlarmPolicy.IsSustainedOverrideId(
                    memory.OverrideId))
            .GroupBy(memory => memory.OverrideId, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in groups)
        {
            var memories = group
                .OrderBy(memory => memory.Sequence)
                .ToArray();
            if (memories.Length == 0)
            {
                continue;
            }
            var target = memories[memories.Length - 1];
            var isActive = memories.Any(memory => memory.IsActive);
            var requiresAcknowledgement = memories.Any(memory =>
                memory.IsGoneUnacknowledged ||
                memory.IsActive && !memory.IsAcknowledged);
            target.Key =
                SustainedVanillaAlarmPolicy.AlarmKeyForOverrideId(
                    target.OverrideId);
            target.IsActive = isActive;
            target.IsAcknowledged = isActive && !requiresAcknowledgement;
            target.IsGoneUnacknowledged =
                !isActive && requiresAcknowledgement;
            target.OccurrenceId = target.OverrideId;
            target.SlotId = string.IsNullOrWhiteSpace(target.SlotId)
                ? target.OverrideId
                : target.SlotId;

            var history = AlarmHistory.Find(item =>
                item != null && item.Sequence == target.Sequence);
            if (history != null)
            {
                history.AlarmKey = target.Key;
            }
            foreach (var memory in memories)
            {
                if (!ReferenceEquals(memory, target))
                {
                    var supersededHistory = AlarmHistory.Find(item =>
                        item != null && item.Sequence == memory.Sequence);
                    if (supersededHistory != null)
                    {
                        supersededHistory.IsGone = true;
                    }
                    AlarmMemories.Remove(memory);
                }
            }
        }
    }

    private static void NormalizePanelSlots(
        List<PanelSlotDefinition> slots)
    {
        slots.RemoveAll(slot =>
            slot == null || string.IsNullOrWhiteSpace(slot.AlarmId));
        var alarmIds = new HashSet<string>(StringComparer.Ordinal);
        slots.RemoveAll(slot => !alarmIds.Add(slot.AlarmId.Trim()));
        foreach (var slot in slots)
        {
            slot.AlarmId = slot.AlarmId.Trim();
            slot.DisplayName = string.IsNullOrWhiteSpace(slot.DisplayName)
                ? UnmaText.Get("default.notification", "NOTIFICATION")
                : slot.DisplayName.Trim();
            slot.Detail ??= "";
            slot.Source ??= "";
            slot.ActiveColor ??= "#F0C541";
        }
    }

    private void SeedPanelSlots(bool includeMemories)
    {
        foreach (var panel in Panels)
        {
            if (panel.IsDashboard)
            {
                continue;
            }
            panel.Slots ??= new List<PanelSlotDefinition>();
            if (panel.IncludeSystem)
            {
                foreach (var alarm in SystemAlarms.Where(alarm =>
                             MatchesPanelFilter(
                                 panel,
                                 alarm.DisplayName,
                                 alarm.Id)))
                {
                    AddPanelSlotIfMissing(
                        panel,
                        CreateSystemPanelSlot(alarm));
                }
            }

            foreach (var rule in Rules.Where(rule =>
                         PanelTopologyPolicy.IsRuleAssignedToPanel(
                             rule,
                             panel,
                             Panels)))
            {
                AddPanelSlotIfMissing(panel, CreateRulePanelSlot(rule));
            }

            if (!includeMemories)
            {
                continue;
            }
            foreach (var memory in AlarmMemories.Where(memory =>
                         IsMemoryEligibleForPanel(memory, panel)))
            {
                if (string.Equals(
                        memory.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(memory.SlotId) &&
                    !string.IsNullOrWhiteSpace(memory.OverrideId))
                {
                    memory.SlotId = PanelSlotProjection.LegacyVanillaSlotId(
                        memory.OverrideId,
                        memory.Detail);
                }
                var view = new AlarmView
                {
                    Key = memory.Key,
                    Name = memory.Name,
                    Detail = memory.Detail,
                    Source = memory.Source,
                    PanelId = memory.PanelId,
                    ActiveColor = memory.ActiveColor,
                    OverrideId = memory.OverrideId,
                    OccurrenceId = memory.OccurrenceId,
                    SlotId = memory.SlotId,
                    Severity = memory.Severity,
                };
                var slot = PanelSlotProjection.CreateSlot(view);
                if (slot != null)
                {
                    AddPanelSlotIfMissing(panel, slot);
                }
            }
            foreach (var history in AlarmHistory.Where(history =>
                         IsHistoryEligibleForPanel(history, panel)))
            {
                var prototypeId = ExtractVanillaPrototypeId(history.Detail);
                if (string.IsNullOrWhiteSpace(prototypeId))
                {
                    continue;
                }
                var overrideId = "vanilla:" + prototypeId;
                var view = new AlarmView
                {
                    Key = history.AlarmKey,
                    Name = history.Message,
                    Detail = history.Detail,
                    Source = history.Source,
                    PanelId = history.PanelId,
                    ActiveColor = ColorForSeverity(history.Severity),
                    OverrideId = overrideId,
                    SlotId = PanelSlotProjection.LegacyVanillaSlotId(
                        overrideId,
                        history.Detail),
                    Severity = history.Severity,
                };
                AddPanelSlotIfMissing(
                    panel,
                    PanelSlotProjection.CreateSlot(view));
            }
            NormalizePanelSlots(panel.Slots);
        }
    }

    private void SynchronizeAutomaticSystemSlots()
    {
        foreach (var panel in Panels.Where(panel =>
                     !panel.IsDashboard && panel.IncludeSystem))
        {
            foreach (var alarm in SystemAlarms.Where(alarm =>
                         MatchesPanelFilter(
                             panel,
                             alarm.DisplayName,
                             alarm.Id)))
            {
                AddPanelSlotIfMissing(
                    panel,
                    CreateSystemPanelSlot(alarm));
            }
        }
    }

    private void SynchronizeRuleSlots()
    {
        var rulesById = new Dictionary<string, AlarmRuleDefinition>(
            StringComparer.Ordinal);
        foreach (var rule in Rules)
        {
            rulesById["rule:" + rule.Id] = rule;
        }
        foreach (var panel in Panels.Where(panel => !panel.IsDashboard))
        {
            panel.Slots.RemoveAll(slot =>
                string.Equals(
                    slot.Source,
                    "custom",
                    StringComparison.Ordinal) &&
                (!rulesById.TryGetValue(slot.AlarmId, out var rule) ||
                 !PanelTopologyPolicy.IsRuleAssignedToPanel(
                     rule,
                     panel,
                     Panels)));
        }
        foreach (var rule in Rules)
        {
            foreach (var panelId in PanelTopologyPolicy.GetRulePanelIds(
                         rule,
                         Panels))
            {
                var panel = Panels.Find(candidate => string.Equals(
                    candidate.Id,
                    panelId,
                    StringComparison.Ordinal));
                if (panel == null)
                {
                    continue;
                }
                var alarmId = "rule:" + rule.Id;
                var existing = panel.Slots.Find(slot => string.Equals(
                    slot.AlarmId,
                    alarmId,
                    StringComparison.Ordinal));
                if (existing == null)
                {
                    panel.Slots.Add(CreateRulePanelSlot(rule));
                }
                else
                {
                    existing.DisplayName = rule.Name;
                    existing.Detail =
                        rule.Conditions.Count + UnmaText.Get("auto.38bf168a03a3");
                    existing.Source = "custom";
                    existing.Severity = rule.Severity;
                    existing.ActiveColor = rule.ActiveColor;
                }
            }
        }
    }

    private bool IsMemoryEligibleForPanel(
        AlarmMemoryDefinition memory,
        PanelDefinition panel)
    {
        if (string.Equals(memory.Source, "custom", StringComparison.Ordinal))
        {
            return PanelTopologyPolicy.IsCustomMemoryEligibleForPanel(
                memory,
                panel,
                Rules,
                Panels);
        }
        if (string.Equals(memory.Source, "vanilla", StringComparison.Ordinal) &&
            !panel.IncludeVanilla)
        {
            return false;
        }
        if (string.Equals(memory.Source, "system", StringComparison.Ordinal) &&
            !panel.IncludeSystem)
        {
            return false;
        }
        return MatchesPanelFilter(
            panel,
            memory.Name,
            memory.Detail + " " + memory.Key);
    }

    private static bool IsHistoryEligibleForPanel(
        AlarmHistoryDefinition history,
        PanelDefinition panel)
    {
        return string.Equals(
                   history.Source,
                   "vanilla",
                   StringComparison.Ordinal) &&
               panel.IncludeVanilla &&
               MatchesPanelFilter(
                   panel,
                   history.Message,
                   history.Detail + " " + history.AlarmKey);
    }

    private static string ExtractVanillaPrototypeId(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "";
        }
        var separator = detail.IndexOf('·');
        return (separator < 0 ? detail : detail.Substring(0, separator)).Trim();
    }

    private static bool MatchesPanelFilter(
        PanelDefinition panel,
        string name,
        string detail)
    {
        if (string.IsNullOrWhiteSpace(panel.NotificationFilter))
        {
            return true;
        }
        var filters = panel.NotificationFilter
            .Split(new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (filters.Length == 0)
        {
            return true;
        }
        var haystack = (name ?? "") + " " + (detail ?? "");
        return filters.Any(filter => haystack.IndexOf(
                filter,
                StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void AddPanelSlotIfMissing(
        PanelDefinition panel,
        PanelSlotDefinition slot)
    {
        if (slot != null &&
            !IsPanelSlotExcluded(panel, slot.AlarmId) &&
            !panel.Slots.Exists(candidate => string.Equals(
                candidate.AlarmId,
                slot.AlarmId,
                StringComparison.Ordinal)))
        {
            panel.Slots.Add(slot);
        }
    }

    private static bool IsPanelSlotExcluded(
        PanelDefinition panel,
        string alarmId)
    {
        return panel.ExcludedAlarmIds != null &&
               panel.ExcludedAlarmIds.Contains(
                   alarmId,
                   StringComparer.Ordinal);
    }

    private static string ColorForSeverity(AlarmSeverity severity)
    {
        return severity switch
        {
            AlarmSeverity.Emergency => "#E51B23",
            AlarmSeverity.Critical => "#F05A32",
            AlarmSeverity.Warning => "#F0C541",
            _ => "#83C5BE",
        };
    }

    private static PanelSlotDefinition CreateSystemPanelSlot(
        SystemAlarmDefinition alarm)
    {
        var stage = alarm.Stages
            .Where(candidate => candidate.Enabled)
            .OrderBy(candidate => candidate.Priority)
            .FirstOrDefault();
        return new PanelSlotDefinition
        {
            AlarmId = alarm.Id,
            DisplayName = alarm.DisplayName,
            Detail = UnmaText.Get(
                "alarm.detail.system_notification",
                "System notification"),
            Source = "system",
            Severity = stage?.Severity ?? AlarmSeverity.Warning,
            ActiveColor = stage?.ActiveColor ?? "auto",
        };
    }

    private static PanelSlotDefinition CreateRulePanelSlot(
        AlarmRuleDefinition rule)
    {
        return PanelSlotProjection.CreateRuleSlot(rule);
    }

    private void MigrateAlarmHistory()
    {
        var nextSequence = Math.Max(
            AlarmMemories.Count == 0
                ? 0
                : AlarmMemories.Max(item => item.Sequence),
            AlarmHistory.Count == 0
                ? 0
                : AlarmHistory.Max(item => item.Sequence));
        var usedSequences = new HashSet<long>(
            AlarmHistory.Select(item => item.Sequence));

        foreach (var memory in AlarmMemories.OrderBy(item => item.Sequence))
        {
            if (memory.Sequence <= 0 ||
                usedSequences.Contains(memory.Sequence))
            {
                memory.Sequence = ++nextSequence;
            }
            usedSequences.Add(memory.Sequence);
            AlarmHistory.Add(new AlarmHistoryDefinition
            {
                Sequence = memory.Sequence,
                AlarmKey = memory.Key,
                Message = memory.Name,
                Detail = memory.Detail,
                Source = memory.Source,
                PanelId = memory.PanelId,
                Severity = memory.Severity,
                IsGone = memory.IsGoneUnacknowledged,
                IsAcknowledged = memory.IsAcknowledged,
            });
        }
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

    private void MergeDefaultSystemAlarms(int loadedSchemaVersion)
    {
        SystemAlarms.RemoveAll(item => item == null);
        foreach (var alarm in SystemAlarms)
        {
            alarm.Id ??= "";
            alarm.DisplayName ??= "";
            alarm.Stages ??= new List<SystemAlarmStageDefinition>();
            NormalizeSystemStages(alarm.Stages, loadedSchemaVersion);
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
        List<SystemAlarmStageDefinition> stages,
        int loadedSchemaVersion)
    {
        stages.RemoveAll(item => item == null);
        foreach (var stage in stages)
        {
            stage.OperatorAction = loadedSchemaVersion < 19
                ? AlarmOperatorAction.None
                : AlarmEscalationPolicy.NormalizeOperatorAction(
                    stage.OperatorAction);
            var timing = loadedSchemaVersion < 18
                ? AlarmTimingPolicy.LegacyMigrationDefaults
                : AlarmTimingPolicy.Normalize(new AlarmTimingSettings(
                    stage.ActivationDelayTicks,
                    stage.ResetDelayTicks,
                    stage.MinimumActiveTicks,
                    AlarmTimingPolicy.LegacyHysteresis));
            stage.ActivationDelayTicks = timing.ActivationDelayTicks;
            stage.ResetDelayTicks = timing.ResetDelayTicks;
            stage.MinimumActiveTicks = timing.MinimumActiveTicks;
            stage.Id ??= "";
            stage.Message ??= "";
            stage.Conditions ??= new List<SystemConditionDefinition>();
            stage.Conditions.RemoveAll(item => item == null);
            foreach (var condition in stage.Conditions)
            {
                condition.Hysteresis = loadedSchemaVersion < 18
                    ? AlarmTimingPolicy.LegacyHysteresis
                    : NormalizeHysteresis(condition.Hysteresis);
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
            AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
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
            ActivationDelayTicks = source.ActivationDelayTicks,
            ResetDelayTicks = source.ResetDelayTicks,
            MinimumActiveTicks = source.MinimumActiveTicks,
            OperatorAction = source.OperatorAction,
        };
        foreach (var condition in source.Conditions)
        {
            clone.Conditions.Add(new SystemConditionDefinition
            {
                MetricId = condition.MetricId,
                Comparison = condition.Comparison,
                Threshold = condition.Threshold,
                Hysteresis = condition.Hysteresis,
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
    public string OccurrenceId = "";
    public string SlotId = "";
    public int OccurrencePriority;
    public int EntityId = -1;
    public string EntityPrototypeId = "";
    public string EntityTitle = "";
    public long Sequence;
    public AlarmSeverity Severity;
    public bool IsActive;
    public bool IsAcknowledged;
    public bool IsGoneUnacknowledged;
    public bool IsMissingSource;
    public double LastValue;

    public bool RequiresAcknowledgement =>
        IsGoneUnacknowledged || IsActive && !IsAcknowledged;

    public bool IsLatched => IsActive || IsGoneUnacknowledged;
}

public static class AlarmEvaluation
{
    private const double EqualityTolerance = 0.000001d;

    public static bool TryCalculateComparable(
        double actual,
        ConditionValueMode valueMode,
        double reference,
        out double comparable)
    {
        comparable = 0d;
        if (!IsFinite(actual))
        {
            return false;
        }

        if (valueMode == ConditionValueMode.Absolute)
        {
            comparable = actual;
            return true;
        }

        if (valueMode != ConditionValueMode.PercentOfReference ||
            !IsFinite(reference) || reference <= EqualityTolerance)
        {
            return false;
        }

        comparable = actual / reference * 100d;
        return IsFinite(comparable);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

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
        bool wasGoneUnacknowledged,
        AlarmSeverity previousSeverity,
        bool isActive,
        AlarmSeverity severity,
        bool autoAcknowledgeOnClear,
        bool occurrenceEscalated = false,
        bool initiallyAcknowledged = false)
    {
        if (!isActive)
        {
            var remainsGoneUnacknowledged =
                !autoAcknowledgeOnClear &&
                (wasGoneUnacknowledged || wasActive && !wasAcknowledged);
            return new AlarmTransition(
                false,
                false,
                remainsGoneUnacknowledged,
                false);
        }

        var isNewOccurrence =
            !wasActive ||
            severity > previousSeverity ||
            occurrenceEscalated;
        return new AlarmTransition(
            true,
            isNewOccurrence ? initiallyAcknowledged : wasAcknowledged,
            false,
            isNewOccurrence);
    }
}

public readonly struct AlarmTransition
{
    public bool IsActive { get; }
    public bool IsAcknowledged { get; }
    public bool IsGoneUnacknowledged { get; }
    public bool IsNewOccurrence { get; }

    public AlarmTransition(
        bool isActive,
        bool isAcknowledged,
        bool isGoneUnacknowledged,
        bool isNewOccurrence)
    {
        IsActive = isActive;
        IsAcknowledged = isAcknowledged;
        IsGoneUnacknowledged = isGoneUnacknowledged;
        IsNewOccurrence = isNewOccurrence;
    }
}
