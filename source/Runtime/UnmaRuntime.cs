using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.Products;
using Mafi.Core.Maintenance;
using Mafi.Core.Simulation;
using UNMA.Api;
using UNMA.Domain;
using UNMA.Extensions;
using UNMA.Integration;
using UNMA.Localization;

namespace UNMA.Runtime;

public readonly struct InstrumentHistoryState
{
    public int SampleCount { get; }
    public double FirstTimestampSeconds { get; }
    public double LastTimestampSeconds { get; }
    public double FirstValue { get; }
    public double LastValue { get; }

    public InstrumentHistoryState(
        int sampleCount,
        double firstTimestampSeconds,
        double lastTimestampSeconds,
        double firstValue,
        double lastValue)
    {
        SampleCount = sampleCount;
        FirstTimestampSeconds = firstTimestampSeconds;
        LastTimestampSeconds = lastTimestampSeconds;
        FirstValue = firstValue;
        LastValue = lastValue;
    }
}

public readonly struct InstrumentHistoryBucket
{
    public double FirstValue { get; }
    public double MinimumValue { get; }
    public double MaximumValue { get; }
    public double LastValue { get; }

    public InstrumentHistoryBucket(
        double firstValue,
        double minimumValue,
        double maximumValue,
        double lastValue)
    {
        FirstValue = firstValue;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        LastValue = lastValue;
    }
}

public sealed class UnmaSettings
{
    public bool ShowOnGameStart = true;
    public bool EnableAudio = true;
    public int AudioVolumePercent = 65;
    public int PollIntervalMs = 500;
    public bool EnableSystemAlarms = true;
}

public sealed class ExternalIntegrationStatus
{
    public int ActiveProviderCount { get; }
    public int ScannedFileCount { get; }
    public int LoadedFileCount { get; }
    public int JsonAlarmCount { get; }
    public int ApiMetricCount { get; }
    public int ApiAlarmCount { get; }
    public int ApiStateCount { get; }
    public int DiagnosticCount { get; }

    public ExternalIntegrationStatus(
        int activeProviderCount,
        int scannedFileCount,
        int loadedFileCount,
        int jsonAlarmCount,
        int apiMetricCount,
        int apiAlarmCount,
        int apiStateCount,
        int diagnosticCount)
    {
        ActiveProviderCount = activeProviderCount;
        ScannedFileCount = scannedFileCount;
        LoadedFileCount = loadedFileCount;
        JsonAlarmCount = jsonAlarmCount;
        ApiMetricCount = apiMetricCount;
        ApiAlarmCount = apiAlarmCount;
        ApiStateCount = apiStateCount;
        DiagnosticCount = diagnosticCount;
    }
}

public sealed class UnmaRuntime : IDisposable
{
    private sealed class AlarmState
    {
        public readonly AlarmView View = new();
        public long Sequence;
    }

    private sealed class RemovedAlarmState
    {
        public string Key;
        public AlarmState State;
    }

    private sealed class ClosedHistoryState
    {
        public AlarmHistoryDefinition History;
        public bool WasGone;
        public bool WasAcknowledged;
        public double WasRaisedAtTicks;
        public double WasClearedAtTicks;
        public double WasAcknowledgedAtTicks;
    }

    private sealed class ExternalEntityEvaluation
    {
        public bool IsActive;
        public bool IsMissingSource;
        public double LastValue;
        public string Detail = "";
    }

    private sealed class SustainedConditionState
    {
        public string Signature = "";
        public long StartedAtTick;
    }

    private sealed class AlarmTimingOwnerRuntimeSnapshot
    {
        public bool HasState;
        public AlarmTimingState State;
        public bool HasConditionLatches;
        public Dictionary<int, bool> ConditionLatches;
        public bool HasSignature;
        public string Signature = "";
    }

    private sealed class AlarmAreaPanelSnapshot
    {
        public PanelDefinition Panel;
        public HashSet<string> SlotIds;
    }

    private sealed class AlarmAreaAcknowledgementTarget
    {
        public string Key;
        public long Sequence;
    }

    private sealed class AlarmIncidentHistoryCapture
    {
        public IReadOnlyDictionary<long, double> RaisedAtTicksBySequence;
        public IReadOnlyList<AlarmOccurrenceSignal> RecentSignals;
    }

    private readonly struct AlarmIncidentHistoryRow
    {
        public string Key { get; }
        public AlarmSeverity Severity { get; }
        public long Sequence { get; }
        public double RaisedAtTicks { get; }

        public AlarmIncidentHistoryRow(
            string key,
            AlarmSeverity severity,
            long sequence,
            double raisedAtTicks)
        {
            Key = key;
            Severity = severity;
            Sequence = sequence;
            RaisedAtTicks = raisedAtTicks;
        }
    }

    private static readonly string[] s_emergencyNotificationTokens =
    {
        "meltdown",
        "starvedtodeath",
        "degraded",
        "destroyed",
        "collapse",
        "fatal",
        "gameover",
    };

    private static readonly string[] s_criticalNotificationTokens =
    {
        "starving",
        "nofuel",
        "notEnoughMaintenance",
        "notEnoughElectricity",
        "cannotfindpath",
        "broken",
        "failed",
    };

    private static readonly HashSet<string> s_pollutionHealthCategoryIds =
        new(StringComparer.Ordinal)
        {
            IdsCore.HealthPointsCategories.LandfillPollution.Value,
            IdsCore.HealthPointsCategories.WaterPollution.Value,
            IdsCore.HealthPointsCategories.AirPollution.Value,
            IdsCore.HealthPointsCategories.AirPollutionVehicles.Value,
            IdsCore.HealthPointsCategories.AirPollutionShips.Value,
            IdsCore.HealthPointsCategories.AirPollutionTrains.Value,
            IdsCore.HealthPointsCategories.WasteInSettlement.Value,
        };

    private readonly object m_gate = new();
    private readonly object m_configurationGate = new();
    private readonly object m_inspectionGate = new();
    private readonly object m_systemMetricsGate = new();
    private readonly object m_persistenceGate = new();
    private readonly object m_alarmPersistenceBatchGate = new();
    private readonly object m_externalDefinitionsGate = new();
    private readonly object m_removedEntitiesGate = new();
    private readonly object m_notificationEntityAliasesGate = new();
    private readonly object m_instrumentValuesGate = new();
    private readonly object m_alarmTimingGate = new();
    private readonly INotificationsManager m_notificationsManager;
    private readonly IEntitiesManager m_entitiesManager;
    private readonly TransportsManager m_transportsManager;
    private readonly IEventNonSaveable<IEntity> m_entityRemovedEvent;
    private readonly IWorkersManager m_workersManager;
    private readonly SettlementsManager m_settlementsManager;
    private readonly PopsHealthManager m_healthManager;
    private readonly IProductsManager m_productsManager;
    private readonly MaintenanceManager m_maintenanceManager;
    private readonly ICalendar m_calendar;
    private readonly IEventNonSaveable m_newMonthStartEvent;
    private readonly ISimLoopEvents m_simLoopEvents;
    private readonly UnmaStateStore m_store;
    private readonly UnmaTransferProfileStore m_transferProfileStore;
    private readonly object m_transferProfileGate = new();
    private readonly ExternalDisplayNotificationWriter m_externalDisplay =
        new();
    private readonly ExternalProviderDescriptor[] m_externalProviders;
    private readonly Dictionary<string, AlarmState> m_alarms =
        new(StringComparer.Ordinal);
    private readonly GroupedVanillaNotificationTracker
        m_groupedVanillaNotifications = new();
    private readonly List<AlarmHistoryDefinition> m_alarmHistory = new();
    private readonly HashSet<string> m_previousExternalKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_retiredExternalKeys =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool>
        m_externalAutoAcknowledgeByKey = new(StringComparer.Ordinal);
    private volatile HashSet<string> m_disabledVanillaOverrideIds =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, IEntity> m_removedEntityCandidates = new();
    private readonly StaticEntityMissingGraceTracker
        m_missingStaticEntityTracker = new();
    private readonly Dictionary<string, bool> m_staticEntityTypeCache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, Dictionary<int, NotificationEntityAlias>>
        m_notificationOwnersByChild = new();
    private readonly Dictionary<int, HashSet<int>>
        m_notificationChildrenByOwner = new();
    private readonly Dictionary<string, double> m_lastInstrumentValues =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<InstrumentValueSample>>
        m_instrumentHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InstrumentForecastRange>
        m_instrumentForecastRanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_instrumentSignatures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SustainedConditionState>
        m_sustainedConditionStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlarmTimingState>
        m_ruleTimingStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, bool>>
        m_ruleConditionLatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_ruleTimingSignatures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlarmTimingState>
        m_systemStageTimingStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, bool>>
        m_systemStageConditionLatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_systemStageTimingSignatures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlarmAudioSnoozeState>
        m_alarmAudioSnoozes = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_escalatedRuleIds =
        new(StringComparer.Ordinal);
    private readonly List<AlarmAttentionRequest> m_attentionRequests = new();
    private double m_lastInstrumentCaptureTimestampTicks;

    private const int MaximumInstrumentHistorySamples = 100000;
    private const int MaximumAlarmIncidentHistoryCaptureAttempts = 2;
    private const double InstrumentHistorySampleIntervalTicks =
        GameTimeWindowPolicy.SimTicksPerDay;

    private readonly struct InstrumentForecastRange
    {
        public double Minimum { get; }
        public double Maximum { get; }

        public InstrumentForecastRange(double minimum, double maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }
    }

    private static bool IsInstrumentForecastSampleInWindow(
        double sampleTimestampTicks,
        double currentTimestampTicks,
        int windowTicks)
    {
        if (sampleTimestampTicks > currentTimestampTicks)
        {
            return false;
        }
        return windowTicks <= 0 ||
               sampleTimestampTicks >= currentTimestampTicks - windowTicks;
    }

    private static bool DidInstrumentClockRollBack(
        double currentTimestampTicks,
        double previousTimestampTicks) =>
        currentTimestampTicks < previousTimestampTicks;

    private sealed class NotificationEntityAlias
    {
        public int OwnerEntityId;
        public string OwnerEntityPrototypeId = "";
        public string OwnerEntityTitle = "";
    }

    private long m_sequence;
    private long m_alarmHistoryRevision;
    private long m_alarmIncidentHistoryCaptureRevision = -1;
    private AlarmIncidentHistoryCapture m_alarmIncidentHistoryCapture;
    private OperatorSilenceReminderSnapshot
        m_pendingOperatorSilenceReminder;
    private long m_nextEvaluationTimestamp;
    private long m_nextEvaluationErrorLogTimestamp;
    private long m_externalDefinitionRevision;
    private string m_lastExternalCollisionStamp = "";
    private long m_registeredExternalApiRevision = -1;
    private string m_registeredExternalNamespaceSignature = "";
    private volatile bool m_gameplayActive;
    private volatile UnmaSettings m_settings;
    private int m_requestedInspectionEntityId = -1;
    private long m_inspectionRequestGeneration;
    private EntityInspectionSnapshot m_completedInspection;
    private Dictionary<string, double> m_lastSystemMetrics =
        new(StringComparer.Ordinal);
    private bool m_simListenerAdded;
    private bool m_monthStartListenerAdded;
    private int m_alarmPersistenceSuppressionDepth;
    private bool m_alarmPersistencePending;
    private bool m_disposed;
    private ExternalDefinitionLoadResult m_externalDefinitions;
    private bool m_transferProfileLoaded;
    private UnmaTransferProfile m_transferProfile;

    public UnmaConfiguration Configuration { get; }
    public UnmaSettings Settings => m_settings;
    public string LastPersistenceError { get; private set; } = "";
    public string LastTransferProfileError { get; private set; } = "";
    public string TransferProfilePath => m_transferProfileStore?.Path ?? "";
    public PanelCloneFailure LastPanelCloneFailure { get; private set; }

    public UnmaRuntime(
        INotificationsManager notificationsManager,
        IEntitiesManager entitiesManager,
        TransportsManager transportsManager,
        IWorkersManager workersManager,
        SettlementsManager settlementsManager,
        PopsHealthManager healthManager,
        IProductsManager productsManager,
        MaintenanceManager maintenanceManager,
        ICalendar calendar,
        ISimLoopEvents simLoopEvents,
        UnmaStateStore store,
        UnmaSettings settings,
        IEnumerable<ExternalProviderDescriptor> externalProviders = null,
        UnmaTransferProfileStore transferProfileStore = null)
    {
        m_notificationsManager = notificationsManager;
        m_entitiesManager = entitiesManager;
        m_transportsManager = transportsManager;
        // Narrow the reference at construction time so saveable Add/Remove
        // are not even available to this runtime-only subscription.
        m_entityRemovedEvent = entitiesManager.EntityRemoved;
        m_workersManager = workersManager;
        m_settlementsManager = settlementsManager;
        m_healthManager = healthManager;
        m_productsManager = productsManager;
        m_maintenanceManager = maintenanceManager;
        m_calendar = calendar;
        // Keep this UI-reminder event runtime-only. A saveable subscription
        // would make the runtime service part of the game save graph.
        m_newMonthStartEvent = calendar.NewMonthStart;
        m_simLoopEvents = simLoopEvents;
        m_store = store;
        m_transferProfileStore = transferProfileStore;
        m_externalProviders = (externalProviders ??
                Enumerable.Empty<ExternalProviderDescriptor>())
            .Where(provider => provider != null)
            .Select(provider => new ExternalProviderDescriptor(
                provider.Id,
                provider.RootDirectoryPath))
            .ToArray();
        m_settings = settings ?? new UnmaSettings();
        Configuration = store.Load();
        if (store.IsWriteBlocked)
        {
            LastPersistenceError = store.WriteBlockReason;
        }
        RefreshDisabledVanillaOverrideIds();
        RestoreAlarmHistory();
        RestoreAlarmMemories();
        RestoreAlarmTimingStates();
        foreach (var key in m_alarms
                     .Where(pair => string.Equals(
                         pair.Value.View.Source,
                         "external",
                         StringComparison.Ordinal))
                     .Select(pair => pair.Key))
        {
            m_previousExternalKeys.Add(key);
        }
        ReloadExternalDefinitions(reloadLanguageFiles: false);
    }

    private void RestoreAlarmHistory()
    {
        var globallyIgnoredOverrideIds =
            GetGloballyIgnoredHistoryPurgeOverrideIds(
                Configuration.VanillaNotificationRules);
        foreach (var item in Configuration.AlarmHistory)
        {
            m_sequence = Math.Max(m_sequence, item.Sequence);
            if (globallyIgnoredOverrideIds.Any(overrideId =>
                    VanillaNotificationSuppressionPolicy
                        .MatchesHistoryForOverride(item, overrideId)))
            {
                continue;
            }
            m_alarmHistory.Add(CloneHistory(item));
        }
        if (m_alarmHistory.Count > 0)
        {
            m_alarmHistoryRevision = 1;
        }
    }

    public void Initialize()
    {
        RestoreNotificationEntityAliases();

        // This callback owns runtime-only cleanup state. Registering it with
        // Add() would make COI serialize this UnmaRuntime owner with the game
        // save and fail because runtime services are intentionally not save
        // data. The non-saveable subscription is the matching lifecycle API.
        m_entityRemovedEvent.AddNonSaveable(
            this,
            OnEntityRemoved);
        m_notificationsManager.NotificationAdded += OnNotificationAdded;
        m_notificationsManager.NotificationRemoved += OnNotificationRemoved;
        m_notificationsManager.NotificationSuppressChanged +=
            OnNotificationSuppressChanged;

        BeginAlarmPersistenceBatch();
        try
        {
            var currentNotifications = m_notificationsManager
                .FetchAllNotifications()
                .ToArray();
            RefreshGroupedVanillaNotificationMembers(
                currentNotifications,
                replaceCurrentMembers: true);
            foreach (var notification in currentNotifications)
            {
                OnNotificationAdded(notification);
            }
            ClearRestoredVanillaAlarmsNoLongerPresent(
                currentNotifications);
            EvaluateSustainedVanillaAlarms();
        }
        finally
        {
            EndAlarmPersistenceBatch();
        }
        PersistAlarmState();
        PublishExternalDisplaySnapshot();
        PublishExternalDisplayPanelState();

        m_simLoopEvents.UpdateEndForUi.AddNonSaveable(
            this,
            OnUpdateEndForUi);
        m_simListenerAdded = true;
        m_newMonthStartEvent.AddNonSaveable(
            this,
            OnNewMonthStart);
        m_monthStartListenerAdded = true;
    }

