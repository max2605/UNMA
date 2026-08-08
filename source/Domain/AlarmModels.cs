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

public enum ConditionValueMode
{
    Absolute = 0,
    PercentOfReference = 1,
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
    [DataMember(Order = 5)] public bool AutoAcknowledgeOnClear;
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
    [DataMember(Order = 10)] public bool AutoAcknowledgeOnClear;
    [DataMember(Order = 11)] public List<string> LinkedPanelIds = new();
}

[DataContract]
public sealed class PanelSlotDefinition
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public string DisplayName = "MELDUNG";
    [DataMember(Order = 3)] public string Detail = "";
    [DataMember(Order = 4)] public string Source = "";
    [DataMember(Order = 5)] public AlarmSeverity Severity =
        AlarmSeverity.Warning;
    [DataMember(Order = 6)] public string ActiveColor = "#F0C541";
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
    [DataMember(Order = 7)] public List<PanelSlotDefinition> Slots = new();
    [DataMember(Order = 8)] public List<string> ExcludedAlarmIds = new();
    [DataMember(Order = 9)] public bool IsDashboard;
    [DataMember(Order = 10)] public int OwnerEntityId = -1;
    [DataMember(Order = 11)] public string OwnerEntityTitle = "";
    [DataMember(Order = 12)] public string OwnerEntityPrototypeId = "";
    [DataMember(Order = 13)] public string OwnerEntityType = "";
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
}

[DataContract]
public sealed class UnmaConfiguration
{
    [DataMember(Order = 1)] public int SchemaVersion = 12;
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
            IsDashboard = true,
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
                ? "MELDETAFEL"
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
        // Keep legacy dashboard slots serialized for lossless downgrade and
        // recovery. Dashboard projection and editing deliberately ignore them.

        foreach (var rule in Rules)
        {
            rule.Id = string.IsNullOrWhiteSpace(rule.Id)
                ? Guid.NewGuid().ToString("N")
                : rule.Id.Trim();
            rule.PanelId = string.IsNullOrWhiteSpace(rule.PanelId)
                ? Panels[0].Id
                : rule.PanelId.Trim();
            rule.LinkedPanelIds =
                PanelTopologyPolicy.NormalizeLinkedPanelIds(
                    rule.PanelId,
                    rule.LinkedPanelIds,
                    Panels);
            rule.Name = string.IsNullOrWhiteSpace(rule.Name)
                ? "MELDUNG"
                : rule.Name.Trim();
            rule.Conditions ??= new List<ConditionDefinition>();
            rule.Conditions.RemoveAll(condition => condition == null);
            foreach (var condition in rule.Conditions)
            {
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
        }

        if (loadedSchemaVersion < 6)
        {
            MigrateAlarmHistory();
        }

        MergeDefaultSystemAlarms();
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
        SchemaVersion = Math.Max(SchemaVersion, 12);
    }

    private static float NormalizeFinite(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? fallback
            : value;
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
                ? "MELDUNG"
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
                        rule.Conditions.Count + " Bedingung(en)";
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
            Detail = "Systemmeldung",
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
    public string OccurrenceId = "";
    public string SlotId = "";
    public int OccurrencePriority;
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