    private void RestoreAlarmMemories()
    {
        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var vanillaRules = Configuration.VanillaNotificationRules
            .Select(CloneVanillaNotificationRule)
            .ToArray();
        var closedSuppressedHistory = false;
        foreach (var memory in Configuration.AlarmMemories)
        {
            var groupedMemory =
                GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    memory.OverrideId);
            var ignoredVanillaMemory = string.Equals(
                    memory.Source,
                    "vanilla",
                    StringComparison.Ordinal) &&
                VanillaNotificationSuppressionPolicy.ResolveBehavior(
                    vanillaRules,
                    memory.OverrideId,
                    groupedMemory ? -1 : memory.EntityId,
                    groupedMemory ? "" : memory.EntityPrototypeId) ==
                VanillaNotificationBehavior.Ignored;
            if (ignoredVanillaMemory || IsSuppressedVanillaAlarm(
                    memory.Source,
                    memory.OverrideId,
                    disabledVanillaOverrideIds,
                    memory.SlotId))
            {
                m_sequence = Math.Max(m_sequence, memory.Sequence);
                var history = FindHistoryLocked(memory.Sequence);
                if (history != null)
                {
                    closedSuppressedHistory |= history.SetState(
                        isGone: true,
                        isAcknowledged: true,
                        currentGameTicks: CurrentGameTicks);
                }
                continue;
            }

            var state = new AlarmState
            {
                Sequence = memory.Sequence,
            };
            state.View.Key = memory.Key;
            state.View.Name = memory.Name;
            state.View.Detail = memory.Detail;
            state.View.Source = memory.Source;
            state.View.PanelId = memory.PanelId;
            state.View.ActiveColor = memory.ActiveColor;
            state.View.SoundId = memory.SoundId;
            state.View.OverrideId = memory.OverrideId;
            state.View.OccurrenceId = memory.OccurrenceId;
            state.View.SlotId = memory.SlotId;
            state.View.OccurrencePriority = memory.OccurrencePriority;
            state.View.Sequence = memory.Sequence;
            state.View.Severity = memory.Severity;
            state.View.IsActive = memory.IsActive;
            state.View.IsAcknowledged = memory.IsAcknowledged;
            state.View.IsOperatorSilenced = memory.IsOperatorSilenced;
            state.View.OperatorSilencedAtGameTick =
                memory.OperatorSilencedAtGameTick;
            state.View.IsGoneUnacknowledged =
                memory.IsGoneUnacknowledged;
            state.View.IsMissingSource = memory.IsMissingSource;
            state.View.LastValue = memory.LastValue;
            state.View.EntityId = memory.EntityId;
            state.View.EntityPrototypeId =
                memory.EntityPrototypeId ?? "";
            state.View.EntityTitle = memory.EntityTitle ?? "";
            m_alarms[memory.Key] = state;
            if (string.Equals(
                    memory.Source,
                    "external",
                    StringComparison.Ordinal))
            {
                m_externalAutoAcknowledgeByKey[memory.Key] =
                    memory.AutoAcknowledgeOnClear;
            }
            m_sequence = Math.Max(m_sequence, memory.Sequence);
            if (FindHistoryLocked(memory.Sequence) == null)
            {
                m_alarmHistory.Add(CreateHistoryFromState(state));
                m_alarmHistoryRevision++;
            }
        }
        if (closedSuppressedHistory)
        {
            m_alarmHistoryRevision++;
        }
    }

    private void RestoreAlarmTimingStates()
    {
        AlarmView[] restoredActiveAlarms;
        lock (m_gate)
        {
            restoredActiveAlarms = m_alarms.Values
                .Where(state => state.View.IsActive)
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }

        AlarmRuleDefinition[] rules;
        SystemAlarmDefinition[] systemAlarms;
        AlarmTimingMemoryDefinition[] timingMemories;
        lock (m_configurationGate)
        {
            rules = Configuration.Rules
                .Where(rule => rule != null)
                .Select(CloneRuleForEvaluation)
                .ToArray();
            systemAlarms = Configuration.SystemAlarms
                .Where(alarm => alarm != null)
                .Select(CloneSystemAlarmForEditing)
                .ToArray();
            timingMemories = Configuration.AlarmTimingMemories
                .Where(memory => memory != null)
                .Select(AlarmTimingMemoryPolicy.CloneMemory)
                .ToArray();
        }

        var memoryByOwner = timingMemories
            .GroupBy(memory => memory.OwnerKey ?? "", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
        var currentGameTick = CurrentGameTick;
        lock (m_alarmTimingGate)
        {
            m_ruleTimingStates.Clear();
            m_ruleConditionLatches.Clear();
            m_ruleTimingSignatures.Clear();
            m_systemStageTimingStates.Clear();
            m_systemStageConditionLatches.Clear();
            m_systemStageTimingSignatures.Clear();
            m_escalatedRuleIds.Clear();

            foreach (var rule in rules.Where(rule => rule.Enabled))
            {
                var ownerKey = AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id);
                var signature =
                    AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule);
                if (!memoryByOwner.TryGetValue(ownerKey, out var memory) ||
                    !AlarmTimingMemoryPolicy.TryRestore(
                        memory,
                        ownerKey,
                        signature,
                        rule.Conditions?.Count ?? 0,
                        out var state,
                        out var latches))
                {
                    continue;
                }
                m_ruleTimingStates[rule.Id] = state;
                m_ruleConditionLatches[rule.Id] = latches;
                m_ruleTimingSignatures[rule.Id] = signature;
            }

            foreach (var alarm in systemAlarms.Where(alarm => alarm.Enabled))
            {
                for (var stageIndex = 0;
                     stageIndex < alarm.Stages.Count;
                     stageIndex++)
                {
                    var stage = alarm.Stages[stageIndex];
                    if (stage == null || !stage.Enabled)
                    {
                        continue;
                    }
                    var ownerKey = AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                        alarm.Id,
                        stage.Id,
                        stageIndex);
                    var signature = AlarmTimingMemoryPolicy
                        .SystemStageDefinitionSignature(stage);
                    if (!memoryByOwner.TryGetValue(ownerKey, out var memory) ||
                        !AlarmTimingMemoryPolicy.TryRestore(
                            memory,
                            ownerKey,
                            signature,
                            stage.Conditions?.Count ?? 0,
                            out var state,
                            out var latches))
                    {
                        continue;
                    }
                    var stageKey = SystemStageTimingKey(
                        alarm.Id,
                        stage.Id,
                        stageIndex);
                    m_systemStageTimingStates[stageKey] = state;
                    m_systemStageConditionLatches[stageKey] = latches;
                    m_systemStageTimingSignatures[stageKey] = signature;
                }
            }

            // Schema-17 saves have no timing memory. Preserve an already
            // active annunciation and seed its latches on the active side of
            // the Schmitt trigger, so the first poll cannot clear it merely
            // because its value is currently inside the dead band.
            foreach (var view in restoredActiveAlarms)
            {
                if (string.Equals(
                        view.Source,
                        "custom",
                        StringComparison.Ordinal) &&
                    (PanelTopologyPolicy.TryGetRuleId(
                         view.Key,
                         out var ruleId) ||
                     PanelTopologyPolicy.TryGetRuleId(
                         view.SlotId,
                         out ruleId)))
                {
                    var rule = rules.FirstOrDefault(candidate =>
                        candidate.Enabled && string.Equals(
                            candidate.Id,
                            ruleId,
                            StringComparison.Ordinal));
                    if (rule != null &&
                        (!m_ruleTimingStates.TryGetValue(
                             ruleId,
                             out var state) ||
                         !state.IsActive))
                    {
                        m_ruleTimingStates[ruleId] =
                            AlarmTimingState.ActiveAt(currentGameTick);
                        m_ruleConditionLatches[ruleId] =
                            AlarmTimingPolicy.CreateActiveConditionLatches(
                                rule.Conditions?.Count ?? 0);
                        m_ruleTimingSignatures[ruleId] =
                            AlarmTimingMemoryPolicy
                                .RuleDefinitionSignature(rule);
                    }
                    if (rule != null &&
                        AlarmEscalationPolicy.IsEscalatedOccurrenceId(
                            rule.Id,
                            view.OccurrenceId) &&
                        AlarmEscalationPolicy.Normalize(
                            rule.Escalation,
                            rule.Severity).Enabled)
                    {
                        // Escalation memory is deliberately runtime-only.
                        // Restore it exclusively from the exact active
                        // occurrence so a base occurrence can never inherit
                        // a stale escalation latch from severity or priority.
                        m_escalatedRuleIds.Add(rule.Id);
                    }
                    continue;
                }

                if (!string.Equals(
                        view.Source,
                        "system",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                var alarm = systemAlarms.FirstOrDefault(candidate =>
                    candidate.Enabled &&
                    (string.Equals(
                         candidate.Id,
                         view.Key,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         candidate.Id,
                         view.OverrideId,
                         StringComparison.Ordinal)));
                if (alarm == null)
                {
                    continue;
                }
                var restoredStageIndex = AlarmTimingMemoryPolicy
                    .FindRestoredSystemStageIndex(
                        alarm.Stages,
                        view.OccurrenceId,
                        view.OccurrencePriority,
                        view.Severity);
                if (restoredStageIndex < 0)
                {
                    continue;
                }
                var restoredStage = alarm.Stages[restoredStageIndex];
                var stageKey = SystemStageTimingKey(
                    alarm.Id,
                    restoredStage.Id,
                    restoredStageIndex);
                if (!m_systemStageTimingStates.TryGetValue(
                        stageKey,
                        out var stageState) ||
                    !stageState.IsActive)
                {
                    m_systemStageTimingStates[stageKey] =
                        AlarmTimingState.ActiveAt(currentGameTick);
                    m_systemStageConditionLatches[stageKey] =
                        AlarmTimingPolicy.CreateActiveConditionLatches(
                            restoredStage.Conditions?.Count ?? 0);
                    m_systemStageTimingSignatures[stageKey] =
                        AlarmTimingMemoryPolicy
                            .SystemStageDefinitionSignature(
                                restoredStage);
                }
            }
        }
    }

    private void ClearRestoredVanillaAlarmsNoLongerPresent(
        IReadOnlyList<INotification> currentNotifications)
    {
        var currentKeys = new HashSet<string>(
            currentNotifications.Select(AlarmKeyForNotification),
            StringComparer.Ordinal);
        AlarmView[] staleActiveViews;
        lock (m_gate)
        {
            staleActiveViews = m_alarms.Values
                .Where(state =>
                    state.View.Source == "vanilla" &&
                    state.View.IsActive &&
                    !SustainedVanillaAlarmPolicy.IsSustainedOverrideId(
                        state.View.OverrideId) &&
                    !currentKeys.Contains(state.View.Key))
                .Select(state => Clone(state.View))
                .ToArray();
        }

        foreach (var view in staleActiveViews)
        {
            ClearAlarm(
                view.Key,
                ResolveAutoAcknowledgeOnClear(view.OverrideId),
                false);
        }
        if (staleActiveViews.Length > 0)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
    }

    public void ApplySettings(UnmaSettings settings)
    {
        m_settings = settings ?? new UnmaSettings();
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        if (!m_settings.EnableSystemAlarms)
        {
            foreach (var alarm in Configuration.SystemAlarms)
            {
                ForceNormal(alarm.Id);
            }
        }
    }

    public ExternalIntegrationStatus GetExternalIntegrationStatus()
    {
        ExternalDefinitionLoadResult definitions;
        lock (m_externalDefinitionsGate)
        {
            definitions = m_externalDefinitions;
        }
        var api = UnmaApi.GetSnapshot();
        return new ExternalIntegrationStatus(
            definitions?.ProviderCount ?? 0,
            definitions?.ScannedFileCount ?? 0,
            definitions?.LoadedFileCount ?? 0,
            definitions?.AlarmTemplates.Count ?? 0,
            api.Metrics.Count,
            api.AlarmTemplates.Count,
            api.AlarmStates.Count,
            definitions?.Diagnostics.Count ?? 0);
    }

    public IReadOnlyList<ExternalLoadDiagnostic>
        GetExternalIntegrationDiagnostics()
    {
        lock (m_externalDefinitionsGate)
        {
            return m_externalDefinitions?.Diagnostics.ToArray() ??
                   Array.Empty<ExternalLoadDiagnostic>();
        }
    }

    public bool ReloadExternalDefinitions(bool reloadLanguageFiles = true)
    {
        var loaded = ExternalDefinitionLoader.Load(m_externalProviders);
        lock (m_externalDefinitionsGate)
        {
            m_externalDefinitions = loaded;
            m_externalDefinitionRevision++;
        }

        var api = UnmaApi.GetSnapshot();
        RegisterExternalLocalizationNamespaces(loaded, api);
        if (reloadLanguageFiles)
        {
            UnmaText.Reload();
        }

        foreach (var diagnostic in loaded.Diagnostics.Take(20))
        {
            Log.Warning(
                UnmaText.Get("auto.3289fc75fbec") + diagnostic.Code +
                " [" + diagnostic.ProviderId + "] " +
                diagnostic.Message);
        }
        if (loaded.Diagnostics.Count > 20)
        {
            Log.Warning(
                UnmaText.Get("auto.a01209a822a4") + (loaded.Diagnostics.Count - 20) +
                UnmaText.Get("auto.2e4a6d9e30ad"));
        }

        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        return !loaded.HasErrors;
    }

    private void RegisterExternalLocalizationNamespaces(
        ExternalDefinitionLoadResult definitions,
        ExternalRegistrySnapshot api)
    {
        var providersById = m_externalProviders
            .Where(provider =>
                provider != null &&
                !string.IsNullOrWhiteSpace(provider.Id))
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var namespaceOwners = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var registeredNamespaces = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providersById.Values)
        {
            if (!UnmaText.IsValidNamespace(provider.Id))
            {
                continue;
            }
            namespaceOwners[provider.Id] = provider.Id;
            if (registeredNamespaces.Add(provider.Id))
            {
                UnmaText.TryRegisterProvider(
                    provider.Id,
                    provider.RootDirectoryPath,
                    out _);
            }
        }

        var aliases = (definitions?.AlarmTemplates ??
                Array.Empty<ExternalAlarmTemplateSnapshot>())
            .Select(item => new
            {
                Owner = item.OwnerModId,
                Namespace = item.LocalizationNamespace,
            })
            .Concat(api.AlarmTemplates.Select(item => new
            {
                Owner = item.OwnerModId,
                Namespace = item.LocalizationNamespace,
            }))
            .Concat(api.AlarmStates.Select(item => new
            {
                Owner = item.OwnerModId,
                Namespace = item.LocalizationNamespace,
            }))
            .GroupBy(
                item => item.Owner + "\u001f" + item.Namespace,
                StringComparer.Ordinal)
            .Select(group => group.First());
        foreach (var alias in aliases)
        {
            if (!providersById.TryGetValue(alias.Owner, out var provider) ||
                !UnmaText.IsValidNamespace(alias.Namespace))
            {
                continue;
            }
            if (namespaceOwners.TryGetValue(
                    alias.Namespace,
                    out var existingOwner) &&
                !string.Equals(
                    existingOwner,
                    alias.Owner,
                    StringComparison.Ordinal))
            {
                Log.Warning(
                    UnmaText.Get("auto.97b72c42d7ed") + alias.Namespace +
                    UnmaText.Get("auto.c634fae93531") + existingOwner +
                    UnmaText.Get("auto.c5da52c41902") + alias.Owner +
                    UnmaText.Get("auto.3740d62d7a01"));
                continue;
            }
            namespaceOwners[alias.Namespace] = alias.Owner;
            if (registeredNamespaces.Add(alias.Namespace))
            {
                UnmaText.TryRegisterProvider(
                    alias.Namespace,
                    provider.RootDirectoryPath,
                    out _);
            }
        }
        m_registeredExternalApiRevision = api.Revision;
        m_registeredExternalNamespaceSignature =
            ExternalNamespaceSignature(api);
    }

    private static string ExternalNamespaceSignature(
        ExternalRegistrySnapshot api)
    {
        return string.Join(
            "\u001e",
            api.AlarmTemplates
                .Select(item => item.OwnerModId + "\u001f" +
                                item.LocalizationNamespace)
                .Concat(api.AlarmStates.Select(item =>
                    item.OwnerModId + "\u001f" +
                    item.LocalizationNamespace))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal));
    }

    public void SetGameplayActive(bool isActive)
    {
        if (m_gameplayActive == isActive)
        {
            return;
        }

        m_gameplayActive = isActive;
        if (isActive)
        {
            Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        }
    }

    public void RequestEntityInspection(int entityId)
    {
        lock (m_inspectionGate)
        {
            m_inspectionRequestGeneration++;
            m_requestedInspectionEntityId = entityId;
            m_completedInspection = null;
        }
    }

    public void CancelEntityInspectionRequest()
    {
        lock (m_inspectionGate)
        {
            m_inspectionRequestGeneration++;
            m_requestedInspectionEntityId = -1;
            m_completedInspection = null;
        }
    }

    public bool TryTakeCompletedInspection(
        out EntityInspectionSnapshot inspection)
    {
        lock (m_inspectionGate)
        {
            inspection = m_completedInspection;
            m_completedInspection = null;
            return inspection != null;
        }
    }

    public PanelDefinition GetOrCreateEntityPanel(
        EntityInspectionSnapshot inspection)
    {
        if (inspection == null ||
            inspection.EntityId <= 0 ||
            !string.IsNullOrWhiteSpace(inspection.Error))
        {
            return null;
        }
        return GetOrCreateEntityPanel(
            inspection.EntityId,
            inspection.Title,
            inspection.EntityType,
            inspection.PrototypeId);
    }

    public PanelDefinition GetEntityPanel(int entityId)
    {
        if (entityId <= 0)
        {
            return null;
        }
        lock (m_configurationGate)
        {
            return Configuration.Panels.FirstOrDefault(panel =>
                PanelTopologyPolicy.IsEntityPanel(panel) &&
                panel.OwnerEntityId == entityId);
        }
    }

    private void RestoreNotificationEntityAliases()
    {
        int[] ownerEntityIds;
        lock (m_configurationGate)
        {
            ownerEntityIds = Configuration.Panels
                .Where(PanelTopologyPolicy.IsEntityPanel)
                .Select(panel => panel.OwnerEntityId)
                .Distinct()
                .ToArray();
        }
        foreach (var ownerEntityId in ownerEntityIds)
        {
            if (!TryGetLiveEntity(ownerEntityId, out var owner))
            {
                continue;
            }
            RegisterNotificationEntityAliases(
                ownerEntityId,
                owner.Prototype.Id.Value,
                EntityMetricCatalog.GetEntityTitle(owner),
                GetNotificationEntities(owner));
        }
    }

    private IEntity[] GetNotificationEntities(IEntity owner)
    {
        if (owner == null)
        {
            return Array.Empty<IEntity>();
        }
        var entities = new List<IEntity> { owner };
        if (m_transportsManager == null)
        {
            return entities.ToArray();
        }

        var pillars = new Lyst<TransportPillar>();
        if (owner is Transport transport)
        {
            m_transportsManager.FindAttachedPillars(transport, pillars);
        }
        else if (owner is LayoutEntity layoutEntity)
        {
            m_transportsManager.FindAttachedPillars(layoutEntity, pillars);
        }
        foreach (var pillar in pillars)
        {
            if (pillar != null &&
                entities.All(entity => entity.Id != pillar.Id))
            {
                entities.Add(pillar);
            }
        }
        return entities.ToArray();
    }

    private void RegisterNotificationEntityAliases(
        int ownerEntityId,
        string ownerEntityPrototypeId,
        string ownerEntityTitle,
        IEnumerable<IEntity> notificationEntities)
    {
        if (ownerEntityId <= 0)
        {
            return;
        }
        var childIds = new HashSet<int>(
            (notificationEntities ?? Enumerable.Empty<IEntity>())
            .Where(entity =>
                entity != null && entity.Id.Value != ownerEntityId)
            .Select(entity => entity.Id.Value));
        lock (m_notificationEntityAliasesGate)
        {
            if (m_notificationChildrenByOwner.TryGetValue(
                    ownerEntityId,
                    out var previousChildIds))
            {
                foreach (var childId in previousChildIds)
                {
                    if (m_notificationOwnersByChild.TryGetValue(
                            childId,
                            out var owners))
                    {
                        owners.Remove(ownerEntityId);
                        if (owners.Count == 0)
                        {
                            m_notificationOwnersByChild.Remove(childId);
                        }
                    }
                }
            }
            m_notificationChildrenByOwner[ownerEntityId] = childIds;
            foreach (var childId in childIds)
            {
                if (!m_notificationOwnersByChild.TryGetValue(
                        childId,
                        out var owners))
                {
                    owners = new Dictionary<int, NotificationEntityAlias>();
                    m_notificationOwnersByChild[childId] = owners;
                }
                owners[ownerEntityId] = new NotificationEntityAlias
                {
                    OwnerEntityId = ownerEntityId,
                    OwnerEntityPrototypeId =
                        ownerEntityPrototypeId?.Trim() ?? "",
                    OwnerEntityTitle = ownerEntityTitle?.Trim() ?? "",
                };
            }
        }
    }

    private HashSet<int> GetNotificationChildIds(int ownerEntityId)
    {
        lock (m_notificationEntityAliasesGate)
        {
            return m_notificationChildrenByOwner.TryGetValue(
                    ownerEntityId,
                    out var childIds)
                ? new HashSet<int>(childIds)
                : new HashSet<int>();
        }
    }

    private NotificationEntityAlias[] GetNotificationOwnerAliases(
        int childEntityId)
    {
        lock (m_notificationEntityAliasesGate)
        {
            return m_notificationOwnersByChild.TryGetValue(
                    childEntityId,
                    out var owners)
                ? owners.Values.Select(alias => new NotificationEntityAlias
                {
                    OwnerEntityId = alias.OwnerEntityId,
                    OwnerEntityPrototypeId = alias.OwnerEntityPrototypeId,
                    OwnerEntityTitle = alias.OwnerEntityTitle,
                }).ToArray()
                : Array.Empty<NotificationEntityAlias>();
        }
    }

    public PanelDefinition GetOrCreateEntityPanel(
        int entityId,
        string entityTitle,
        string entityType,
        string entityPrototypeId)
    {
        if (entityId <= 0)
        {
            return null;
        }

        lock (m_persistenceGate)
        {
            var notificationEntities = TryGetLiveEntity(
                    entityId,
                    out var liveEntity)
                ? GetNotificationEntities(liveEntity)
                : Array.Empty<IEntity>();
            RegisterNotificationEntityAliases(
                entityId,
                entityPrototypeId,
                entityTitle,
                notificationEntities);
            var relatedEntityIds = new HashSet<int>(
                notificationEntities.Select(entity => entity.Id.Value));
            var definedVanillaSlots = notificationEntities
                .SelectMany(entity =>
                    EntityVanillaNotificationCatalog.DiscoverSlots(
                        entity,
                        entityTitle,
                        ColorFor))
                .ToArray();
            PanelSlotDefinition[] runtimeVanillaSlots;
            lock (m_gate)
            {
                runtimeVanillaSlots = m_alarms.Values
                    .Select(state => state.View)
                    .Where(view =>
                        string.Equals(
                            view.Source,
                            "vanilla",
                            StringComparison.Ordinal) &&
                        (relatedEntityIds.Contains(view.EntityId) ||
                         !string.IsNullOrWhiteSpace(entityPrototypeId) &&
                         string.Equals(
                             view.EntityPrototypeId,
                             entityPrototypeId,
                             StringComparison.Ordinal)))
                    .Select(PanelSlotProjection.CreateSlot)
                    .Where(slot => slot != null)
                    .ToArray();
            }
            PanelDefinition panel;
            var wasCreated = false;
            string previousName = null;
            string previousOwnerTitle = null;
            string previousOwnerPrototypeId = null;
            string previousOwnerEntityType = null;
            var previousOwnerEntityId = -1;
            var previousIncludeVanilla = false;
            var previousIncludeSystem = false;
            var previousIsDashboard = false;
            List<PanelSlotDefinition> previousSlots = null;
            lock (m_configurationGate)
            {
                panel = Configuration.Panels.FirstOrDefault(candidate =>
                    candidate != null &&
                    PanelTopologyPolicy.IsEntityPanel(candidate) &&
                    candidate.OwnerEntityId == entityId);
                if (panel == null)
                {
                    panel = new PanelDefinition
                    {
                        Id = CreateEntityPanelIdLocked(entityId),
                        Name = EntityPanelDisplayName(entityId, entityTitle),
                        Columns = 3,
                        IncludeVanilla = true,
                        IncludeSystem = false,
                        NotificationFilter = "",
                        IsDashboard = false,
                        OwnerEntityId = entityId,
                        OwnerEntityTitle = entityTitle?.Trim() ?? "",
                        OwnerEntityPrototypeId =
                            entityPrototypeId?.Trim() ?? "",
                        OwnerEntityType = entityType?.Trim() ?? "",
                    };
                    Configuration.Panels.Add(panel);
                    wasCreated = true;
                }
                else
                {
                    previousName = panel.Name;
                    previousOwnerTitle = panel.OwnerEntityTitle;
                    previousOwnerPrototypeId = panel.OwnerEntityPrototypeId;
                    previousOwnerEntityType = panel.OwnerEntityType;
                    previousOwnerEntityId = panel.OwnerEntityId;
                    previousIncludeVanilla = panel.IncludeVanilla;
                    previousIncludeSystem = panel.IncludeSystem;
                    previousIsDashboard = panel.IsDashboard;
                    previousSlots = panel.Slots
                        .Select(PanelSlotProjection.CloneSlot)
                        .ToList();
                    panel.Name = EntityPanelDisplayName(entityId, entityTitle);
                    panel.OwnerEntityTitle = entityTitle?.Trim() ?? "";
                    panel.OwnerEntityPrototypeId =
                        entityPrototypeId?.Trim() ?? "";
                    panel.OwnerEntityType = entityType?.Trim() ?? "";
                    panel.IncludeVanilla = true;
                    panel.IncludeSystem = false;
                    panel.IsDashboard = false;
                    panel.OwnerEntityId = entityId;
                }

                var knownPrototypeSlots = string.IsNullOrWhiteSpace(
                        panel.OwnerEntityPrototypeId)
                    ? Array.Empty<PanelSlotDefinition>()
                    : Configuration.Panels
                        .Where(candidate =>
                            candidate != null &&
                            PanelTopologyPolicy.IsEntityPanel(candidate) &&
                            candidate.OwnerEntityId != entityId &&
                            string.Equals(
                                candidate.OwnerEntityPrototypeId,
                                panel.OwnerEntityPrototypeId,
                                StringComparison.Ordinal))
                        .SelectMany(candidate =>
                            candidate.Slots ??
                            new List<PanelSlotDefinition>())
                        .Where(slot => string.Equals(
                            slot?.Source,
                            "vanilla",
                            StringComparison.Ordinal))
                        .Select(PanelSlotProjection.CloneSlot)
                        .ToArray();
                EntityVanillaSlotPolicy.Synchronize(
                    panel,
                    definedVanillaSlots
                        .Concat(runtimeVanillaSlots)
                        .Concat(knownPrototypeSlots));
            }

            if (SaveConfiguration())
            {
                return panel;
            }

            lock (m_configurationGate)
            {
                if (wasCreated)
                {
                    Configuration.Panels.Remove(panel);
                }
                else
                {
                    panel.Name = previousName;
                    panel.OwnerEntityTitle = previousOwnerTitle;
                    panel.OwnerEntityPrototypeId = previousOwnerPrototypeId;
                    panel.OwnerEntityType = previousOwnerEntityType;
                    panel.OwnerEntityId = previousOwnerEntityId;
                    panel.IncludeVanilla = previousIncludeVanilla;
                    panel.IncludeSystem = previousIncludeSystem;
                    panel.IsDashboard = previousIsDashboard;
                    panel.Slots = previousSlots;
                }
            }
            return null;
        }
    }

    public bool LinkRuleToPanel(
        string ruleId,
        string panelId,
        int preferredSlotIndex = -1)
    {
        ruleId = ruleId?.Trim() ?? "";
        panelId = panelId?.Trim() ?? "";
        if (ruleId.Length == 0 || panelId.Length == 0)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            AlarmRuleDefinition rule;
            PanelDefinition panel;
            List<string> previousLinks;
            List<PanelSlotDefinition> previousSlots;
            lock (m_configurationGate)
            {
                rule = Configuration.Rules.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, ruleId,
                        StringComparison.Ordinal));
                panel = Configuration.Panels.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, panelId,
                        StringComparison.Ordinal));
                if (rule == null ||
                    panel == null ||
                    panel.IsDashboard ||
                    PanelTopologyPolicy.IsEntityPanel(panel))
                {
                    return false;
                }
                if (string.Equals(rule.PanelId, panel.Id,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                rule.LinkedPanelIds ??= new List<string>();
                if (rule.LinkedPanelIds.Contains(
                        panel.Id,
                        StringComparer.Ordinal))
                {
                    return true;
                }
                previousLinks = rule.LinkedPanelIds.ToList();
                previousSlots = (panel.Slots ??
                        new List<PanelSlotDefinition>())
                    .Select(PanelSlotProjection.CloneSlot)
                    .ToList();
                rule.LinkedPanelIds.Add(panel.Id);
                panel.Slots ??= new List<PanelSlotDefinition>();
                if (preferredSlotIndex >= 0)
                {
                    PanelSlotProjection.InsertRuleSlot(
                        panel,
                        rule,
                        preferredSlotIndex);
                }
                else if (!panel.Slots.Any(slot => string.Equals(
                             slot?.AlarmId,
                             "rule:" + rule.Id,
                             StringComparison.Ordinal)))
                {
                    panel.Slots.Add(PanelSlotProjection.CreateRuleSlot(rule));
                }
            }

            if (SaveConfiguration())
            {
                return true;
            }

            lock (m_configurationGate)
            {
                rule.LinkedPanelIds = previousLinks;
                panel.Slots = previousSlots;
            }
            return false;
        }
    }

    public bool UnlinkRuleFromPanel(string ruleId, string panelId)
    {
        ruleId = ruleId?.Trim() ?? "";
        panelId = panelId?.Trim() ?? "";
        if (ruleId.Length == 0 || panelId.Length == 0)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            AlarmRuleDefinition rule;
            PanelDefinition panel;
            List<string> previousLinks;
            List<PanelSlotDefinition> previousSlots;
            lock (m_configurationGate)
            {
                rule = Configuration.Rules.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, ruleId,
                        StringComparison.Ordinal));
                panel = Configuration.Panels.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, panelId,
                        StringComparison.Ordinal));
                if (rule == null ||
                    panel == null ||
                    string.Equals(rule.PanelId, panel.Id,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                rule.LinkedPanelIds ??= new List<string>();
                if (!rule.LinkedPanelIds.Contains(
                        panel.Id,
                        StringComparer.Ordinal))
                {
                    return true;
                }
                previousLinks = rule.LinkedPanelIds.ToList();
                previousSlots = (panel.Slots ??
                        new List<PanelSlotDefinition>())
                    .Select(PanelSlotProjection.CloneSlot)
                    .ToList();
                rule.LinkedPanelIds.RemoveAll(linkedPanelId => string.Equals(
                    linkedPanelId,
                    panel.Id,
                    StringComparison.Ordinal));
                panel.Slots?.RemoveAll(slot => string.Equals(
                    slot?.AlarmId,
                    "rule:" + rule.Id,
                    StringComparison.Ordinal));
            }

            if (SaveConfiguration())
            {
                return true;
            }

            lock (m_configurationGate)
            {
                rule.LinkedPanelIds = previousLinks;
                panel.Slots = previousSlots;
            }
            return false;
        }
    }

    public bool TryGetLiveEntity(int entityId, out IEntity entity)
    {
        entity = null;
        if (entityId < 0)
        {
            return false;
        }
        try
        {
            var option = m_entitiesManager.GetEntity(new EntityId(entityId));
            if (option.IsNone || option.Value.IsDestroyed)
            {
                return false;
            }
            entity = option.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryResolveNavigationEntity(
        PanelDefinition panel,
        AlarmView alarm,
        out IEntity entity)
    {
        var entityId = -1;
        lock (m_configurationGate)
        {
            if (PanelTopologyPolicy.IsEntityPanel(panel))
            {
                entityId = panel.OwnerEntityId;
            }

            if (entityId < 0 &&
                PanelSlotProjection.TryGetCustomRuleId(
                    alarm,
                    out var ruleId))
            {
                var rule = Configuration.Rules.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, ruleId,
                        StringComparison.Ordinal));
                var ownerPanel = rule == null
                    ? null
                    : Configuration.Panels.FirstOrDefault(candidate =>
                        string.Equals(candidate?.Id, rule.PanelId,
                            StringComparison.Ordinal));
                if (PanelTopologyPolicy.IsEntityPanel(ownerPanel))
                {
                    entityId = ownerPanel.OwnerEntityId;
                }
                else
                {
                    var firstCondition = rule?.Conditions?
                        .FirstOrDefault(condition => condition != null);
                    entityId = firstCondition?.EntityId ?? -1;
                    if (entityId < 0 &&
                        !string.IsNullOrWhiteSpace(
                            firstCondition?.InstrumentId))
                    {
                        var instrument = Configuration.Instruments
                            .FirstOrDefault(candidate => string.Equals(
                                candidate?.Id,
                                firstCondition.InstrumentId,
                                StringComparison.Ordinal));
                        entityId = GetInstrumentSources(instrument)
                            .FirstOrDefault()?.EntityId ?? -1;
                    }
                }
            }
        }

        if (entityId < 0)
        {
            entityId = alarm?.EntityId ?? -1;
        }
        if (entityId < 0)
        {
            TryParseEntityId(
                PanelSlotProjection.StableAlarmId(alarm),
                out entityId);
        }
        return TryGetLiveEntity(entityId, out entity);
    }

    public bool TryResolveNavigationEntity(
        AlarmView alarm,
        out IEntity entity)
    {
        return TryResolveNavigationEntity(null, alarm, out entity);
    }

    private string CreateEntityPanelIdLocked(int entityId)
    {
        var preferredId = "entity:" + entityId.ToString(
            CultureInfo.InvariantCulture);
        if (!Configuration.Panels.Any(panel => string.Equals(
                panel?.Id,
                preferredId,
                StringComparison.Ordinal)))
        {
            return preferredId;
        }
        return "entity:" + entityId.ToString(CultureInfo.InvariantCulture) +
               ":" + Guid.NewGuid().ToString("N");
    }

    private static string EntityPanelDisplayName(
        int entityId,
        string entityTitle)
    {
        return string.IsNullOrWhiteSpace(entityTitle)
            ? UnmaText.Get("auto.2623e678be24") + entityId.ToString(CultureInfo.InvariantCulture)
            : entityTitle.Trim();
    }

    private static bool TryParseEntityId(string value, out int entityId)
    {
        entityId = -1;
        const string token = ":entity:";
        var index = value?.LastIndexOf(token, StringComparison.Ordinal) ?? -1;
        return index >= 0 &&
               int.TryParse(
                   value.Substring(index + token.Length),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out entityId) &&
               entityId >= 0;
    }

    private void Evaluate()
    {
        BeginAlarmPersistenceBatch();
        try
        {
            var settings = m_settings;
            var systemMetrics = CaptureSystemMetrics();
            var instrumentValues = CaptureInstrumentValues();
            if (settings.EnableSystemAlarms)
            {
                EvaluateSystemAlarms(systemMetrics);
            }
            EvaluateSustainedVanillaAlarms();
            EvaluateCustomRules(systemMetrics, instrumentValues);
            EvaluateExternalAlarms();
        }
        finally
        {
            if (EndAlarmPersistenceBatch())
            {
                PersistAlarmState();
            }
        }
    }

    public IReadOnlyList<AlarmView> GetViews(PanelDefinition panel)
    {
        if (panel == null)
        {
            return Array.Empty<AlarmView>();
        }

        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var vanillaRules = GetVanillaNotificationRulesSnapshot();
        if (panel.IsDashboard)
        {
            AlarmView[] activeCandidates;
            lock (m_gate)
            {
                activeCandidates = m_alarms.Values
                    .Where(state =>
                        state.View.IsActive &&
                        !IsSuppressedVanillaAlarm(
                            state.View,
                            disabledVanillaOverrideIds) &&
                        !IsVanillaAlarmHidden(state.View, vanillaRules))
                    .Select(state => Clone(state.View, state.Sequence))
                    .ToArray();
            }
            return PanelSlotProjection.ProjectActive(activeCandidates);
        }

        PanelSlotDefinition[] slots;
        var groupedBehavior = ResolveVanillaNotificationBehavior(
            vanillaRules,
            GroupedVanillaNotificationPolicy.OverrideId,
            -1,
            "");
        var showGroupedPersistentSlot =
            groupedBehavior != VanillaNotificationBehavior.Hidden &&
            groupedBehavior != VanillaNotificationBehavior.Ignored;
        lock (m_configurationGate)
        {
            slots = (panel.Slots ?? new List<PanelSlotDefinition>())
                .Select(PanelSlotProjection.CloneSlot)
                .Where(slot =>
                    slot != null &&
                    IsPersistedSlotAllowedOnPanelLocked(panel, slot) &&
                    (showGroupedPersistentSlot ||
                     !GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                         slot.AlarmId)) &&
                    !VanillaNotificationSuppressionPolicy
                        .IsSlotSuppressed(
                            slot,
                            disabledVanillaOverrideIds))
                .ToArray();
        }
        AlarmView[] candidates;
        var slotIds = new HashSet<string>(
            slots.Select(slot => slot.AlarmId),
            StringComparer.Ordinal);
        var relatedEntityIds = PanelTopologyPolicy.IsEntityPanel(panel)
            ? GetNotificationChildIds(panel.OwnerEntityId)
            : new HashSet<int>();
        lock (m_gate)
        {
            candidates = m_alarms.Values
                .Where(state =>
                    (slotIds.Contains(
                         PanelSlotProjection.StableAlarmId(state.View)) ||
                     string.Equals(
                         state.View.Source,
                         "vanilla",
                         StringComparison.Ordinal) &&
                     relatedEntityIds.Contains(state.View.EntityId)) &&
                    !IsVanillaAlarmHiddenOnPanel(
                        state.View,
                        vanillaRules,
                        panel,
                        relatedEntityIds))
                .Select(state => ProjectAlarmForPanel(
                    Clone(state.View, state.Sequence),
                    panel,
                    relatedEntityIds))
                .ToArray();
        }
        return PanelSlotProjection.Project(slots, candidates);
    }

    public bool TryGetDashboardViews(
        AlarmAreaFilter filter,
        out IReadOnlyList<AlarmView> views)
    {
        views = Array.Empty<AlarmView>();
        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        if (!TryCaptureAlarmAreaProjection(
                filter,
                disabledVanillaOverrideIds,
                out var normalizedFilter,
                out var dashboard,
                out var panels,
                out var vanillaRules))
        {
            return false;
        }

        if (normalizedFilter.Kind == AlarmAreaFilterKind.All)
        {
            views = GetViews(dashboard);
            return true;
        }

        AlarmView[] activeCandidates;
        lock (m_gate)
        {
            activeCandidates = m_alarms.Values
                .Where(state => state.View.IsActive)
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }
        views = ProjectActiveDashboardArea(activeCandidates.Where(candidate =>
            panels.Any(panel => IsAlarmVisibleOnAreaPanel(
                candidate,
                panel,
                vanillaRules))));
        return true;
    }

    public bool TryGetAlarmIncidentSnapshot(
        AlarmAreaFilter filter,
        out AlarmIncidentSnapshot snapshot)
    {
        snapshot = null;

        // Read the game clock exactly once before taking any UNMA monitor.
        // The pure policy can then reject an invalid or rolled-back timeline
        // without reaching back into Mafi while snapshots are being analyzed.
        var currentGameTick = CurrentGameTicks;
        if (!TryGetDashboardViews(filter, out var scopedViews))
        {
            return false;
        }

        var activeSamples = new List<AlarmIncidentActiveSample>(
            Math.Min(
                scopedViews.Count,
                AlarmIncidentPolicy.MaximumActiveSamples));
        var historyCapture = GetAlarmIncidentHistoryCapture();

        foreach (var view in scopedViews)
        {
            if (view == null)
            {
                continue;
            }
            var historyRaisedAtTicks = 0d;
            var hasExactHistory = view.Sequence > 0 &&
                historyCapture.RaisedAtTicksBySequence.TryGetValue(
                    view.Sequence,
                    out historyRaisedAtTicks);
            var activeSample = CreateAlarmIncidentActiveSample(
                view,
                hasExactHistory ? view.Sequence : 0L,
                historyRaisedAtTicks,
                currentGameTick);
            if (activeSample != null)
            {
                activeSamples.Add(activeSample);
            }
        }

        snapshot = AlarmIncidentPolicy.Analyze(
            activeSamples,
            historyCapture.RecentSignals,
            currentGameTick);
        return true;
    }

    private static AlarmIncidentActiveSample
        CreateAlarmIncidentActiveSample(
            AlarmView view,
            long historySequence,
            double historyRaisedAtTicks,
            double currentGameTick)
    {
        if (view == null)
        {
            return null;
        }
        var hasExactHistory = view.Sequence > 0 &&
            view.Sequence == historySequence &&
            !double.IsNaN(historyRaisedAtTicks) &&
            !double.IsInfinity(historyRaisedAtTicks) &&
            historyRaisedAtTicks >= 0d &&
            historyRaisedAtTicks <= currentGameTick;
        var raisedAtTicks = hasExactHistory
            ? historyRaisedAtTicks
            : currentGameTick;
        return new AlarmIncidentActiveSample(
            view.Key,
            PanelSlotProjection.StableAlarmId(view),
            view.Name,
            view.Detail,
            view.Source,
            view.PanelId,
            view.SlotId,
            view.EntityId,
            view.EntityPrototypeId,
            view.EntityTitle,
            view.Severity,
            view.Sequence,
            raisedAtTicks,
            view.IsAcknowledged);
    }

    private AlarmIncidentHistoryCapture GetAlarmIncidentHistoryCapture()
    {
        AlarmIncidentHistoryCapture latestCapture = null;
        for (var attempt = 0;
             attempt < MaximumAlarmIncidentHistoryCaptureAttempts;
             attempt++)
        {
            long revision;
            AlarmIncidentHistoryRow[] rows;
            lock (m_gate)
            {
                if (m_alarmIncidentHistoryCapture != null &&
                    m_alarmIncidentHistoryCaptureRevision ==
                    m_alarmHistoryRevision)
                {
                    return m_alarmIncidentHistoryCapture;
                }
                revision = m_alarmHistoryRevision;
                rows = m_alarmHistory
                    .Where(item => item != null)
                    .Select(item => new AlarmIncidentHistoryRow(
                        item.AlarmKey,
                        item.Severity,
                        item.Sequence,
                        item.RaisedAtTicks))
                    .ToArray();
            }

            // Sorting and immutable-map construction are deliberately outside
            // the alarm monitor. A racing history mutation changes the
            // revision and causes this optimistic capture to retry at most
            // once. A second race returns the coherent local result without
            // caching it so the render path always makes progress.
            latestCapture = BuildAlarmIncidentHistoryCapture(rows);
            lock (m_gate)
            {
                if (revision != m_alarmHistoryRevision)
                {
                    continue;
                }
                m_alarmIncidentHistoryCapture = latestCapture;
                m_alarmIncidentHistoryCaptureRevision = revision;
                return latestCapture;
            }
        }
        return latestCapture ?? BuildAlarmIncidentHistoryCapture(
            Array.Empty<AlarmIncidentHistoryRow>());
    }

    private static AlarmIncidentHistoryCapture
        BuildAlarmIncidentHistoryCapture(
            IReadOnlyList<AlarmIncidentHistoryRow> rows)
    {
        var raisedAtTicksBySequence = new Dictionary<long, double>();
        if (rows != null)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Sequence > 0 &&
                    !raisedAtTicksBySequence.ContainsKey(row.Sequence))
                {
                    raisedAtTicksBySequence.Add(
                        row.Sequence,
                        row.RaisedAtTicks);
                }
            }
        }
        var recentSignals = (rows ??
                Array.Empty<AlarmIncidentHistoryRow>())
            .OrderByDescending(row => row.Sequence)
            .Take(AlarmIncidentPolicy.MaximumOccurrenceSignals)
            .Select(row => new AlarmOccurrenceSignal(
                row.Key,
                row.Severity,
                row.Sequence,
                row.RaisedAtTicks))
            .ToArray();
        return new AlarmIncidentHistoryCapture
        {
            RaisedAtTicksBySequence =
                new ReadOnlyDictionary<long, double>(
                    raisedAtTicksBySequence),
            RecentSignals = Array.AsReadOnly(recentSignals),
        };
    }

    private bool TryCaptureAlarmAreaProjection(
        AlarmAreaFilter filter,
        ISet<string> disabledVanillaOverrideIds,
        out AlarmAreaFilter normalizedFilter,
        out PanelDefinition dashboard,
        out AlarmAreaPanelSnapshot[] panels,
        out VanillaNotificationRule[] vanillaRules)
    {
        normalizedFilter = AlarmAreaFilter.All;
        dashboard = null;
        panels = Array.Empty<AlarmAreaPanelSnapshot>();
        vanillaRules = Array.Empty<VanillaNotificationRule>();
        lock (m_configurationGate)
        {
            normalizedFilter = AlarmAreaPolicy.NormalizeFilter(
                filter,
                Configuration.AlarmAreas);
            if (!IsExactAlarmAreaFilter(filter, normalizedFilter))
            {
                return false;
            }

            dashboard = Configuration.Panels.FirstOrDefault(panel =>
                panel != null && panel.IsDashboard);
            if (dashboard == null)
            {
                return false;
            }
            if (normalizedFilter.Kind == AlarmAreaFilterKind.All)
            {
                return true;
            }

            vanillaRules = Configuration.VanillaNotificationRules
                .Where(rule => rule != null)
                .Select(CloneVanillaNotificationRule)
                .ToArray();
            panels = AlarmAreaPolicy.SelectGlobalPanels(
                    Configuration.Panels,
                    normalizedFilter)
                .Select(panel => CreateAlarmAreaPanelSnapshotLocked(
                    panel,
                    disabledVanillaOverrideIds))
                .ToArray();
            return true;
        }
    }

    private AlarmAreaPanelSnapshot CreateAlarmAreaPanelSnapshotLocked(
        PanelDefinition panel,
        ISet<string> disabledVanillaOverrideIds)
    {
        return new AlarmAreaPanelSnapshot
        {
            Panel = new PanelDefinition
            {
                Id = panel.Id,
                Name = panel.Name,
                Columns = panel.Columns,
                IncludeVanilla = panel.IncludeVanilla,
                IncludeSystem = panel.IncludeSystem,
                NotificationFilter = panel.NotificationFilter,
                IsDashboard = panel.IsDashboard,
                OwnerEntityId = panel.OwnerEntityId,
                OwnerEntityTitle = panel.OwnerEntityTitle,
                OwnerEntityPrototypeId = panel.OwnerEntityPrototypeId,
                OwnerEntityType = panel.OwnerEntityType,
                AreaId = panel.AreaId,
            },
            SlotIds = new HashSet<string>(
                (panel.Slots ?? new List<PanelSlotDefinition>())
                .Where(slot =>
                    slot != null &&
                    !string.IsNullOrWhiteSpace(slot.AlarmId) &&
                    IsPersistedSlotAllowedOnPanelLocked(panel, slot) &&
                    !VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
                        slot,
                        disabledVanillaOverrideIds))
                .Select(slot => slot.AlarmId.Trim()),
                StringComparer.Ordinal),
        };
    }

    private bool IsAlarmVisibleOnAreaPanel(
        AlarmView view,
        AlarmAreaPanelSnapshot panel,
        IEnumerable<VanillaNotificationRule> vanillaRules)
    {
        if (view == null || panel?.Panel == null ||
            panel.SlotIds == null ||
            !panel.SlotIds.Contains(
                PanelSlotProjection.StableAlarmId(view)))
        {
            return false;
        }
        return !IsVanillaAlarmHiddenOnPanel(
            view,
            vanillaRules,
            panel.Panel,
            Array.Empty<int>());
    }

    private static bool IsExactAlarmAreaFilter(
        AlarmAreaFilter requested,
        AlarmAreaFilter normalized)
    {
        if (requested.Kind != normalized.Kind)
        {
            return false;
        }
        return requested.Kind switch
        {
            AlarmAreaFilterKind.All => true,
            AlarmAreaFilterKind.Unassigned => true,
            AlarmAreaFilterKind.Area =>
                !string.IsNullOrWhiteSpace(requested.AreaId) &&
                string.Equals(
                    requested.AreaId.Trim(),
                    normalized.AreaId,
                    StringComparison.Ordinal),
            _ => false,
        };
    }

    private static IReadOnlyList<AlarmView> ProjectActiveDashboardArea(
        IEnumerable<AlarmView> candidates)
    {
        return PanelSlotProjection.ProjectActive(
            (candidates ?? Enumerable.Empty<AlarmView>())
            .Where(candidate => candidate != null && candidate.IsActive));
    }

    private static AlarmView ProjectAlarmForPanel(
        AlarmView view,
        PanelDefinition panel,
        IReadOnlyCollection<int> relatedEntityIds)
    {
        if (view == null ||
            !PanelTopologyPolicy.IsEntityPanel(panel) ||
            !string.Equals(view.Source, "vanilla", StringComparison.Ordinal) ||
            relatedEntityIds == null ||
            !relatedEntityIds.Contains(view.EntityId))
        {
            return view;
        }
        var overrideId = VanillaNotificationSuppressionPolicy
            .GetOverrideIdForSlotId(
                PanelSlotProjection.StableAlarmId(view));
        view.SlotId = overrideId + ":entity:" + panel.OwnerEntityId;
        view.EntityId = panel.OwnerEntityId;
        view.EntityPrototypeId = panel.OwnerEntityPrototypeId;
        view.EntityTitle = panel.OwnerEntityTitle;
        return view;
    }

    public AlarmView GetAudibleAlarm()
    {
        var currentGameTick = CurrentGameTick;
        if (!Settings.EnableAudio)
        {
            PruneAlarmAudioSnoozes(currentGameTick);
            return null;
        }

        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var vanillaRules = GetVanillaNotificationRulesSnapshot();
        lock (m_gate)
        {
            PruneAlarmAudioSnoozesLocked(currentGameTick);
            AlarmState best = null;
            foreach (var candidate in m_alarms.Values)
            {
                if (!candidate.View.RequiresAcknowledgement ||
                    m_alarmAudioSnoozes.ContainsKey(
                        candidate.View.Key ?? "") ||
                    IsSuppressedVanillaAlarm(
                        candidate.View,
                        disabledVanillaOverrideIds) ||
                    ResolveVanillaNotificationBehavior(
                        candidate.View,
                        vanillaRules) !=
                        VanillaNotificationBehavior.Normal ||
                    string.Equals(
                        candidate.View.SoundId,
                        "none",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (best == null ||
                    candidate.View.Severity > best.View.Severity ||
                    candidate.View.Severity == best.View.Severity &&
                    candidate.Sequence > best.Sequence)
                {
                    best = candidate;
                }
            }
            return best == null ? null : Clone(best.View);
        }
    }

    /// <summary>
    /// Takes the strongest pending presentation request. Requests are
    /// runtime-only and become stale when their exact occurrence is no
    /// longer active and unacknowledged. The UI decides how to present the
    /// request; this method never mutates the simulation or machine state.
    /// </summary>
    public bool TryTakeAttentionRequest(
        out AlarmAttentionRequest request)
    {
        lock (m_gate)
        {
            return AlarmAttentionQueuePolicy.TryTakeBest(
                m_attentionRequests,
                IsAttentionRequestRelevantLocked,
                out request);
        }
    }

    /// <summary>
    /// Takes the silent monthly summary queued on the first day of a game
    /// month. The UI owns presentation; taking it never changes alarms,
    /// history, acknowledgement, or audio state.
    /// </summary>
    public bool TryTakeOperatorSilenceReminder(
        out OperatorSilenceReminderSnapshot reminder)
    {
        lock (m_gate)
        {
            reminder = m_pendingOperatorSilenceReminder;
            m_pendingOperatorSilenceReminder = null;
            return reminder != null;
        }
    }

    private bool IsAttentionRequestRelevantLocked(
        AlarmAttentionRequest request)
    {
        return m_alarms.TryGetValue(request.AlarmKey, out var alarm) &&
               alarm.Sequence == request.Sequence &&
               alarm.View.IsActive &&
               !alarm.View.IsAcknowledged;
    }

    private void PruneAlarmAudioSnoozes(long currentGameTick)
    {
        lock (m_gate)
        {
            PruneAlarmAudioSnoozesLocked(currentGameTick);
        }
    }

    private void PruneAlarmAudioSnoozesLocked(long currentGameTick)
    {
        foreach (var pair in m_alarmAudioSnoozes.ToArray())
        {
            if (!m_alarms.TryGetValue(pair.Key, out var alarm) ||
                !alarm.View.RequiresAcknowledgement ||
                !AlarmAudioSnoozePolicy.IsSnoozed(
                    pair.Value,
                    alarm.View.Key,
                    alarm.Sequence,
                    currentGameTick,
                    alarm.View.IsActive))
            {
                m_alarmAudioSnoozes.Remove(pair.Key);
            }
        }
    }

    public int ActiveCount
    {
        get
        {
            var disabledVanillaOverrideIds =
                GetDisabledVanillaOverrideIds();
            var vanillaRules = GetVanillaNotificationRulesSnapshot();
            lock (m_gate)
            {
                return m_alarms.Values.Count(
                    state =>
                        state.View.IsActive &&
                        !IsSuppressedVanillaAlarm(
                            state.View,
                            disabledVanillaOverrideIds) &&
                        !IsVanillaAlarmHidden(state.View, vanillaRules));
            }
        }
    }

    public int UnacknowledgedCount
    {
        get
        {
            var disabledVanillaOverrideIds =
                GetDisabledVanillaOverrideIds();
            var vanillaRules = GetVanillaNotificationRulesSnapshot();
            lock (m_gate)
            {
                return m_alarms.Values.Count(
                    state =>
                        state.View.RequiresAcknowledgement &&
                        !IsSuppressedVanillaAlarm(
                            state.View,
                            disabledVanillaOverrideIds) &&
                        !IsVanillaAlarmHidden(state.View, vanillaRules));
            }
        }
    }

    public int SnoozeAlarmAudio(
        string panelId,
        string slotId,
        int durationTicks)
    {
        if (string.IsNullOrWhiteSpace(slotId) || durationTicks <= 0)
        {
            return 0;
        }
        var panel = FindPanel(panelId);
        if (panel == null)
        {
            return 0;
        }

        var currentGameTick = CurrentGameTick;
        var requestedUntilGameTick =
            currentGameTick > long.MaxValue - durationTicks
                ? long.MaxValue
                : currentGameTick + durationTicks;
        PruneAlarmAudioSnoozes(currentGameTick);
        return ApplyToProjectedAlarmStates(
            panel,
            new HashSet<string>(StringComparer.Ordinal)
            {
                slotId.Trim(),
            },
            alarm =>
            {
                if (!AlarmAudioSnoozePolicy.TryCreateUntilTick(
                        alarm.View.Key,
                        alarm.Sequence,
                        currentGameTick,
                        requestedUntilGameTick,
                        out var snooze))
                {
                    return false;
                }
                m_alarmAudioSnoozes[alarm.View.Key] = snooze;
                return true;
            });
    }

    public int UnsnoozeAlarmAudio(string panelId, string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return 0;
        }
        var panel = FindPanel(panelId);
        if (panel == null)
        {
            return 0;
        }

        PruneAlarmAudioSnoozes(CurrentGameTick);
        return ApplyToProjectedAlarmStates(
            panel,
            new HashSet<string>(StringComparer.Ordinal)
            {
                slotId.Trim(),
            },
            alarm => m_alarmAudioSnoozes.Remove(alarm.View.Key));
    }

    public bool IsAlarmAudioSnoozed(AlarmView alarm)
    {
        if (alarm == null)
        {
            return false;
        }
        var currentGameTick = CurrentGameTick;
        lock (m_gate)
        {
            PruneAlarmAudioSnoozesLocked(currentGameTick);
            return m_alarmAudioSnoozes.TryGetValue(
                       alarm.Key ?? "",
                       out var snooze) &&
                   AlarmAudioSnoozePolicy.IsSnoozed(
                       snooze,
                       alarm.Key,
                       alarm.Sequence,
                       currentGameTick,
                       alarm.IsActive);
        }
    }

    public bool IsAlarmAudioSnoozed(string panelId, string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return false;
        }
        var panel = FindPanel(panelId);
        if (panel == null)
        {
            return false;
        }

        var currentGameTick = CurrentGameTick;
        PruneAlarmAudioSnoozes(currentGameTick);
        var matchedCount = 0;
        var snoozedCount = ApplyToProjectedAlarmStates(
            panel,
            new HashSet<string>(StringComparer.Ordinal)
            {
                slotId.Trim(),
            },
            alarm =>
            {
                matchedCount++;
                return m_alarmAudioSnoozes.TryGetValue(
                           alarm.View.Key ?? "",
                           out var snooze) &&
                       AlarmAudioSnoozePolicy.IsSnoozed(
                           snooze,
                           alarm.View.Key,
                           alarm.Sequence,
                           currentGameTick,
                           alarm.View.IsActive);
            });
        return matchedCount > 0 && snoozedCount == matchedCount;
    }

    public bool AcknowledgeAlarm(string panelId, string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return false;
        }
        return AcknowledgeVisible(panelId, new[] { slotId }) > 0;
    }

    public int AcknowledgePanel(string panelId)
    {
        var panel = FindPanel(panelId);
        return panel == null
            ? 0
            : AcknowledgeProjectedSlots(panel, null);
    }

    public int AcknowledgeVisible(
        string panelId,
        IEnumerable<string> slotIds)
    {
        if (slotIds == null)
        {
            return 0;
        }
        var targets = new HashSet<string>(
            slotIds
                .Where(slotId => !string.IsNullOrWhiteSpace(slotId))
                .Select(slotId => slotId.Trim()),
            StringComparer.Ordinal);
        if (targets.Count == 0)
        {
            return 0;
        }

        var panel = FindPanel(panelId);
        return panel == null
            ? 0
            : AcknowledgeProjectedSlots(panel, targets);
    }

    public bool TryAcknowledgeDashboard(
        AlarmAreaFilter filter,
        IEnumerable<string> slotIds,
        out int count)
    {
        count = 0;
        HashSet<string> targetSlotIds = null;
        if (slotIds != null)
        {
            targetSlotIds = new HashSet<string>(
                slotIds
                    .Where(slotId => !string.IsNullOrWhiteSpace(slotId))
                    .Select(slotId => slotId.Trim()),
                StringComparer.Ordinal);
        }

        if (filter.Kind == AlarmAreaFilterKind.All)
        {
            var disabledVanillaOverrideIds =
                GetDisabledVanillaOverrideIds();
            if (!TryCaptureAlarmAreaProjection(
                    filter,
                    disabledVanillaOverrideIds,
                    out var normalizedFilter,
                    out var dashboard,
                    out _,
                    out _) ||
                normalizedFilter.Kind != AlarmAreaFilterKind.All)
            {
                return false;
            }
            count = targetSlotIds == null
                ? AcknowledgePanel(dashboard.Id)
                : AcknowledgeVisible(dashboard.Id, targetSlotIds);
            return true;
        }

        lock (m_persistenceGate)
        {
            var disabledVanillaOverrideIds =
                GetDisabledVanillaOverrideIds();
            if (!TryCaptureAlarmAreaProjection(
                    filter,
                    disabledVanillaOverrideIds,
                    out var normalizedFilter,
                    out _,
                    out var panels,
                    out var vanillaRules) ||
                normalizedFilter.Kind == AlarmAreaFilterKind.All)
            {
                return false;
            }
            if (targetSlotIds?.Count == 0)
            {
                return true;
            }

            AlarmView[] candidates;
            lock (m_gate)
            {
                candidates = m_alarms.Values
                    .Where(state =>
                        CanAcknowledgeFilteredDashboardAlarm(
                            state.View))
                    .Select(state => Clone(state.View, state.Sequence))
                    .ToArray();
            }
            var targets = candidates
                .Where(candidate =>
                    (targetSlotIds == null || targetSlotIds.Contains(
                        PanelSlotProjection.StableAlarmId(candidate))) &&
                    panels.Any(panel => IsAlarmVisibleOnAreaPanel(
                        candidate,
                        panel,
                        vanillaRules)))
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Key))
                .Select(candidate => new AlarmAreaAcknowledgementTarget
                {
                    Key = candidate.Key,
                    Sequence = candidate.Sequence,
                })
                .ToArray();

            if (targets.Length == 0)
            {
                return true;
            }
            lock (m_gate)
            {
                foreach (var target in targets)
                {
                    if (!m_alarms.TryGetValue(target.Key, out var alarm) ||
                        alarm.Sequence != target.Sequence ||
                        !CanAcknowledgeFilteredDashboardAlarm(alarm.View))
                    {
                        continue;
                    }
                    if (AcknowledgeAlarmStateLocked(alarm))
                    {
                        count++;
                    }
                }
                if (count > 0)
                {
                    m_alarmHistoryRevision++;
                }
            }
            if (count > 0)
            {
                PersistAlarmState();
            }
            return true;
        }
    }

    private static bool CanAcknowledgeFilteredDashboardAlarm(
        AlarmView view)
    {
        return view != null &&
               view.IsActive &&
               (view.RequiresAcknowledgement ||
                !view.IsOperatorSilenced);
    }

    public bool TryGetNextDashboardUnacknowledged(
        AlarmAreaFilter filter,
        string afterSlotId,
        out AlarmView view)
    {
        view = null;
        if (!TryGetDashboardViews(filter, out var views))
        {
            return false;
        }
        if (views.Count == 0)
        {
            return true;
        }

        var startIndex = -1;
        if (!string.IsNullOrWhiteSpace(afterSlotId))
        {
            var normalizedAfterSlotId = afterSlotId.Trim();
            for (var index = 0; index < views.Count; index++)
            {
                if (string.Equals(
                        PanelSlotProjection.StableAlarmId(views[index]),
                        normalizedAfterSlotId,
                        StringComparison.Ordinal))
                {
                    startIndex = index;
                    break;
                }
            }
        }

        for (var offset = 1; offset <= views.Count; offset++)
        {
            var candidate = views[(startIndex + offset) % views.Count];
            if (candidate.RequiresAcknowledgement)
            {
                view = candidate;
                break;
            }
        }
        return true;
    }

    public AlarmView GetNextUnacknowledged(
        string panelId,
        string afterSlotId = null)
    {
        var panel = FindPanel(panelId);
        if (panel == null)
        {
            return null;
        }

        var views = GetViews(panel);
        if (views.Count == 0)
        {
            return null;
        }

        var startIndex = -1;
        if (!string.IsNullOrWhiteSpace(afterSlotId))
        {
            var normalizedAfterSlotId = afterSlotId.Trim();
            for (var index = 0; index < views.Count; index++)
            {
                if (string.Equals(
                        PanelSlotProjection.StableAlarmId(views[index]),
                        normalizedAfterSlotId,
                        StringComparison.Ordinal))
                {
                    startIndex = index;
                    break;
                }
            }
        }

        for (var offset = 1; offset <= views.Count; offset++)
        {
            var candidate = views[(startIndex + offset) % views.Count];
            if (candidate.RequiresAcknowledgement)
            {
                return candidate;
            }
        }
        return null;
    }

    private PanelDefinition FindPanel(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
        {
            return null;
        }
        var normalizedPanelId = panelId.Trim();
        lock (m_configurationGate)
        {
            return Configuration.Panels.FirstOrDefault(panel =>
                panel != null && string.Equals(
                    panel.Id,
                    normalizedPanelId,
                    StringComparison.Ordinal));
        }
    }

    private string ResolveAttentionPanelId(
        string preferredPanelId,
        string slotId)
    {
        var normalizedPreferredPanelId = preferredPanelId?.Trim() ?? "";
        var normalizedSlotId = slotId?.Trim() ?? "";
        lock (m_configurationGate)
        {
            if (normalizedPreferredPanelId.Length > 0 &&
                Configuration.Panels.Any(panel =>
                    panel != null && string.Equals(
                        panel.Id,
                        normalizedPreferredPanelId,
                        StringComparison.Ordinal)))
            {
                return normalizedPreferredPanelId;
            }

            var slottedPanel = Configuration.Panels.FirstOrDefault(panel =>
                panel != null &&
                !panel.IsDashboard &&
                (panel.Slots ?? new List<PanelSlotDefinition>()).Any(slot =>
                    slot != null && string.Equals(
                        slot.AlarmId,
                        normalizedSlotId,
                        StringComparison.Ordinal)));
            if (slottedPanel != null)
            {
                return slottedPanel.Id ?? "";
            }

            return Configuration.Panels.FirstOrDefault(panel =>
                       panel != null && panel.IsDashboard)?.Id ??
                   Configuration.Panels.FirstOrDefault(panel =>
                       panel != null)?.Id ??
                   "";
        }
    }

    private int AcknowledgeProjectedSlots(
        PanelDefinition panel,
        ISet<string> targetSlotIds)
    {
        var acknowledgedCount = ApplyToProjectedAlarmStates(
            panel,
            targetSlotIds,
            AcknowledgeAlarmStateLocked,
            count =>
            {
                if (count > 0)
                {
                    m_alarmHistoryRevision++;
                }
            },
            includeAcknowledgedActive: true);

        var prunedExternal = PruneRetiredExternalAlarms();
        if (acknowledgedCount > 0 || prunedExternal)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
        return acknowledgedCount;
    }

    private int ApplyToProjectedAlarmStates(
        PanelDefinition panel,
        ISet<string> targetSlotIds,
        Func<AlarmState, bool> applyLocked,
        Action<int> completedLocked = null,
        bool includeAcknowledgedActive = false)
    {
        if (panel == null || applyLocked == null)
        {
            return 0;
        }
        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var vanillaRules = GetVanillaNotificationRulesSnapshot();
        HashSet<string> panelSlotIds = null;
        HashSet<int> relatedEntityIds = null;
        if (!panel.IsDashboard)
        {
            lock (m_configurationGate)
            {
                panelSlotIds = new HashSet<string>(
                    (panel.Slots ?? new List<PanelSlotDefinition>())
                    .Where(slot =>
                        slot != null &&
                        IsPersistedSlotAllowedOnPanelLocked(panel, slot) &&
                        !VanillaNotificationSuppressionPolicy
                            .IsSlotSuppressed(
                                slot,
                                disabledVanillaOverrideIds))
                    .Select(slot => slot.AlarmId),
                    StringComparer.Ordinal);
            }
            relatedEntityIds = PanelTopologyPolicy.IsEntityPanel(panel)
                ? GetNotificationChildIds(panel.OwnerEntityId)
                : new HashSet<int>();
        }

        var affectedCount = 0;
        lock (m_gate)
        {
            foreach (var alarm in m_alarms.Values)
            {
                if (!alarm.View.RequiresAcknowledgement &&
                    !(includeAcknowledgedActive &&
                      alarm.View.IsActive &&
                      !alarm.View.IsOperatorSilenced))
                {
                    continue;
                }

                AlarmView projected;
                if (panel.IsDashboard)
                {
                    if (!alarm.View.IsActive ||
                        IsSuppressedVanillaAlarm(
                            alarm.View,
                            disabledVanillaOverrideIds) ||
                        IsVanillaAlarmHidden(alarm.View, vanillaRules))
                    {
                        continue;
                    }
                    projected = alarm.View;
                }
                else
                {
                    var stableAlarmId =
                        PanelSlotProjection.StableAlarmId(alarm.View);
                    var belongsToPanel = panelSlotIds.Contains(stableAlarmId) ||
                        string.Equals(
                            alarm.View.Source,
                            "vanilla",
                            StringComparison.Ordinal) &&
                        relatedEntityIds.Contains(alarm.View.EntityId);
                    if (!belongsToPanel ||
                        IsVanillaAlarmHiddenOnPanel(
                            alarm.View,
                            vanillaRules,
                            panel,
                            relatedEntityIds))
                    {
                        continue;
                    }
                    projected = ProjectAlarmForPanel(
                        Clone(alarm.View, alarm.Sequence),
                        panel,
                        relatedEntityIds);
                    if (!panelSlotIds.Contains(
                            PanelSlotProjection.StableAlarmId(projected)))
                    {
                        continue;
                    }
                }

                var projectedSlotId =
                    PanelSlotProjection.StableAlarmId(projected);
                if (targetSlotIds != null &&
                    !targetSlotIds.Contains(projectedSlotId))
                {
                    continue;
                }

                if (applyLocked(alarm))
                {
                    affectedCount++;
                }
            }
            completedLocked?.Invoke(affectedCount);
        }
        return affectedCount;
    }

    private bool AcknowledgeAlarmStateLocked(AlarmState alarm)
    {
        if (alarm == null)
        {
            return false;
        }

        if (alarm.View.IsGoneUnacknowledged)
        {
            UpdateHistoryFromStateLocked(alarm, true, true);
            alarm.View.IsGoneUnacknowledged = false;
            alarm.View.IsAcknowledged = false;
            alarm.View.IsOperatorSilenced = false;
            alarm.View.OperatorSilencedAtGameTick = -1;
            return true;
        }

        if (!alarm.View.IsActive ||
            alarm.View.IsAcknowledged && alarm.View.IsOperatorSilenced)
        {
            return false;
        }

        alarm.View.IsAcknowledged = true;
        alarm.View.IsOperatorSilenced = true;
        alarm.View.OperatorSilencedAtGameTick = CurrentGameTick;
        UpdateHistoryFromStateLocked(alarm, false, true);
        return true;
    }

    public void AcknowledgeAll()
    {
        var changed = false;
        var currentGameTick = CurrentGameTick;
        lock (m_gate)
        {
            foreach (var item in m_alarmHistory)
            {
                if (!item.IsAcknowledged)
                {
                    item.SetState(
                        item.IsGone,
                        isAcknowledged: true,
                        currentGameTicks: CurrentGameTicks);
                    changed = true;
                }
            }
            foreach (var alarm in m_alarms.Values)
            {
                if (alarm.View.IsGoneUnacknowledged)
                {
                    alarm.View.IsGoneUnacknowledged = false;
                    alarm.View.IsAcknowledged = false;
                    alarm.View.IsOperatorSilenced = false;
                    alarm.View.OperatorSilencedAtGameTick = -1;
                    changed = true;
                }
                else if (alarm.View.IsActive &&
                         (!alarm.View.IsAcknowledged ||
                          !alarm.View.IsOperatorSilenced))
                {
                    alarm.View.IsAcknowledged = true;
                    alarm.View.IsOperatorSilenced = true;
                    alarm.View.OperatorSilencedAtGameTick =
                        currentGameTick;
                    changed = true;
                }
            }
            if (changed)
            {
                m_alarmHistoryRevision++;
            }
        }
        var prunedExternal = PruneRetiredExternalAlarms();
        if (changed || prunedExternal)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
    }

    public UnmaTransferProfile GetTransferProfile()
    {
        lock (m_transferProfileGate)
        {
            if (m_transferProfileLoaded)
            {
                return m_transferProfile;
            }

            m_transferProfileLoaded = true;
            if (m_transferProfileStore == null)
            {
                LastTransferProfileError =
                    "Transfer profile storage is unavailable.";
                return null;
            }

            m_transferProfile = m_transferProfileStore.Load(out var error);
            LastTransferProfileError = error ?? "";
            if (ConfigurationTransferPolicy
                .ShouldInitializeRecommendedProfile(
                    m_transferProfile,
                    LastTransferProfileError,
                    m_transferProfileStore.IsWriteBlocked))
            {
                var recommendedProfile =
                    ConfigurationTransferPolicy
                        .CreateRecommendedQuietProfile("0.10.3");
                if (m_transferProfileStore.SaveIfMissing(
                        recommendedProfile,
                        out var alreadyExists,
                        out var saveError))
                {
                    m_transferProfile = recommendedProfile;
                }
                else if (alreadyExists)
                {
                    m_transferProfile = m_transferProfileStore.Load(
                        out var concurrentLoadError);
                    LastTransferProfileError = concurrentLoadError ?? "";
                    if (m_transferProfile == null &&
                        string.IsNullOrWhiteSpace(LastTransferProfileError))
                    {
                        LastTransferProfileError =
                            "Transfer profile changed while the recommended " +
                            "profile was being initialized.";
                    }
                }
                else
                {
                    LastTransferProfileError = saveError;
                }
            }
            if (ConfigurationTransferPolicy
                .TryRefreshPreviousRecommendedProfile(
                    m_transferProfile,
                    "0.10.3",
                    out var upgradedRecommendedProfile))
            {
                // Keep custom and on-disk profiles untouched. This refreshes
                // only an exact earlier built-in baseline for this session.
                m_transferProfile = upgradedRecommendedProfile;
            }
            if (m_transferProfileStore.IsWriteBlocked &&
                string.IsNullOrWhiteSpace(LastTransferProfileError))
            {
                LastTransferProfileError =
                    m_transferProfileStore.WriteBlockReason;
            }
            return m_transferProfile;
        }
    }

    public bool SaveTransferProfile(
        string profileName,
        TransferProfileSelection selection)
    {
        if (m_transferProfileStore == null)
        {
            LastTransferProfileError =
                "Transfer profile storage is unavailable.";
            return false;
        }

        try
        {
            UnmaConfiguration snapshot;
            lock (m_configurationGate)
            {
                snapshot = CloneConfiguration(Configuration);
            }
            var profile = ConfigurationTransferPolicy.CreateProfile(
                snapshot,
                selection ?? new TransferProfileSelection(),
                profileName,
                "0.10.3");
            if (!m_transferProfileStore.Save(profile, out var error))
            {
                LastTransferProfileError = error;
                return false;
            }

            lock (m_transferProfileGate)
            {
                m_transferProfile = profile;
                m_transferProfileLoaded = true;
                LastTransferProfileError = "";
            }
            return true;
        }
        catch (Exception exception)
        {
            LastTransferProfileError = ExceptionDetail(
                exception,
                "Transfer profile could not be saved.");
            return false;
        }
    }

    public TransferImportPreview PreviewTransferProfile(
        TransferProfileSelection selection)
    {
        try
        {
            var profile = SelectTransferProfile(selection);
            if (profile == null)
            {
                if (string.IsNullOrWhiteSpace(LastTransferProfileError))
                {
                    LastTransferProfileError =
                        "No transfer profile has been saved yet.";
                }
                return null;
            }

            UnmaConfiguration snapshot;
            lock (m_configurationGate)
            {
                snapshot = CloneConfiguration(Configuration);
            }
            var preview = ConfigurationTransferPolicy.PreviewImport(
                snapshot,
                profile);
            LastTransferProfileError = "";
            return preview;
        }
        catch (Exception exception)
        {
            LastTransferProfileError = ExceptionDetail(
                exception,
                "Transfer profile preview could not be prepared.");
            return null;
        }
    }

    public bool ImportTransferProfile(
        TransferProfileSelection selection,
        out TransferImportPreview preview)
    {
        preview = null;
        UnmaTransferProfile profile;
        try
        {
            profile = SelectTransferProfile(selection);
        }
        catch (Exception exception)
        {
            LastTransferProfileError = ExceptionDetail(
                exception,
                "Transfer profile could not be read.");
            return false;
        }
        if (profile == null)
        {
            if (string.IsNullOrWhiteSpace(LastTransferProfileError))
            {
                LastTransferProfileError =
                    "No transfer profile has been saved yet.";
            }
            return false;
        }

        var systemAlarmReconciliations =
            new List<KeyValuePair<
                SystemAlarmDefinition,
                SystemAlarmDefinition>>();
        lock (m_persistenceGate)
        {
            UnmaConfiguration snapshot;
            TransferImportResult result;
            try
            {
                lock (m_configurationGate)
                {
                    snapshot = CloneConfiguration(Configuration);
                }
                result = ConfigurationTransferPolicy.Merge(
                    snapshot,
                    profile);
                if (profile.Selection?.SystemAlarms == true)
                {
                    foreach (var importedAlarm in profile.SystemAlarms ??
                                 new List<SystemAlarmDefinition>())
                    {
                        var alarmId = importedAlarm?.Id?.Trim() ?? "";
                        if (alarmId.Length == 0)
                        {
                            continue;
                        }
                        var previousAlarm = (snapshot.SystemAlarms ??
                                new List<SystemAlarmDefinition>())
                            .LastOrDefault(alarm => string.Equals(
                                alarm?.Id?.Trim(),
                                alarmId,
                                StringComparison.Ordinal));
                        var currentAlarm = (result.Configuration.SystemAlarms ??
                                new List<SystemAlarmDefinition>())
                            .LastOrDefault(alarm => string.Equals(
                                alarm?.Id?.Trim(),
                                alarmId,
                                StringComparison.Ordinal));
                        if (currentAlarm != null)
                        {
                            systemAlarmReconciliations.Add(
                                new KeyValuePair<
                                    SystemAlarmDefinition,
                                    SystemAlarmDefinition>(
                                    previousAlarm,
                                    currentAlarm));
                        }
                    }
                }
                lock (m_configurationGate)
                {
                    RestoreConfiguration(
                        Configuration,
                        result.Configuration);
                }
            }
            catch (Exception exception)
            {
                LastTransferProfileError = ExceptionDetail(
                    exception,
                    "Transfer profile could not be applied.");
                return false;
            }

            if (!SaveConfiguration())
            {
                lock (m_configurationGate)
                {
                    RestoreConfiguration(Configuration, snapshot);
                }
                RestoreConfigurationAlarmSnapshots();
                LastTransferProfileError = string.IsNullOrWhiteSpace(
                    LastPersistenceError)
                    ? "Imported configuration could not be saved."
                    : LastPersistenceError;
                return false;
            }
            preview = result.Preview;
        }

        LastTransferProfileError = "";
        try
        {
            foreach (var reconciliation in systemAlarmReconciliations)
            {
                ReconcileSystemAlarmTimingDefinition(
                    reconciliation.Key,
                    reconciliation.Value);
            }
            RefreshDisabledVanillaOverrideIds();
            var soundStateChanged = ApplyTransferredSoundSettings(profile);
            ReconcileTransferredVanillaNotifications(
                profile,
                soundStateChanged);
            Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
            if (!SaveConfiguration())
            {
                LastTransferProfileError =
                    "Transfer profile settings were saved, but the " +
                    "reconciled live alarm state could not be saved: " +
                    (string.IsNullOrWhiteSpace(LastPersistenceError)
                        ? "unknown persistence error"
                        : LastPersistenceError);
                Log.Warning("UNMA: " + LastTransferProfileError);
                preview?.Diagnostics.Add(LastTransferProfileError);
                return false;
            }
        }
        catch (Exception exception)
        {
            LastTransferProfileError =
                "Transfer profile was saved, but live state refresh failed: " +
                ExceptionDetail(exception, "unknown error");
            Log.Warning("UNMA: " + LastTransferProfileError);
            preview?.Diagnostics.Add(LastTransferProfileError);
            return false;
        }
        return true;
    }

    public bool SaveConfiguration()
    {
        lock (m_persistenceGate)
        {
            AlarmView[] knownAlarms;
            lock (m_gate)
            {
                knownAlarms = m_alarms.Values
                    .Select(state => Clone(state.View, state.Sequence))
                    .ToArray();
            }
            foreach (var alarm in knownAlarms)
            {
                EnsurePanelSlotsForAlarm(alarm);
            }

            CapturePersistentAlarmState(
                out var alarmMemories,
                out var alarmHistory,
                out var alarmTimingMemories);
            bool saved;
            string error;
            lock (m_configurationGate)
            {
                SanitizeEntityPanelSlotsLocked();
                Configuration.AlarmMemories = alarmMemories;
                Configuration.AlarmHistory = alarmHistory;
                Configuration.AlarmTimingMemories = alarmTimingMemories;
                saved = m_store.Save(Configuration, out error);
            }

            if (saved)
            {
                LastPersistenceError = "";
                return true;
            }

            LastPersistenceError = error;
            return false;
        }
    }

    private void CapturePersistentAlarmState(
        out List<AlarmMemoryDefinition> alarmMemories,
        out List<AlarmHistoryDefinition> alarmHistory,
        out List<AlarmTimingMemoryDefinition> alarmTimingMemories)
    {
        lock (m_gate)
        {
            alarmMemories = m_alarms.Values
                .Where(state => state.View.IsLatched)
                .OrderBy(state => state.Sequence)
                .Select(state => new AlarmMemoryDefinition
                {
                    Key = state.View.Key,
                    Name = state.View.Name,
                    Detail = state.View.Detail,
                    Source = state.View.Source,
                    PanelId = state.View.PanelId,
                    ActiveColor = state.View.ActiveColor,
                    SoundId = state.View.SoundId,
                    OverrideId = state.View.OverrideId,
                    OccurrenceId = state.View.OccurrenceId,
                    SlotId = state.View.SlotId,
                    OccurrencePriority =
                        state.View.OccurrencePriority,
                    Severity = state.View.Severity,
                    IsActive = state.View.IsActive,
                    IsAcknowledged = state.View.IsAcknowledged,
                    IsOperatorSilenced = state.View.IsOperatorSilenced,
                    OperatorSilencedAtGameTick =
                        state.View.OperatorSilencedAtGameTick,
                    IsGoneUnacknowledged =
                        state.View.IsGoneUnacknowledged,
                    IsMissingSource = state.View.IsMissingSource,
                    LastValue = state.View.LastValue,
                    Sequence = state.Sequence,
                    AutoAcknowledgeOnClear = string.Equals(
                            state.View.Source,
                            "external",
                            StringComparison.Ordinal) &&
                        m_externalAutoAcknowledgeByKey.TryGetValue(
                            state.View.Key,
                            out var autoAcknowledgeOnClear) &&
                        autoAcknowledgeOnClear,
                    EntityId = state.View.EntityId,
                    EntityPrototypeId = state.View.EntityPrototypeId,
                    EntityTitle = state.View.EntityTitle,
                })
                .ToList();
            alarmHistory = m_alarmHistory
                .OrderBy(item => item.Sequence)
                .Select(CloneHistory)
                .ToList();
        }
        alarmTimingMemories = CapturePersistentTimingState();
    }

    private List<AlarmTimingMemoryDefinition> CapturePersistentTimingState()
    {
        var memories = new List<AlarmTimingMemoryDefinition>();
        lock (m_alarmTimingGate)
        {
            foreach (var ruleId in m_ruleTimingStates.Keys
                         .Concat(m_ruleConditionLatches.Keys)
                         .Concat(m_ruleTimingSignatures.Keys)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!m_ruleTimingStates.TryGetValue(ruleId, out var state) ||
                    !m_ruleTimingSignatures.TryGetValue(
                        ruleId,
                        out var signature))
                {
                    continue;
                }
                m_ruleConditionLatches.TryGetValue(ruleId, out var latches);
                var memory = AlarmTimingMemoryPolicy.CreateMemory(
                    AlarmTimingMemoryPolicy.RuleOwnerKey(ruleId),
                    signature,
                    state,
                    latches);
                if (memory != null)
                {
                    memories.Add(memory);
                }
            }

            foreach (var stageKey in m_systemStageTimingStates.Keys
                         .Concat(m_systemStageConditionLatches.Keys)
                         .Concat(m_systemStageTimingSignatures.Keys)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(key => key, StringComparer.Ordinal))
            {
                if (!m_systemStageTimingStates.TryGetValue(
                        stageKey,
                        out var state) ||
                    !m_systemStageTimingSignatures.TryGetValue(
                        stageKey,
                        out var signature))
                {
                    continue;
                }
                m_systemStageConditionLatches.TryGetValue(
                    stageKey,
                    out var latches);
                var memory = AlarmTimingMemoryPolicy.CreateMemory(
                    stageKey,
                    signature,
                    state,
                    latches);
                if (memory != null)
                {
                    memories.Add(memory);
                }
            }
        }
        return memories
            .OrderBy(memory => memory.OwnerKey, StringComparer.Ordinal)
            .ToList();
    }

    public long AlarmHistoryRevision
    {
        get
        {
            lock (m_gate)
            {
                return m_alarmHistoryRevision;
            }
        }
    }

    public IReadOnlyList<AlarmHistoryDefinition> GetAlarmHistory()
    {
        lock (m_gate)
        {
            return m_alarmHistory
                .OrderByDescending(item => item.Sequence)
                .Select(CloneHistory)
                .ToArray();
        }
    }

    public bool DeleteAlarmHistoryEntry(long sequence)
    {
        lock (m_persistenceGate)
        {
            AlarmHistoryDefinition removed;
            var removedIndex = -1;
            lock (m_gate)
            {
                removedIndex = m_alarmHistory.FindIndex(item =>
                    item.Sequence == sequence && item.CanDelete);
                if (removedIndex < 0)
                {
                    return false;
                }
                removed = m_alarmHistory[removedIndex];
                m_alarmHistory.RemoveAt(removedIndex);
                m_alarmHistoryRevision++;
            }

            if (SaveConfiguration())
            {
                return true;
            }

            lock (m_gate)
            {
                m_alarmHistory.Insert(removedIndex, removed);
                m_alarmHistoryRevision++;
            }
            return false;
        }
    }

    public bool DeleteCompletedAlarmHistory(out int deletedCount)
    {
        deletedCount = 0;
        lock (m_persistenceGate)
        {
            List<AlarmHistoryDefinition> removed;
            lock (m_gate)
            {
                removed = m_alarmHistory
                    .Where(item => item.CanDelete)
                    .Select(CloneHistory)
                    .ToList();
                if (removed.Count == 0)
                {
                    return true;
                }
                m_alarmHistory.RemoveAll(item => item.CanDelete);
                m_alarmHistoryRevision++;
            }

            if (SaveConfiguration())
            {
                deletedCount = removed.Count;
                return true;
            }

            lock (m_gate)
            {
                m_alarmHistory.AddRange(removed);
                m_alarmHistory.Sort((left, right) =>
                    left.Sequence.CompareTo(right.Sequence));
                m_alarmHistoryRevision++;
            }
            return false;
        }
    }

    private void PersistAlarmState()
    {
        lock (m_alarmPersistenceBatchGate)
        {
            if (m_alarmPersistenceSuppressionDepth > 0)
            {
                m_alarmPersistencePending = true;
                return;
            }
            m_alarmPersistencePending = false;
        }
        SaveConfiguration();
    }

    private void BeginAlarmPersistenceBatch()
    {
        lock (m_alarmPersistenceBatchGate)
        {
            m_alarmPersistenceSuppressionDepth++;
        }
    }

    private bool EndAlarmPersistenceBatch()
    {
        lock (m_alarmPersistenceBatchGate)
        {
            if (m_alarmPersistenceSuppressionDepth <= 0)
            {
                return false;
            }
            m_alarmPersistenceSuppressionDepth--;
            return m_alarmPersistenceSuppressionDepth == 0 &&
                   m_alarmPersistencePending;
        }
    }

    public bool AddRule(
        AlarmRuleDefinition rule,
        int preferredSlotIndex = -1)
    {
        if (rule == null)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            return AddRuleWithPersistenceLock(rule, preferredSlotIndex);
        }
    }

    private bool AddRuleWithPersistenceLock(
        AlarmRuleDefinition rule,
        int preferredSlotIndex)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N");
        }
        else
        {
            rule.Id = rule.Id.Trim();
        }
        lock (m_configurationGate)
        {
            PanelDefinition preferredPanel = null;
            if (preferredSlotIndex >= 0)
            {
                preferredPanel = Configuration.Panels.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.Id,
                        rule.PanelId,
                        StringComparison.Ordinal));
                if (preferredPanel == null || preferredPanel.IsDashboard)
                {
                    return false;
                }
            }
            if (Configuration.Rules.Any(existing =>
                    string.Equals(
                        existing?.Id,
                        rule.Id,
                        StringComparison.Ordinal)))
            {
                return false;
            }
            Configuration.Rules.Add(rule);
            if (preferredSlotIndex >= 0)
            {
                PanelSlotProjection.InsertRuleSlot(
                    preferredPanel,
                    rule,
                    preferredSlotIndex);
            }
        }
        if (SaveConfiguration())
        {
            Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
            return true;
        }

        lock (m_configurationGate)
        {
            Configuration.Rules.Remove(rule);
            var alarmId = "rule:" + rule.Id;
            foreach (var panel in Configuration.Panels)
            {
                panel.Slots?.RemoveAll(slot => string.Equals(
                    slot.AlarmId,
                    alarmId,
                    StringComparison.Ordinal));
            }
        }
        return false;
    }

    public bool UpdateRule(AlarmRuleDefinition updatedRule)
    {
        if (updatedRule == null ||
            string.IsNullOrWhiteSpace(updatedRule.Id))
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            return UpdateRuleWithPersistenceLock(updatedRule);
        }
    }

    private bool UpdateRuleWithPersistenceLock(
        AlarmRuleDefinition updatedRule)
    {
        AlarmRuleDefinition previousRule;
        AlarmTimingOwnerRuntimeSnapshot previousTiming;
        UnmaConfiguration configurationSnapshot;
        bool timingSemanticsChanged;
        var ruleIndex = -1;
        lock (m_configurationGate)
        {
            ruleIndex = Configuration.Rules.FindIndex(rule =>
                string.Equals(
                    rule.Id,
                    updatedRule.Id,
                    StringComparison.Ordinal));
            if (ruleIndex < 0)
            {
                return false;
            }
            previousRule = Configuration.Rules[ruleIndex];
            try
            {
                configurationSnapshot = CloneConfiguration(Configuration);
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration snapshot could not be created.");
                return false;
            }
            previousTiming = CaptureRuleTimingSnapshot(updatedRule.Id);
            Configuration.Rules[ruleIndex] = updatedRule;
            timingSemanticsChanged = ReconcileRuleTimingDefinition(
                previousRule,
                updatedRule);
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                RestoreConfiguration(Configuration, configurationSnapshot);
            }
            RestoreRuleTimingSnapshot(updatedRule.Id, previousTiming);
            RestoreConfigurationAlarmSnapshots();
            return false;
        }

        if (timingSemanticsChanged)
        {
            RemoveSustainedStatesForRule(updatedRule.Id);
        }
        if (!updatedRule.Enabled)
        {
            ForceNormal("rule:" + updatedRule.Id);
        }
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        return true;
    }

    public bool RemoveRule(string ruleId)
    {
        lock (m_persistenceGate)
        {
            return RemoveRulesWithPersistenceLock(
                new[] { ruleId },
                out _);
        }
    }

    private bool RemoveRulesWithPersistenceLock(
        IEnumerable<string> ruleIds,
        out int removedCount)
    {
        removedCount = 0;
        var requestedIds = new HashSet<string>(
            (ruleIds ?? Enumerable.Empty<string>()).Where(
                id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        if (requestedIds.Count == 0)
        {
            return false;
        }

        List<AlarmRuleDefinition> previousRules;
        Dictionary<PanelDefinition, List<PanelSlotDefinition>>
            previousPanelSlots;
        Dictionary<PanelDefinition, List<string>> previousExcludedAlarmIds;
        AlarmRuleDefinition[] removedRules;
        lock (m_configurationGate)
        {
            removedRules = Configuration.Rules
                .Where(rule => rule != null && requestedIds.Contains(rule.Id))
                .ToArray();
            if (removedRules.Length == 0)
            {
                return false;
            }
            previousRules = Configuration.Rules.ToList();
            previousPanelSlots = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.Slots ?? new List<PanelSlotDefinition>())
                    .Select(PanelSlotProjection.CloneSlot)
                    .ToList());
            previousExcludedAlarmIds = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.ExcludedAlarmIds ?? new List<string>())
                    .ToList());
            Configuration.Rules.RemoveAll(rule =>
                rule != null && requestedIds.Contains(rule.Id));
            var removedAlarmIds = new HashSet<string>(
                removedRules.Select(rule => "rule:" + rule.Id),
                StringComparer.Ordinal);
            foreach (var panel in Configuration.Panels)
            {
                panel.Slots?.RemoveAll(slot =>
                    slot != null && removedAlarmIds.Contains(slot.AlarmId));
                panel.ExcludedAlarmIds?.RemoveAll(removedAlarmIds.Contains);
            }
        }

        var removedAlarmStates = new Dictionary<string, AlarmState>(
            StringComparer.Ordinal);
        List<AlarmHistoryDefinition> previousHistory;
        long previousHistoryRevision;
        lock (m_gate)
        {
            previousHistory = m_alarmHistory
                .Select(CloneHistory)
                .ToList();
            previousHistoryRevision = m_alarmHistoryRevision;
            var historyChanged = false;
            foreach (var removedRule in removedRules)
            {
                var alarmKey = "rule:" + removedRule.Id;
                if (m_alarms.TryGetValue(alarmKey, out var removedState))
                {
                    removedAlarmStates[alarmKey] = removedState;
                    historyChanged |= CloseHistoryLocked(
                        removedState.Sequence,
                        removedState.View.IsAcknowledged);
                }
                m_alarms.Remove(alarmKey);
            }
            if (historyChanged)
            {
                m_alarmHistoryRevision++;
            }
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Rules.Clear();
                Configuration.Rules.AddRange(previousRules);
                foreach (var pair in previousPanelSlots)
                {
                    pair.Key.Slots = pair.Value;
                }
                foreach (var pair in previousExcludedAlarmIds)
                {
                    pair.Key.ExcludedAlarmIds = pair.Value;
                }
            }
            lock (m_gate)
            {
                foreach (var pair in removedAlarmStates)
                {
                    m_alarms[pair.Key] = pair.Value;
                }
                m_alarmHistory.Clear();
                m_alarmHistory.AddRange(previousHistory);
                m_alarmHistoryRevision = previousHistoryRevision;
            }
            CapturePersistentAlarmState(
                out var restoredMemories,
                out var restoredHistory,
                out var restoredTimingMemories);
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = restoredMemories;
                Configuration.AlarmHistory = restoredHistory;
                Configuration.AlarmTimingMemories = restoredTimingMemories;
            }
            return false;
        }
        foreach (var removedRule in removedRules)
        {
            InvalidateRuleTiming(removedRule.Id);
        }
        removedCount = removedRules.Length;
        return true;
    }

    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        lock (m_persistenceGate)
        {
            return SetRuleEnabledWithPersistenceLock(ruleId, enabled);
        }
    }

    private bool SetRuleEnabledWithPersistenceLock(
        string ruleId,
        bool enabled)
    {
        AlarmRuleDefinition rule;
        bool previous;
        UnmaConfiguration configurationSnapshot;
        lock (m_configurationGate)
        {
            rule = Configuration.Rules.FirstOrDefault(
                candidate => string.Equals(
                    candidate.Id,
                    ruleId,
                    StringComparison.Ordinal));
            if (rule == null)
            {
                return false;
            }
            previous = rule.Enabled;
            if (previous == enabled)
            {
                return true;
            }
            try
            {
                configurationSnapshot = CloneConfiguration(Configuration);
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration snapshot could not be created.");
                return false;
            }
            rule.Enabled = enabled;
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                RestoreConfiguration(Configuration, configurationSnapshot);
            }
            RestoreConfigurationAlarmSnapshots();
            return false;
        }

        InvalidateRuleTiming(ruleId);
        if (!enabled)
        {
            ForceNormal("rule:" + ruleId);
        }
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        return true;
    }

    public IReadOnlyList<SystemAlarmDefinition> GetSystemAlarmDefinitions()
    {
        lock (m_configurationGate)
        {
            return Configuration.SystemAlarms
                .Select(CloneSystemAlarmForEditing)
                .ToArray();
        }
    }

    public IReadOnlyDictionary<string, double> GetSystemMetricValues()
    {
        lock (m_systemMetricsGate)
        {
            return new Dictionary<string, double>(
                m_lastSystemMetrics,
                StringComparer.Ordinal);
        }
    }

    public bool UpdateSystemAlarm(SystemAlarmDefinition updatedAlarm)
    {
        if (updatedAlarm == null ||
            string.IsNullOrWhiteSpace(updatedAlarm.Id))
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            return UpdateSystemAlarmWithPersistenceLock(updatedAlarm);
        }
    }

    private bool UpdateSystemAlarmWithPersistenceLock(
        SystemAlarmDefinition updatedAlarm)
    {

        SystemAlarmDefinition previousAlarm;
        Dictionary<string, AlarmTimingOwnerRuntimeSnapshot> previousTiming;
        UnmaConfiguration configurationSnapshot;
        var alarmIndex = -1;
        var replacement = CloneSystemAlarmForEditing(updatedAlarm);
        lock (m_configurationGate)
        {
            alarmIndex = Configuration.SystemAlarms.FindIndex(alarm =>
                string.Equals(
                    alarm.Id,
                    updatedAlarm.Id,
                    StringComparison.Ordinal));
            if (alarmIndex < 0)
            {
                return false;
            }
            previousAlarm = Configuration.SystemAlarms[alarmIndex];
            try
            {
                configurationSnapshot = CloneConfiguration(Configuration);
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration snapshot could not be created.");
                return false;
            }
            previousTiming = CaptureSystemAlarmTimingSnapshot(
                updatedAlarm.Id);
            Configuration.SystemAlarms[alarmIndex] = replacement;
            ReconcileSystemAlarmTimingDefinition(
                previousAlarm,
                replacement);
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                RestoreConfiguration(Configuration, configurationSnapshot);
            }
            RestoreSystemAlarmTimingSnapshot(
                replacement.Id,
                previousTiming);
            RestoreConfigurationAlarmSnapshots();
            return false;
        }

        if (!replacement.Enabled)
        {
            ForceNormal(replacement.Id);
        }
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        return true;
    }

    public bool ResetSystemAlarm(string alarmId)
    {
        var defaultAlarm = UnmaConfiguration.CreateDefaultSystemAlarms()
            .FirstOrDefault(alarm => string.Equals(
                alarm.Id,
                alarmId,
                StringComparison.Ordinal));
        return defaultAlarm != null && UpdateSystemAlarm(defaultAlarm);
    }

    public bool RemovePanel(string panelId)
    {
        lock (m_persistenceGate)
        {
            return RemovePanelWithPersistenceLock(panelId);
        }
    }

    private bool RemovePanelWithPersistenceLock(string panelId)
    {
        string[] additionalRuleIds;
        lock (m_configurationGate)
        {
            if (Configuration.Panels.Count <= 1)
            {
                return false;
            }
            var panel = Configuration.Panels.FirstOrDefault(candidate =>
                string.Equals(candidate?.Id, panelId,
                    StringComparison.Ordinal));
            if (panel == null || panel.IsDashboard)
            {
                return false;
            }
            additionalRuleIds = PanelTopologyPolicy.IsEntityPanel(panel) &&
                                panel.OwnerEntityId >= 0
                ? CustomRuleLifecyclePolicy.FindRulesReferencingEntities(
                        Configuration.Rules,
                        new[] { panel.OwnerEntityId })
                    .ToArray()
                : Array.Empty<string>();
        }
        return RemovePanelsAndRulesWithPersistenceLock(
            new[] { panelId },
            additionalRuleIds,
            out _);
    }

    private bool RemovePanelsAndRulesWithPersistenceLock(
        IEnumerable<string> panelIds,
        IEnumerable<string> additionalRuleIds,
        out int removedRuleCount)
    {
        removedRuleCount = 0;
        var requestedPanelIds = new HashSet<string>(
            (panelIds ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim()),
            StringComparer.Ordinal);
        var requestedRuleIds = new HashSet<string>(
            (additionalRuleIds ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim()),
            StringComparer.Ordinal);
        if (requestedPanelIds.Count == 0 && requestedRuleIds.Count == 0)
        {
            return false;
        }

        List<PanelDefinition> previousPanels;
        List<AlarmRuleDefinition> previousRules;
        Dictionary<PanelDefinition, List<PanelSlotDefinition>>
            previousPanelSlots;
        Dictionary<PanelDefinition, List<string>> previousExcludedAlarmIds;
        Dictionary<AlarmRuleDefinition, List<string>> previousRuleLinks;
        AlarmRuleDefinition[] removedRules;
        PanelDefinition[] removedPanels;
        lock (m_configurationGate)
        {
            removedPanels = Configuration.Panels
                .Where(panel =>
                    panel != null && requestedPanelIds.Contains(panel.Id))
                .ToArray();
            if (removedPanels.Any(panel => panel.IsDashboard))
            {
                return false;
            }
            foreach (var rule in Configuration.Rules.Where(rule =>
                         rule != null &&
                         requestedPanelIds.Contains(rule.PanelId)))
            {
                requestedRuleIds.Add(rule.Id);
            }
            removedRules = Configuration.Rules
                .Where(rule =>
                    rule != null && requestedRuleIds.Contains(rule.Id))
                .ToArray();
            if (removedPanels.Length == 0 && removedRules.Length == 0)
            {
                return false;
            }

            previousPanels = Configuration.Panels.ToList();
            previousRules = Configuration.Rules.ToList();
            previousPanelSlots = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.Slots ?? new List<PanelSlotDefinition>())
                    .Select(PanelSlotProjection.CloneSlot)
                    .ToList());
            previousExcludedAlarmIds = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.ExcludedAlarmIds ?? new List<string>())
                    .ToList());
            previousRuleLinks = Configuration.Rules.ToDictionary(
                rule => rule,
                rule => (rule.LinkedPanelIds ?? new List<string>()).ToList());

            Configuration.Panels.RemoveAll(panel =>
                panel != null && requestedPanelIds.Contains(panel.Id));
            Configuration.Rules.RemoveAll(rule =>
                rule != null && requestedRuleIds.Contains(rule.Id));

            foreach (var rule in Configuration.Rules)
            {
                rule.LinkedPanelIds?.RemoveAll(requestedPanelIds.Contains);
            }
            var removedAlarmIds = new HashSet<string>(
                removedRules.Select(rule => "rule:" + rule.Id),
                StringComparer.Ordinal);
            foreach (var panel in Configuration.Panels)
            {
                panel.Slots?.RemoveAll(slot =>
                    slot != null && removedAlarmIds.Contains(slot.AlarmId));
                panel.ExcludedAlarmIds?.RemoveAll(removedAlarmIds.Contains);
            }
        }

        var removedAlarmStates = new Dictionary<string, AlarmState>(
            StringComparer.Ordinal);
        List<AlarmHistoryDefinition> previousHistory;
        long previousHistoryRevision;
        lock (m_gate)
        {
            previousHistory = m_alarmHistory
                .Select(CloneHistory)
                .ToList();
            previousHistoryRevision = m_alarmHistoryRevision;
            var historyChanged = false;
            foreach (var rule in removedRules)
            {
                var alarmKey = "rule:" + rule.Id;
                if (m_alarms.TryGetValue(alarmKey, out var state))
                {
                    removedAlarmStates[alarmKey] = state;
                    historyChanged |= CloseHistoryLocked(
                        state.Sequence,
                        state.View.IsAcknowledged);
                }
                m_alarms.Remove(alarmKey);
            }
            if (historyChanged)
            {
                m_alarmHistoryRevision++;
            }
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Panels.Clear();
                Configuration.Panels.AddRange(previousPanels);
                Configuration.Rules.Clear();
                Configuration.Rules.AddRange(previousRules);
                foreach (var pair in previousPanelSlots)
                {
                    pair.Key.Slots = pair.Value;
                }
                foreach (var pair in previousExcludedAlarmIds)
                {
                    pair.Key.ExcludedAlarmIds = pair.Value;
                }
                foreach (var pair in previousRuleLinks)
                {
                    pair.Key.LinkedPanelIds = pair.Value;
                }
            }
            lock (m_gate)
            {
                foreach (var pair in removedAlarmStates)
                {
                    m_alarms[pair.Key] = pair.Value;
                }
                m_alarmHistory.Clear();
                m_alarmHistory.AddRange(previousHistory);
                m_alarmHistoryRevision = previousHistoryRevision;
            }
            CapturePersistentAlarmState(
                out var restoredMemories,
                out var restoredHistory,
                out var restoredTimingMemories);
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = restoredMemories;
                Configuration.AlarmHistory = restoredHistory;
                Configuration.AlarmTimingMemories = restoredTimingMemories;
            }
            return false;
        }
        foreach (var removedRule in removedRules)
        {
            InvalidateRuleTiming(removedRule.Id);
        }
        removedRuleCount = removedRules.Length;
        return true;
    }

    public bool AddPanel(PanelDefinition panel)
    {
        if (panel == null)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            lock (m_configurationGate)
            {
                Configuration.Panels.Add(panel);
            }
            if (SaveConfiguration())
            {
                return true;
            }

            lock (m_configurationGate)
            {
                Configuration.Panels.Remove(panel);
            }
            return false;
        }
    }

    public bool ReplaceAlarmAreas(
        IReadOnlyList<AlarmAreaDefinition> draft,
        out int unassignedPanelCount)
    {
        unassignedPanelCount = 0;
        if (!AlarmAreaPolicy.ValidateReplacement(
                draft,
                out var replacement,
                out var failure))
        {
            LastPersistenceError =
                "Alarm area replacement is invalid: " + failure + ".";
            return false;
        }

        lock (m_persistenceGate)
        {
            UnmaConfiguration configurationSnapshot;
            var pendingUnassignedPanelCount = 0;
            lock (m_configurationGate)
            {
                try
                {
                    configurationSnapshot = CloneConfiguration(Configuration);
                }
                catch (Exception exception)
                {
                    LastPersistenceError = ExceptionDetail(
                        exception,
                        "Configuration snapshot could not be created.");
                    return false;
                }

                var previouslyAssignedPanels = Configuration.Panels
                    .Where(panel =>
                        panel != null &&
                        !string.IsNullOrWhiteSpace(panel.AreaId))
                    .ToArray();
                Configuration.AlarmAreas = replacement;
                AlarmAreaPolicy.NormalizePanelAssignments(
                    Configuration.Panels,
                    Configuration.AlarmAreas);
                pendingUnassignedPanelCount = previouslyAssignedPanels.Count(
                    panel => string.IsNullOrWhiteSpace(panel.AreaId));
            }

            var saved = false;
            try
            {
                saved = SaveConfiguration();
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration could not be saved.");
            }
            if (saved)
            {
                unassignedPanelCount = pendingUnassignedPanelCount;
                return true;
            }

            lock (m_configurationGate)
            {
                RestoreConfiguration(
                    Configuration,
                    configurationSnapshot);
            }
            RestoreConfigurationAlarmSnapshots();
            if (string.IsNullOrWhiteSpace(LastPersistenceError))
            {
                LastPersistenceError =
                    "Configuration could not be saved.";
            }
            return false;
        }
    }

    public bool TryCloneGlobalPanel(
        string sourcePanelId,
        string requestedName,
        out PanelDefinition clonedPanel,
        out int clonedRuleCount,
        out int skippedSlotCount)
    {
        clonedPanel = null;
        clonedRuleCount = 0;
        skippedSlotCount = 0;
        LastPanelCloneFailure = PanelCloneFailure.None;
        sourcePanelId = sourcePanelId?.Trim() ?? "";
        if (sourcePanelId.Length == 0)
        {
            LastPanelCloneFailure = PanelCloneFailure.InvalidSource;
            LastPersistenceError = "Source panel ID is required.";
            return false;
        }

        lock (m_persistenceGate)
        {
            PanelClonePlan plan;
            UnmaConfiguration configurationSnapshot;
            lock (m_configurationGate)
            {
                var sourcePanel = Configuration.Panels.FirstOrDefault(
                    candidate => candidate != null && string.Equals(
                        candidate.Id,
                        sourcePanelId,
                        StringComparison.Ordinal));
                if (sourcePanel == null)
                {
                    LastPanelCloneFailure = PanelCloneFailure.InvalidSource;
                    LastPersistenceError = "Source panel was not found.";
                    return false;
                }
                if (!PanelClonePolicy.TryCreatePlan(
                        sourcePanel,
                        Configuration.Panels,
                        Configuration.Rules,
                        () => Guid.NewGuid().ToString("N"),
                        out plan,
                        out var failure))
                {
                    LastPanelCloneFailure = failure;
                    LastPersistenceError = PanelCloneFailureMessage(failure);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(requestedName))
                {
                    plan.Panel.Name = requestedName.Trim();
                }
                try
                {
                    configurationSnapshot = CloneConfiguration(
                        Configuration);
                }
                catch (Exception exception)
                {
                    LastPersistenceError = ExceptionDetail(
                        exception,
                        "Configuration snapshot could not be created.");
                    return false;
                }
                try
                {
                    Configuration.Panels.Add(plan.Panel);
                    Configuration.Rules.AddRange(plan.Rules);
                }
                catch (Exception exception)
                {
                    RestoreConfiguration(
                        Configuration,
                        configurationSnapshot);
                    LastPersistenceError = ExceptionDetail(
                        exception,
                        "Panel copy could not be prepared.");
                    return false;
                }
            }

            var saved = false;
            try
            {
                saved = SaveConfiguration();
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration could not be saved.");
            }
            if (saved)
            {
                clonedPanel = plan.Panel;
                clonedRuleCount = plan.Rules.Count;
                skippedSlotCount = plan.OrphanRuleSlotCount;
                Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
                return true;
            }

            lock (m_configurationGate)
            {
                RestoreConfiguration(
                    Configuration,
                    configurationSnapshot);
            }
            if (string.IsNullOrWhiteSpace(LastPersistenceError))
            {
                LastPersistenceError =
                    "Configuration could not be saved.";
            }
            return false;
        }
    }

    private static string PanelCloneFailureMessage(PanelCloneFailure failure)
    {
        return failure switch
        {
            PanelCloneFailure.DashboardNotSupported =>
                "The dashboard cannot be copied.",
            PanelCloneFailure.EntityPanelNotSupported =>
                "Entity panels cannot be copied.",
            PanelCloneFailure.InvalidSourceData =>
                "Source panel data is invalid.",
            PanelCloneFailure.IdGenerationFailed =>
                "Unique IDs could not be generated.",
            _ => "The source panel is invalid.",
        };
    }

    private UnmaTransferProfile SelectTransferProfile(
        TransferProfileSelection requestedSelection)
    {
        var storedProfile = GetTransferProfile();
        if (storedProfile == null)
        {
            return null;
        }
        var profile = CloneTransferProfile(storedProfile);
        if (requestedSelection == null)
        {
            return profile;
        }

        var storedSelection = profile.Selection ??
                              new TransferProfileSelection();
        var selected = new TransferProfileSelection
        {
            NotificationBehaviors =
                storedSelection.NotificationBehaviors &&
                requestedSelection.NotificationBehaviors,
            SoundSettings = storedSelection.SoundSettings &&
                            requestedSelection.SoundSettings,
            Appearance = storedSelection.Appearance &&
                         requestedSelection.Appearance,
            SystemAlarms = storedSelection.SystemAlarms &&
                           requestedSelection.SystemAlarms,
            WindowLayout = storedSelection.WindowLayout &&
                           requestedSelection.WindowLayout,
            NotificationRuleIdentities = requestedSelection
                .NotificationRuleIdentities?.ToList(),
        };
        profile.Selection = selected;

        if (!selected.NotificationBehaviors)
        {
            profile.NotificationRules.Clear();
        }
        else if (selected.NotificationRuleIdentities != null)
        {
            var identities = new HashSet<string>(
                selected.NotificationRuleIdentities
                    .Where(identity => !string.IsNullOrWhiteSpace(identity))
                    .Select(identity => identity.Trim()),
                StringComparer.Ordinal);
            profile.NotificationRules = profile.NotificationRules
                .Where(rule => identities.Contains(
                    TransferNotificationRuleIdentity(rule)))
                .ToList();
        }
        return profile;
    }

    private static UnmaTransferProfile CloneTransferProfile(
        UnmaTransferProfile source)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(
            typeof(UnmaTransferProfile));
        serializer.WriteObject(stream, source);
        stream.Position = 0;
        return serializer.ReadObject(stream) as UnmaTransferProfile ??
               throw new InvalidDataException(
                   "Transfer profile snapshot is empty.");
    }

    private bool ApplyTransferredSoundSettings(
        UnmaTransferProfile profile)
    {
        if (profile?.Selection?.SoundSettings != true ||
            profile.SoundSettings == null ||
            profile.SoundSettings.Count == 0)
        {
            return false;
        }
        var settings = profile.SoundSettings
            .Where(item => item != null &&
                           !string.IsNullOrWhiteSpace(item.AlarmId))
            .GroupBy(item => item.AlarmId.Trim(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
        var changedLatchedAlarm = false;
        lock (m_gate)
        {
            foreach (var state in m_alarms.Values)
            {
                if (!settings.TryGetValue(
                        state.View.OverrideId ?? "",
                        out var setting))
                {
                    continue;
                }
                var soundId = string.IsNullOrWhiteSpace(setting.SoundId)
                    ? "auto"
                    : setting.SoundId;
                var changed = !string.Equals(
                    state.View.SoundId,
                    soundId,
                    StringComparison.Ordinal);
                state.View.SoundId = soundId;
                changedLatchedAlarm |= changed && state.View.IsLatched;
            }
        }
        return changedLatchedAlarm;
    }

    private void ReconcileTransferredVanillaNotifications(
        UnmaTransferProfile profile,
        bool persistTransferredSoundState)
    {
        var affectedOverrideIds = new HashSet<string>(StringComparer.Ordinal);
        if (profile?.Selection?.NotificationBehaviors == true)
        {
            foreach (var rule in profile.NotificationRules ??
                         new List<TransferNotificationRule>())
            {
                if (rule != null &&
                    VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                        rule.AlarmId))
                {
                    affectedOverrideIds.Add(rule.AlarmId.Trim());
                }
            }
        }
        if (profile?.Selection?.SoundSettings == true)
        {
            foreach (var setting in profile.SoundSettings ??
                         new List<TransferSoundSetting>())
            {
                if (setting != null &&
                    VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                        setting.AlarmId))
                {
                    affectedOverrideIds.Add(setting.AlarmId.Trim());
                }
            }
        }
        if (profile?.Selection?.Appearance == true)
        {
            lock (m_gate)
            {
                foreach (var overrideId in m_alarms.Values
                             .Where(state => string.Equals(
                                 state.View.Source,
                                 "vanilla",
                                 StringComparison.Ordinal))
                             .Select(state => state.View.OverrideId)
                             .Where(VanillaNotificationSuppressionPolicy
                                 .IsVanillaOverrideId))
                {
                    affectedOverrideIds.Add(overrideId.Trim());
                }
            }
        }

        if (affectedOverrideIds.Count == 0)
        {
            if (persistTransferredSoundState)
            {
                PersistAlarmState();
            }
            return;
        }

        var rules = GetVanillaNotificationRulesSnapshot();
        var disabledOverrideIds = GetDisabledVanillaOverrideIds();
        var globallyIgnoredOverrideIds =
            GetGloballyIgnoredHistoryPurgeOverrideIds(
                rules,
                affectedOverrideIds);
        var purged = false;
        AlarmView[] externallyCleared = Array.Empty<AlarmView>();
        lock (m_gate)
        {
            var matchingStates = m_alarms
                .Where(pair =>
                    string.Equals(
                        pair.Value.View.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    affectedOverrideIds.Contains(
                        pair.Value.View.OverrideId ?? "") &&
                    (disabledOverrideIds.Contains(
                         pair.Value.View.OverrideId ?? "") ||
                     ResolveVanillaNotificationBehavior(
                         pair.Value.View,
                         rules) == VanillaNotificationBehavior.Ignored))
                .ToArray();
            externallyCleared = matchingStates
                .Where(pair => pair.Value.View.IsActive)
                .Select(pair => Clone(
                    pair.Value.View,
                    pair.Value.Sequence))
                .ToArray();
            var ignoredStates = matchingStates.Where(pair =>
                !disabledOverrideIds.Contains(
                    pair.Value.View.OverrideId ?? "")).ToArray();
            foreach (var disabledState in matchingStates.Where(pair =>
                         disabledOverrideIds.Contains(
                             pair.Value.View.OverrideId ?? "")))
            {
                var history = FindHistoryLocked(
                    disabledState.Value.Sequence);
                history?.SetState(
                    isGone: true,
                    isAcknowledged: true,
                    currentGameTicks: CurrentGameTicks);
            }
            var sequences = new HashSet<long>(ignoredStates
                .Select(pair => pair.Value.Sequence)
                .Where(sequence => sequence > 0));
            foreach (var matchingState in matchingStates)
            {
                m_alarms.Remove(matchingState.Key);
            }
            var removedHistoryCount = m_alarmHistory.RemoveAll(history =>
                sequences.Contains(history.Sequence) ||
                globallyIgnoredOverrideIds.Any(overrideId =>
                    VanillaNotificationSuppressionPolicy
                        .MatchesHistoryForOverride(history, overrideId)));
            if (matchingStates.Length > 0 || removedHistoryCount > 0)
            {
                m_alarmHistoryRevision++;
                purged = true;
            }
        }

        foreach (var alarm in externallyCleared)
        {
            PublishExternalDisplayAlarm(alarm, false);
        }

        INotification[] currentNotifications;
        try
        {
            currentNotifications = m_notificationsManager
                .FetchAllNotifications()
                .Where(notification => affectedOverrideIds.Contains(
                    "vanilla:" + notification.Proto.Id.Value))
                .ToArray();
        }
        catch (Exception exception)
        {
            Log.Warning(
                "UNMA: transferred vanilla notifications could not be " +
                "replayed: " + exception.Message);
            if (purged || persistTransferredSoundState)
            {
                PersistAlarmState();
            }
            throw;
        }

        RefreshGroupedVanillaNotificationMembers(
            currentNotifications,
            replaceCurrentMembers: affectedOverrideIds.Contains(
                GroupedVanillaNotificationPolicy.OverrideId));

        BeginAlarmPersistenceBatch();
        var replayChangedState = false;
        try
        {
            foreach (var notification in currentNotifications)
            {
                OnNotificationAdded(notification);
            }
        }
        finally
        {
            replayChangedState = EndAlarmPersistenceBatch();
        }
        if (purged || replayChangedState || persistTransferredSoundState)
        {
            PersistAlarmState();
        }
    }

    private static HashSet<string>
        GetGloballyIgnoredHistoryPurgeOverrideIds(
            IEnumerable<VanillaNotificationRule> rules,
            ISet<string> affectedOverrideIds = null)
    {
        var snapshot = (rules ?? Enumerable.Empty<VanillaNotificationRule>())
            .Where(rule =>
                rule != null &&
                VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                    rule.AlarmId))
            .ToArray();
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in snapshot.Where(rule =>
                     rule.Scope ==
                         VanillaNotificationScope.NotificationType &&
                     rule.Behavior == VanillaNotificationBehavior.Ignored))
        {
            var overrideId = rule.AlarmId.Trim();
            if ((affectedOverrideIds != null &&
                 !affectedOverrideIds.Contains(overrideId)) ||
                !GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    overrideId) &&
                snapshot.Any(exception =>
                    exception.Scope !=
                        VanillaNotificationScope.NotificationType &&
                    exception.Behavior !=
                        VanillaNotificationBehavior.Ignored &&
                    string.Equals(
                        exception.AlarmId?.Trim(),
                        overrideId,
                        StringComparison.Ordinal)))
            {
                continue;
            }
            result.Add(overrideId);
        }
        return result;
    }

    private static string TransferNotificationRuleIdentity(
        TransferNotificationRule rule)
    {
        if (rule == null)
        {
            return "";
        }
        return VanillaNotificationSuppressionPolicy.RuleIdentity(
            new VanillaNotificationRule
            {
                AlarmId = rule.AlarmId,
                Scope = rule.Scope,
                Behavior = rule.Behavior,
                EntityId = -1,
                EntityPrototypeId = rule.EntityPrototypeId,
            });
    }

    private static UnmaConfiguration CloneConfiguration(
        UnmaConfiguration source)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        serializer.WriteObject(stream, source);
        stream.Position = 0;
        return serializer.ReadObject(stream) as UnmaConfiguration ??
               throw new InvalidDataException(
                   "Configuration snapshot is empty.");
    }

    private static void RestoreConfiguration(
        UnmaConfiguration target,
        UnmaConfiguration snapshot)
    {
        target.SchemaVersion = snapshot.SchemaVersion;
        target.Panels = snapshot.Panels;
        target.Rules = snapshot.Rules;
        target.WarningColor = snapshot.WarningColor;
        target.CriticalColor = snapshot.CriticalColor;
        target.EmergencyColor = snapshot.EmergencyColor;
        target.WindowX = snapshot.WindowX;
        target.WindowY = snapshot.WindowY;
        target.WindowWidth = snapshot.WindowWidth;
        target.WindowHeight = snapshot.WindowHeight;
        target.SoundOverrides = snapshot.SoundOverrides;
        target.LauncherX = snapshot.LauncherX;
        target.LauncherY = snapshot.LauncherY;
        target.SystemAlarms = snapshot.SystemAlarms;
        target.AlarmMemories = snapshot.AlarmMemories;
        target.AlarmHistory = snapshot.AlarmHistory;
        target.AlarmTimingMemories = snapshot.AlarmTimingMemories;
        target.LegacySustainedAlarmReconciliationPending =
            snapshot.LegacySustainedAlarmReconciliationPending;
        target.UiScalePercent = snapshot.UiScalePercent;
        target.ReducedMotion = snapshot.ReducedMotion;
        target.EditorWindowX = snapshot.EditorWindowX;
        target.EditorWindowY = snapshot.EditorWindowY;
        target.EditorWindowWidth = snapshot.EditorWindowWidth;
        target.EditorWindowHeight = snapshot.EditorWindowHeight;
        target.VanillaNotificationRules =
            snapshot.VanillaNotificationRules;
        target.Instruments = snapshot.Instruments;
        target.InstrumentPanels = snapshot.InstrumentPanels;
        target.AlarmAreas = snapshot.AlarmAreas;
        target.DetachedPanelLayouts = snapshot.DetachedPanelLayouts;
    }

    private static string ExceptionDetail(
        Exception exception,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(exception?.Message)
            ? fallback
            : exception.Message.Trim();
    }

    public bool UpdatePanelSettings(
        string panelId,
        string name,
        int columns,
        bool includeVanilla,
        bool includeSystem,
        string notificationFilter)
    {
        return UpdatePanelSettings(
            panelId,
            name,
            columns,
            includeVanilla,
            includeSystem,
            notificationFilter,
            null);
    }

    public bool UpdatePanelSettings(
        string panelId,
        string name,
        int columns,
        bool includeVanilla,
        bool includeSystem,
        string notificationFilter,
        string areaId)
    {
        panelId = panelId?.Trim() ?? "";
        if (panelId.Length == 0)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            UnmaConfiguration configurationSnapshot;
            lock (m_configurationGate)
            {
                var panel = Configuration.Panels.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate?.Id,
                        panelId,
                        StringComparison.Ordinal));
                if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
                {
                    return false;
                }

                try
                {
                    configurationSnapshot = CloneConfiguration(Configuration);
                }
                catch (Exception exception)
                {
                    LastPersistenceError = ExceptionDetail(
                        exception,
                        "Configuration snapshot could not be created.");
                    return false;
                }
                if (areaId != null && panel.IsDashboard)
                {
                    if (!string.IsNullOrWhiteSpace(areaId))
                    {
                        LastPersistenceError =
                            "The dashboard cannot be assigned to an alarm area.";
                        return false;
                    }
                    panel.AreaId = "";
                }
                else if (areaId != null)
                {
                    if (!AlarmAreaPolicy.TryAssign(
                            Configuration.Panels,
                            Configuration.AlarmAreas,
                            panelId,
                            areaId,
                            out panel,
                            out var assignmentFailure))
                    {
                        LastPersistenceError =
                            "Alarm area assignment is invalid: " +
                            assignmentFailure + ".";
                        return false;
                    }
                }

                panel.Name = string.IsNullOrWhiteSpace(name)
                    ? UnmaText.Get("default.panel", "PANEL")
                    : name.Trim();
                panel.Columns = Math.Max(1, Math.Min(8, columns));
                if (!panel.IsDashboard)
                {
                    panel.IncludeVanilla = includeVanilla;
                    panel.IncludeSystem = includeSystem;
                    panel.NotificationFilter = notificationFilter ?? "";
                }
            }

            var saved = false;
            try
            {
                saved = SaveConfiguration();
            }
            catch (Exception exception)
            {
                LastPersistenceError = ExceptionDetail(
                    exception,
                    "Configuration could not be saved.");
            }
            if (saved)
            {
                return true;
            }

            lock (m_configurationGate)
            {
                RestoreConfiguration(
                    Configuration,
                    configurationSnapshot);
            }
            RestoreConfigurationAlarmSnapshots();
            if (string.IsNullOrWhiteSpace(LastPersistenceError))
            {
                LastPersistenceError =
                    "Configuration could not be saved.";
            }
            return false;
        }
    }

    public IReadOnlyList<PanelSlotDefinition> GetPanelSlotCandidates()
    {
        var candidates = new Dictionary<string, PanelSlotDefinition>(
            StringComparer.Ordinal);
        lock (m_configurationGate)
        {
            foreach (var alarm in Configuration.SystemAlarms)
            {
                var stage = alarm.Stages
                    .Where(item => item.Enabled)
                    .OrderBy(item => item.Priority)
                    .FirstOrDefault();
                candidates[alarm.Id] = new PanelSlotDefinition
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
            foreach (var rule in Configuration.Rules)
            {
                var alarmId = "rule:" + rule.Id;
                candidates[alarmId] = new PanelSlotDefinition
                {
                    AlarmId = alarmId,
                    DisplayName = rule.Name,
                    Detail = rule.Conditions.Count + UnmaText.Get("auto.38bf168a03a3"),
                    Source = "custom",
                    Severity = rule.Severity,
                    ActiveColor = rule.ActiveColor,
                };
            }
        }

        AlarmView[] runtimeViews;
        lock (m_gate)
        {
            runtimeViews = m_alarms.Values
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }
        foreach (var group in runtimeViews.GroupBy(
                     PanelSlotProjection.StableAlarmId,
                     StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                continue;
            }
            var representative =
                PanelSlotProjection.SelectRepresentative(group);
            var slot = PanelSlotProjection.CreateSlot(representative);
            if (slot != null &&
                (!candidates.ContainsKey(group.Key) ||
                 string.Equals(
                     slot.Source,
                     "vanilla",
                     StringComparison.Ordinal)))
            {
                candidates[group.Key] = slot;
            }
        }

        return candidates.Values
            .OrderBy(slot => slot.Source)
            .ThenBy(slot => slot.DisplayName)
            .ThenBy(slot => slot.AlarmId)
            .Select(PanelSlotProjection.CloneSlot)
            .ToArray();
    }

    public IReadOnlyList<AlarmView> GetSoundOverrideCandidates()
    {
        AlarmView[] runtimeCandidates;
        lock (m_gate)
        {
            runtimeCandidates = m_alarms.Values
                .Where(state =>
                    (state.View.Source == "vanilla" ||
                     state.View.Source == "external") &&
                    !string.IsNullOrWhiteSpace(state.View.OverrideId))
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }

        PanelSlotDefinition[] fixedVanillaSlots;
        string[] persistedVanillaOverrideIds;
        VanillaNotificationRule[] persistedVanillaRules;
        lock (m_configurationGate)
        {
            fixedVanillaSlots = Configuration.Panels
                .Where(panel => !panel.IsDashboard)
                .SelectMany(panel =>
                    panel.Slots ?? new List<PanelSlotDefinition>())
                .Where(slot =>
                    slot != null &&
                    (string.Equals(
                         slot.Source,
                         "vanilla",
                         StringComparison.Ordinal) ||
                     VanillaNotificationSuppressionPolicy
                         .IsVanillaOverrideId(
                             VanillaNotificationSuppressionPolicy
                                 .GetOverrideIdForSlotId(slot.AlarmId))))
                .Select(PanelSlotProjection.CloneSlot)
                .ToArray();
            persistedVanillaOverrideIds = Configuration.SoundOverrides
                .Where(soundOverride =>
                    soundOverride != null &&
                    VanillaNotificationSuppressionPolicy
                        .IsVanillaOverrideId(soundOverride.AlarmId))
                .Select(soundOverride => soundOverride.AlarmId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            persistedVanillaRules = Configuration.VanillaNotificationRules
                .Select(CloneVanillaNotificationRule)
                .ToArray();
        }

        var candidates = runtimeCandidates
            .GroupBy(
                candidate => GroupedVanillaNotificationPolicy
                        .IsGroupedOverride(candidate.OverrideId)
                    ? candidate.OverrideId
                    : candidate.EntityId >= 0
                    ? candidate.SlotId
                    : candidate.OverrideId,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Clone(group
                    .OrderByDescending(candidate => candidate.Sequence)
                    .First()),
                StringComparer.Ordinal);

        foreach (var slot in fixedVanillaSlots)
        {
            var overrideId = VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(slot.AlarmId);
            if (!VanillaNotificationSuppressionPolicy
                    .IsVanillaOverrideId(overrideId) ||
                candidates.ContainsKey(overrideId))
            {
                continue;
            }
            candidates.Add(
                overrideId,
                CreateVanillaOverrideCandidate(overrideId, slot));
        }

        foreach (var overrideId in persistedVanillaOverrideIds)
        {
            if (!candidates.ContainsKey(overrideId))
            {
                candidates.Add(
                    overrideId,
                    CreateVanillaOverrideCandidate(overrideId));
            }
        }

        foreach (var rule in persistedVanillaRules)
        {
            if (GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    rule.AlarmId))
            {
                if (!candidates.Values.Any(candidate => string.Equals(
                        candidate.OverrideId,
                        rule.AlarmId,
                        StringComparison.Ordinal)))
                {
                    candidates[rule.AlarmId] =
                        CreateVanillaOverrideCandidate(rule.AlarmId);
                }
                continue;
            }
            var alreadyRepresented = candidates.Values.Any(candidate =>
                string.Equals(
                    candidate.OverrideId,
                    rule.AlarmId,
                    StringComparison.Ordinal) &&
                (rule.Scope == VanillaNotificationScope.NotificationType ||
                 rule.Scope == VanillaNotificationScope.Entity &&
                 candidate.EntityId == rule.EntityId ||
                 rule.Scope == VanillaNotificationScope.EntityPrototype &&
                 string.Equals(
                     candidate.EntityPrototypeId,
                     rule.EntityPrototypeId,
                     StringComparison.Ordinal)));
            if (alreadyRepresented)
            {
                continue;
            }
            candidates[VanillaNotificationSuppressionPolicy.RuleIdentity(
                rule)] = CreateVanillaOverrideCandidate(
                rule.AlarmId,
                null,
                rule.EntityId,
                rule.EntityPrototypeId);
        }

        return candidates.Values
            .OrderBy(candidate => candidate.Source)
            .ThenBy(candidate => candidate.Name)
            .ThenBy(candidate => candidate.OverrideId)
            .ThenBy(candidate => candidate.EntityId)
            .ThenBy(
                candidate => candidate.EntityPrototypeId,
                StringComparer.Ordinal)
            .ThenBy(
                candidate => candidate.SlotId,
                StringComparer.Ordinal)
            .Select(candidate => Clone(candidate))
            .ToArray();
    }

    private static AlarmView CreateVanillaOverrideCandidate(
        string overrideId,
        PanelSlotDefinition slot = null,
        int entityId = -1,
        string entityPrototypeId = "")
    {
        const string vanillaPrefix = "vanilla:";
        var prototypeId = overrideId.StartsWith(
                vanillaPrefix,
                StringComparison.Ordinal)
            ? overrideId.Substring(vanillaPrefix.Length)
            : overrideId;
        return new AlarmView
        {
            Key = overrideId,
            OverrideId = overrideId,
            SlotId = overrideId,
            Name = string.IsNullOrWhiteSpace(slot?.DisplayName)
                ? prototypeId
                : slot.DisplayName,
            Detail = string.IsNullOrWhiteSpace(slot?.Detail)
                ? overrideId
                : slot.Detail,
            Source = "vanilla",
            Severity = slot?.Severity ?? AlarmSeverity.Warning,
            EntityId = entityId,
            EntityPrototypeId = entityPrototypeId ?? "",
            ActiveColor = string.IsNullOrWhiteSpace(slot?.ActiveColor)
                ? "#F0C541"
                : slot.ActiveColor,
        };
    }

    public string GetConfiguredSound(string alarmId)
    {
        string fallback;
        lock (m_gate)
        {
            fallback = m_alarms.Values
                .Where(state => string.Equals(
                    state.View.OverrideId,
                    alarmId,
                    StringComparison.Ordinal))
                .OrderByDescending(state => state.Sequence)
                .Select(state => state.View.SoundId)
                .FirstOrDefault();
        }
        return ResolveConfiguredSound(alarmId, fallback);
    }

    public bool GetVanillaNotificationEnabled(string overrideId)
    {
        overrideId = overrideId?.Trim() ?? "";
        if (!VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                overrideId))
        {
            return false;
        }

        return !GetDisabledVanillaOverrideIds().Contains(overrideId);
    }

    public bool SetVanillaNotificationEnabled(
        string overrideId,
        bool enabled)
    {
        overrideId = overrideId?.Trim() ?? "";
        if (!VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                overrideId))
        {
            return false;
        }

        AlarmSoundOverride soundOverride;
        var createdOverride = false;
        var wasDisabled = false;
        lock (m_configurationGate)
        {
            soundOverride = Configuration.SoundOverrides.FirstOrDefault(
                candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.AlarmId,
                        overrideId,
                        StringComparison.Ordinal));
            wasDisabled = soundOverride?.IsGloballyDisabled == true;
            if (enabled && !wasDisabled)
            {
                return true;
            }

            if (soundOverride == null)
            {
                soundOverride = new AlarmSoundOverride
                {
                    AlarmId = overrideId,
                    SoundId = "auto",
                    IsGloballyDisabled = !enabled,
                };
                Configuration.SoundOverrides.Add(soundOverride);
                createdOverride = true;
            }
            else
            {
                soundOverride.IsGloballyDisabled = !enabled;
            }
        }
        RefreshDisabledVanillaOverrideIds();

        if (enabled)
        {
            if (!SaveConfiguration())
            {
                RollBackVanillaNotificationOverride(
                    soundOverride,
                    createdOverride,
                    wasDisabled);
                return false;
            }

            ReplayCurrentVanillaNotifications(overrideId);
            return true;
        }

        var disabledOverrideIds = new[] { overrideId };
        var removedStates = new List<RemovedAlarmState>();
        var closedHistoryStates = new List<ClosedHistoryState>();
        long previousHistoryRevision;
        lock (m_gate)
        {
            previousHistoryRevision = m_alarmHistoryRevision;
            foreach (var pair in m_alarms
                         .Where(pair => IsSuppressedVanillaAlarm(
                             pair.Value.View.Source,
                             pair.Value.View.OverrideId,
                             disabledOverrideIds,
                             pair.Value.View.SlotId))
                         .ToArray())
            {
                removedStates.Add(new RemovedAlarmState
                {
                    Key = pair.Key,
                    State = pair.Value,
                });
                var history = FindHistoryLocked(pair.Value.Sequence);
                if (history != null &&
                    (!history.IsGone || !history.IsAcknowledged))
                {
                    closedHistoryStates.Add(new ClosedHistoryState
                    {
                        History = history,
                        WasGone = history.IsGone,
                        WasAcknowledged = history.IsAcknowledged,
                        WasRaisedAtTicks = history.RaisedAtTicks,
                        WasClearedAtTicks = history.ClearedAtTicks,
                        WasAcknowledgedAtTicks =
                            history.AcknowledgedAtTicks,
                    });
                    history.SetState(
                        isGone: true,
                        isAcknowledged: true,
                        currentGameTicks: CurrentGameTicks);
                }
                m_alarms.Remove(pair.Key);
            }
            if (closedHistoryStates.Count > 0)
            {
                m_alarmHistoryRevision++;
            }
        }

        if (SaveConfiguration())
        {
            foreach (var removedState in removedStates.Where(item =>
                         item.State.View.IsActive))
            {
                PublishExternalDisplayAlarm(
                    Clone(
                        removedState.State.View,
                        removedState.State.Sequence),
                    false);
            }
            return true;
        }

        RollBackVanillaNotificationOverride(
            soundOverride,
            createdOverride,
            wasDisabled);
        lock (m_gate)
        {
            foreach (var removedState in removedStates)
            {
                m_alarms[removedState.Key] = removedState.State;
            }
            foreach (var closedHistoryState in closedHistoryStates)
            {
                closedHistoryState.History.IsGone =
                    closedHistoryState.WasGone;
                closedHistoryState.History.IsAcknowledged =
                    closedHistoryState.WasAcknowledged;
                closedHistoryState.History.RaisedAtTicks =
                    closedHistoryState.WasRaisedAtTicks;
                closedHistoryState.History.ClearedAtTicks =
                    closedHistoryState.WasClearedAtTicks;
                closedHistoryState.History.AcknowledgedAtTicks =
                    closedHistoryState.WasAcknowledgedAtTicks;
            }
            m_alarmHistoryRevision = previousHistoryRevision;
        }
        RestoreConfigurationAlarmSnapshots();
        return false;
    }

    public VanillaNotificationBehavior GetVanillaNotificationBehavior(
        string overrideId,
        VanillaNotificationScope scope,
        int entityId = -1,
        string entityPrototypeId = "")
    {
        lock (m_configurationGate)
        {
            return Configuration.VanillaNotificationRules
                .LastOrDefault(rule =>
                    VanillaNotificationSuppressionPolicy.MatchesScope(
                        rule,
                        overrideId,
                        scope,
                        entityId,
                        entityPrototypeId))?.Behavior ??
                   VanillaNotificationBehavior.Normal;
        }
    }

    public IReadOnlyList<SystemMetricDescriptor> GetAvailableSystemMetrics()
    {
        var result = new List<SystemMetricDescriptor>(SystemMetricCatalog.All);
        var quantityUnit = UnmaText.Get("unit.quantity", "units");
        var quantityPerMonthUnit = UnmaText.Get(
            "unit.quantity_per_month",
            "units/month");
        var percentUnit = UnmaText.Get("unit.percent", "%");

        foreach (var buffer in m_maintenanceManager.MaintenanceBuffers
                     .Where(item => item != null && item.ShouldShowInUi)
                     .OrderBy(
                         item => ProductDisplayName(item.Product),
                         StringComparer.CurrentCultureIgnoreCase))
        {
            var productId = buffer.Product.Id.Value;
            var productName = ProductDisplayName(buffer.Product);
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceFillId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.fill.label",
                    "{0} · maintenance fill",
                    productName),
                percentUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceQuantityId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.quantity.label",
                    "{0} · maintenance reserve",
                    productName),
                quantityUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceCapacityId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.capacity.label",
                    "{0} · maintenance capacity",
                    productName),
                quantityUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceDeltaId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.delta_month.label",
                    "{0} · maintenance change last month",
                    productName),
                quantityPerMonthUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceNeededId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.needed_month.label",
                    "{0} · maintenance demand per month",
                    productName),
                quantityPerMonthUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.MaintenanceNeededMaxId(productId),
                UnmaText.Format(
                    "system_metric.maintenance.needed_month_max.label",
                    "{0} · maximum maintenance demand per month",
                    productName),
                quantityPerMonthUnit));
        }

        foreach (var stats in m_productsManager.ProductStats
                     .Where(item => IsSelectableGlobalProduct(item?.Product))
                     .OrderBy(
                         item => ProductDisplayName(item.Product),
                         StringComparer.CurrentCultureIgnoreCase))
        {
            var productId = stats.Product.Id.Value;
            var productName = ProductDisplayName(stats.Product);
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.ProductStoredId(productId),
                UnmaText.Format(
                    "system_metric.product.stored.label",
                    "{0} · global stored quantity",
                    productName),
                quantityUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.ProductCapacityId(productId),
                UnmaText.Format(
                    "system_metric.product.capacity.label",
                    "{0} · global storage capacity",
                    productName),
                quantityUnit));
            result.Add(new SystemMetricDescriptor(
                SystemMetricCatalog.ProductFillId(productId),
                UnmaText.Format(
                    "system_metric.product.fill.label",
                    "{0} · global storage fill",
                    productName),
                percentUnit));
        }

        return result;
    }

    public bool TryReadInstrumentValue(
        InstrumentDefinition instrument,
        out double value,
        out int validSources)
    {
        value = 0d;
        validSources = 0;
        if (instrument == null ||
            string.IsNullOrWhiteSpace(instrument.MetricPath))
        {
            return false;
        }

        var sources = GetInstrumentSources(instrument);
        if (sources.Count == 0)
        {
            return false;
        }

        var sourcesToRead = instrument.Aggregation ==
                InstrumentAggregationMode.Single
            ? sources.Take(1).ToArray()
            : sources;

        var values = new List<double>(sourcesToRead.Count);
        foreach (var source in sourcesToRead)
        {
            if (!TryGetLiveEntity(source.EntityId, out var entity) ||
                !string.IsNullOrWhiteSpace(source.EntityPrototypeId) &&
                !string.Equals(
                    source.EntityPrototypeId,
                    entity.Prototype.Id.Value,
                    StringComparison.Ordinal) ||
                !EntityMetricCatalog.TryRead(
                    entity,
                    instrument.MetricPath,
                    out var sourceValue))
            {
                continue;
            }
            validSources++;
            values.Add(sourceValue);
        }

        // Calculated sums and averages must not silently shrink when one of
        // their configured buildings is missing or no longer exposes the
        // selected variable.
        return validSources == sourcesToRead.Count &&
               InstrumentValuePolicy.TryAggregate(
                   instrument.Aggregation,
                   values,
                   out value);
    }

    public bool TryGetInstrumentCurrentValue(
        string instrumentId,
        out double value)
    {
        lock (m_instrumentValuesGate)
        {
            return m_lastInstrumentValues.TryGetValue(
                instrumentId ?? "",
                out value);
        }
    }

    public IReadOnlyList<InstrumentValueSample> GetInstrumentHistory(
        string instrumentId,
        int windowTicks = 0)
    {
        lock (m_instrumentValuesGate)
        {
            if (!m_instrumentHistory.TryGetValue(
                    instrumentId ?? "",
                    out var history))
            {
                return Array.Empty<InstrumentValueSample>();
            }
            if (windowTicks <= 0 || history.Count == 0)
            {
                return history.ToArray();
            }
            var cutoff = m_calendar.RealTime.Ticks - windowTicks;
            return history
                .Where(sample => sample.TimestampSeconds >= cutoff)
                .ToArray();
        }
    }

    public bool TryGetInstrumentForecast(
        string instrumentId,
        out InstrumentForecastResult result) =>
        TryGetInstrumentForecast(instrumentId, 0, out result);

    /// <summary>
    /// Creates a session-history forecast from one coherent capture epoch.
    /// Configuration ranges are mirrored together with current values and
    /// history, avoiding nested configuration/history locks. The pure policy
    /// evaluates the defensive sample copy after the lock is released and
    /// incorporates the current aggregate as the newest sample.
    /// </summary>
    public bool TryGetInstrumentForecast(
        string instrumentId,
        int windowTicks,
        out InstrumentForecastResult result)
    {
        result = default;
        var canonicalId = instrumentId ?? "";
        double currentTimestampTicks;
        double currentValue;
        InstrumentForecastRange range;
        InstrumentValueSample[] historySnapshot;

        lock (m_instrumentValuesGate)
        {
            if (!m_instrumentForecastRanges.TryGetValue(
                    canonicalId,
                    out range) ||
                !m_lastInstrumentValues.TryGetValue(
                    canonicalId,
                    out currentValue))
            {
                return false;
            }

            currentTimestampTicks = m_lastInstrumentCaptureTimestampTicks;
            if (!m_instrumentHistory.TryGetValue(
                    canonicalId,
                    out var history) ||
                history.Count == 0)
            {
                historySnapshot = Array.Empty<InstrumentValueSample>();
            }
            else
            {
                var selected = new List<InstrumentValueSample>(
                    history.Count);
                for (var index = 0; index < history.Count; index++)
                {
                    var sample = history[index];
                    if (IsInstrumentForecastSampleInWindow(
                            sample.TimestampSeconds,
                            currentTimestampTicks,
                            windowTicks))
                    {
                        selected.Add(sample);
                    }
                }
                historySnapshot = selected.ToArray();
            }
        }

        return InstrumentForecastPolicy.TryAnalyze(
            historySnapshot,
            currentTimestampTicks,
            currentValue,
            range.Minimum,
            range.Maximum,
            out result);
    }

    /// <summary>
    /// Returns a cheap identity for the retained history without copying its
    /// samples. UI caches use the first/last sample in addition to the count
    /// because a capacity-limited history can advance while its count stays
    /// constant.
    /// </summary>
    public bool TryGetInstrumentHistoryState(
        string instrumentId,
        out InstrumentHistoryState state)
    {
        lock (m_instrumentValuesGate)
        {
            if (!m_instrumentHistory.TryGetValue(
                    instrumentId ?? "",
                    out var history) ||
                history.Count == 0)
            {
                state = default;
                return false;
            }

            var first = history[0];
            var last = history[history.Count - 1];
            state = new InstrumentHistoryState(
                history.Count,
                first.TimestampSeconds,
                last.TimestampSeconds,
                first.Value,
                last.Value);
            return true;
        }
    }

    /// <summary>
    /// Copies a min/max-preserving, pixel-width history envelope. At most one
    /// bucket is emitted per requested column, so the archive renderer never
    /// receives tens of thousands of points that collapse onto the same
    /// screen pixels.
    /// </summary>
    public bool CopyDecimatedInstrumentHistory(
        string instrumentId,
        int windowTicks,
        int maximumColumns,
        List<InstrumentHistoryBucket> destination,
        out InstrumentHistoryState state,
        out double observedMinimum,
        out double observedMaximum)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.Clear();
        observedMinimum = 0d;
        observedMaximum = 0d;
        lock (m_instrumentValuesGate)
        {
            if (!m_instrumentHistory.TryGetValue(
                    instrumentId ?? "",
                    out var history) ||
                history.Count == 0)
            {
                state = default;
                return false;
            }

            var firstRetained = history[0];
            var lastRetained = history[history.Count - 1];
            state = new InstrumentHistoryState(
                history.Count,
                firstRetained.TimestampSeconds,
                lastRetained.TimestampSeconds,
                firstRetained.Value,
                lastRetained.Value);

            var startIndex = 0;
            if (windowTicks > 0)
            {
                var cutoff = m_calendar.RealTime.Ticks - windowTicks;
                var low = 0;
                var high = history.Count;
                while (low < high)
                {
                    var middle = low + (high - low) / 2;
                    if (history[middle].TimestampSeconds < cutoff)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                startIndex = low;
            }

            var selectedCount = history.Count - startIndex;
            if (selectedCount <= 0)
            {
                return false;
            }

            var columnCount = Math.Min(
                selectedCount,
                Math.Max(1, maximumColumns));
            observedMinimum = double.PositiveInfinity;
            observedMaximum = double.NegativeInfinity;
            for (var column = 0; column < columnCount; column++)
            {
                var bucketStart = startIndex +
                                  (int)((long)column * selectedCount /
                                        columnCount);
                var bucketEnd = startIndex +
                                (int)((long)(column + 1) * selectedCount /
                                      columnCount);
                bucketEnd = Math.Max(bucketStart + 1, bucketEnd);

                var firstValue = history[bucketStart].Value;
                var lastValue = history[bucketEnd - 1].Value;
                var minimum = firstValue;
                var maximum = firstValue;
                for (var index = bucketStart + 1;
                     index < bucketEnd;
                     index++)
                {
                    var value = history[index].Value;
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }

                observedMinimum = Math.Min(observedMinimum, minimum);
                observedMaximum = Math.Max(observedMaximum, maximum);
                destination.Add(new InstrumentHistoryBucket(
                    firstValue,
                    minimum,
                    maximum,
                    lastValue));
            }
            return destination.Count > 0;
        }
    }

    public bool TryEvaluateInstrumentTrend(
        string instrumentId,
        InstrumentTrendMode trendMode,
        int windowTicks,
        out double change)
    {
        change = 0d;
        lock (m_instrumentValuesGate)
        {
            if (!m_lastInstrumentValues.TryGetValue(
                    instrumentId ?? "",
                    out var currentValue) ||
                !m_instrumentHistory.TryGetValue(
                    instrumentId ?? "",
                    out var history))
            {
                return false;
            }
            return InstrumentValuePolicy.TryCalculateTrend(
                history,
                m_calendar.RealTime.Ticks,
                currentValue,
                trendMode,
                windowTicks,
                out change);
        }
    }

    public bool TryEvaluateInstrumentComparisonSustained(
        string instrumentId,
        int windowTicks,
        ComparisonOperator comparison,
        double threshold,
        out bool sustained)
    {
        sustained = false;
        lock (m_instrumentValuesGate)
        {
            if (!m_lastInstrumentValues.TryGetValue(
                    instrumentId ?? "",
                    out var currentValue) ||
                !m_instrumentHistory.TryGetValue(
                    instrumentId ?? "",
                    out var history))
            {
                return false;
            }
            return InstrumentValuePolicy.TryEvaluateSustainedComparison(
                history,
                m_calendar.RealTime.Ticks,
                currentValue,
                windowTicks,
                comparison,
                threshold,
                out sustained);
        }
    }

    private static IReadOnlyList<InstrumentSourceDefinition>
        GetInstrumentSources(InstrumentDefinition instrument)
    {
        if (instrument == null)
        {
            return Array.Empty<InstrumentSourceDefinition>();
        }
        var sources = (instrument.Sources ??
                new List<InstrumentSourceDefinition>())
            .Where(source => source != null && source.EntityId > 0)
            .GroupBy(source => source.EntityId)
            .Select(group => group.First())
            .ToList();
        if (sources.Count == 0 && instrument.EntityId > 0)
        {
            sources.Add(new InstrumentSourceDefinition
            {
                EntityId = instrument.EntityId,
                EntityTitle = instrument.EntityTitle,
                EntityPrototypeId = instrument.EntityPrototypeId,
            });
        }
        return sources;
    }

    public bool SetVanillaNotificationBehavior(
        string overrideId,
        VanillaNotificationScope scope,
        VanillaNotificationBehavior behavior,
        int entityId = -1,
        string entityPrototypeId = "")
    {
        overrideId = overrideId?.Trim() ?? "";
        entityPrototypeId = entityPrototypeId?.Trim() ?? "";
        if (!VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                overrideId) ||
            GroupedVanillaNotificationPolicy.IsGroupedOverride(overrideId) &&
            scope != VanillaNotificationScope.NotificationType ||
            scope == VanillaNotificationScope.Entity && entityId < 0 ||
            scope == VanillaNotificationScope.EntityPrototype &&
            entityPrototypeId.Length == 0)
        {
            return false;
        }

        List<VanillaNotificationRule> previousRules;
        lock (m_configurationGate)
        {
            previousRules = Configuration.VanillaNotificationRules
                .Select(CloneVanillaNotificationRule)
                .ToList();
            Configuration.VanillaNotificationRules.RemoveAll(rule =>
                VanillaNotificationSuppressionPolicy.MatchesScope(
                    rule,
                    overrideId,
                    scope,
                    entityId,
                    entityPrototypeId));
            Configuration.VanillaNotificationRules.Add(
                new VanillaNotificationRule
                {
                    AlarmId = overrideId,
                    Scope = scope,
                    Behavior = behavior,
                    EntityId = scope == VanillaNotificationScope.Entity
                        ? entityId
                        : -1,
                    EntityPrototypeId = scope ==
                        VanillaNotificationScope.EntityPrototype
                        ? entityPrototypeId
                        : "",
                });
        }

        if (SaveConfiguration())
        {
            if (behavior != VanillaNotificationBehavior.Ignored)
            {
                if (GroupedVanillaNotificationPolicy.IsGroupedOverride(
                        overrideId))
                {
                    ReplayCurrentVanillaNotifications(overrideId);
                }
                return true;
            }
            PurgeIgnoredVanillaAlarms(
                overrideId,
                scope,
                entityId,
                entityPrototypeId);
            return SaveConfiguration();
        }

        lock (m_configurationGate)
        {
            Configuration.VanillaNotificationRules = previousRules;
        }
        return false;
    }

    private void PurgeIgnoredVanillaAlarms(
        string overrideId,
        VanillaNotificationScope scope,
        int entityId,
        string entityPrototypeId)
    {
        var targetRule = new VanillaNotificationRule
        {
            AlarmId = overrideId,
            Scope = scope,
            EntityId = entityId,
            EntityPrototypeId = entityPrototypeId,
            Behavior = VanillaNotificationBehavior.Ignored,
        };
        var rules = GetVanillaNotificationRulesSnapshot();
        var purgeAllHistory =
            scope == VanillaNotificationScope.NotificationType &&
            GetGloballyIgnoredHistoryPurgeOverrideIds(rules)
                .Contains(overrideId);
        AlarmView[] externallyCleared = Array.Empty<AlarmView>();
        lock (m_gate)
        {
            var matchingStates = m_alarms
                .Where(pair =>
                    string.Equals(
                        pair.Value.View.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    MatchesVanillaScopeIncludingAliases(
                        targetRule,
                        scope,
                        pair.Value.View) &&
                    ResolveVanillaNotificationBehavior(
                        pair.Value.View,
                        rules) == VanillaNotificationBehavior.Ignored)
                .ToArray();
            externallyCleared = matchingStates
                .Where(pair => pair.Value.View.IsActive)
                .Select(pair => Clone(
                    pair.Value.View,
                    pair.Value.Sequence))
                .ToArray();
            var sequences = new HashSet<long>(
                matchingStates
                    .Select(pair => pair.Value.Sequence)
                    .Where(sequence => sequence > 0));
            foreach (var matchingState in matchingStates)
            {
                m_alarms.Remove(matchingState.Key);
            }
            var removedHistoryCount = m_alarmHistory.RemoveAll(history =>
                sequences.Contains(history.Sequence) ||
                purgeAllHistory &&
                VanillaNotificationSuppressionPolicy
                    .MatchesHistoryForOverride(history, overrideId));
            if (matchingStates.Length == 0 && removedHistoryCount == 0)
            {
                return;
            }
            m_alarmHistoryRevision++;
        }
        foreach (var alarm in externallyCleared)
        {
            PublishExternalDisplayAlarm(alarm, false);
        }
    }

    private bool MatchesVanillaScopeIncludingAliases(
        VanillaNotificationRule rule,
        VanillaNotificationScope scope,
        AlarmView view)
    {
        if (view == null)
        {
            return false;
        }
        if (GroupedVanillaNotificationPolicy.IsGroupedOverride(
                view.OverrideId) &&
            scope != VanillaNotificationScope.NotificationType)
        {
            return false;
        }
        if (VanillaNotificationSuppressionPolicy.MatchesScope(
                rule,
                view.OverrideId,
                scope,
                view.EntityId,
                view.EntityPrototypeId))
        {
            return true;
        }
        return GetNotificationOwnerAliases(view.EntityId).Any(alias =>
            VanillaNotificationSuppressionPolicy.MatchesScope(
                rule,
                view.OverrideId,
                scope,
                alias.OwnerEntityId,
                alias.OwnerEntityPrototypeId));
    }

    public bool SetConfiguredSound(string alarmId, string soundId)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return false;
        }

        soundId = string.IsNullOrWhiteSpace(soundId) ? "auto" : soundId;
        var defaultAutoAcknowledge =
            ResolveExternalDefaultAutoAcknowledge(alarmId);
        AlarmSoundOverride existing;
        string previousSound = null;
        var created = false;
        lock (m_configurationGate)
        {
            existing = Configuration.SoundOverrides.FirstOrDefault(item =>
                string.Equals(
                    item.AlarmId,
                    alarmId,
                    StringComparison.Ordinal));
            if (existing == null)
            {
                existing = new AlarmSoundOverride
                {
                    AlarmId = alarmId,
                    SoundId = soundId,
                    AutoAcknowledgeOnClear = defaultAutoAcknowledge,
                };
                Configuration.SoundOverrides.Add(existing);
                created = true;
            }
            else
            {
                previousSound = existing.SoundId;
                existing.SoundId = soundId;
            }
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                if (created)
                {
                    Configuration.SoundOverrides.Remove(existing);
                }
                else
                {
                    existing.SoundId = previousSound;
                }
            }
            return false;
        }

        var updatedLatchedAlarm = false;
        lock (m_gate)
        {
            foreach (var state in m_alarms.Values.Where(state =>
                         string.Equals(
                             state.View.OverrideId,
                             alarmId,
                             StringComparison.Ordinal)))
            {
                state.View.SoundId = soundId;
                updatedLatchedAlarm |= state.View.IsLatched;
            }
        }
        if (updatedLatchedAlarm)
        {
            PersistAlarmState();
        }
        return true;
    }

    public bool GetConfiguredAutoAcknowledgeOnClear(string alarmId)
    {
        return ResolveAutoAcknowledgeOnClear(
            alarmId,
            ResolveExternalDefaultAutoAcknowledge(alarmId));
    }

    public bool SetConfiguredAutoAcknowledgeOnClear(
        string alarmId,
        bool autoAcknowledgeOnClear)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return false;
        }

        var defaultSound = GetConfiguredSound(alarmId);

        AlarmSoundOverride existing;
        var previousValue = false;
        var created = false;
        lock (m_configurationGate)
        {
            existing = Configuration.SoundOverrides.FirstOrDefault(item =>
                string.Equals(
                    item.AlarmId,
                    alarmId,
                    StringComparison.Ordinal));
            if (existing == null)
            {
                existing = new AlarmSoundOverride
                {
                    AlarmId = alarmId,
                    SoundId = defaultSound,
                    AutoAcknowledgeOnClear = autoAcknowledgeOnClear,
                };
                Configuration.SoundOverrides.Add(existing);
                created = true;
            }
            else
            {
                previousValue = existing.AutoAcknowledgeOnClear;
                existing.AutoAcknowledgeOnClear = autoAcknowledgeOnClear;
            }
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                if (created)
                {
                    Configuration.SoundOverrides.Remove(existing);
                }
                else
                {
                    existing.AutoAcknowledgeOnClear = previousValue;
                }
            }
            return false;
        }

        var clearedGoneAlarm = false;
        if (autoAcknowledgeOnClear)
        {
            lock (m_gate)
            {
                foreach (var state in m_alarms.Values.Where(state =>
                             string.Equals(
                                 state.View.OverrideId,
                                 alarmId,
                                 StringComparison.Ordinal) &&
                             state.View.IsGoneUnacknowledged))
                {
                    var history = FindHistoryLocked(state.Sequence);
                    if (history != null)
                    {
                        history.SetState(
                            isGone: true,
                            isAcknowledged: true,
                            currentGameTicks: CurrentGameTicks);
                    }
                    state.View.IsGoneUnacknowledged = false;
                    state.View.IsAcknowledged = false;
                    state.View.IsOperatorSilenced = false;
                    state.View.OperatorSilencedAtGameTick = -1;
                    clearedGoneAlarm = true;
                }
                if (clearedGoneAlarm)
                {
                    m_alarmHistoryRevision++;
                }
            }
        }
        if (clearedGoneAlarm)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
        return true;
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        if (m_simListenerAdded)
        {
            m_simLoopEvents.UpdateEndForUi.RemoveNonSaveable(
                this,
                OnUpdateEndForUi);
            m_simListenerAdded = false;
        }
        if (m_monthStartListenerAdded)
        {
            m_newMonthStartEvent.RemoveNonSaveable(
                this,
                OnNewMonthStart);
            m_monthStartListenerAdded = false;
        }
        m_notificationsManager.NotificationAdded -= OnNotificationAdded;
        m_notificationsManager.NotificationRemoved -= OnNotificationRemoved;
        m_notificationsManager.NotificationSuppressChanged -=
            OnNotificationSuppressChanged;
        m_entityRemovedEvent.RemoveNonSaveable(
            this,
            OnEntityRemoved);
        m_groupedVanillaNotifications.Clear();
    }

    private void OnNewMonthStart()
    {
        if (m_disposed)
        {
            return;
        }

        AlarmView[] alarms;
        lock (m_gate)
        {
            alarms = m_alarms.Values
                .Where(state => state.View.IsActive &&
                                state.View.IsOperatorSilenced)
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }

        var rules = GetVanillaNotificationRulesSnapshot();
        var currentGameTick = CurrentGameTick;
        var snapshot = OperatorSilenceReminderPolicy.Build(
            alarms.Select(alarm => new OperatorSilenceReminderSample(
                !string.IsNullOrWhiteSpace(alarm.OverrideId)
                    ? alarm.OverrideId
                    : PanelSlotProjection.StableAlarmId(alarm),
                GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    alarm.OverrideId)
                    ? GroupedVanillaNotificationPolicy.FormatTitle(
                        alarm.Name,
                        Math.Max(1, (int)Math.Round(alarm.LastValue)))
                    : alarm.Name,
                alarm.IsActive,
                alarm.IsOperatorSilenced,
                ResolveVanillaNotificationBehavior(alarm, rules),
                alarm.SoundId,
                alarm.OperatorSilencedAtGameTick)),
            currentGameTick,
            GameTimeWindowPolicy.SimTicksPerMonth);

        lock (m_gate)
        {
            // Coalesce missed UI presentation into the newest monthly state.
            m_pendingOperatorSilenceReminder = snapshot.AlarmCount > 0
                ? snapshot
                : null;
        }
    }

    public int AcknowledgeableCount
    {
        get
        {
            var disabledVanillaOverrideIds =
                GetDisabledVanillaOverrideIds();
            var vanillaRules = GetVanillaNotificationRulesSnapshot();
            lock (m_gate)
            {
                return m_alarms.Values.Count(state =>
                    (state.View.RequiresAcknowledgement ||
                     state.View.IsActive &&
                     !state.View.IsOperatorSilenced) &&
                    !IsSuppressedVanillaAlarm(
                        state.View,
                        disabledVanillaOverrideIds) &&
                    !IsVanillaAlarmHidden(state.View, vanillaRules));
            }
        }
    }

    private void OnUpdateEndForUi()
    {
        if (m_disposed)
        {
            return;
        }

        FlushGroupedVanillaNotificationClear();
        if (!m_gameplayActive)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        try
        {
            ProcessRequestedEntityInspection();
            if (now < Interlocked.Read(ref m_nextEvaluationTimestamp))
            {
                return;
            }

            var intervalMs = Math.Max(100, m_settings.PollIntervalMs);
            Interlocked.Exchange(
                ref m_nextEvaluationTimestamp,
                now + Stopwatch.Frequency * intervalMs / 1000L);
            ProcessRemovedEntities();
            Evaluate();
            PublishExternalDisplayPanelState();
        }
        catch (Exception exception)
        {
            if (now >= Interlocked.Read(
                    ref m_nextEvaluationErrorLogTimestamp))
            {
                Interlocked.Exchange(
                    ref m_nextEvaluationErrorLogTimestamp,
                    now + Stopwatch.Frequency * 30L);
                Log.Warning(
                    $"UNMA: Alarm-Auswertung fehlgeschlagen: {exception.Message}");
            }
        }
    }

    private void OnEntityRemoved(IEntity entity)
    {
        if (m_disposed || entity == null)
        {
            return;
        }
        lock (m_removedEntitiesGate)
        {
            m_removedEntityCandidates[entity.Id.Value] = entity;
        }
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
    }

    private void ProcessRemovedEntities()
    {
        KeyValuePair<int, IEntity>[] candidates;
        lock (m_removedEntitiesGate)
        {
            if (m_removedEntityCandidates.Count == 0)
            {
                return;
            }
            candidates = m_removedEntityCandidates.ToArray();
        }

        var confirmed = new List<KeyValuePair<int, IEntity>>();
        foreach (var candidate in candidates)
        {
            var current = m_entitiesManager.GetEntity(
                new EntityId(candidate.Key));
            var hasLiveReplacement = !current.IsNone &&
                                     !current.Value.IsDestroyed;
            if (CustomRuleLifecyclePolicy.ShouldDeleteForRemovedEntity(
                    candidate.Value.IsDestroyed,
                    hasLiveReplacement))
            {
                confirmed.Add(candidate);
            }
        }

        lock (m_removedEntitiesGate)
        {
            foreach (var candidate in candidates)
            {
                if (m_removedEntityCandidates.TryGetValue(
                        candidate.Key,
                        out var currentCandidate) &&
                    ReferenceEquals(currentCandidate, candidate.Value))
                {
                    m_removedEntityCandidates.Remove(candidate.Key);
                }
            }
        }

        if (confirmed.Count == 0)
        {
            return;
        }
        if (!TryRemoveRulesReferencingEntities(
                confirmed.Select(candidate => candidate.Key),
                out _))
        {
            lock (m_removedEntitiesGate)
            {
                foreach (var candidate in confirmed)
                {
                    if (!m_removedEntityCandidates.ContainsKey(candidate.Key))
                    {
                        m_removedEntityCandidates[candidate.Key] =
                            candidate.Value;
                    }
                }
            }
        }
    }

    private bool TryRemoveRulesReferencingEntities(
        IEnumerable<int> entityIds,
        out int removedCount)
    {
        removedCount = 0;
        var removedEntityIds = new HashSet<int>(
            entityIds ?? Enumerable.Empty<int>());
        if (removedEntityIds.Count == 0)
        {
            return true;
        }

        string[] ruleIds;
        string[] entityPanelIds;
        lock (m_persistenceGate)
        {
            lock (m_configurationGate)
            {
                entityPanelIds = Configuration.Panels
                    .Where(panel =>
                        panel != null &&
                        PanelTopologyPolicy.IsEntityPanel(panel) &&
                        removedEntityIds.Contains(panel.OwnerEntityId))
                    .Select(panel => panel.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                ruleIds = CustomRuleLifecyclePolicy
                    .FindRulesReferencingEntities(
                        Configuration.Rules,
                        removedEntityIds)
                    .ToArray();
            }
            if (ruleIds.Length == 0 && entityPanelIds.Length == 0)
            {
                return true;
            }
            if (!RemovePanelsAndRulesWithPersistenceLock(
                    entityPanelIds,
                    ruleIds,
                    out removedCount))
            {
                return false;
            }
        }

        Log.Info(
            UnmaText.Get("auto.a01209a822a4") + removedCount +
            UnmaText.Get("auto.9548beef5dd8"));
        foreach (var entityId in removedEntityIds)
        {
            m_missingStaticEntityTracker.Forget(entityId);
        }
        return true;
    }

    private int[] ObserveMissingStaticRuleEntities(
        IReadOnlyList<AlarmRuleDefinition> rules)
    {
        var staticEntityIds = new HashSet<int>(
            (rules ?? Array.Empty<AlarmRuleDefinition>())
            .Where(rule => rule?.Conditions != null)
            .SelectMany(rule => rule.Conditions)
            .Where(condition =>
                condition != null &&
                string.IsNullOrWhiteSpace(condition.InstrumentId) &&
                IsStaticEntityType(condition.EntityType))
            .Select(condition => condition.EntityId));
        lock (m_configurationGate)
        {
            foreach (var panel in Configuration.Panels.Where(panel =>
                         PanelTopologyPolicy.IsEntityPanel(panel) &&
                         IsStaticEntityType(panel.OwnerEntityType)))
            {
                staticEntityIds.Add(panel.OwnerEntityId);
            }
        }
        m_missingStaticEntityTracker.RetainOnly(staticEntityIds);

        var confirmed = new List<int>();
        var currentTimestamp = Stopwatch.GetTimestamp();
        foreach (var entityId in staticEntityIds)
        {
            var entity = m_entitiesManager.GetEntity(new EntityId(entityId));
            if (!entity.IsNone && !entity.Value.IsDestroyed)
            {
                m_missingStaticEntityTracker.ObserveLive(entityId);
                continue;
            }
            // EntityRemoved is the immediate, authoritative path. Polling can
            // be transient during load or replacement and therefore always
            // has to survive the full grace period before deleting data.
            if (m_missingStaticEntityTracker.ObserveMissing(
                    entityId,
                    currentTimestamp,
                    Stopwatch.Frequency))
            {
                confirmed.Add(entityId);
            }
        }
        return confirmed.ToArray();
    }

    private bool IsStaticEntityType(string entityTypeName)
    {
        if (string.IsNullOrWhiteSpace(entityTypeName))
        {
            return false;
        }
        if (m_staticEntityTypeCache.TryGetValue(
                entityTypeName,
                out var cached))
        {
            return cached;
        }

        Type entityType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            entityType = assembly.GetType(
                entityTypeName,
                throwOnError: false,
                ignoreCase: false);
            if (entityType != null)
            {
                break;
            }
        }
        var isStatic = entityType != null &&
                       typeof(IStaticEntity).IsAssignableFrom(entityType);
        m_staticEntityTypeCache[entityTypeName] = isStatic;
        return isStatic;
    }

    private void ProcessRequestedEntityInspection()
    {
        int entityId;
        long requestGeneration;
        lock (m_inspectionGate)
        {
            entityId = m_requestedInspectionEntityId;
            requestGeneration = m_inspectionRequestGeneration;
            m_requestedInspectionEntityId = -1;
        }
        if (entityId < 0)
        {
            return;
        }

        EntityInspectionSnapshot result;
        try
        {
            var entityOption = m_entitiesManager.GetEntity(
                new EntityId(entityId));
            if (entityOption.IsNone || entityOption.Value.IsDestroyed)
            {
                result = new EntityInspectionSnapshot(
                    entityId,
                    "",
                    "",
                    "",
                    "",
                    Array.Empty<MetricDescriptor>(),
                    UnmaText.Get("auto.402d28b2c076"));
            }
            else
            {
                var entity = entityOption.Value;
                result = new EntityInspectionSnapshot(
                    entityId,
                    EntityMetricCatalog.GetEntityTitle(entity),
                    entity.GetType().FullName ?? entity.GetType().Name,
                    entity.Prototype.Id.Value,
                    EntityMetricCatalog.TryGetStoredProductId(entity),
                    EntityMetricCatalog.Discover(entity));
            }
        }
        catch (Exception exception)
        {
            result = new EntityInspectionSnapshot(
                entityId,
                "",
                "",
                "",
                "",
                Array.Empty<MetricDescriptor>(),
                UnmaText.Get("auto.631c5fe440a9") +
                exception.Message);
        }

        lock (m_inspectionGate)
        {
            if (requestGeneration == m_inspectionRequestGeneration)
            {
                m_completedInspection = result;
            }
        }
    }

    private bool EvaluateRuleCondition(
        string ruleId,
        int conditionIndex,
        double actual,
        ComparisonOperator comparison,
        double threshold,
        double hysteresis)
    {
        return EvaluateConditionLatch(
            m_ruleConditionLatches,
            ruleId,
            conditionIndex,
            actual,
            comparison,
            threshold,
            hysteresis);
    }

    private bool EvaluateSystemStageCondition(
        string stageKey,
        int conditionIndex,
        double actual,
        ComparisonOperator comparison,
        double threshold,
        double hysteresis)
    {
        return EvaluateConditionLatch(
            m_systemStageConditionLatches,
            stageKey,
            conditionIndex,
            actual,
            comparison,
            threshold,
            hysteresis);
    }

    private bool EvaluateConditionLatch(
        IDictionary<string, Dictionary<int, bool>> conditionLatches,
        string ownerKey,
        int conditionIndex,
        double actual,
        ComparisonOperator comparison,
        double threshold,
        double hysteresis)
    {
        bool changed;
        bool isLatched;
        lock (m_alarmTimingGate)
        {
            if (!conditionLatches.TryGetValue(
                    ownerKey,
                    out var latches))
            {
                latches = new Dictionary<int, bool>();
                conditionLatches[ownerKey] = latches;
            }
            var hasLatch = latches.TryGetValue(
                conditionIndex,
                out var currentLatch);
            isLatched = AlarmTimingPolicy.EvaluateConditionLatch(
                actual,
                comparison,
                threshold,
                hysteresis,
                hasLatch,
                currentLatch);
            changed = !hasLatch || currentLatch != isLatched;
            latches[conditionIndex] = isLatched;
        }
        if (changed)
        {
            PersistAlarmState();
        }
        return isLatched;
    }

    private void SetRuleConditionLatch(
        string ruleId,
        int conditionIndex,
        bool value)
    {
        SetConditionLatch(
            m_ruleConditionLatches,
            ruleId,
            conditionIndex,
            value);
    }

    private void SetSystemStageConditionLatch(
        string stageKey,
        int conditionIndex,
        bool value)
    {
        SetConditionLatch(
            m_systemStageConditionLatches,
            stageKey,
            conditionIndex,
            value);
    }

    private void SetConditionLatch(
        IDictionary<string, Dictionary<int, bool>> conditionLatches,
        string ownerKey,
        int conditionIndex,
        bool value)
    {
        bool changed;
        lock (m_alarmTimingGate)
        {
            if (!conditionLatches.TryGetValue(
                    ownerKey,
                    out var latches))
            {
                latches = new Dictionary<int, bool>();
                conditionLatches[ownerKey] = latches;
            }
            changed = !latches.TryGetValue(
                          conditionIndex,
                          out var previous) ||
                      previous != value;
            latches[conditionIndex] = value;
        }
        if (changed)
        {
            PersistAlarmState();
        }
    }

    private bool AdvanceRuleTiming(
        AlarmRuleDefinition rule,
        bool conditionMet,
        long currentGameTick,
        out AlarmEscalationEvaluation escalation)
    {
        AlarmTimingEvaluation evaluation;
        bool changed;
        lock (m_alarmTimingGate)
        {
            m_ruleTimingStates.TryGetValue(rule.Id, out var state);
            evaluation = AlarmTimingPolicy.Advance(
                state,
                conditionMet,
                currentGameTick,
                new AlarmTimingSettings(
                    rule.ActivationDelayTicks,
                    rule.ResetDelayTicks,
                    rule.MinimumActiveTicks,
                    0d));
            m_ruleTimingStates[rule.Id] = evaluation.State;
            changed = AlarmTimingPolicy.HasPersistentStateChanged(
                state,
                evaluation.State);
            var wasEscalated = m_escalatedRuleIds.Contains(rule.Id);
            escalation = AlarmEscalationPolicy.Evaluate(
                rule.Escalation,
                rule.Severity,
                rule.SoundId,
                wasEscalated,
                evaluation.IsActive,
                evaluation.State.ActiveSinceTick,
                currentGameTick);
            if (escalation.IsEscalated)
            {
                m_escalatedRuleIds.Add(rule.Id);
            }
            else
            {
                m_escalatedRuleIds.Remove(rule.Id);
            }
        }
        if (changed)
        {
            PersistAlarmState();
        }
        return evaluation.IsActive;
    }

    private bool AdvanceSystemStageTiming(
        string stageKey,
        SystemAlarmStageDefinition stage,
        bool conditionMet,
        long currentGameTick)
    {
        AlarmTimingEvaluation evaluation;
        bool changed;
        lock (m_alarmTimingGate)
        {
            m_systemStageTimingStates.TryGetValue(stageKey, out var state);
            evaluation = AlarmTimingPolicy.Advance(
                state,
                conditionMet,
                currentGameTick,
                new AlarmTimingSettings(
                    stage.ActivationDelayTicks,
                    stage.ResetDelayTicks,
                    stage.MinimumActiveTicks,
                    0d));
            m_systemStageTimingStates[stageKey] = evaluation.State;
            changed = AlarmTimingPolicy.HasPersistentStateChanged(
                state,
                evaluation.State);
        }
        if (changed)
        {
            PersistAlarmState();
        }
        return evaluation.IsActive;
    }

    private static string SystemStageTimingKey(
        string alarmId,
        string stageId,
        int stageIndex)
    {
        return AlarmTimingMemoryPolicy.SystemStageOwnerKey(
            alarmId,
            stageId,
            stageIndex);
    }

    private void EnsureRuleTimingDefinition(AlarmRuleDefinition rule)
    {
        var signature =
            AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule);
        bool persistentStateChanged;
        lock (m_alarmTimingGate)
        {
            persistentStateChanged = EnsureTimingOwnerDefinitionLocked(
                m_ruleTimingStates,
                m_ruleConditionLatches,
                m_ruleTimingSignatures,
                rule.Id,
                signature,
                rule.Conditions?.Count ?? 0,
                CurrentGameTick);
        }
        if (persistentStateChanged)
        {
            PersistAlarmState();
        }
    }

    private void EnsureSystemStageTimingDefinition(
        string stageKey,
        SystemAlarmStageDefinition stage)
    {
        var signature = AlarmTimingMemoryPolicy
            .SystemStageDefinitionSignature(stage);
        bool persistentStateChanged;
        lock (m_alarmTimingGate)
        {
            persistentStateChanged = EnsureTimingOwnerDefinitionLocked(
                m_systemStageTimingStates,
                m_systemStageConditionLatches,
                m_systemStageTimingSignatures,
                stageKey,
                signature,
                stage.Conditions?.Count ?? 0,
                CurrentGameTick);
        }
        if (persistentStateChanged)
        {
            PersistAlarmState();
        }
    }

    private static bool EnsureTimingOwnerDefinitionLocked(
        IDictionary<string, AlarmTimingState> timingStates,
        IDictionary<string, Dictionary<int, bool>> conditionLatches,
        IDictionary<string, string> timingSignatures,
        string ownerKey,
        string signature,
        int conditionCount,
        long currentGameTick)
    {
        if (!timingSignatures.TryGetValue(
                ownerKey,
                out var previousSignature))
        {
            timingSignatures[ownerKey] = signature;
            return false;
        }
        if (string.Equals(
                previousSignature,
                signature,
                StringComparison.Ordinal))
        {
            return false;
        }
        return ReconcileTimingOwnerDefinitionLocked(
            timingStates,
            conditionLatches,
            timingSignatures,
            ownerKey,
            signature,
            conditionCount,
            currentGameTick);
    }

    private static bool ReconcileTimingOwnerDefinitionLocked(
        IDictionary<string, AlarmTimingState> timingStates,
        IDictionary<string, Dictionary<int, bool>> conditionLatches,
        IDictionary<string, string> timingSignatures,
        string ownerKey,
        string signature,
        int conditionCount,
        long currentGameTick)
    {
        var hasState = timingStates.TryGetValue(ownerKey, out var state);
        var hadLatches = conditionLatches.ContainsKey(ownerKey);
        timingStates.Remove(ownerKey);
        conditionLatches.Remove(ownerKey);
        timingSignatures[ownerKey] = signature;
        if (hasState && state.IsActive)
        {
            timingStates[ownerKey] = AlarmTimingPolicy
                .PreserveActiveForDefinitionChange(
                    state,
                    currentGameTick);
            conditionLatches[ownerKey] =
                AlarmTimingPolicy.CreateActiveConditionLatches(
                    conditionCount);
        }
        return hasState || hadLatches;
    }

    private bool ReconcileRuleTimingDefinition(
        AlarmRuleDefinition previousRule,
        AlarmRuleDefinition currentRule)
    {
        var previousSignature = AlarmTimingMemoryPolicy
            .RuleDefinitionSignature(previousRule);
        var currentSignature = AlarmTimingMemoryPolicy
            .RuleDefinitionSignature(currentRule);
        var semanticsChanged = !string.Equals(
            previousSignature,
            currentSignature,
            StringComparison.Ordinal);
        lock (m_alarmTimingGate)
        {
            if (!currentRule.Enabled)
            {
                m_ruleTimingStates.Remove(currentRule.Id);
                m_ruleConditionLatches.Remove(currentRule.Id);
                m_ruleTimingSignatures.Remove(currentRule.Id);
            }
            else if (semanticsChanged)
            {
                ReconcileTimingOwnerDefinitionLocked(
                    m_ruleTimingStates,
                    m_ruleConditionLatches,
                    m_ruleTimingSignatures,
                    currentRule.Id,
                    currentSignature,
                    currentRule.Conditions?.Count ?? 0,
                    CurrentGameTick);
            }
            else
            {
                m_ruleTimingSignatures[currentRule.Id] = currentSignature;
            }
        }
        return semanticsChanged ||
               previousRule.Enabled != currentRule.Enabled;
    }

    private void ReconcileSystemAlarmTimingDefinition(
        SystemAlarmDefinition previousAlarm,
        SystemAlarmDefinition currentAlarm)
    {
        var prefix = AlarmTimingMemoryPolicy.SystemAlarmOwnerPrefix(
            currentAlarm.Id);
        lock (m_alarmTimingGate)
        {
            var previousRuntime = CaptureSystemAlarmTimingSnapshotLocked(
                prefix);
            RemoveSystemAlarmTimingLocked(prefix);
            if (!currentAlarm.Enabled)
            {
                return;
            }

            var previousStages = previousAlarm?.Stages ??
                                 new List<SystemAlarmStageDefinition>();
            var currentStages = currentAlarm.Stages ??
                                new List<SystemAlarmStageDefinition>();
            var usedPreviousStageIndexes = new HashSet<int>();
            for (var currentIndex = 0;
                 currentIndex < currentStages.Count;
                 currentIndex++)
            {
                var currentStage = currentStages[currentIndex];
                if (currentStage == null || !currentStage.Enabled)
                {
                    continue;
                }
                var currentKey = SystemStageTimingKey(
                    currentAlarm.Id,
                    currentStage.Id,
                    currentIndex);
                var currentSignature = AlarmTimingMemoryPolicy
                    .SystemStageDefinitionSignature(currentStage);
                var previousIndex = FindMatchingSystemStageIndex(
                    previousStages,
                    currentStage,
                    currentIndex,
                    usedPreviousStageIndexes);
                AlarmTimingOwnerRuntimeSnapshot previous = null;
                SystemAlarmStageDefinition previousStage = null;
                if (previousIndex >= 0)
                {
                    usedPreviousStageIndexes.Add(previousIndex);
                    previousStage = previousStages[previousIndex];
                    var previousKey = SystemStageTimingKey(
                        previousAlarm.Id,
                        previousStage.Id,
                        previousIndex);
                    previousRuntime.TryGetValue(previousKey, out previous);
                }

                if (previous == null)
                {
                    m_systemStageTimingSignatures[currentKey] =
                        currentSignature;
                    continue;
                }
                var semanticsChanged = previousStage == null ||
                    !string.Equals(
                        AlarmTimingMemoryPolicy
                            .SystemStageDefinitionSignature(previousStage),
                        currentSignature,
                        StringComparison.Ordinal);
                if (!semanticsChanged)
                {
                    RestoreTimingOwnerSnapshotLocked(
                        m_systemStageTimingStates,
                        m_systemStageConditionLatches,
                        m_systemStageTimingSignatures,
                        currentKey,
                        previous);
                    m_systemStageTimingSignatures[currentKey] =
                        currentSignature;
                    continue;
                }

                if (previous.HasState && previous.State.IsActive)
                {
                    m_systemStageTimingStates[currentKey] =
                        AlarmTimingPolicy
                            .PreserveActiveForDefinitionChange(
                                previous.State,
                                CurrentGameTick);
                    m_systemStageConditionLatches[currentKey] =
                        AlarmTimingPolicy.CreateActiveConditionLatches(
                            currentStage.Conditions?.Count ?? 0);
                }
                m_systemStageTimingSignatures[currentKey] =
                    currentSignature;
            }
        }
    }

    private static int FindMatchingSystemStageIndex(
        IReadOnlyList<SystemAlarmStageDefinition> previousStages,
        SystemAlarmStageDefinition currentStage,
        int currentIndex,
        ISet<int> usedPreviousStageIndexes)
    {
        var currentId = currentStage?.Id ?? "";
        if (currentIndex >= 0 &&
            currentIndex < previousStages.Count &&
            !usedPreviousStageIndexes.Contains(currentIndex) &&
            string.Equals(
                previousStages[currentIndex]?.Id ?? "",
                currentId,
                StringComparison.Ordinal))
        {
            return currentIndex;
        }
        if (currentId.Length > 0)
        {
            for (var index = 0; index < previousStages.Count; index++)
            {
                if (!usedPreviousStageIndexes.Contains(index) &&
                    string.Equals(
                        previousStages[index]?.Id,
                        currentId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }
        if (currentIndex >= 0 &&
            currentIndex < previousStages.Count &&
            !usedPreviousStageIndexes.Contains(currentIndex) &&
            string.IsNullOrEmpty(previousStages[currentIndex]?.Id))
        {
            return currentIndex;
        }
        return -1;
    }

    private AlarmTimingOwnerRuntimeSnapshot CaptureRuleTimingSnapshot(
        string ruleId)
    {
        lock (m_alarmTimingGate)
        {
            return CaptureTimingOwnerSnapshotLocked(
                m_ruleTimingStates,
                m_ruleConditionLatches,
                m_ruleTimingSignatures,
                ruleId);
        }
    }

    private void RestoreRuleTimingSnapshot(
        string ruleId,
        AlarmTimingOwnerRuntimeSnapshot snapshot)
    {
        lock (m_alarmTimingGate)
        {
            RestoreTimingOwnerSnapshotLocked(
                m_ruleTimingStates,
                m_ruleConditionLatches,
                m_ruleTimingSignatures,
                ruleId,
                snapshot);
        }
    }

    private Dictionary<string, AlarmTimingOwnerRuntimeSnapshot>
        CaptureSystemAlarmTimingSnapshot(string alarmId)
    {
        var prefix = AlarmTimingMemoryPolicy.SystemAlarmOwnerPrefix(alarmId);
        lock (m_alarmTimingGate)
        {
            return CaptureSystemAlarmTimingSnapshotLocked(prefix);
        }
    }

    private void RestoreSystemAlarmTimingSnapshot(
        string alarmId,
        IReadOnlyDictionary<string, AlarmTimingOwnerRuntimeSnapshot> snapshot)
    {
        var prefix = AlarmTimingMemoryPolicy.SystemAlarmOwnerPrefix(alarmId);
        lock (m_alarmTimingGate)
        {
            RemoveSystemAlarmTimingLocked(prefix);
            foreach (var pair in snapshot)
            {
                RestoreTimingOwnerSnapshotLocked(
                    m_systemStageTimingStates,
                    m_systemStageConditionLatches,
                    m_systemStageTimingSignatures,
                    pair.Key,
                    pair.Value);
            }
        }
    }

    private Dictionary<string, AlarmTimingOwnerRuntimeSnapshot>
        CaptureSystemAlarmTimingSnapshotLocked(string prefix)
    {
        return m_systemStageTimingStates.Keys
            .Concat(m_systemStageConditionLatches.Keys)
            .Concat(m_systemStageTimingSignatures.Keys)
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                key => key,
                key => CaptureTimingOwnerSnapshotLocked(
                    m_systemStageTimingStates,
                    m_systemStageConditionLatches,
                    m_systemStageTimingSignatures,
                    key),
                StringComparer.Ordinal);
    }

    private static AlarmTimingOwnerRuntimeSnapshot
        CaptureTimingOwnerSnapshotLocked(
            IDictionary<string, AlarmTimingState> timingStates,
            IDictionary<string, Dictionary<int, bool>> conditionLatches,
            IDictionary<string, string> timingSignatures,
            string ownerKey)
    {
        var snapshot = new AlarmTimingOwnerRuntimeSnapshot
        {
            HasState = timingStates.TryGetValue(ownerKey, out var state),
            State = state,
            HasConditionLatches = conditionLatches.TryGetValue(
                ownerKey,
                out var latches),
            ConditionLatches = latches == null
                ? null
                : new Dictionary<int, bool>(latches),
            HasSignature = timingSignatures.TryGetValue(
                ownerKey,
                out var signature),
            Signature = signature ?? "",
        };
        return snapshot;
    }

    private static void RestoreTimingOwnerSnapshotLocked(
        IDictionary<string, AlarmTimingState> timingStates,
        IDictionary<string, Dictionary<int, bool>> conditionLatches,
        IDictionary<string, string> timingSignatures,
        string ownerKey,
        AlarmTimingOwnerRuntimeSnapshot snapshot)
    {
        timingStates.Remove(ownerKey);
        conditionLatches.Remove(ownerKey);
        timingSignatures.Remove(ownerKey);
        if (snapshot == null)
        {
            return;
        }
        if (snapshot.HasState)
        {
            timingStates[ownerKey] = snapshot.State;
        }
        if (snapshot.HasConditionLatches)
        {
            conditionLatches[ownerKey] = new Dictionary<int, bool>(
                snapshot.ConditionLatches ??
                new Dictionary<int, bool>());
        }
        if (snapshot.HasSignature)
        {
            timingSignatures[ownerKey] = snapshot.Signature;
        }
    }

    private void RemoveSystemAlarmTimingLocked(string prefix)
    {
        foreach (var stageKey in m_systemStageTimingStates.Keys
                     .Concat(m_systemStageConditionLatches.Keys)
                     .Concat(m_systemStageTimingSignatures.Keys)
                     .Where(key => key.StartsWith(
                         prefix,
                         StringComparison.Ordinal))
                     .Distinct(StringComparer.Ordinal)
                     .ToArray())
        {
            m_systemStageTimingStates.Remove(stageKey);
            m_systemStageConditionLatches.Remove(stageKey);
            m_systemStageTimingSignatures.Remove(stageKey);
        }
    }

    private bool InvalidateRuleTiming(string ruleId)
    {
        ruleId ??= "";
        if (ruleId.Length == 0)
        {
            return false;
        }
        bool changed;
        lock (m_alarmTimingGate)
        {
            changed = m_ruleTimingStates.Remove(ruleId);
            changed |= m_ruleConditionLatches.Remove(ruleId);
            changed |= m_ruleTimingSignatures.Remove(ruleId);
            changed |= m_escalatedRuleIds.Remove(ruleId);
        }
        RemoveSustainedStatesForRule(ruleId);
        return changed;
    }

    private bool InvalidateSystemAlarmTiming(string alarmId)
    {
        alarmId ??= "";
        if (alarmId.Length == 0)
        {
            return false;
        }
        var prefix = AlarmTimingMemoryPolicy.SystemAlarmOwnerPrefix(alarmId);
        var changed = false;
        lock (m_alarmTimingGate)
        {
            foreach (var stageKey in m_systemStageTimingStates.Keys
                         .Concat(m_systemStageConditionLatches.Keys)
                         .Concat(m_systemStageTimingSignatures.Keys)
                         .Where(key => key.StartsWith(
                             prefix,
                             StringComparison.Ordinal))
                         .Distinct(StringComparer.Ordinal)
                         .ToArray())
            {
                changed |= m_systemStageTimingStates.Remove(stageKey);
                changed |= m_systemStageConditionLatches.Remove(stageKey);
                changed |= m_systemStageTimingSignatures.Remove(stageKey);
            }
        }
        return changed;
    }

    private bool InvalidateTimingForAlarmKey(string alarmKey)
    {
        if (PanelTopologyPolicy.TryGetRuleId(alarmKey, out var ruleId))
        {
            return InvalidateRuleTiming(ruleId);
        }
        return InvalidateSystemAlarmTiming(alarmKey);
    }

    private void EvaluateSystemAlarms(
        IReadOnlyDictionary<string, double> metrics)
    {
        SystemAlarmDefinition[] alarms;
        lock (m_configurationGate)
        {
            alarms = Configuration.SystemAlarms
                .Select(CloneSystemAlarmForEditing)
                .ToArray();
        }

        if (metrics["population.total"] <= 0d)
        {
            foreach (var alarm in alarms)
            {
                ForceNormal(alarm.Id);
            }
            return;
        }

        foreach (var alarm in alarms)
        {
            if (!alarm.Enabled)
            {
                ForceNormal(alarm.Id);
                continue;
            }

            SystemAlarmStageDefinition selectedStage = null;
            var currentGameTick = CurrentGameTick;
            for (var stageIndex = 0;
                 stageIndex < alarm.Stages.Count;
                 stageIndex++)
            {
                var stage = alarm.Stages[stageIndex];
                if (stage == null || !stage.Enabled)
                {
                    continue;
                }

                var stageKey = SystemStageTimingKey(
                    alarm.Id,
                    stage.Id,
                    stageIndex);
                EnsureSystemStageTimingDefinition(stageKey, stage);
                var conditionValues = new bool[stage.Conditions.Count];
                for (var conditionIndex = 0;
                     conditionIndex < stage.Conditions.Count;
                     conditionIndex++)
                {
                    var condition = stage.Conditions[conditionIndex];
                    if (condition == null ||
                        metrics == null ||
                        !metrics.TryGetValue(
                            condition.MetricId ?? "",
                            out var actual))
                    {
                        SetSystemStageConditionLatch(
                            stageKey,
                            conditionIndex,
                            false);
                        continue;
                    }
                    conditionValues[conditionIndex] =
                        EvaluateSystemStageCondition(
                            stageKey,
                            conditionIndex,
                            actual,
                            condition.Comparison,
                            condition.Threshold,
                            condition.Hysteresis);
                }

                var stageIsActive = AdvanceSystemStageTiming(
                    stageKey,
                    stage,
                    AlarmEvaluation.Combine(
                        conditionValues,
                        stage.Logic),
                    currentGameTick);
                if (!stageIsActive ||
                    selectedStage != null &&
                    (stage.Severity < selectedStage.Severity ||
                     stage.Severity == selectedStage.Severity &&
                     stage.Priority <= selectedStage.Priority))
                {
                    continue;
                }
                selectedStage = stage;
            }

            if (selectedStage == null)
            {
                ClearAlarm(
                    alarm.Id,
                    alarm.AutoAcknowledgeOnClear);
                continue;
            }

            var soundId = string.IsNullOrWhiteSpace(selectedStage.SoundId)
                ? "auto"
                : selectedStage.SoundId;
            var activeColor = string.IsNullOrWhiteSpace(
                                  selectedStage.ActiveColor) ||
                              string.Equals(
                                  selectedStage.ActiveColor,
                                  "auto",
                                  StringComparison.OrdinalIgnoreCase)
                ? ColorFor(selectedStage.Severity)
                : selectedStage.ActiveColor;

            SetAlarm(
                alarm.Id,
                string.IsNullOrWhiteSpace(selectedStage.Message)
                    ? alarm.DisplayName
                    : selectedStage.Message,
                FormatSystemAlarmDetail(alarm.Id, metrics),
                "system",
                "",
                selectedStage.Severity,
                true,
                false,
                soundId,
                activeColor,
                LastValueForSystemAlarm(alarm.Id, metrics),
                overrideId: alarm.Id,
                autoAcknowledgeOnClear:
                    alarm.AutoAcknowledgeOnClear,
                occurrenceId: selectedStage.Id,
                occurrencePriority: selectedStage.Priority,
                slotId: alarm.Id,
                operatorAction: selectedStage.OperatorAction,
                attentionPanelId: ResolveAttentionPanelId(
                    preferredPanelId: "",
                    slotId: alarm.Id));
        }
    }

    private void EvaluateSustainedVanillaAlarms()
    {
        if (!SustainedVanillaAlarmPolicy.ShouldClear(
                SustainedVanillaAlarmPolicy.HomelessLeftPrototypeId,
                m_settlementsManager.LastPopulationDiff))
        {
            return;
        }

        var clearedLegacyReconciliation = false;
        lock (m_configurationGate)
        {
            if (Configuration
                .LegacySustainedAlarmReconciliationPending)
            {
                Configuration
                    .LegacySustainedAlarmReconciliationPending = false;
                clearedLegacyReconciliation = true;
            }
        }

        var overrideId = "vanilla:" +
                         SustainedVanillaAlarmPolicy
                             .HomelessLeftPrototypeId;
        ClearAlarm(
            SustainedVanillaAlarmPolicy.AlarmKeyForOverrideId(overrideId),
            ResolveAutoAcknowledgeOnClear(overrideId));
        PruneInactiveVanillaHistory(500);
        if (clearedLegacyReconciliation)
        {
            PersistAlarmState();
        }
    }

    private bool RestoreSustainedVanillaAlarmFromHistory(
        string prototypeId)
    {
        lock (m_configurationGate)
        {
            if (!Configuration.LegacySustainedAlarmReconciliationPending)
            {
                return false;
            }
            Configuration.LegacySustainedAlarmReconciliationPending = false;
        }
        if (SustainedVanillaAlarmPolicy.ShouldClear(
                prototypeId,
                m_settlementsManager.LastPopulationDiff))
        {
            return true;
        }

        var overrideId = "vanilla:" + prototypeId;
        var key = SustainedVanillaAlarmPolicy.AlarmKeyForOverrideId(
            overrideId);
        var soundId = ResolveConfiguredSound(overrideId);
        AlarmView slotCandidate = null;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var current) &&
                current.View.IsActive)
            {
                return true;
            }

            var history = m_alarmHistory
                .Where(item =>
                    item != null &&
                    string.Equals(
                        item.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    SustainedVanillaAlarmPolicy.MatchesHistory(
                        prototypeId,
                        item.AlarmKey,
                        item.Detail))
                .OrderByDescending(item => item.Sequence)
                .FirstOrDefault();
            if (history == null)
            {
                return true;
            }

            var state = current ?? new AlarmState();
            state.Sequence = history.Sequence;
            state.View.Key = key;
            state.View.Name = history.Message;
            state.View.Detail = history.Detail;
            state.View.Source = "vanilla";
            state.View.PanelId = history.PanelId;
            state.View.Severity = history.Severity;
            state.View.IsActive = true;
            state.View.IsAcknowledged = history.IsAcknowledged;
            state.View.IsGoneUnacknowledged = false;
            state.View.IsOperatorSilenced = false;
            state.View.OperatorSilencedAtGameTick = -1;
            state.View.IsMissingSource = false;
            state.View.SoundId = soundId;
            state.View.OverrideId = overrideId;
            state.View.OccurrenceId = overrideId;
            state.View.SlotId = overrideId;
            state.View.ActiveColor = ColorFor(history.Severity);
            state.View.LastValue = 1d;
            state.View.Sequence = history.Sequence;
            m_alarms[key] = state;
            m_sequence = Math.Max(m_sequence, history.Sequence);

            history.AlarmKey = key;
            history.SetState(
                isGone: false,
                isAcknowledged: history.IsAcknowledged,
                currentGameTicks: CurrentGameTicks);
            m_alarmHistoryRevision++;
            slotCandidate = Clone(state.View, state.Sequence);
        }
        if (slotCandidate != null)
        {
            EnsurePanelSlotsForAlarm(slotCandidate);
        }
        return true;
    }

    private Dictionary<string, double> CaptureSystemMetrics()
    {
        var health = m_healthManager.HealthStats.HealthLastMonth
            .ToDouble() * 100d;
        var diseasePenalty = 0d;
        var diseaseMortalityPercent = 0d;
        var pollutionPenalty = 0d;
        var netPopulationChangePercent = 0d;
        foreach (var entry in m_healthManager.HealthStats.LastMonthRecords)
        {
            var change = entry.Change.ToDouble() * 100d;
            var categoryId = entry.Category.Id.Value;
            if (string.Equals(
                    categoryId,
                    IdsCore.HealthPointsCategories.Disease.Value,
                    StringComparison.Ordinal))
            {
                diseasePenalty += Math.Min(0, change);
            }
            if (s_pollutionHealthCategoryIds.Contains(categoryId))
            {
                pollutionPenalty += Math.Min(0, change);
            }
        }
        foreach (var entry in m_healthManager.BirthStats.LastMonthRecords)
        {
            var categoryId = entry.Category.Id.Value;
            var changePercent = entry.Change.ToDouble() * 100d;
            if (string.Equals(
                    categoryId,
                    IdsCore.BirthRateCategories.Disease.Value,
                    StringComparison.Ordinal))
            {
                diseaseMortalityPercent += Math.Max(
                    0d,
                    -changePercent);
            }
            if (!string.Equals(
                    categoryId,
                    IdsCore.BirthRateCategories.Starvation.Value,
                    StringComparison.Ordinal))
            {
                // The game applies starvation immediately and excludes it
                // from its later population-rate calculation as well.
                netPopulationChangePercent += changePercent;
            }
        }

        var population = m_settlementsManager.GetTotalPopulation();
        var populationWithoutHomeless =
            m_settlementsManager.GetTotalPopulationWithoutHomeless();
        var homelessPopulation = Math.Max(
            0,
            population - populationWithoutHomeless);
        var employablePopulation = Math.Max(
            0,
            populationWithoutHomeless -
            m_workersManager.NumberOfWorkersWithheld);
        var workers = m_workersManager.AmountOfFreeWorkersOrMissing;
        var reservePercent = SystemMetricCatalog.CalculateWorkerReservePercent(
            workers,
            employablePopulation);
        var diseaseMonthsLeft = m_healthManager.CurrentDisease.HasValue
            ? SystemMetricCatalog.CalculateEffectiveDiseaseMonths(
                m_healthManager.CurrentDiseaseMonthsLeft)
            : 0;
        var diseaseActive = diseaseMonthsLeft > 0;
        if (!diseaseActive)
        {
            diseaseMortalityPercent = 0d;
        }
        var expectedLoss =
            SystemMetricCatalog.CalculateExpectedPopulationLoss(
                population,
                netPopulationChangePercent);
        var workerBufferMonths =
            SystemMetricCatalog.CalculateWorkerBufferMonths(
                Math.Max(0, workers),
                homelessPopulation,
                expectedLoss);
        var workerSpiralMargin =
            SystemMetricCatalog.CalculateWorkerSpiralMargin(
                workerBufferMonths,
                diseaseMonthsLeft);
        var foodSpiral = SystemMetricCatalog.CalculateFoodSpiral(
            m_settlementsManager.ArePeopleStarving,
            workers,
            m_workersManager.NumberOfWorkersWithheld,
            populationWithoutHomeless,
            m_settlementsManager.AmountStarvedToDeathLastMonth);
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["health.value"] = health,
            ["health.disease_penalty"] = diseasePenalty,
            ["health.disease_mortality"] = diseaseMortalityPercent,
            ["health.pollution_penalty"] = pollutionPenalty,
            ["health.structural_value"] = health - diseasePenalty,
            ["health.expected_loss"] = expectedLoss,
            ["health.lost_last_month"] =
                m_healthManager.LostTotal.LastMonth.ToIntRounded(),
            ["health.disease_active"] = diseaseActive ? 1d : 0d,
            ["health.disease_months_left"] = diseaseMonthsLeft,
            ["health.worker_buffer_months"] = workerBufferMonths,
            ["health.worker_spiral_margin"] = workerSpiralMargin,
            ["workers.reserve_percent"] = reservePercent,
            ["workers.free_or_missing"] = workers,
            ["workers.missing"] = Math.Max(0, -workers),
            ["food.months"] = m_settlementsManager.MonthsOfFood,
            ["food.starving"] =
                m_settlementsManager.ArePeopleStarving ? 1d : 0d,
            ["food.starved_last_month"] =
                m_settlementsManager.AmountStarvedToDeathLastMonth,
            ["food.spiral"] = foodSpiral ? 1d : 0d,
            ["population.net_change_percent"] =
                netPopulationChangePercent,
            [SustainedVanillaAlarmPolicy.PopulationDeltaMetricId] =
                m_settlementsManager.LastPopulationDiff,
            ["population.total"] = population,
        };

        foreach (var stats in m_productsManager.ProductStats)
        {
            if (!IsSelectableGlobalProduct(stats?.Product))
            {
                continue;
            }
            var productId = stats.Product.Id.Value;
            var stored = (double)stats.StoredQuantityTotal.Value;
            var capacity = (double)stats.StorageCapacity.Value;
            metrics[SystemMetricCatalog.ProductStoredId(productId)] = stored;
            metrics[SystemMetricCatalog.ProductCapacityId(productId)] = capacity;
            metrics[SystemMetricCatalog.ProductFillId(productId)] =
                SystemMetricCatalog.CalculateFillPercent(stored, capacity);
        }

        foreach (var buffer in m_maintenanceManager.MaintenanceBuffers)
        {
            if (buffer == null || !buffer.ShouldShowInUi ||
                buffer.Product == null)
            {
                continue;
            }
            var productId = buffer.Product.Id.Value;
            var quantity = (double)buffer.Quantity.Value;
            var capacity = (double)buffer.Capacity.Value;
            metrics[SystemMetricCatalog.MaintenanceQuantityId(productId)] =
                quantity;
            metrics[SystemMetricCatalog.MaintenanceCapacityId(productId)] =
                capacity;
            metrics[SystemMetricCatalog.MaintenanceFillId(productId)] =
                SystemMetricCatalog.CalculateFillPercent(quantity, capacity);
            metrics[SystemMetricCatalog.MaintenanceDeltaId(productId)] =
                buffer.DeltaLastMonth.Value.ToDouble();
            metrics[SystemMetricCatalog.MaintenanceNeededId(productId)] =
                buffer.MonthlyNeededMaintenance.Value.ToDouble();
            metrics[SystemMetricCatalog.MaintenanceNeededMaxId(productId)] =
                buffer.MonthlyNeededMaintenanceMax.Value.ToDouble();
        }
        lock (m_systemMetricsGate)
        {
            m_lastSystemMetrics = new Dictionary<string, double>(
                metrics,
                StringComparer.Ordinal);
        }
        return metrics;
    }

    private static bool IsSelectableGlobalProduct(ProductProto product)
    {
        return product != null &&
               product.IsNotPhantom &&
               product.IsUnlockedAndAvailable &&
               product.IsStorable &&
               !product.IsExcludedFromStats;
    }

    private static string ProductDisplayName(ProductProto product)
    {
        if (product == null)
        {
            return UnmaText.Get(
                "system_metric.product.unknown",
                "Unknown product");
        }
        var translated = product.Strings.Name.TranslatedString;
        return string.IsNullOrWhiteSpace(translated)
            ? product.Id.Value
            : translated;
    }

    private static string FormatSystemAlarmDetail(
        string alarmId,
        IReadOnlyDictionary<string, double> metrics)
    {
        if (string.Equals(alarmId, "system:health", StringComparison.Ordinal))
        {
            return UnmaText.Format(
                "runtime.system.health_detail",
                "Health {0} (neutral 10) · disease {1} · disease mortality " +
                "{2} % · pollution/waste {3} · worker reserve {4} % · " +
                "expected net loss {5}/month",
                Metric(metrics, "health.value"),
                Metric(metrics, "health.disease_penalty"),
                Metric(metrics, "health.disease_mortality"),
                Metric(metrics, "health.pollution_penalty"),
                Metric(metrics, "workers.reserve_percent"),
                Metric(metrics, "health.expected_loss"));
        }
        if (string.Equals(alarmId, "system:food", StringComparison.Ordinal))
        {
            return UnmaText.Format(
                "runtime.system.food_detail",
                "Food {0} months · starving {1} · starved {2}",
                Metric(metrics, "food.months"),
                MetricValue(metrics, "food.starving") >= 1d
                    ? UnmaText.Get("common.yes", "yes")
                    : UnmaText.Get("common.no", "no"),
                Metric(metrics, "food.starved_last_month"));
        }
        if (string.Equals(alarmId, "system:workers", StringComparison.Ordinal))
        {
            return UnmaText.Format(
                "runtime.system.workers_detail",
                "Worker reserve {0} % · free/missing {1}",
                Metric(metrics, "workers.reserve_percent"),
                Metric(metrics, "workers.free_or_missing"));
        }
        return UnmaText.Get(
            "alarm.detail.system_notification",
            "System notification");
    }

    private static double LastValueForSystemAlarm(
        string alarmId,
        IReadOnlyDictionary<string, double> metrics)
    {
        if (string.Equals(alarmId, "system:food", StringComparison.Ordinal))
        {
            return MetricValue(metrics, "food.months");
        }
        if (string.Equals(alarmId, "system:workers", StringComparison.Ordinal))
        {
            return MetricValue(metrics, "workers.reserve_percent");
        }
        return MetricValue(metrics, "health.value");
    }

    private static string Metric(
        IReadOnlyDictionary<string, double> metrics,
        string metricId)
    {
        return MetricValue(metrics, metricId).ToString(
            "0.##",
            CultureInfo.CurrentCulture);
    }

    private static double MetricValue(
        IReadOnlyDictionary<string, double> metrics,
        string metricId)
    {
        return metrics.TryGetValue(metricId, out var value) ? value : 0d;
    }

    private IReadOnlyDictionary<string, double> CaptureInstrumentValues()
    {
        InstrumentDefinition[] instruments;
        Dictionary<string, int> requiredTrendWindows;
        lock (m_configurationGate)
        {
            instruments = Configuration.Instruments
                .Where(instrument => instrument != null)
                .Select(CloneInstrumentDefinition)
                .ToArray();
            requiredTrendWindows = Configuration.Rules
                .Where(rule => rule?.Conditions != null)
                .SelectMany(rule => rule.Conditions)
                .Where(condition =>
                    condition != null &&
                    !string.IsNullOrWhiteSpace(condition.InstrumentId) &&
                    condition.TrendMode != InstrumentTrendMode.None)
                .GroupBy(
                    condition => condition.InstrumentId,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Max(condition =>
                        GameTimeWindowPolicy.ToSimTicks(
                            condition.WindowAmount,
                            condition.WindowUnit)),
                    StringComparer.Ordinal);
        }

        var timestampSeconds = (double)m_calendar.RealTime.Ticks;
        var captured = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var instrument in instruments)
        {
            if (TryReadInstrumentValue(
                    instrument,
                    out var value,
                    out _))
            {
                captured[instrument.Id] = value;
            }
        }

        var activeIds = new HashSet<string>(
            instruments.Select(instrument => instrument.Id),
            StringComparer.Ordinal);
        lock (m_instrumentValuesGate)
        {
            var clockRolledBack = DidInstrumentClockRollBack(
                timestampSeconds,
                m_lastInstrumentCaptureTimestampTicks);
            m_lastInstrumentValues.Clear();
            m_instrumentForecastRanges.Clear();
            if (clockRolledBack)
            {
                // Any backwards jump starts a new session-history epoch,
                // even when it lands between daily samples or exactly on the
                // latest retained sample. The last capture timestamp is the
                // authoritative clock edge for detecting that transition.
                m_instrumentHistory.Clear();
            }
            m_lastInstrumentCaptureTimestampTicks = timestampSeconds;
            foreach (var instrument in instruments)
            {
                m_instrumentForecastRanges[instrument.Id] =
                    new InstrumentForecastRange(
                        instrument.Minimum,
                        instrument.Maximum);
                var signature = InstrumentValuePolicy.DefinitionSignature(
                    instrument);
                if (!m_instrumentSignatures.TryGetValue(
                        instrument.Id,
                        out var previousSignature) ||
                    !string.Equals(
                        previousSignature,
                        signature,
                        StringComparison.Ordinal))
                {
                    m_instrumentHistory.Remove(instrument.Id);
                }
                m_instrumentSignatures[instrument.Id] = signature;
            }

            // A missing source splits the history into two independent
            // epochs. Clearing here prevents the first recovered value from
            // being compared with a pre-outage baseline.
            foreach (var unavailableId in activeIds.Where(id =>
                         !captured.ContainsKey(id)))
            {
                m_instrumentHistory.Remove(unavailableId);
            }

            foreach (var pair in captured)
            {
                m_lastInstrumentValues[pair.Key] = pair.Value;
                if (!m_instrumentHistory.TryGetValue(
                        pair.Key,
                        out var history))
                {
                    history = new List<InstrumentValueSample>();
                    m_instrumentHistory[pair.Key] = history;
                }
                else if (history.Count > 0 &&
                         history[history.Count - 1].TimestampSeconds >
                         timestampSeconds)
                {
                    // Loading or rewinding to an earlier game tick starts a
                    // new in-memory epoch. Samples from the previous future
                    // would otherwise distort statistics until time caught
                    // up again.
                    history.Clear();
                }
                if (history.Count == 0 ||
                    timestampSeconds -
                    history[history.Count - 1].TimestampSeconds >=
                    InstrumentHistorySampleIntervalTicks)
                {
                    history.Add(new InstrumentValueSample(
                        timestampSeconds,
                        pair.Value));
                }

                var instrument = instruments.First(candidate =>
                    string.Equals(
                        candidate.Id,
                        pair.Key,
                        StringComparison.Ordinal));
                var retentionSeconds = GameTimeWindowPolicy.ToSimTicks(
                    instrument.HistoryDurationAmount,
                    instrument.HistoryDurationUnit);
                if (requiredTrendWindows.TryGetValue(
                        pair.Key,
                        out var requiredWindow))
                {
                    retentionSeconds = Math.Max(
                        retentionSeconds,
                        requiredWindow +
                        GameTimeWindowPolicy.SimTicksPerDay * 2);
                }
                var cutoff = timestampSeconds - retentionSeconds;
                var removeCount = 0;
                while (removeCount + 1 < history.Count &&
                       history[removeCount + 1].TimestampSeconds < cutoff)
                {
                    removeCount++;
                }
                if (removeCount > 0)
                {
                    history.RemoveRange(0, removeCount);
                }
                if (history.Count > MaximumInstrumentHistorySamples)
                {
                    history.RemoveRange(
                        0,
                        history.Count - MaximumInstrumentHistorySamples);
                }
            }

            foreach (var staleId in m_instrumentHistory.Keys
                         .Where(id => !activeIds.Contains(id))
                         .ToArray())
            {
                m_instrumentHistory.Remove(staleId);
            }
            foreach (var staleId in m_instrumentSignatures.Keys
                         .Where(id => !activeIds.Contains(id))
                         .ToArray())
            {
                m_instrumentSignatures.Remove(staleId);
            }
        }
        return captured;
    }

    private void EvaluateCustomRules(
        IReadOnlyDictionary<string, double> globalMetrics,
        IReadOnlyDictionary<string, double> instrumentValues)
    {
        AlarmRuleDefinition[] rules;
        lock (m_configurationGate)
        {
            rules = Configuration.Rules
                .Select(CloneRuleForEvaluation)
                .ToArray();
        }

        var missingStaticEntityIds =
            ObserveMissingStaticRuleEntities(rules);
        if (missingStaticEntityIds.Length > 0 &&
            TryRemoveRulesReferencingEntities(
                missingStaticEntityIds,
                out var removedCount) &&
            removedCount > 0)
        {
            lock (m_configurationGate)
            {
                rules = Configuration.Rules
                    .Select(CloneRuleForEvaluation)
                    .ToArray();
            }
        }

        foreach (var rule in rules)
        {
            EvaluateCustomRule(rule, globalMetrics, instrumentValues);
        }
    }

    private void EvaluateCustomRule(
        AlarmRuleDefinition rule,
        IReadOnlyDictionary<string, double> globalMetrics,
        IReadOnlyDictionary<string, double> instrumentValues)
    {
        if (!rule.Enabled)
        {
            ForceNormal("rule:" + rule.Id);
            return;
        }

        EnsureRuleTimingDefinition(rule);

        var values = new List<bool>(rule.Conditions.Count);
        var details = new List<string>(rule.Conditions.Count);
        var missingSource = false;
        var lastValue = 0d;

        void AddUnavailableCondition(int conditionIndex)
        {
            SetRuleConditionLatch(rule.Id, conditionIndex, false);
            values.Add(false);
        }

        for (var conditionIndex = 0;
             conditionIndex < rule.Conditions.Count;
             conditionIndex++)
        {
            var condition = rule.Conditions[conditionIndex];
            var sustainedStateKey = rule.Id + ":" + conditionIndex;
            if (!string.IsNullOrWhiteSpace(condition.InstrumentId))
            {
                var label = string.IsNullOrWhiteSpace(condition.MetricLabel)
                    ? condition.InstrumentId
                    : condition.MetricLabel;
                if (instrumentValues == null ||
                    !instrumentValues.TryGetValue(
                        condition.InstrumentId,
                        out var instrumentValue))
                {
                    m_sustainedConditionStates.Remove(sustainedStateKey);
                    missingSource = true;
                    AddUnavailableCondition(conditionIndex);
                    details.Add(UnmaText.Format(
                        "runtime.condition.instrument_missing",
                        "{0}: calculated metric is missing",
                        label));
                    continue;
                }

                if (condition.TrendMode != InstrumentTrendMode.None)
                {
                    var windowTicks = GameTimeWindowPolicy.ToSimTicks(
                        condition.WindowAmount,
                        condition.WindowUnit);
                    var windowLabel = FormatGameTimeWindow(
                        condition.WindowAmount,
                        condition.WindowUnit);
                    if (condition.TrendMode ==
                        InstrumentTrendMode.SustainComparison)
                    {
                        var comparisonMatches = EvaluateRuleCondition(
                            rule.Id,
                            conditionIndex,
                            instrumentValue,
                            condition.Comparison,
                            condition.Threshold,
                            condition.Hysteresis);
                        var sustained = EvaluateSustainedCondition(
                            sustainedStateKey,
                            condition,
                            windowTicks,
                            comparisonMatches);
                        lastValue = instrumentValue;
                        values.Add(sustained);
                        details.Add(UnmaText.Format(
                            "runtime.condition.instrument_sustained",
                            "{0} {1} {2:0.###} for {3} " +
                            "(actual {4:0.###})",
                            label,
                            OperatorText(condition.Comparison),
                            condition.Threshold,
                            windowLabel,
                            instrumentValue));
                        continue;
                    }
                    m_sustainedConditionStates.Remove(sustainedStateKey);
                    if (!TryEvaluateInstrumentTrend(
                            condition.InstrumentId,
                            condition.TrendMode,
                            windowTicks,
                            out var change))
                    {
                        missingSource = true;
                        AddUnavailableCondition(conditionIndex);
                        details.Add(UnmaText.Format(
                            "runtime.condition.instrument_history_incomplete",
                            "{0}: history for {1} is not complete yet",
                            label,
                            windowLabel));
                        continue;
                    }

                    lastValue = change;
                    var trendMatches = InstrumentValuePolicy.IsTrendTriggered(
                        change,
                        condition.DeltaThreshold);
                    SetRuleConditionLatch(
                        rule.Id,
                        conditionIndex,
                        trendMatches);
                    values.Add(trendMatches);
                    var isPercent = condition.TrendMode ==
                                    InstrumentTrendMode.DecreasePercent ||
                                    condition.TrendMode ==
                                    InstrumentTrendMode.IncreasePercent;
                    var isIncrease = condition.TrendMode ==
                                     InstrumentTrendMode.IncreaseAbsolute ||
                                     condition.TrendMode ==
                                     InstrumentTrendMode.IncreasePercent;
                    details.Add(UnmaText.Format(
                        isIncrease
                            ? "runtime.condition.instrument_increase"
                            : "runtime.condition.instrument_decrease",
                        isIncrease
                            ? "{0} · increase {1:0.###}{2} / {3} " +
                              "(threshold ≥ {4:0.###})"
                            : "{0} · decrease {1:0.###}{2} / {3} " +
                              "(threshold ≥ {4:0.###})",
                        label,
                        change,
                        isPercent ? " %" : "",
                        windowLabel,
                        condition.DeltaThreshold));
                    continue;
                }

                m_sustainedConditionStates.Remove(sustainedStateKey);
                lastValue = instrumentValue;
                values.Add(EvaluateRuleCondition(
                    rule.Id,
                    conditionIndex,
                    instrumentValue,
                    condition.Comparison,
                    condition.Threshold,
                    condition.Hysteresis));
                details.Add(UnmaText.Format(
                    "runtime.condition.comparison",
                    "{0} {1} {2:0.###} (actual {3:0.###})",
                    label,
                    OperatorText(condition.Comparison),
                    condition.Threshold,
                    instrumentValue));
                continue;
            }

            m_sustainedConditionStates.Remove(sustainedStateKey);
            if (SystemMetricCatalog.TryParseRulePath(
                    condition.MetricPath,
                    out var globalMetricId))
            {
                if (!globalMetrics.TryGetValue(
                        globalMetricId,
                        out var globalActual))
                {
                    missingSource = true;
                    AddUnavailableCondition(conditionIndex);
                    details.Add(UnmaText.Format(
                        "runtime.condition.metric_missing",
                        "{0}: metric is missing",
                        condition.MetricLabel));
                    continue;
                }

                var globalReference = 0d;
                if (condition.ValueMode ==
                        ConditionValueMode.PercentOfReference &&
                    (!SystemMetricCatalog.TryParseRulePath(
                         condition.ReferenceMetricPath,
                         out var referenceMetricId) ||
                     !globalMetrics.TryGetValue(
                         referenceMetricId,
                         out globalReference)))
                {
                    missingSource = true;
                    AddUnavailableCondition(conditionIndex);
                    details.Add(UnmaText.Format(
                        "runtime.condition.reference_metric_missing",
                        "{0}: reference metric is missing",
                        condition.MetricLabel));
                    continue;
                }

                if (!AlarmEvaluation.TryCalculateComparable(
                        globalActual,
                        condition.ValueMode,
                        globalReference,
                        out var globalComparable))
                {
                    missingSource = true;
                    AddUnavailableCondition(conditionIndex);
                    details.Add(UnmaText.Format(
                        "runtime.condition.reference_not_calculable",
                        "{0}: reference cannot be calculated",
                        condition.MetricLabel));
                    continue;
                }

                lastValue = globalComparable;
                values.Add(EvaluateRuleCondition(
                    rule.Id,
                    conditionIndex,
                    globalComparable,
                    condition.Comparison,
                    condition.Threshold,
                    condition.Hysteresis));
                details.Add(UnmaText.Format(
                    "runtime.condition.global_comparison",
                    "GLOBAL · {0} {1} {2:0.###} (actual {3:0.###})",
                    condition.MetricLabel,
                    OperatorText(condition.Comparison),
                    condition.Threshold,
                    globalComparable));
                continue;
            }

            var option = m_entitiesManager.GetEntity(
                new EntityId(condition.EntityId));
            if (option.IsNone)
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(
                    condition.EntityTitle + ": " +
                    UnmaText.Get("runtime.source_missing"));
                continue;
            }

            var entity = option.Value;
            var actualEntityType = entity.GetType().FullName ??
                                   entity.GetType().Name;
            if (!string.IsNullOrWhiteSpace(condition.EntityType) &&
                !string.Equals(
                    condition.EntityType,
                    actualEntityType,
                    StringComparison.Ordinal))
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.entity_type_mismatch",
                    "{0}: wrong entity type",
                    condition.EntityTitle));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(condition.EntityPrototypeId) &&
                !string.Equals(
                    condition.EntityPrototypeId,
                    entity.Prototype.Id.Value,
                    StringComparison.Ordinal))
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.entity_prototype_mismatch",
                    "{0}: wrong prototype",
                    condition.EntityTitle));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(condition.ExpectedProductId) &&
                !string.Equals(
                    condition.ExpectedProductId,
                    EntityMetricCatalog.TryGetStoredProductId(entity),
                    StringComparison.Ordinal))
            {
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.product_mismatch",
                    "{0}: different product",
                    condition.EntityTitle));
                continue;
            }

            if (!EntityMetricCatalog.TryRead(
                    entity,
                    condition.MetricPath,
                    out var actual))
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.metric_missing",
                    "{0}: metric is missing",
                    condition.EntityTitle));
                continue;
            }

            var reference = 0d;
            if (condition.ValueMode ==
                ConditionValueMode.PercentOfReference &&
                (string.IsNullOrWhiteSpace(
                     condition.ReferenceMetricPath) ||
                 !EntityMetricCatalog.TryRead(
                     entity,
                     condition.ReferenceMetricPath,
                     out reference)))
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.reference_metric_missing",
                    "{0}: reference metric is missing",
                    condition.EntityTitle));
                continue;
            }

            if (!AlarmEvaluation.TryCalculateComparable(
                    actual,
                    condition.ValueMode,
                    reference,
                    out var comparable))
            {
                missingSource = true;
                AddUnavailableCondition(conditionIndex);
                details.Add(UnmaText.Format(
                    "runtime.condition.reference_not_calculable_values",
                    "{0} · {1}: reference cannot be calculated " +
                    "(actual {2:0.###}, reference {3:0.###})",
                    condition.EntityTitle,
                    condition.MetricLabel,
                    actual,
                    reference));
                continue;
            }

            lastValue = comparable;
            var matches = EvaluateRuleCondition(
                rule.Id,
                conditionIndex,
                comparable,
                condition.Comparison,
                condition.Threshold,
                condition.Hysteresis);
            values.Add(matches);
            if (condition.ValueMode ==
                ConditionValueMode.PercentOfReference)
            {
                var referenceLabel = string.IsNullOrWhiteSpace(
                    condition.ReferenceMetricLabel)
                    ? condition.ReferenceMetricPath
                    : condition.ReferenceMetricLabel;
                details.Add(UnmaText.Format(
                    "runtime.condition.percent_comparison",
                    "{0} · {1} % of {2} {3} {4:0.###} " +
                    "(actual {5:0.###} %; {6:0.###} / {7:0.###})",
                    condition.EntityTitle,
                    condition.MetricLabel,
                    referenceLabel,
                    OperatorText(condition.Comparison),
                    condition.Threshold,
                    comparable,
                    actual,
                    reference));
            }
            else
            {
                details.Add(UnmaText.Format(
                    "runtime.condition.entity_comparison",
                    "{0} · {1} {2} {3:0.###} (actual {4:0.###})",
                    condition.EntityTitle,
                    condition.MetricLabel,
                    OperatorText(condition.Comparison),
                    condition.Threshold,
                    actual));
            }
        }

        var currentGameTick = CurrentGameTick;
        var isActive = AdvanceRuleTiming(
            rule,
            AlarmEvaluation.Combine(values, rule.Logic),
            currentGameTick,
            out var escalation);
        var alarmKey = "rule:" + rule.Id;
        if (!isActive)
        {
            ClearAlarm(
                alarmKey,
                rule.AutoAcknowledgeOnClear);
            return;
        }
        SetAlarm(
            alarmKey,
            rule.Name,
            string.Join(
                rule.Logic == AlarmLogic.All ? UnmaText.Get("auto.a3f10eb98ea4") : UnmaText.Get("auto.5f15b34155a9"),
                details),
            "custom",
            rule.PanelId,
            escalation.Severity,
            isActive,
            missingSource,
            escalation.SoundId,
            escalation.IsEscalated
                ? ColorFor(escalation.Severity)
                : rule.ActiveColor,
            lastValue,
            autoAcknowledgeOnClear:
                rule.AutoAcknowledgeOnClear,
            occurrenceId: AlarmEscalationPolicy.GetOccurrenceId(
                rule.Id,
                escalation.IsEscalated),
            occurrencePriority: escalation.IsEscalated
                ? AlarmEscalationPolicy.EscalatedOccurrencePriority
                : AlarmEscalationPolicy.BaseOccurrencePriority,
            slotId: "rule:" + rule.Id,
            operatorAction: escalation.OperatorAction);
    }

    private bool EvaluateSustainedCondition(
        string stateKey,
        ConditionDefinition condition,
        int windowTicks,
        bool comparisonMatches)
    {
        if (!comparisonMatches)
        {
            m_sustainedConditionStates.Remove(stateKey);
            return false;
        }

        var signature = condition.InstrumentId + "|" +
                        (int)condition.Comparison + "|" +
                        condition.Threshold.ToString(
                            "R",
                            CultureInfo.InvariantCulture) + "|" +
                        condition.Hysteresis.ToString(
                            "R",
                            CultureInfo.InvariantCulture) + "|" +
                        windowTicks;
        var currentGameTick = CurrentGameTick;
        if (!m_sustainedConditionStates.TryGetValue(
                stateKey,
                out var state) ||
            !string.Equals(
                state.Signature,
                signature,
                StringComparison.Ordinal) ||
            currentGameTick < state.StartedAtTick)
        {
            m_sustainedConditionStates[stateKey] =
                new SustainedConditionState
                {
                    Signature = signature,
                    StartedAtTick = currentGameTick,
                };
            return false;
        }
        return currentGameTick - state.StartedAtTick >= windowTicks;
    }

    private void RemoveSustainedStatesForRule(string ruleId)
    {
        var prefix = (ruleId ?? "") + ":";
        foreach (var key in m_sustainedConditionStates.Keys
                     .Where(key => key.StartsWith(
                         prefix,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            m_sustainedConditionStates.Remove(key);
        }
    }

    private void EvaluateExternalAlarms()
    {
        BeginAlarmPersistenceBatch();
        try
        {
            EvaluateExternalAlarmsCore();
        }
        finally
        {
            if (EndAlarmPersistenceBatch())
            {
                PersistAlarmState();
            }
        }
    }

    private void EvaluateExternalAlarmsCore()
    {
        ExternalAlarmTemplateSnapshot[] jsonTemplates;
        lock (m_externalDefinitionsGate)
        {
            jsonTemplates = m_externalDefinitions?.AlarmTemplates.ToArray() ??
                            Array.Empty<ExternalAlarmTemplateSnapshot>();
        }
        var api = UnmaApi.GetSnapshot();
        if (api.Revision != m_registeredExternalApiRevision)
        {
            var namespaceSignature = ExternalNamespaceSignature(api);
            if (!string.Equals(
                    namespaceSignature,
                    m_registeredExternalNamespaceSignature,
                    StringComparison.Ordinal))
            {
                ExternalDefinitionLoadResult definitions;
                lock (m_externalDefinitionsGate)
                {
                    definitions = m_externalDefinitions;
                }
                RegisterExternalLocalizationNamespaces(definitions, api);
            }
            else
            {
                m_registeredExternalApiRevision = api.Revision;
            }
        }
        var templates = new Dictionary<string,
            ExternalAlarmTemplateSnapshot>(StringComparer.Ordinal);
        foreach (var template in jsonTemplates)
        {
            templates[ExternalTemplateIdentity(template)] = template;
        }
        var collisionStamp = api.Revision.ToString(
                                 CultureInfo.InvariantCulture) + ":" +
                             m_externalDefinitionRevision.ToString(
                                 CultureInfo.InvariantCulture);
        var logCollisions = !string.Equals(
            collisionStamp,
            m_lastExternalCollisionStamp,
            StringComparison.Ordinal);
        foreach (var template in api.AlarmTemplates)
        {
            // A declarative file owns the same provider/id namespace as the
            // compiled API. The deterministic JSON definition wins instead
            // of silently changing semantics with mod load order.
            var identity = ExternalTemplateIdentity(template);
            if (!templates.ContainsKey(identity))
            {
                templates.Add(identity, template);
            }
            else if (logCollisions)
            {
                Log.Warning(
                    UnmaText.Get("auto.ba86c9b0922c") + template.OwnerModId +
                    ":" + template.Id +
                    UnmaText.Get("auto.1e204fc5991c"));
            }
        }
        m_lastExternalCollisionStamp = collisionStamp;

        IEntity[] entities = Array.Empty<IEntity>();
        if (templates.Count > 0)
        {
            try
            {
                entities = m_entitiesManager.Entities
                    .AsEnumerable()
                    .Where(entity => entity != null)
                    .ToArray();
            }
            catch (Exception exception)
            {
                Log.Warning(
                    UnmaText.Get("auto.2055eaefc4f5") +
                    UnmaText.Get("auto.a5aa7c9bef16") + exception.Message);
            }
        }
        var liveEntitiesByPrototype = IndexExternalEntities(
            entities,
            includeDestroyed: false);
        var templatesUsingDestroyedEntities = templates.Values.Any(
            UsesDestroyedEntityMetric);
        var allEntitiesByPrototype = templatesUsingDestroyedEntities
            ? IndexExternalEntities(entities, includeDestroyed: true)
            : liveEntitiesByPrototype;

        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        var currentAutoAcknowledge = new Dictionary<string, bool>(
            StringComparer.Ordinal);
        foreach (var template in templates.Values
                     .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            EvaluateExternalTemplate(
                template,
                api,
                UsesDestroyedEntityMetric(template)
                    ? allEntitiesByPrototype
                    : liveEntitiesByPrototype,
                currentKeys,
                currentAutoAcknowledge);
        }
        EvaluatePushedExternalStates(
            api,
            currentKeys,
            currentAutoAcknowledge);

        var staleKeys = m_previousExternalKeys
            .Where(key => !currentKeys.Contains(key))
            .ToArray();
        foreach (var staleKey in staleKeys)
        {
            var rememberedAutoAcknowledge =
                m_externalAutoAcknowledgeByKey.TryGetValue(
                    staleKey,
                    out var remembered)
                    ? remembered
                    : ResolveExternalAutoAcknowledgeForKey(staleKey);
            string overrideId;
            lock (m_gate)
            {
                overrideId = m_alarms.TryGetValue(staleKey, out var state)
                    ? state.View.OverrideId
                    : "";
            }
            var autoAcknowledge = string.IsNullOrWhiteSpace(overrideId)
                ? rememberedAutoAcknowledge
                : ResolveAutoAcknowledgeOnClear(
                    overrideId,
                    rememberedAutoAcknowledge);
            ClearAlarm(
                staleKey,
                autoAcknowledgeOnClear: autoAcknowledge,
                persist: false);
            m_retiredExternalKeys.Add(staleKey);
        }
        var prunedStaleAlarms = PruneRetiredExternalAlarms();
        if (staleKeys.Length > 0 || prunedStaleAlarms)
        {
            PersistAlarmState();
        }

        m_previousExternalKeys.Clear();
        foreach (var key in currentKeys)
        {
            m_previousExternalKeys.Add(key);
            m_retiredExternalKeys.Remove(key);
        }
        m_externalAutoAcknowledgeByKey.Clear();
        foreach (var pair in currentAutoAcknowledge)
        {
            m_externalAutoAcknowledgeByKey.Add(pair.Key, pair.Value);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<IEntity>>
        IndexExternalEntities(
            IEnumerable<IEntity> entities,
            bool includeDestroyed)
    {
        var index = new Dictionary<string, List<IEntity>>(
            StringComparer.Ordinal);
        foreach (var entity in entities ?? Enumerable.Empty<IEntity>())
        {
            try
            {
                if (entity == null ||
                    !includeDestroyed && entity.IsDestroyed)
                {
                    continue;
                }

                var prototypeId = entity.Prototype.Id.Value;
                if (string.IsNullOrWhiteSpace(prototypeId))
                {
                    continue;
                }
                if (!index.TryGetValue(prototypeId, out var matching))
                {
                    matching = new List<IEntity>();
                    index.Add(prototypeId, matching);
                }
                matching.Add(entity);
            }
            catch
            {
                // A disposed tombstone must not abort another provider's
                // complete alarm evaluation.
            }
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IEntity>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static bool UsesDestroyedEntityMetric(
        ExternalAlarmTemplateSnapshot template)
    {
        return template?.Conditions != null &&
               template.Conditions.Any(condition =>
                   string.Equals(
                       condition.Metric,
                       "$entity.destroyed",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       condition.ReferenceMetric,
                       "$entity.destroyed",
                       StringComparison.Ordinal));
    }

    private bool PruneRetiredExternalAlarms()
    {
        var removed = false;
        lock (m_gate)
        {
            foreach (var key in m_retiredExternalKeys.ToArray())
            {
                if (m_alarms.TryGetValue(key, out var state) &&
                    !state.View.IsLatched)
                {
                    m_alarms.Remove(key);
                    m_retiredExternalKeys.Remove(key);
                    removed = true;
                }
                else if (!m_alarms.ContainsKey(key))
                {
                    m_retiredExternalKeys.Remove(key);
                }
            }
        }
        return removed;
    }

    private void EvaluateExternalTemplate(
        ExternalAlarmTemplateSnapshot template,
        ExternalRegistrySnapshot api,
        IReadOnlyDictionary<string, IReadOnlyList<IEntity>>
            entitiesByPrototype,
        ISet<string> currentKeys,
        IDictionary<string, bool> currentAutoAcknowledge)
    {
        var matchingEntities = template.PrototypeIds
            .SelectMany(prototypeId =>
                entitiesByPrototype.TryGetValue(
                    prototypeId,
                    out var matching)
                    ? matching
                    : Array.Empty<IEntity>())
            .ToArray();
        var overrideId = ExternalAlarmId(
            template.OwnerModId,
            template.Id);
        var panelId = ResolveExternalPanelId(template.PanelId);
        var severity = ParseExternalSeverity(template.Severity);
        var message = UnmaText.Resolve(
            template.MessageKey,
            string.IsNullOrWhiteSpace(template.MessageFallback)
                ? template.Id
                : template.MessageFallback);
        var detailPrefix = UnmaText.Resolve(
            template.DetailKey,
            template.DetailFallback);
        var soundId = ResolveConfiguredSound(
            overrideId,
            template.SoundId);
        var autoAcknowledge = ResolveAutoAcknowledgeOnClear(
            overrideId,
            template.AutoAcknowledgeOnClear);
        var activeColor = string.IsNullOrWhiteSpace(template.ActiveColor) ||
                          string.Equals(
                              template.ActiveColor,
                              "auto",
                              StringComparison.OrdinalIgnoreCase)
            ? ColorFor(severity)
            : template.ActiveColor;

        if (string.Equals(
                template.Scope,
                "per_entity",
                StringComparison.Ordinal))
        {
            foreach (var entity in matchingEntities)
            {
                var evaluation = EvaluateExternalEntity(
                    template,
                    api,
                    entity);
                var key = overrideId + ":entity:" + entity.Id.Value;
                currentKeys.Add(key);
                currentAutoAcknowledge[key] = autoAcknowledge;
                SetAlarm(
                    key,
                    message,
                    JoinExternalDetail(
                        detailPrefix,
                        EntityMetricCatalog.GetEntityTitle(entity),
                        evaluation.Detail),
                    "external",
                    panelId,
                    severity,
                    evaluation.IsActive,
                    evaluation.IsMissingSource,
                    soundId,
                    activeColor,
                    evaluation.LastValue,
                    overrideId: overrideId,
                    autoAcknowledgeOnClear: autoAcknowledge,
                    occurrenceId: template.Id + ":" + entity.Id.Value,
                    slotId: key);
            }
            return;
        }

        var evaluations = matchingEntities
            .Select(entity => new
            {
                Entity = entity,
                Result = EvaluateExternalEntity(template, api, entity),
            })
            .ToArray();
        var active = evaluations
            .Where(item => item.Result.IsActive)
            .ToArray();
        var representative = active.FirstOrDefault() ??
                             evaluations.FirstOrDefault();
        var aggregateDetail = matchingEntities.Length == 0
            ? UnmaText.Get(
                "external.no_matching_entity",
                UnmaText.Get("auto.89833959965c"))
            : active.Length + "/" + matchingEntities.Length + " " +
              UnmaText.Get("external.active", "aktiv");
        if (representative != null)
        {
            aggregateDetail = JoinExternalDetail(
                aggregateDetail,
                EntityMetricCatalog.GetEntityTitle(representative.Entity),
                representative.Result.Detail);
        }
        aggregateDetail = JoinExternalDetail(
            detailPrefix,
            aggregateDetail);

        currentKeys.Add(overrideId);
        currentAutoAcknowledge[overrideId] = autoAcknowledge;
        SetAlarm(
            overrideId,
            message,
            aggregateDetail,
            "external",
            panelId,
            severity,
            active.Length > 0,
            matchingEntities.Length == 0 ||
            evaluations.All(item => item.Result.IsMissingSource),
            soundId,
            activeColor,
            representative?.Result.LastValue ?? 0d,
            overrideId: overrideId,
            autoAcknowledgeOnClear: autoAcknowledge,
            occurrenceId: template.Id,
            slotId: overrideId);
    }

    private static ExternalEntityEvaluation EvaluateExternalEntity(
        ExternalAlarmTemplateSnapshot template,
        ExternalRegistrySnapshot api,
        IEntity entity)
    {
        var values = new List<bool>(template.Conditions.Count);
        var details = new List<string>(template.Conditions.Count);
        var result = new ExternalEntityEvaluation();
        foreach (var condition in template.Conditions)
        {
            var label = UnmaText.Resolve(
                condition.LabelKey,
                string.IsNullOrWhiteSpace(condition.LabelFallback)
                    ? condition.Metric
                    : condition.LabelFallback);
            if (!TryReadExternalMetric(
                    api,
                    template.OwnerModId,
                    entity,
                    condition.Metric,
                    out var actual))
            {
                values.Add(false);
                result.IsMissingSource = true;
                details.Add(UnmaText.Format(
                    "runtime.condition.metric_missing",
                    "{0}: metric is missing",
                    label));
                continue;
            }

            var reference = 0d;
            var isPercent = string.Equals(
                condition.ValueMode,
                "percent_of_reference",
                StringComparison.Ordinal);
            if (isPercent &&
                !TryReadExternalMetric(
                    api,
                    template.OwnerModId,
                    entity,
                    condition.ReferenceMetric,
                    out reference))
            {
                values.Add(false);
                result.IsMissingSource = true;
                details.Add(UnmaText.Format(
                    "runtime.condition.reference_metric_missing",
                    "{0}: reference metric is missing",
                    label));
                continue;
            }

            if (!AlarmEvaluation.TryCalculateComparable(
                    actual,
                    isPercent
                        ? ConditionValueMode.PercentOfReference
                        : ConditionValueMode.Absolute,
                    reference,
                    out var comparable))
            {
                values.Add(false);
                result.IsMissingSource = true;
                details.Add(UnmaText.Format(
                    "runtime.condition.reference_not_calculable",
                    "{0}: reference cannot be calculated",
                    label));
                continue;
            }

            result.LastValue = comparable;
            var matches = AlarmEvaluation.Compare(
                comparable,
                ParseExternalComparison(condition.Operator),
                condition.Threshold);
            values.Add(matches);
            if (isPercent)
            {
                var referenceLabel = UnmaText.Resolve(
                    condition.ReferenceLabelKey,
                    string.IsNullOrWhiteSpace(
                        condition.ReferenceLabelFallback)
                        ? condition.ReferenceMetric
                        : condition.ReferenceLabelFallback);
                details.Add(UnmaText.Format(
                    "runtime.condition.external_percent_comparison",
                    "{0} % of {1} {2} {3:0.###} " +
                    "(actual {4:0.###} %; {5:0.###} / {6:0.###})",
                    label,
                    referenceLabel,
                    condition.Operator,
                    condition.Threshold,
                    comparable,
                    actual,
                    reference));
            }
            else
            {
                details.Add(UnmaText.Format(
                    "runtime.condition.comparison",
                    "{0} {1} {2:0.###} (actual {3:0.###})",
                    label,
                    condition.Operator,
                    condition.Threshold,
                    comparable));
            }
        }

        result.IsActive = AlarmEvaluation.Combine(
            values,
            string.Equals(template.Logic, "any", StringComparison.Ordinal)
                ? AlarmLogic.Any
                : AlarmLogic.All);
        result.Detail = string.Join(
            string.Equals(template.Logic, "any", StringComparison.Ordinal)
                ? UnmaText.Get("auto.5f15b34155a9")
                : UnmaText.Get("auto.a3f10eb98ea4"),
            details);
        return result;
    }

    private void EvaluatePushedExternalStates(
        ExternalRegistrySnapshot api,
        ISet<string> currentKeys,
        IDictionary<string, bool> currentAutoAcknowledge)
    {
        foreach (var state in api.AlarmStates)
        {
            var overrideId = ExternalAlarmId(state.OwnerModId, state.Id);
            var instanceId = Uri.EscapeDataString(state.InstanceId);
            var key = overrideId + ":push:" + instanceId;
            var slotId = string.IsNullOrWhiteSpace(state.EntityKey)
                ? key
                : overrideId + ":entity:" +
                  Uri.EscapeDataString(state.EntityKey);
            var severity = ParseExternalSeverity(state.Severity);
            currentKeys.Add(key);
            var autoAcknowledge = ResolveAutoAcknowledgeOnClear(
                overrideId,
                state.AutoAcknowledgeOnClear);
            currentAutoAcknowledge[key] = autoAcknowledge;
            SetAlarm(
                key,
                UnmaText.Resolve(
                    state.MessageKey,
                    string.IsNullOrWhiteSpace(state.MessageFallback)
                        ? state.Id
                        : state.MessageFallback),
                UnmaText.Resolve(state.DetailKey, state.DetailFallback),
                "external",
                ResolveExternalPanelId(state.PanelId),
                severity,
                state.Active,
                false,
                ResolveConfiguredSound(overrideId, state.SoundId),
                string.IsNullOrWhiteSpace(state.ActiveColor) ||
                string.Equals(
                    state.ActiveColor,
                    "auto",
                    StringComparison.OrdinalIgnoreCase)
                    ? ColorFor(severity)
                    : state.ActiveColor,
                state.CurrentValue ?? 0d,
                overrideId: overrideId,
                autoAcknowledgeOnClear: autoAcknowledge,
                occurrenceId: state.Id + ":" + state.InstanceId,
                slotId: slotId);
        }
    }

    private static bool TryReadExternalMetric(
        ExternalRegistrySnapshot api,
        string ownerModId,
        IEntity entity,
        string metric,
        out double value)
    {
        return api.TryReadMetric(
                   ownerModId,
                   entity.Prototype.Id.Value,
                   metric,
                   entity,
                   out value) ||
               EntityMetricCatalog.TryRead(entity, metric, out value);
    }

    private string ResolveExternalPanelId(string requestedPanelId)
    {
        lock (m_configurationGate)
        {
            var requested = Configuration.Panels.FirstOrDefault(panel =>
                string.Equals(
                    panel.Id,
                    requestedPanelId,
                    StringComparison.Ordinal));
            return requested?.Id ??
                   Configuration.Panels.FirstOrDefault(panel =>
                       panel.IsDashboard)?.Id ??
                   Configuration.Panels.FirstOrDefault()?.Id ?? "";
        }
    }

    private static string ExternalTemplateIdentity(
        ExternalAlarmTemplateSnapshot template)
    {
        return template.OwnerModId + "\u001f" + template.Id;
    }

    private static string ExternalAlarmId(string ownerModId, string alarmId)
    {
        return "external:" + Uri.EscapeDataString(ownerModId) + ":" +
               Uri.EscapeDataString(alarmId);
    }

    private static AlarmSeverity ParseExternalSeverity(string severity)
    {
        return severity switch
        {
            "emergency" => AlarmSeverity.Emergency,
            "critical" => AlarmSeverity.Critical,
            "warning" => AlarmSeverity.Warning,
            _ => AlarmSeverity.Notice,
        };
    }

    private static ComparisonOperator ParseExternalComparison(
        string comparison)
    {
        return comparison switch
        {
            "<" => ComparisonOperator.Less,
            "<=" => ComparisonOperator.LessOrEqual,
            "==" => ComparisonOperator.Equal,
            "!=" => ComparisonOperator.NotEqual,
            ">=" => ComparisonOperator.GreaterOrEqual,
            ">" => ComparisonOperator.Greater,
            _ => ComparisonOperator.Equal,
        };
    }

    private static string JoinExternalDetail(params string[] parts)
    {
        return string.Join(
            " · ",
            (parts ?? Array.Empty<string>())
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void OnNotificationAdded(INotification notification)
    {
        var id = notification.Proto.Id.Value;
        var overrideId = "vanilla:" + id;
        if (GroupedVanillaNotificationPolicy.IsGroupedPrototype(id))
        {
            var snapshot = m_groupedVanillaNotifications.Add(
                CreateGroupedVanillaNotificationMember(notification));
            if (!GetVanillaNotificationEnabled(overrideId) ||
                ResolveVanillaNotificationBehavior(
                    overrideId,
                    -1,
                    "") == VanillaNotificationBehavior.Ignored)
            {
                return;
            }
            SetGroupedVanillaAlarm(snapshot);
            return;
        }
        if (!GetVanillaNotificationEnabled(overrideId))
        {
            return;
        }
        if (!SustainedVanillaAlarmPolicy.ShouldProcessNotification(
                id,
                m_settlementsManager.LastPopulationDiff))
        {
            return;
        }
        GetNotificationEntityScope(
            notification,
            out var entityId,
            out var entityPrototypeId);
        if (ResolveVanillaNotificationBehavior(
                overrideId,
                entityId,
                entityPrototypeId) == VanillaNotificationBehavior.Ignored)
        {
            return;
        }

        var slotId = overrideId;
        var message = notification.Message.Value;
        var severity = ClassifyNotification(notification);
        var detail = id;
        var entityTitle = "";
        if (notification.Object.HasValue)
        {
            var notificationObject = notification.Object.Value;
            var objectTitle = notificationObject.DefaultTitle.Value;
            if (!string.IsNullOrWhiteSpace(objectTitle))
            {
                entityTitle = objectTitle;
                detail += " · " + objectTitle;
            }
            if (entityId >= 0)
            {
                slotId += ":entity:" + entityId;
            }
        }
        var reconciledLegacyHistory =
            SustainedVanillaAlarmPolicy.IsSustainedPrototype(id) &&
            RestoreSustainedVanillaAlarmFromHistory(id);

        SetAlarm(
            AlarmKeyForNotification(notification),
            string.IsNullOrWhiteSpace(message) ? id : message,
            detail,
            "vanilla",
            "",
            severity,
            true,
            false,
            ResolveConfiguredSound(overrideId),
            ColorFor(severity),
            1d,
            notification.IsSuppressed,
            overrideId,
            ResolveAutoAcknowledgeOnClear(overrideId),
            overrideId,
            slotId: slotId,
            entityId: entityId,
            entityPrototypeId: entityPrototypeId,
            entityTitle: entityTitle);
        if (reconciledLegacyHistory)
        {
            PersistAlarmState();
        }
    }

    private void OnNotificationRemoved(INotification notification)
    {
        var prototypeId = notification.Proto.Id.Value;
        var overrideId = "vanilla:" + prototypeId;
        if (GroupedVanillaNotificationPolicy.IsGroupedPrototype(prototypeId))
        {
            var snapshot = m_groupedVanillaNotifications.Remove(
                NotificationKey(notification));
            if (snapshot.HasMembers &&
                GetVanillaNotificationEnabled(overrideId) &&
                ResolveVanillaNotificationBehavior(
                    overrideId,
                    -1,
                    "") != VanillaNotificationBehavior.Ignored)
            {
                SetGroupedVanillaAlarm(snapshot);
            }
            Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
            return;
        }
        if (!GetVanillaNotificationEnabled(overrideId))
        {
            return;
        }
        GetNotificationEntityScope(
            notification,
            out var entityId,
            out var entityPrototypeId);
        if (ResolveVanillaNotificationBehavior(
                overrideId,
                entityId,
                entityPrototypeId) == VanillaNotificationBehavior.Ignored)
        {
            return;
        }
        if (SustainedVanillaAlarmPolicy.IgnoresNotificationRemoval(
                prototypeId))
        {
            return;
        }
        ClearAlarm(
            AlarmKeyForNotification(notification),
            ResolveAutoAcknowledgeOnClear(overrideId));
        PruneInactiveVanillaHistory(500);
    }

    private void PruneInactiveVanillaHistory(int maximum)
    {
        lock (m_gate)
        {
            var inactive = m_alarms
                .Where(pair =>
                    pair.Value.View.Source == "vanilla" &&
                    !pair.Value.View.IsLatched)
                .OrderBy(pair => pair.Value.Sequence)
                .ToArray();
            var removeCount = Math.Max(0, inactive.Length - maximum);
            for (var index = 0; index < removeCount; index++)
            {
                m_alarms.Remove(inactive[index].Key);
            }
        }
    }

    private void OnNotificationSuppressChanged(INotification notification)
    {
        var prototypeId = notification.Proto.Id.Value;
        var overrideId = "vanilla:" + prototypeId;
        if (GroupedVanillaNotificationPolicy.IsGroupedPrototype(prototypeId))
        {
            var snapshot = m_groupedVanillaNotifications.Add(
                CreateGroupedVanillaNotificationMember(notification));
            if (!GetVanillaNotificationEnabled(overrideId) ||
                ResolveVanillaNotificationBehavior(
                    overrideId,
                    -1,
                    "") == VanillaNotificationBehavior.Ignored)
            {
                return;
            }

            SetGroupedVanillaAlarm(snapshot);
            if (!GroupedVanillaNotificationPolicy
                    .AreAllMembersSuppressed(snapshot))
            {
                return;
            }

            var groupedChanged = false;
            lock (m_gate)
            {
                if (m_alarms.TryGetValue(
                        GroupedVanillaNotificationPolicy.GroupKey,
                        out var groupedAlarm) &&
                    groupedAlarm.View.IsActive &&
                    !groupedAlarm.View.IsAcknowledged)
                {
                    groupedChanged = true;
                    groupedAlarm.View.IsAcknowledged = true;
                    var history = FindHistoryLocked(groupedAlarm.Sequence);
                    if (history != null && !history.IsGone)
                    {
                        history.SetState(
                            isGone: false,
                            isAcknowledged: true,
                            currentGameTicks: CurrentGameTicks);
                    }
                    m_alarmHistoryRevision++;
                }
            }
            if (groupedChanged)
            {
                PersistAlarmState();
            }
            return;
        }
        if (!GetVanillaNotificationEnabled(overrideId))
        {
            return;
        }
        GetNotificationEntityScope(
            notification,
            out var entityId,
            out var entityPrototypeId);
        if (ResolveVanillaNotificationBehavior(
                overrideId,
                entityId,
                entityPrototypeId) == VanillaNotificationBehavior.Ignored)
        {
            return;
        }
        if (!notification.IsSuppressed)
        {
            return;
        }

        var changed = false;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(
                    AlarmKeyForNotification(notification),
                    out var alarm) &&
                alarm.View.IsActive &&
                !alarm.View.IsAcknowledged)
            {
                changed = true;
                alarm.View.IsAcknowledged = true;
                var history = FindHistoryLocked(alarm.Sequence);
                if (history != null && !history.IsGone)
                {
                    history.SetState(
                        isGone: false,
                        isAcknowledged: true,
                        currentGameTicks: CurrentGameTicks);
                }
                if (changed)
                {
                    m_alarmHistoryRevision++;
                }
            }
        }
        if (changed)
        {
            PersistAlarmState();
        }
    }

    private static string AlarmKeyForNotification(
        INotification notification)
    {
        var prototypeId = notification.Proto.Id.Value;
        var occurrenceKey = NotificationKey(notification);
        if (GroupedVanillaNotificationPolicy.IsGroupedPrototype(prototypeId))
        {
            return GroupedVanillaNotificationPolicy.AlarmKeyForNotification(
                prototypeId,
                occurrenceKey);
        }
        return SustainedVanillaAlarmPolicy.AlarmKeyForNotification(
            prototypeId,
            occurrenceKey);
    }

    private static GroupedVanillaNotificationMemberSnapshot
        CreateGroupedVanillaNotificationMember(INotification notification)
    {
        GetNotificationEntityScope(
            notification,
            out var entityId,
            out var entityPrototypeId);
        var message = notification.Message.Value;
        var entityTitle = "";
        if (notification.Object.HasValue)
        {
            entityTitle = notification.Object.Value.DefaultTitle.Value ?? "";
        }
        var title = string.IsNullOrWhiteSpace(message)
            ? GroupedVanillaNotificationPolicy.PrototypeId
            : message;
        var detail = GroupedVanillaNotificationPolicy.PrototypeId;
        if (!string.IsNullOrWhiteSpace(entityTitle))
        {
            detail += " · " + entityTitle;
        }
        return new GroupedVanillaNotificationMemberSnapshot(
            NotificationKey(notification),
            title,
            detail,
            notification.IsSuppressed,
            entityId,
            entityPrototypeId,
            entityTitle);
    }

    private void RefreshGroupedVanillaNotificationMembers(
        IEnumerable<INotification> notifications,
        bool replaceCurrentMembers)
    {
        if (!replaceCurrentMembers)
        {
            return;
        }
        var current = (notifications ??
                     Enumerable.Empty<INotification>())
                 .Where(item => item != null &&
                     GroupedVanillaNotificationPolicy.IsGroupedPrototype(
                         item.Proto.Id.Value))
                 .OrderBy(
                     NotificationKey,
                     StringComparer.Ordinal)
                 .ToArray();
        var currentKeys = new HashSet<string>(
            current.Select(NotificationKey),
            StringComparer.Ordinal);
        foreach (var staleKey in m_groupedVanillaNotifications
                     .GetNotificationKeys()
                     .Where(key => !currentKeys.Contains(key)))
        {
            m_groupedVanillaNotifications.Remove(staleKey);
        }
        foreach (var notification in current)
        {
            m_groupedVanillaNotifications.Add(
                CreateGroupedVanillaNotificationMember(notification));
        }
    }

    private void SetGroupedVanillaAlarm(
        GroupedVanillaNotificationSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.HasMembers)
        {
            return;
        }
        var representative = snapshot.OldestRepresentative;
        SetAlarm(
            GroupedVanillaNotificationPolicy.GroupKey,
            GroupedVanillaNotificationPolicy.FormatTitle(
                representative.Title,
                1),
            GroupedVanillaNotificationPolicy.FormatDetail(
                representative.Detail,
                snapshot.Count),
            "vanilla",
            "",
            AlarmSeverity.Critical,
            true,
            false,
            ResolveConfiguredSound(
                GroupedVanillaNotificationPolicy.OverrideId),
            ColorFor(AlarmSeverity.Critical),
            snapshot.Count,
            GroupedVanillaNotificationPolicy.AreAllMembersSuppressed(
                snapshot),
            GroupedVanillaNotificationPolicy.OverrideId,
            ResolveAutoAcknowledgeOnClear(
                GroupedVanillaNotificationPolicy.OverrideId),
            GroupedVanillaNotificationPolicy.OverrideId,
            slotId: GroupedVanillaNotificationPolicy.SlotId,
            entityId: representative.EntityId,
            entityPrototypeId: representative.EntityPrototypeId,
            entityTitle: representative.EntityTitle);
    }

    private void FlushGroupedVanillaNotificationClear()
    {
        if (!m_groupedVanillaNotifications.TryTakePendingLastClear(out _))
        {
            return;
        }
        ClearAlarm(
            GroupedVanillaNotificationPolicy.GroupKey,
            ResolveAutoAcknowledgeOnClear(
                GroupedVanillaNotificationPolicy.OverrideId));
        PruneInactiveVanillaHistory(500);
    }

    private static void GetNotificationEntityScope(
        INotification notification,
        out int entityId,
        out string entityPrototypeId)
    {
        entityId = -1;
        entityPrototypeId = "";
        if (notification.Object.HasValue &&
            notification.Object.Value is IEntity entity)
        {
            entityId = entity.Id.Value;
            entityPrototypeId = entity.Prototype.Id.Value;
        }
    }

    private void SetAlarm(
        string key,
        string name,
        string detail,
        string source,
        string panelId,
        AlarmSeverity severity,
        bool isActive,
        bool missingSource,
        string soundId,
        string activeColor,
        double lastValue,
        bool initiallyAcknowledged = false,
        string overrideId = "",
        bool autoAcknowledgeOnClear = false,
        string occurrenceId = "",
        int occurrencePriority = 0,
        string slotId = "",
        int entityId = -1,
        string entityPrototypeId = "",
        string entityTitle = "",
        AlarmOperatorAction operatorAction = AlarmOperatorAction.None,
        string attentionPanelId = "")
    {
        var shouldPersist = false;
        var shouldPublishExternal = false;
        AlarmView slotCandidate;
        lock (m_gate)
        {
            if (IsSuppressedVanillaAlarm(
                    source,
                    overrideId,
                    GetDisabledVanillaOverrideIds(),
                    slotId))
            {
                return;
            }

            var historyChanged = false;
            var created = false;
            if (!m_alarms.TryGetValue(key, out var state))
            {
                state = new AlarmState();
                state.View.Key = key;
                state.View.IsAcknowledged = initiallyAcknowledged;
                m_alarms[key] = state;
                created = true;
            }

            var wasActive = state.View.IsActive;
            var previousSeverity = state.View.Severity;
            var wasAcknowledged = state.View.IsAcknowledged;
            var wasOperatorSilenced = state.View.IsOperatorSilenced;
            var previousOperatorSilencedAtGameTick =
                state.View.OperatorSilencedAtGameTick;
            var wasGoneUnacknowledged =
                state.View.IsGoneUnacknowledged;
            var previousName = state.View.Name;
            var previousPanelId = state.View.PanelId;
            var previousActiveColor = state.View.ActiveColor;
            var previousSoundId = state.View.SoundId;
            var previousOverrideId = state.View.OverrideId;
            var previousOccurrenceId = state.View.OccurrenceId;
            var previousSlotId = state.View.SlotId;
            var previousOccurrencePriority =
                state.View.OccurrencePriority;
            state.View.Name = name ?? UnmaText.Get(
                "default.notification",
                "NOTIFICATION");
            state.View.Detail = detail ?? "";
            state.View.Source = source ?? "";
            state.View.PanelId = panelId ?? "";
            occurrenceId ??= "";
            var occurrenceEscalated =
                wasActive &&
                severity == previousSeverity &&
                !string.IsNullOrWhiteSpace(occurrenceId) &&
                !string.Equals(
                    previousOccurrenceId,
                    occurrenceId,
                    StringComparison.Ordinal) &&
                occurrencePriority >= previousOccurrencePriority;
            var transition = AlarmEvaluation.Transition(
                wasActive,
                wasAcknowledged,
                wasGoneUnacknowledged,
                previousSeverity,
                isActive,
                severity,
                autoAcknowledgeOnClear,
                occurrenceEscalated,
                initiallyAcknowledged);
            state.View.Severity = severity;
            state.View.IsActive = transition.IsActive;
            state.View.IsAcknowledged = transition.IsAcknowledged;
            state.View.IsGoneUnacknowledged =
                transition.IsGoneUnacknowledged;
            if (transition.IsNewOccurrence || !transition.IsActive)
            {
                state.View.IsOperatorSilenced = false;
                state.View.OperatorSilencedAtGameTick = -1;
            }
            state.View.IsMissingSource = missingSource;
            state.View.SoundId = string.IsNullOrWhiteSpace(soundId)
                ? "auto"
                : soundId;
            state.View.OverrideId = overrideId ?? "";
            state.View.OccurrenceId = occurrenceId;
            slotId = string.IsNullOrWhiteSpace(slotId)
                ? !string.IsNullOrWhiteSpace(state.View.OverrideId)
                    ? state.View.OverrideId
                    : key
                : slotId;
            state.View.SlotId = slotId;
            state.View.OccurrencePriority = occurrencePriority;
            state.View.EntityId = entityId;
            state.View.EntityPrototypeId = entityPrototypeId ?? "";
            state.View.EntityTitle = entityTitle ?? "";
            state.View.ActiveColor = string.IsNullOrWhiteSpace(activeColor)
                ? ColorFor(severity)
                : activeColor;
            state.View.LastValue = lastValue;

            if (transition.IsNewOccurrence)
            {
                shouldPublishExternal = true;
                if (wasActive)
                {
                    historyChanged |= CloseHistoryLocked(
                        state.Sequence,
                        wasAcknowledged);
                }
                state.Sequence = ++m_sequence;
                state.View.Sequence = state.Sequence;
                m_alarmHistory.Add(CreateHistoryFromState(state));
                historyChanged = true;
                if (ShouldEnqueueAttentionRequest(
                        wasActive,
                        transition.IsNewOccurrence))
                {
                    AlarmAttentionQueuePolicy.TryEnqueue(
                        m_attentionRequests,
                        new AlarmAttentionRequest(
                            state.View.Key,
                            state.Sequence,
                            string.IsNullOrWhiteSpace(attentionPanelId)
                                ? state.View.PanelId
                                : attentionPanelId,
                            state.View.SlotId,
                            state.View.Severity,
                            operatorAction));
                }
            }
            else if (state.Sequence > 0 &&
                     (wasActive || wasGoneUnacknowledged ||
                      state.View.IsLatched))
            {
                var occurrenceAcknowledged = state.View.IsActive
                    ? state.View.IsAcknowledged
                    : wasAcknowledged || autoAcknowledgeOnClear;
                historyChanged |= UpdateHistoryFromStateLocked(
                    state,
                    !state.View.IsActive,
                    occurrenceAcknowledged);
            }
            state.View.Sequence = state.Sequence;

            var migratedLegacySlots = false;
            if (string.Equals(source, "vanilla", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(state.View.OverrideId) &&
                !string.Equals(
                    state.View.SlotId,
                    state.View.OverrideId,
                    StringComparison.Ordinal))
            {
                foreach (var other in m_alarms.Values.Where(other =>
                             !ReferenceEquals(other, state) &&
                             string.Equals(
                                 other.View.OverrideId,
                                 state.View.OverrideId,
                                 StringComparison.Ordinal) &&
                             string.Equals(
                                 other.View.Detail,
                                 state.View.Detail,
                                 StringComparison.Ordinal) &&
                             (string.IsNullOrWhiteSpace(other.View.SlotId) ||
                              string.Equals(
                                  other.View.SlotId,
                                  state.View.OverrideId,
                                  StringComparison.Ordinal) ||
                              PanelSlotProjection.IsLegacyVanillaSlotId(
                                  other.View.SlotId,
                                  state.View.OverrideId))))
                {
                    other.View.SlotId = state.View.SlotId;
                    other.View.Sequence = other.Sequence;
                    migratedLegacySlots = true;
                }
            }

            if (historyChanged)
            {
                m_alarmHistoryRevision++;
            }

            shouldPersist =
                (wasActive || wasGoneUnacknowledged ||
                 state.View.IsLatched) &&
                (created ||
                  wasActive != state.View.IsActive ||
                  wasAcknowledged != state.View.IsAcknowledged ||
                  wasOperatorSilenced != state.View.IsOperatorSilenced ||
                  previousOperatorSilencedAtGameTick !=
                  state.View.OperatorSilencedAtGameTick ||
                  wasGoneUnacknowledged !=
                 state.View.IsGoneUnacknowledged ||
                 previousSeverity != state.View.Severity ||
                 !string.Equals(
                     previousName,
                     state.View.Name,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousPanelId,
                     state.View.PanelId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousActiveColor,
                     state.View.ActiveColor,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousSoundId,
                     state.View.SoundId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousOverrideId,
                     state.View.OverrideId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousOccurrenceId,
                     state.View.OccurrenceId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     previousSlotId,
                     state.View.SlotId,
                     StringComparison.Ordinal) ||
                 previousOccurrencePriority !=
                 state.View.OccurrencePriority ||
                 migratedLegacySlots ||
                 historyChanged);
            slotCandidate = Clone(state.View, state.Sequence);
        }
        var panelSlotsChanged = EnsurePanelSlotsForAlarm(slotCandidate);
        if (shouldPersist || panelSlotsChanged)
        {
            PersistAlarmState();
        }
        if (shouldPublishExternal)
        {
            PublishExternalDisplayAlarm(slotCandidate, true);
        }
    }

    private static bool ShouldEnqueueAttentionRequest(
        bool wasActive,
        bool isNewOccurrence)
    {
        return wasActive && isNewOccurrence;
    }

    private void ClearAlarm(
        string key,
        bool autoAcknowledgeOnClear,
        bool persist = true)
    {
        var changed = false;
        AlarmView clearedAlarm = null;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var state))
            {
                var wasActive = state.View.IsActive;
                var wasAcknowledged = state.View.IsAcknowledged;
                var wasOperatorSilenced = state.View.IsOperatorSilenced;
                var previousOperatorSilencedAtGameTick =
                    state.View.OperatorSilencedAtGameTick;
                var wasGoneUnacknowledged =
                    state.View.IsGoneUnacknowledged;
                var transition = AlarmEvaluation.Transition(
                    state.View.IsActive,
                    state.View.IsAcknowledged,
                    state.View.IsGoneUnacknowledged,
                    state.View.Severity,
                    false,
                    state.View.Severity,
                    autoAcknowledgeOnClear);
                state.View.IsActive = transition.IsActive;
                state.View.IsAcknowledged = transition.IsAcknowledged;
                state.View.IsGoneUnacknowledged =
                    transition.IsGoneUnacknowledged;
                state.View.IsOperatorSilenced = false;
                state.View.OperatorSilencedAtGameTick = -1;
                var occurrenceAcknowledged = wasAcknowledged ||
                                             autoAcknowledgeOnClear;
                var historyChanged = false;
                if (wasActive || wasGoneUnacknowledged)
                {
                    historyChanged = UpdateHistoryFromStateLocked(
                        state,
                        true,
                        occurrenceAcknowledged);
                }
                changed =
                    wasActive != state.View.IsActive ||
                    wasAcknowledged != state.View.IsAcknowledged ||
                    wasOperatorSilenced != state.View.IsOperatorSilenced ||
                    previousOperatorSilencedAtGameTick !=
                    state.View.OperatorSilencedAtGameTick ||
                    wasGoneUnacknowledged !=
                    state.View.IsGoneUnacknowledged ||
                    historyChanged;
                if (wasActive && !state.View.IsActive)
                {
                    clearedAlarm = Clone(state.View, state.Sequence);
                }
                if (historyChanged)
                {
                    m_alarmHistoryRevision++;
                }
            }
        }
        if (changed && persist)
        {
            PersistAlarmState();
        }
        if (clearedAlarm != null)
        {
            PublishExternalDisplayAlarm(clearedAlarm, false);
        }
    }

    private void PublishExternalDisplaySnapshot()
    {
        if (!m_externalDisplay.TryReset(out var resetError))
        {
            Log.Warning(
                "UNMA: External display reset failed: " + resetError);
            return;
        }

        AlarmView[] activeAlarms;
        lock (m_gate)
        {
            activeAlarms = m_alarms.Values
                .Where(state => state.View.IsActive)
                .OrderBy(state => state.Sequence)
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }
        foreach (var alarm in activeAlarms)
        {
            PublishExternalDisplayAlarm(alarm, true);
        }
        Log.Info(
            "UNMA: External display synchronized; active=" +
            activeAlarms.Length + ", path=" + m_externalDisplay.Path);
    }

    private void PublishExternalDisplayPanelState()
    {
        PanelDefinition[] panels;
        lock (m_configurationGate)
        {
            panels = Configuration.Panels
                .Where(panel => panel != null)
                .ToArray();
        }
        if (!m_externalDisplay.TryPublishPanelState(
                panels,
                GetViews,
                out _,
                out var error))
        {
            Log.Warning(
                "UNMA: External panel synchronization failed: " + error);
        }
    }

    private void PublishExternalDisplayAlarm(AlarmView alarm, bool active)
    {
        var severity = active
            ? alarm.Severity switch
            {
                AlarmSeverity.Emergency => "critical",
                AlarmSeverity.Critical => "critical",
                AlarmSeverity.Warning => "warning",
                _ => "info",
            }
            : "success";
        var title = active
            ? alarm.Name
            : UnmaText.Format(
                "external_display.resolved_title",
                "RESOLVED: {0}",
                alarm.Name);
        var detail = string.IsNullOrWhiteSpace(alarm.Detail)
            ? alarm.EntityTitle
            : alarm.Detail;
        if (!m_externalDisplay.TryPublish(
                alarm.Key,
                title,
                detail,
                severity,
                string.IsNullOrWhiteSpace(alarm.Source)
                    ? "UNMA"
                    : "UNMA · " + alarm.Source,
                active,
                out var error))
        {
            Log.Warning(
                "UNMA: External display publish failed: " + error);
        }
    }

    private void ForceNormal(string key, bool persist = true)
    {
        var changed = false;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var state))
            {
                changed = state.View.IsLatched ||
                          state.View.IsAcknowledged ||
                          state.View.IsOperatorSilenced ||
                          state.View.OperatorSilencedAtGameTick >= 0;
                var historyChanged = CloseHistoryLocked(
                    state.Sequence,
                    state.View.IsAcknowledged);
                state.View.IsActive = false;
                state.View.IsAcknowledged = false;
                state.View.IsGoneUnacknowledged = false;
                state.View.IsOperatorSilenced = false;
                state.View.OperatorSilencedAtGameTick = -1;
                changed |= historyChanged;
                if (historyChanged)
                {
                    m_alarmHistoryRevision++;
                }
            }
        }
        changed |= InvalidateTimingForAlarmKey(key);
        if (changed && persist)
        {
            PersistAlarmState();
        }
    }

    private AlarmHistoryDefinition FindHistoryLocked(long sequence)
    {
        return sequence <= 0
            ? null
            : m_alarmHistory.Find(item => item.Sequence == sequence);
    }

    private bool CloseHistoryLocked(long sequence, bool acknowledged)
    {
        var history = FindHistoryLocked(sequence);
        if (history == null)
        {
            return false;
        }
        return history.SetState(
            isGone: true,
            isAcknowledged: acknowledged,
            currentGameTicks: CurrentGameTicks);
    }

    private bool UpdateHistoryFromStateLocked(
        AlarmState state,
        bool isGone,
        bool isAcknowledged)
    {
        var history = FindHistoryLocked(state.Sequence);
        if (history == null)
        {
            if (state.Sequence <= 0)
            {
                return false;
            }
            history = CreateHistoryFromState(state);
            m_alarmHistory.Add(history);
        }

        var changed =
            !string.Equals(
                history.AlarmKey,
                state.View.Key,
                StringComparison.Ordinal) ||
            !string.Equals(
                history.Message,
                state.View.Name,
                StringComparison.Ordinal) ||
            !string.Equals(
                history.Detail,
                state.View.Detail,
                StringComparison.Ordinal) ||
            !string.Equals(
                history.Source,
                state.View.Source,
                StringComparison.Ordinal) ||
            !string.Equals(
                history.PanelId,
                state.View.PanelId,
                StringComparison.Ordinal) ||
            history.Severity != state.View.Severity;

        history.AlarmKey = state.View.Key;
        history.Message = state.View.Name;
        history.Detail = state.View.Detail;
        history.Source = state.View.Source;
        history.PanelId = state.View.PanelId;
        history.Severity = state.View.Severity;
        return history.SetState(
                   isGone,
                   isAcknowledged,
                   CurrentGameTicks) || changed;
    }

    private AlarmHistoryDefinition CreateHistoryFromState(
        AlarmState state)
    {
        var currentGameTicks = CurrentGameTicks;
        var isGone = !state.View.IsActive;
        var isAcknowledged = state.View.IsActive &&
                             state.View.IsAcknowledged;
        return new AlarmHistoryDefinition
        {
            Sequence = state.Sequence,
            AlarmKey = state.View.Key,
            Message = state.View.Name,
            Detail = state.View.Detail,
            Source = state.View.Source,
            PanelId = state.View.PanelId,
            Severity = state.View.Severity,
            IsGone = isGone,
            IsAcknowledged = isAcknowledged,
            RaisedAtTicks = currentGameTicks,
            ClearedAtTicks = isGone ? currentGameTicks : 0d,
            AcknowledgedAtTicks = isAcknowledged
                ? currentGameTicks
                : 0d,
        };
    }

    private static AlarmHistoryDefinition CloneHistory(
        AlarmHistoryDefinition source)
    {
        return new AlarmHistoryDefinition
        {
            Sequence = source.Sequence,
            AlarmKey = source.AlarmKey,
            Message = source.Message,
            Detail = source.Detail,
            Source = source.Source,
            PanelId = source.PanelId,
            Severity = source.Severity,
            IsGone = source.IsGone,
            IsAcknowledged = source.IsAcknowledged,
            RaisedAtTicks = source.RaisedAtTicks,
            ClearedAtTicks = source.ClearedAtTicks,
            AcknowledgedAtTicks = source.AcknowledgedAtTicks,
        };
    }

    private long CurrentGameTick =>
        Math.Max(0L, (long)m_calendar.RealTime.Ticks);

    private double CurrentGameTicks => (double)CurrentGameTick;

    private AlarmSeverity ClassifyNotification(INotification notification)
    {
        var id = notification.Proto.Id.Value;
        if (ContainsAny(id, s_emergencyNotificationTokens))
        {
            return AlarmSeverity.Emergency;
        }
        if (ContainsAny(id, s_criticalNotificationTokens))
        {
            return AlarmSeverity.Critical;
        }

        return notification.Proto.Style switch
        {
            NotificationStyle.Critical => AlarmSeverity.Critical,
            NotificationStyle.Warning => AlarmSeverity.Warning,
            _ => AlarmSeverity.Notice,
        };
    }

    private string ColorFor(AlarmSeverity severity)
    {
        return severity switch
        {
            AlarmSeverity.Emergency => Configuration.EmergencyColor,
            AlarmSeverity.Critical => Configuration.CriticalColor,
            AlarmSeverity.Warning => Configuration.WarningColor,
            _ => "#83C5BE",
        };
    }

    private string ResolveConfiguredSound(
        string alarmId,
        string fallback = "auto")
    {
        lock (m_configurationGate)
        {
            return Configuration.SoundOverrides.FirstOrDefault(item =>
                       string.Equals(
                           item.AlarmId,
                           alarmId,
                           StringComparison.Ordinal))?.SoundId ??
                   (string.IsNullOrWhiteSpace(fallback)
                       ? "auto"
                       : fallback);
        }
    }

    private HashSet<string> GetDisabledVanillaOverrideIds()
    {
        return m_disabledVanillaOverrideIds;
    }

    private VanillaNotificationRule[]
        GetVanillaNotificationRulesSnapshot()
    {
        lock (m_configurationGate)
        {
            return Configuration.VanillaNotificationRules
                .Select(CloneVanillaNotificationRule)
                .ToArray();
        }
    }

    private VanillaNotificationBehavior
        ResolveVanillaNotificationBehavior(
            AlarmView view,
            IEnumerable<VanillaNotificationRule> rules)
    {
        if (view == null ||
            !string.Equals(view.Source, "vanilla", StringComparison.Ordinal))
        {
            return VanillaNotificationBehavior.Normal;
        }
        return ResolveVanillaNotificationBehavior(
            rules,
            view.OverrideId,
            view.EntityId,
            view.EntityPrototypeId);
    }

    private VanillaNotificationBehavior ResolveVanillaNotificationBehavior(
        string overrideId,
        int entityId,
        string entityPrototypeId)
    {
        return ResolveVanillaNotificationBehavior(
            GetVanillaNotificationRulesSnapshot(),
            overrideId,
            entityId,
            entityPrototypeId);
    }

    private VanillaNotificationBehavior ResolveVanillaNotificationBehavior(
        IEnumerable<VanillaNotificationRule> rules,
        string overrideId,
        int entityId,
        string entityPrototypeId)
    {
        if (GroupedVanillaNotificationPolicy.IsGroupedOverride(overrideId))
        {
            entityId = -1;
            entityPrototypeId = "";
        }
        var behavior = VanillaNotificationSuppressionPolicy.ResolveBehavior(
            rules,
            overrideId,
            entityId,
            entityPrototypeId);
        foreach (var alias in GetNotificationOwnerAliases(entityId))
        {
            var aliasBehavior = VanillaNotificationSuppressionPolicy
                .ResolveBehavior(
                    rules,
                    overrideId,
                    alias.OwnerEntityId,
                    alias.OwnerEntityPrototypeId);
            if ((int)aliasBehavior > (int)behavior)
            {
                behavior = aliasBehavior;
            }
        }
        return behavior;
    }

    private bool IsVanillaAlarmHidden(
        AlarmView view,
        IEnumerable<VanillaNotificationRule> rules)
    {
        var behavior = ResolveVanillaNotificationBehavior(view, rules);
        return behavior == VanillaNotificationBehavior.Hidden ||
               behavior == VanillaNotificationBehavior.Ignored;
    }

    private bool IsVanillaAlarmHiddenOnPanel(
        AlarmView view,
        IEnumerable<VanillaNotificationRule> rules,
        PanelDefinition panel,
        IReadOnlyCollection<int> relatedEntityIds)
    {
        var behavior = ResolveVanillaNotificationBehavior(view, rules);
        var isEntityPanel = PanelTopologyPolicy.IsEntityPanel(panel);
        var belongsToEntityPanel = isEntityPanel &&
            view != null &&
            string.Equals(view.Source, "vanilla", StringComparison.Ordinal) &&
            (view.EntityId == panel.OwnerEntityId ||
             relatedEntityIds?.Contains(view.EntityId) == true);
        return VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            behavior,
            isEntityPanel,
            belongsToEntityPanel);
    }

    private void RefreshDisabledVanillaOverrideIds()
    {
        HashSet<string> disabledOverrideIds;
        lock (m_configurationGate)
        {
            disabledOverrideIds = new HashSet<string>(
                Configuration.SoundOverrides
                    .Where(soundOverride =>
                        soundOverride != null &&
                        soundOverride.IsGloballyDisabled &&
                        VanillaNotificationSuppressionPolicy
                            .IsVanillaOverrideId(
                                soundOverride.AlarmId))
                    .Select(soundOverride =>
                        soundOverride.AlarmId.Trim()),
                StringComparer.Ordinal);
        }
        m_disabledVanillaOverrideIds = disabledOverrideIds;
    }

    private static bool IsSuppressedVanillaAlarm(
        AlarmView view,
        IEnumerable<string> disabledOverrideIds)
    {
        return view != null && IsSuppressedVanillaAlarm(
            view.Source,
            view.OverrideId,
            disabledOverrideIds,
            view.SlotId);
    }

    private static bool IsSuppressedVanillaAlarm(
        string source,
        string overrideId,
        IEnumerable<string> disabledOverrideIds,
        string slotId = "")
    {
        if (!string.Equals(source, "vanilla", StringComparison.Ordinal) ||
            disabledOverrideIds == null)
        {
            return false;
        }

        var canonicalOverrideId =
            VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                overrideId)
                ? overrideId.Trim()
                : VanillaNotificationSuppressionPolicy
                    .GetOverrideIdForSlotId(slotId);
        return canonicalOverrideId.Length > 0 &&
               disabledOverrideIds.Contains(
                   canonicalOverrideId,
                   StringComparer.Ordinal);
    }

    private void RollBackVanillaNotificationOverride(
        AlarmSoundOverride soundOverride,
        bool createdOverride,
        bool wasDisabled)
    {
        lock (m_configurationGate)
        {
            if (createdOverride)
            {
                Configuration.SoundOverrides.Remove(soundOverride);
            }
            else
            {
                soundOverride.IsGloballyDisabled = wasDisabled;
            }
        }
        RefreshDisabledVanillaOverrideIds();
    }

    private void RestoreConfigurationAlarmSnapshots()
    {
        CapturePersistentAlarmState(
            out var alarmMemories,
            out var alarmHistory,
            out var alarmTimingMemories);
        lock (m_configurationGate)
        {
            Configuration.AlarmMemories = alarmMemories;
            Configuration.AlarmHistory = alarmHistory;
            Configuration.AlarmTimingMemories = alarmTimingMemories;
        }
    }

    private void ReplayCurrentVanillaNotifications(string overrideId)
    {
        INotification[] currentNotifications;
        try
        {
            currentNotifications = m_notificationsManager
                .FetchAllNotifications()
                .Where(notification => string.Equals(
                    "vanilla:" + notification.Proto.Id.Value,
                    overrideId,
                    StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception exception)
        {
            Log.Warning(
                UnmaText.Get("auto.b1be85351986") +
                UnmaText.Get("auto.daf987e22580") + exception.Message);
            return;
        }

        RefreshGroupedVanillaNotificationMembers(
            currentNotifications,
            replaceCurrentMembers:
                GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    overrideId));

        BeginAlarmPersistenceBatch();
        try
        {
            foreach (var notification in currentNotifications)
            {
                OnNotificationAdded(notification);
            }
        }
        finally
        {
            if (EndAlarmPersistenceBatch())
            {
                PersistAlarmState();
            }
        }
    }

    private bool ResolveAutoAcknowledgeOnClear(
        string alarmId,
        bool fallback = false)
    {
        lock (m_configurationGate)
        {
            return Configuration.SoundOverrides.FirstOrDefault(item =>
                string.Equals(
                    item.AlarmId,
                    alarmId,
                    StringComparison.Ordinal))?.AutoAcknowledgeOnClear ??
                       fallback;
        }
    }

    private bool ResolveExternalDefaultAutoAcknowledge(string alarmId)
    {
        ExternalAlarmTemplateSnapshot jsonTemplate;
        lock (m_externalDefinitionsGate)
        {
            jsonTemplate = m_externalDefinitions?.AlarmTemplates
                .FirstOrDefault(template => string.Equals(
                    ExternalAlarmId(template.OwnerModId, template.Id),
                    alarmId,
                    StringComparison.Ordinal));
        }
        if (jsonTemplate != null)
        {
            return jsonTemplate.AutoAcknowledgeOnClear;
        }

        var api = UnmaApi.GetSnapshot();
        var apiTemplate = api.AlarmTemplates.FirstOrDefault(template =>
            string.Equals(
                ExternalAlarmId(template.OwnerModId, template.Id),
                alarmId,
                StringComparison.Ordinal));
        if (apiTemplate != null)
        {
            return apiTemplate.AutoAcknowledgeOnClear;
        }

        return api.AlarmStates
            .Where(state => string.Equals(
                ExternalAlarmId(state.OwnerModId, state.Id),
                alarmId,
                StringComparison.Ordinal))
            .Any(state => state.AutoAcknowledgeOnClear);
    }

    private bool ResolveExternalAutoAcknowledgeForKey(string alarmKey)
    {
        string overrideId;
        lock (m_gate)
        {
            overrideId = m_alarms.TryGetValue(
                alarmKey,
                out var state)
                ? state.View.OverrideId
                : "";
        }
        if (string.IsNullOrWhiteSpace(overrideId))
        {
            return false;
        }
        return ResolveAutoAcknowledgeOnClear(
            overrideId,
            ResolveExternalDefaultAutoAcknowledge(overrideId));
    }

    private static string NotificationKey(INotification notification)
    {
        return "vanilla:" + notification.NotificationId.Value;
    }

    private static bool ContainsAny(string value, IEnumerable<string> tokens)
    {
        return tokens.Any(token => value.IndexOf(
            token,
            StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string[] SplitFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return Array.Empty<string>();
        }
        return filter.Split(new[] { ',', ';' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private bool EnsurePanelSlotsForAlarm(AlarmView view)
    {
        if (string.Equals(
                view?.Source,
                "vanilla",
                StringComparison.Ordinal) &&
            IsSuppressedVanillaAlarm(
                view,
                GetDisabledVanillaOverrideIds()))
        {
            return false;
        }

        var slot = PanelSlotProjection.CreateSlot(view);
        if (slot == null)
        {
            return false;
        }

        var changed = false;
        lock (m_configurationGate)
        {
            changed |= SanitizeEntityPanelSlotsLocked();
            foreach (var panel in Configuration.Panels)
            {
                if (panel.IsDashboard)
                {
                    continue;
                }
                if (!IsVisibleOnPanel(
                        view,
                        panel,
                        SplitFilter(panel.NotificationFilter)))
                {
                    continue;
                }
                panel.Slots ??= new List<PanelSlotDefinition>();
                panel.ExcludedAlarmIds ??= new List<string>();
                var legacyAlarmId = string.Equals(
                        view.Source,
                        "vanilla",
                        StringComparison.Ordinal)
                    ? PanelSlotProjection.LegacyVanillaSlotId(
                        view.OverrideId,
                        view.Detail)
                    : "";
                if (panel.ExcludedAlarmIds.Contains(
                        slot.AlarmId,
                        StringComparer.Ordinal) ||
                    !string.IsNullOrWhiteSpace(view.OverrideId) &&
                    panel.ExcludedAlarmIds.Contains(
                        view.OverrideId,
                        StringComparer.Ordinal) ||
                    !string.IsNullOrWhiteSpace(legacyAlarmId) &&
                    panel.ExcludedAlarmIds.Contains(
                        legacyAlarmId,
                        StringComparer.Ordinal))
                {
                    continue;
                }
                var existing = panel.Slots.Find(candidate => string.Equals(
                    candidate.AlarmId,
                    slot.AlarmId,
                    StringComparison.Ordinal));

                if (existing == null &&
                    string.Equals(
                        view.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(view.OverrideId) &&
                    !string.Equals(
                        slot.AlarmId,
                        view.OverrideId,
                        StringComparison.Ordinal))
                {
                    existing = panel.Slots.Find(candidate =>
                        string.Equals(
                            candidate.AlarmId,
                            legacyAlarmId,
                            StringComparison.Ordinal) ||
                        string.Equals(
                            candidate.AlarmId,
                            view.OverrideId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            candidate.Detail,
                            view.Detail,
                            StringComparison.Ordinal));
                    if (existing != null)
                    {
                        existing.AlarmId = slot.AlarmId;
                        changed = true;
                    }
                }

                if (existing != null &&
                    string.Equals(
                        view.Source,
                        "vanilla",
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(view.OverrideId))
                {
                    changed |= panel.Slots.RemoveAll(candidate =>
                        !ReferenceEquals(candidate, existing) &&
                        (string.Equals(
                             candidate.AlarmId,
                             legacyAlarmId,
                             StringComparison.Ordinal) ||
                         string.Equals(
                             candidate.AlarmId,
                             view.OverrideId,
                             StringComparison.Ordinal) &&
                         string.Equals(
                             candidate.Detail,
                             view.Detail,
                             StringComparison.Ordinal))) > 0;
                }

                if (existing == null)
                {
                    panel.Slots.Add(PanelSlotProjection.CloneSlot(slot));
                    changed = true;
                    continue;
                }

                if (string.Equals(
                        view.Source,
                        "vanilla",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        view.Source,
                        "external",
                        StringComparison.Ordinal))
                {
                    changed |= UpdatePanelSlotMetadata(
                        existing,
                        slot,
                        updateDetail: !string.Equals(
                            view.Source,
                            "external",
                            StringComparison.Ordinal));
                }
            }
        }
        return changed;
    }

    private static bool UpdatePanelSlotMetadata(
        PanelSlotDefinition target,
        PanelSlotDefinition source,
        bool updateDetail = true)
    {
        var changed = false;
        if (!string.Equals(
                target.DisplayName,
                source.DisplayName,
                StringComparison.Ordinal))
        {
            target.DisplayName = source.DisplayName;
            changed = true;
        }
        if (updateDetail && !string.Equals(
                target.Detail,
                source.Detail,
                StringComparison.Ordinal))
        {
            target.Detail = source.Detail;
            changed = true;
        }
        if (!string.Equals(
                target.Source,
                source.Source,
                StringComparison.Ordinal))
        {
            target.Source = source.Source;
            changed = true;
        }
        if (target.Severity != source.Severity)
        {
            target.Severity = source.Severity;
            changed = true;
        }
        if (!string.Equals(
                target.ActiveColor,
                source.ActiveColor,
                StringComparison.Ordinal))
        {
            target.ActiveColor = source.ActiveColor;
            changed = true;
        }
        return changed;
    }

    private bool IsVisibleOnPanel(
        AlarmView view,
        PanelDefinition panel,
        IReadOnlyList<string> filters)
    {
        if (view == null || panel == null)
        {
            return false;
        }
        if (PanelTopologyPolicy.IsEntityPanel(panel) &&
            string.Equals(view.Source, "vanilla", StringComparison.Ordinal))
        {
            if (GroupedVanillaNotificationPolicy.IsGroupedOverride(
                    view.OverrideId))
            {
                return false;
            }
            return panel.IncludeVanilla &&
                   view.EntityId == panel.OwnerEntityId;
        }
        if (PanelTopologyPolicy.IsEntityPanel(panel) &&
            !string.Equals(view.Source, "custom", StringComparison.Ordinal))
        {
            return false;
        }
        if (string.Equals(view.Source, "custom", StringComparison.Ordinal))
        {
            var stableAlarmId = PanelSlotProjection.StableAlarmId(view);
            if (PanelTopologyPolicy.TryGetRuleId(
                    stableAlarmId,
                    out var ruleId))
            {
                var rule = Configuration.Rules.FirstOrDefault(candidate =>
                    string.Equals(candidate?.Id, ruleId,
                        StringComparison.Ordinal));
                if (rule != null)
                {
                    return PanelTopologyPolicy.IsRuleAssignedToPanel(
                        rule,
                        panel,
                        Configuration.Panels);
                }
            }
            return string.Equals(
                view.PanelId,
                panel.Id,
                StringComparison.Ordinal);
        }
        if (string.Equals(view.Source, "external", StringComparison.Ordinal))
        {
            return string.Equals(
                view.PanelId,
                panel.Id,
                StringComparison.Ordinal);
        }
        if (view.Source == "vanilla" && !panel.IncludeVanilla)
        {
            return false;
        }
        if (view.Source == "system" && !panel.IncludeSystem)
        {
            return false;
        }
        if (filters.Count == 0)
        {
            return true;
        }

        var haystack = view.Name + " " + view.Detail + " " + view.Key;
        return filters.Any(filter => haystack.IndexOf(
            filter,
            StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private bool SanitizeEntityPanelSlotsLocked()
    {
        var changed = false;
        foreach (var panel in Configuration.Panels.Where(
                     PanelTopologyPolicy.IsEntityPanel))
        {
            if (!panel.IncludeVanilla)
            {
                panel.IncludeVanilla = true;
                changed = true;
            }
            if (panel.IncludeSystem)
            {
                panel.IncludeSystem = false;
                changed = true;
            }
            if (panel.Slots == null)
            {
                panel.Slots = new List<PanelSlotDefinition>();
                changed = true;
                continue;
            }
            changed |= panel.Slots.RemoveAll(slot =>
                GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                    slot?.AlarmId) ||
                !IsPersistedSlotAllowedOnPanelLocked(panel, slot)) > 0;
        }
        return changed;
    }

    private bool IsPersistedSlotAllowedOnPanelLocked(
        PanelDefinition panel,
        PanelSlotDefinition slot)
    {
        if (!PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return true;
        }
        if (EntityVanillaSlotPolicy.IsForEntity(
                slot,
                panel.OwnerEntityId))
        {
            return true;
        }
        if (slot == null ||
            !string.Equals(slot.Source, "custom", StringComparison.Ordinal) ||
            !PanelTopologyPolicy.TryGetRuleId(slot.AlarmId, out var ruleId))
        {
            return false;
        }
        var rule = Configuration.Rules.FirstOrDefault(candidate =>
            string.Equals(candidate?.Id, ruleId, StringComparison.Ordinal));
        return rule != null &&
               PanelTopologyPolicy.IsRuleAssignedToPanel(
                   rule,
                   panel,
                   Configuration.Panels);
    }

    private static AlarmView Clone(AlarmView source, long sequence = 0)
    {
        return new AlarmView
        {
            Key = source.Key,
            Name = source.Name,
            Detail = source.Detail,
            Source = source.Source,
            PanelId = source.PanelId,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            OverrideId = source.OverrideId,
            OccurrenceId = source.OccurrenceId,
            SlotId = source.SlotId,
            OccurrencePriority = source.OccurrencePriority,
            EntityId = source.EntityId,
            EntityPrototypeId = source.EntityPrototypeId,
            EntityTitle = source.EntityTitle,
            Sequence = sequence > 0 ? sequence : source.Sequence,
            Severity = source.Severity,
            IsActive = source.IsActive,
            IsAcknowledged = source.IsAcknowledged,
            IsOperatorSilenced = source.IsOperatorSilenced,
            OperatorSilencedAtGameTick =
                source.OperatorSilencedAtGameTick,
            IsGoneUnacknowledged = source.IsGoneUnacknowledged,
            IsMissingSource = source.IsMissingSource,
            LastValue = source.LastValue,
        };
    }

    private static VanillaNotificationRule CloneVanillaNotificationRule(
        VanillaNotificationRule source)
    {
        return new VanillaNotificationRule
        {
            AlarmId = source.AlarmId,
            Scope = source.Scope,
            Behavior = source.Behavior,
            EntityId = source.EntityId,
            EntityPrototypeId = source.EntityPrototypeId,
        };
    }

    private static InstrumentDefinition CloneInstrumentDefinition(
        InstrumentDefinition source)
    {
        return new InstrumentDefinition
        {
            Id = source.Id,
            Title = source.Title,
            DisplayType = source.DisplayType,
            EntityId = source.EntityId,
            EntityTitle = source.EntityTitle,
            EntityPrototypeId = source.EntityPrototypeId,
            MetricPath = source.MetricPath,
            MetricLabel = source.MetricLabel,
            Unit = source.Unit,
            Minimum = source.Minimum,
            Maximum = source.Maximum,
            PanelId = source.PanelId,
            Aggregation = source.Aggregation,
            HistoryDurationSeconds = source.HistoryDurationSeconds,
            HistoryDurationAmount = source.HistoryDurationAmount,
            HistoryDurationUnit = source.HistoryDurationUnit,
            Sources = (source.Sources ??
                    new List<InstrumentSourceDefinition>())
                .Where(item => item != null)
                .Select(item => new InstrumentSourceDefinition
                {
                    EntityId = item.EntityId,
                    EntityTitle = item.EntityTitle,
                    EntityPrototypeId = item.EntityPrototypeId,
                })
                .ToList(),
        };
    }

    private static AlarmRuleDefinition CloneRuleForEvaluation(
        AlarmRuleDefinition source)
    {
        return new AlarmRuleDefinition
        {
            Id = source.Id,
            PanelId = source.PanelId,
            LinkedPanelIds = (source.LinkedPanelIds ?? new List<string>())
                .ToList(),
            Name = source.Name,
            Severity = source.Severity,
            Logic = source.Logic,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            Enabled = source.Enabled,
            AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
            ActivationDelayTicks = source.ActivationDelayTicks,
            ResetDelayTicks = source.ResetDelayTicks,
            MinimumActiveTicks = source.MinimumActiveTicks,
            Escalation = AlarmEscalationPolicy.Clone(source.Escalation),
            Conditions = source.Conditions.Select(condition =>
                new ConditionDefinition
                {
                    EntityId = condition.EntityId,
                    EntityTitle = condition.EntityTitle,
                    EntityType = condition.EntityType,
                    MetricPath = condition.MetricPath,
                    MetricLabel = condition.MetricLabel,
                    Comparison = condition.Comparison,
                    Threshold = condition.Threshold,
                    ExpectedProductId = condition.ExpectedProductId,
                    EntityPrototypeId = condition.EntityPrototypeId,
                    ValueMode = condition.ValueMode,
                    ReferenceMetricPath = condition.ReferenceMetricPath,
                    ReferenceMetricLabel = condition.ReferenceMetricLabel,
                    InstrumentId = condition.InstrumentId,
                    TrendMode = condition.TrendMode,
                    WindowSeconds = condition.WindowSeconds,
                    DeltaThreshold = condition.DeltaThreshold,
                    WindowAmount = condition.WindowAmount,
                    WindowUnit = condition.WindowUnit,
                    Hysteresis = condition.Hysteresis,
                }).ToList(),
        };
    }

    private static SystemAlarmDefinition CloneSystemAlarmForEditing(
        SystemAlarmDefinition source)
    {
        return new SystemAlarmDefinition
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
            Stages = source.Stages.Select(stage =>
                new SystemAlarmStageDefinition
                {
                    Id = stage.Id,
                    Priority = stage.Priority,
                    Enabled = stage.Enabled,
                    Message = stage.Message,
                    Severity = stage.Severity,
                    Logic = stage.Logic,
                    ActiveColor = stage.ActiveColor,
                    SoundId = stage.SoundId,
                    ActivationDelayTicks = stage.ActivationDelayTicks,
                    ResetDelayTicks = stage.ResetDelayTicks,
                    MinimumActiveTicks = stage.MinimumActiveTicks,
                    OperatorAction = stage.OperatorAction,
                    Conditions = stage.Conditions.Select(condition =>
                        new SystemConditionDefinition
                        {
                            MetricId = condition.MetricId,
                            Comparison = condition.Comparison,
                            Threshold = condition.Threshold,
                            Hysteresis = condition.Hysteresis,
                        }).ToList(),
                }).ToList(),
        };
    }

    public static string OperatorText(ComparisonOperator comparison)
    {
        return comparison switch
        {
            ComparisonOperator.Less => "<",
            ComparisonOperator.LessOrEqual => "≤",
            ComparisonOperator.Equal => "=",
            ComparisonOperator.NotEqual => "≠",
            ComparisonOperator.GreaterOrEqual => "≥",
            ComparisonOperator.Greater => ">",
            _ => "?",
        };
    }

    private static string FormatGameTimeWindow(
        int amount,
        GameTimeUnit unit)
    {
        var key = unit switch
        {
            GameTimeUnit.Day => amount == 1
                ? "time.unit.day.one"
                : "time.unit.day.many",
            GameTimeUnit.Month => amount == 1
                ? "time.unit.month.one"
                : "time.unit.month.many",
            GameTimeUnit.Year => amount == 1
                ? "time.unit.year.one"
                : "time.unit.year.many",
            GameTimeUnit.Decade => amount == 1
                ? "time.unit.decade.one"
                : "time.unit.decade.many",
            _ => amount == 1
                ? "time.unit.century.one"
                : "time.unit.century.many",
        };
        var fallback = unit switch
        {
            GameTimeUnit.Day => amount == 1 ? "game day" : "game days",
            GameTimeUnit.Month => amount == 1 ? "game month" : "game months",
            GameTimeUnit.Year => amount == 1 ? "game year" : "game years",
            GameTimeUnit.Decade => amount == 1 ? "decade" : "decades",
            _ => amount == 1 ? "century" : "centuries",
        };
        return UnmaText.Format(
            "runtime.time_window",
            "{0} {1}",
            amount,
            UnmaText.Get(key, fallback));
    }
}
