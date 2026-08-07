using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.Simulation;
using UNMA.Api;
using UNMA.Domain;
using UNMA.Extensions;
using UNMA.Localization;

namespace UNMA.Runtime;

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

    private sealed class ExternalEntityEvaluation
    {
        public bool IsActive;
        public bool IsMissingSource;
        public double LastValue;
        public string Detail = "";
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
    private readonly object m_externalDefinitionsGate = new();
    private readonly object m_removedEntitiesGate = new();
    private readonly INotificationsManager m_notificationsManager;
    private readonly IEntitiesManager m_entitiesManager;
    private readonly IWorkersManager m_workersManager;
    private readonly SettlementsManager m_settlementsManager;
    private readonly PopsHealthManager m_healthManager;
    private readonly ISimLoopEvents m_simLoopEvents;
    private readonly UnmaStateStore m_store;
    private readonly ExternalProviderDescriptor[] m_externalProviders;
    private readonly Dictionary<string, AlarmState> m_alarms =
        new(StringComparer.Ordinal);
    private readonly List<AlarmHistoryDefinition> m_alarmHistory = new();
    private readonly HashSet<string> m_previousExternalKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_retiredExternalKeys =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool>
        m_externalAutoAcknowledgeByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, IEntity> m_removedEntityCandidates = new();
    private readonly Dictionary<int, int>
        m_missingStaticEntityObservations = new();
    private readonly Dictionary<string, bool> m_staticEntityTypeCache =
        new(StringComparer.Ordinal);

    private long m_sequence;
    private long m_alarmHistoryRevision;
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
    private bool m_suppressAlarmPersistence;
    private bool m_alarmPersistencePending;
    private bool m_disposed;
    private ExternalDefinitionLoadResult m_externalDefinitions;

    public UnmaConfiguration Configuration { get; }
    public UnmaSettings Settings => m_settings;
    public string LastPersistenceError { get; private set; } = "";

    public UnmaRuntime(
        INotificationsManager notificationsManager,
        IEntitiesManager entitiesManager,
        IWorkersManager workersManager,
        SettlementsManager settlementsManager,
        PopsHealthManager healthManager,
        ISimLoopEvents simLoopEvents,
        UnmaStateStore store,
        UnmaSettings settings,
        IEnumerable<ExternalProviderDescriptor> externalProviders = null)
    {
        m_notificationsManager = notificationsManager;
        m_entitiesManager = entitiesManager;
        m_workersManager = workersManager;
        m_settlementsManager = settlementsManager;
        m_healthManager = healthManager;
        m_simLoopEvents = simLoopEvents;
        m_store = store;
        m_externalProviders = (externalProviders ??
                Enumerable.Empty<ExternalProviderDescriptor>())
            .Where(provider => provider != null)
            .Select(provider => new ExternalProviderDescriptor(
                provider.Id,
                provider.RootDirectoryPath))
            .ToArray();
        m_settings = settings ?? new UnmaSettings();
        Configuration = store.Load();
        RestoreAlarmHistory();
        RestoreAlarmMemories();
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
        foreach (var item in Configuration.AlarmHistory)
        {
            m_alarmHistory.Add(CloneHistory(item));
            m_sequence = Math.Max(m_sequence, item.Sequence);
        }
        if (m_alarmHistory.Count > 0)
        {
            m_alarmHistoryRevision = 1;
        }
    }

    public void Initialize()
    {
        m_entitiesManager.EntityRemoved.Add(this, OnEntityRemoved);
        m_notificationsManager.NotificationAdded += OnNotificationAdded;
        m_notificationsManager.NotificationRemoved += OnNotificationRemoved;
        m_notificationsManager.NotificationSuppressChanged +=
            OnNotificationSuppressChanged;

        m_suppressAlarmPersistence = true;
        try
        {
            var currentNotifications = m_notificationsManager
                .FetchAllNotifications()
                .ToArray();
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
            m_suppressAlarmPersistence = false;
        }
        PersistAlarmState();

        m_simLoopEvents.UpdateEndForUi.AddNonSaveable(
            this,
            OnUpdateEndForUi);
        m_simListenerAdded = true;
    }

    private void RestoreAlarmMemories()
    {
        foreach (var memory in Configuration.AlarmMemories)
        {
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
            state.View.IsGoneUnacknowledged =
                memory.IsGoneUnacknowledged;
            state.View.IsMissingSource = memory.IsMissingSource;
            state.View.LastValue = memory.LastValue;
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
                "UNMA: Fremdmod-Definition " + diagnostic.Code +
                " [" + diagnostic.ProviderId + "] " +
                diagnostic.Message);
        }
        if (loaded.Diagnostics.Count > 20)
        {
            Log.Warning(
                "UNMA: " + (loaded.Diagnostics.Count - 20) +
                " weitere Fremdmod-Diagnosen wurden zusammengefasst.");
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
                    "UNMA: LangLib-Namensraum '" + alias.Namespace +
                    "' gehört bereits zu '" + existingOwner +
                    "'; Registrierung von '" + alias.Owner +
                    "' wurde abgewiesen.");
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

    private void Evaluate()
    {
        var settings = m_settings;
        if (settings.EnableSystemAlarms)
        {
            EvaluateSystemAlarms(CaptureSystemMetrics());
        }
        EvaluateSustainedVanillaAlarms();
        EvaluateCustomRules();
        EvaluateExternalAlarms();
    }

    public IReadOnlyList<AlarmView> GetViews(PanelDefinition panel)
    {
        if (panel == null)
        {
            return Array.Empty<AlarmView>();
        }

        if (panel.IsDashboard)
        {
            AlarmView[] activeCandidates;
            lock (m_gate)
            {
                activeCandidates = m_alarms.Values
                    .Where(state => state.View.IsActive)
                    .Select(state => Clone(state.View, state.Sequence))
                    .ToArray();
            }
            return PanelSlotProjection.ProjectActive(activeCandidates);
        }

        PanelSlotDefinition[] slots;
        lock (m_configurationGate)
        {
            slots = (panel.Slots ?? new List<PanelSlotDefinition>())
                .Select(PanelSlotProjection.CloneSlot)
                .Where(slot => slot != null)
                .ToArray();
        }
        AlarmView[] candidates;
        var slotIds = new HashSet<string>(
            slots.Select(slot => slot.AlarmId),
            StringComparer.Ordinal);
        lock (m_gate)
        {
            candidates = m_alarms.Values
                .Where(state => slotIds.Contains(
                    PanelSlotProjection.StableAlarmId(state.View)))
                .Select(state => Clone(state.View, state.Sequence))
                .ToArray();
        }
        return PanelSlotProjection.Project(slots, candidates);
    }

    public AlarmView GetAudibleAlarm()
    {
        if (!Settings.EnableAudio)
        {
            return null;
        }

        lock (m_gate)
        {
            AlarmState best = null;
            foreach (var candidate in m_alarms.Values)
            {
                if (!candidate.View.RequiresAcknowledgement ||
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

    public int ActiveCount
    {
        get
        {
            lock (m_gate)
            {
                return m_alarms.Values.Count(
                    state => state.View.IsActive);
            }
        }
    }

    public int UnacknowledgedCount
    {
        get
        {
            lock (m_gate)
            {
                return m_alarms.Values.Count(
                    state => state.View.RequiresAcknowledgement);
            }
        }
    }

    public void AcknowledgeAll()
    {
        var changed = false;
        lock (m_gate)
        {
            foreach (var item in m_alarmHistory)
            {
                if (!item.IsAcknowledged)
                {
                    item.IsAcknowledged = true;
                    changed = true;
                }
            }
            foreach (var alarm in m_alarms.Values)
            {
                if (alarm.View.IsGoneUnacknowledged)
                {
                    alarm.View.IsGoneUnacknowledged = false;
                    alarm.View.IsAcknowledged = false;
                    changed = true;
                }
                else if (alarm.View.IsActive &&
                         !alarm.View.IsAcknowledged)
                {
                    alarm.View.IsAcknowledged = true;
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

    public bool SaveConfiguration()
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

        lock (m_persistenceGate)
        {
            CapturePersistentAlarmState(
                out var alarmMemories,
                out var alarmHistory);
            bool saved;
            string error;
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = alarmMemories;
                Configuration.AlarmHistory = alarmHistory;
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
        out List<AlarmHistoryDefinition> alarmHistory)
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
                })
                .ToList();
            alarmHistory = m_alarmHistory
                .OrderBy(item => item.Sequence)
                .Select(CloneHistory)
                .ToList();
        }
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
        if (m_suppressAlarmPersistence)
        {
            m_alarmPersistencePending = true;
            return;
        }
        m_alarmPersistencePending = false;
        SaveConfiguration();
    }

    public bool AddRule(
        AlarmRuleDefinition rule,
        int preferredSlotIndex = -1)
    {
        if (rule == null)
        {
            return false;
        }

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

        AlarmRuleDefinition previousRule;
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
            Configuration.Rules[ruleIndex] = updatedRule;
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Rules[ruleIndex] = previousRule;
            }
            return false;
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
                out var restoredHistory);
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = restoredMemories;
                Configuration.AlarmHistory = restoredHistory;
            }
            return false;
        }
        removedCount = removedRules.Length;
        return true;
    }

    public bool SetRuleEnabled(string ruleId, bool enabled)
    {
        AlarmRuleDefinition rule;
        bool previous;
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
            rule.Enabled = enabled;
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                rule.Enabled = previous;
            }
            return false;
        }

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

        SystemAlarmDefinition previousAlarm;
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
            Configuration.SystemAlarms[alarmIndex] = replacement;
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.SystemAlarms[alarmIndex] = previousAlarm;
            }
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
        PanelDefinition panel;
        AlarmRuleDefinition[] removedRules;
        var panelIndex = -1;
        lock (m_configurationGate)
        {
            if (Configuration.Panels.Count <= 1)
            {
                return false;
            }
            panelIndex = Configuration.Panels.FindIndex(
                candidate => string.Equals(
                    candidate.Id,
                    panelId,
                    StringComparison.Ordinal));
            if (panelIndex < 0)
            {
                return false;
            }

            panel = Configuration.Panels[panelIndex];
            if (panel.IsDashboard)
            {
                return false;
            }
            removedRules = Configuration.Rules
                .Where(rule => string.Equals(
                    rule.PanelId,
                    panelId,
                    StringComparison.Ordinal))
                .ToArray();
            Configuration.Panels.RemoveAt(panelIndex);
            Configuration.Rules.RemoveAll(rule => string.Equals(
                rule.PanelId,
                panelId,
                StringComparison.Ordinal));
        }

        var removedAlarmStates = new Dictionary<string, AlarmState>(
            StringComparer.Ordinal);
        List<AlarmHistoryDefinition> previousHistory;
        lock (m_gate)
        {
            previousHistory = m_alarmHistory
                .Select(CloneHistory)
                .ToList();
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
                Configuration.Panels.Insert(panelIndex, panel);
                Configuration.Rules.AddRange(removedRules);
            }
            lock (m_gate)
            {
                foreach (var pair in removedAlarmStates)
                {
                    m_alarms[pair.Key] = pair.Value;
                }
                m_alarmHistory.Clear();
                m_alarmHistory.AddRange(previousHistory);
                m_alarmHistoryRevision++;
            }
            return false;
        }
        return true;
    }

    public bool AddPanel(PanelDefinition panel)
    {
        if (panel == null)
        {
            return false;
        }

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
                    Detail = "Systemmeldung",
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
                    Detail = rule.Conditions.Count + " Bedingung(en)",
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
        lock (m_gate)
        {
            return m_alarms.Values
                .Where(state =>
                    (state.View.Source == "vanilla" ||
                     state.View.Source == "external") &&
                    !string.IsNullOrWhiteSpace(state.View.OverrideId))
                .GroupBy(
                    state => state.View.OverrideId,
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(state => state.Sequence)
                    .First())
                .OrderBy(state => state.View.Source)
                .ThenBy(state => state.View.Name)
                .Select(state => Clone(state.View))
                .ToArray();
        }
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
                        history.IsGone = true;
                        history.IsAcknowledged = true;
                    }
                    state.View.IsGoneUnacknowledged = false;
                    state.View.IsAcknowledged = false;
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
        m_notificationsManager.NotificationAdded -= OnNotificationAdded;
        m_notificationsManager.NotificationRemoved -= OnNotificationRemoved;
        m_notificationsManager.NotificationSuppressChanged -=
            OnNotificationSuppressChanged;
        m_entitiesManager.EntityRemoved.Remove(this, OnEntityRemoved);
    }

    private void OnUpdateEndForUi()
    {
        if (m_disposed || !m_gameplayActive)
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
        lock (m_persistenceGate)
        {
            lock (m_configurationGate)
            {
                ruleIds = CustomRuleLifecyclePolicy
                    .FindRulesReferencingEntities(
                        Configuration.Rules,
                        removedEntityIds)
                    .ToArray();
            }
            if (ruleIds.Length == 0)
            {
                return true;
            }
            if (!RemoveRulesWithPersistenceLock(ruleIds, out removedCount))
            {
                return false;
            }
        }

        Log.Info(
            "UNMA: " + removedCount +
            " Meldung(en) entfernter Entitäten automatisch gelöscht.");
        foreach (var entityId in removedEntityIds)
        {
            m_missingStaticEntityObservations.Remove(entityId);
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
                condition != null && IsStaticEntityType(condition.EntityType))
            .Select(condition => condition.EntityId));
        foreach (var staleId in m_missingStaticEntityObservations.Keys
                     .Where(id => !staticEntityIds.Contains(id))
                     .ToArray())
        {
            m_missingStaticEntityObservations.Remove(staleId);
        }

        var confirmed = new List<int>();
        foreach (var entityId in staticEntityIds)
        {
            var entity = m_entitiesManager.GetEntity(new EntityId(entityId));
            if (!entity.IsNone && !entity.Value.IsDestroyed)
            {
                m_missingStaticEntityObservations.Remove(entityId);
                continue;
            }
            if (!entity.IsNone)
            {
                confirmed.Add(entityId);
                continue;
            }

            m_missingStaticEntityObservations.TryGetValue(
                entityId,
                out var observations);
            observations++;
            m_missingStaticEntityObservations[entityId] = observations;
            if (CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
                    observations))
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
                    "Die ausgewählte Entität ist nicht mehr verfügbar.");
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
                "Messwerte konnten nicht gelesen werden: " +
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

            var stage = AlarmEvaluation.SelectSystemStage(alarm, metrics);
            if (stage == null)
            {
                ClearAlarm(
                    alarm.Id,
                    alarm.AutoAcknowledgeOnClear);
                continue;
            }

            var soundId = string.IsNullOrWhiteSpace(stage.SoundId)
                ? "auto"
                : stage.SoundId;
            var activeColor = string.IsNullOrWhiteSpace(stage.ActiveColor) ||
                              string.Equals(
                                  stage.ActiveColor,
                                  "auto",
                                  StringComparison.OrdinalIgnoreCase)
                ? ColorFor(stage.Severity)
                : stage.ActiveColor;

            SetAlarm(
                alarm.Id,
                string.IsNullOrWhiteSpace(stage.Message)
                    ? alarm.DisplayName
                    : stage.Message,
                FormatSystemAlarmDetail(alarm.Id, metrics),
                "system",
                "",
                stage.Severity,
                true,
                false,
                soundId,
                activeColor,
                LastValueForSystemAlarm(alarm.Id, metrics),
                overrideId: alarm.Id,
                autoAcknowledgeOnClear:
                    alarm.AutoAcknowledgeOnClear,
                occurrenceId: stage.Id,
                occurrencePriority: stage.Priority,
                slotId: alarm.Id);
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
            history.IsGone = false;
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
        lock (m_systemMetricsGate)
        {
            m_lastSystemMetrics = new Dictionary<string, double>(
                metrics,
                StringComparer.Ordinal);
        }
        return metrics;
    }

    private static string FormatSystemAlarmDetail(
        string alarmId,
        IReadOnlyDictionary<string, double> metrics)
    {
        if (string.Equals(alarmId, "system:health", StringComparison.Ordinal))
        {
            return "Gesundheit " + Metric(metrics, "health.value") +
                   " (neutral 10) · Krankheit " +
                   Metric(metrics, "health.disease_penalty") +
                   " · Krankheitsmortalität " +
                   Metric(metrics, "health.disease_mortality") + " %" +
                   " · Pollution/Müll " +
                   Metric(metrics, "health.pollution_penalty") +
                   " · Arbeitsreserve " +
                   Metric(metrics, "workers.reserve_percent") + " %" +
                   " · erwarteter Nettoverlust " +
                   Metric(metrics, "health.expected_loss") + "/Monat";
        }
        if (string.Equals(alarmId, "system:food", StringComparison.Ordinal))
        {
            return "Nahrung " + Metric(metrics, "food.months") +
                   " Monate · Hunger " +
                   (MetricValue(metrics, "food.starving") >= 1d
                       ? "JA"
                       : "nein") +
                   " · verhungert " +
                   Metric(metrics, "food.starved_last_month");
        }
        if (string.Equals(alarmId, "system:workers", StringComparison.Ordinal))
        {
            return "Arbeitsreserve " +
                   Metric(metrics, "workers.reserve_percent") + " % · " +
                   "frei/fehlend " +
                   Metric(metrics, "workers.free_or_missing");
        }
        return "Systemmeldung";
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

    private void EvaluateCustomRules()
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
            EvaluateCustomRule(rule);
        }
    }

    private void EvaluateCustomRule(AlarmRuleDefinition rule)
    {
        if (!rule.Enabled)
        {
            ForceNormal("rule:" + rule.Id);
            return;
        }

        var values = new List<bool>(rule.Conditions.Count);
        var details = new List<string>(rule.Conditions.Count);
        var missingSource = false;
        var lastValue = 0d;

        foreach (var condition in rule.Conditions)
        {
            var option = m_entitiesManager.GetEntity(
                new EntityId(condition.EntityId));
            if (option.IsNone)
            {
                missingSource = true;
                values.Add(false);
                details.Add(condition.EntityTitle + ": Quelle fehlt");
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
                values.Add(false);
                details.Add(condition.EntityTitle + ": falsche Entität");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(condition.EntityPrototypeId) &&
                !string.Equals(
                    condition.EntityPrototypeId,
                    entity.Prototype.Id.Value,
                    StringComparison.Ordinal))
            {
                missingSource = true;
                values.Add(false);
                details.Add(condition.EntityTitle + ": falscher Prototyp");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(condition.ExpectedProductId) &&
                !string.Equals(
                    condition.ExpectedProductId,
                    EntityMetricCatalog.TryGetStoredProductId(entity),
                    StringComparison.Ordinal))
            {
                values.Add(false);
                details.Add(condition.EntityTitle + ": anderes Produkt");
                continue;
            }

            if (!EntityMetricCatalog.TryRead(
                    entity,
                    condition.MetricPath,
                    out var actual))
            {
                missingSource = true;
                values.Add(false);
                details.Add(condition.EntityTitle + ": Messwert fehlt");
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
                values.Add(false);
                details.Add(
                    condition.EntityTitle + ": Bezugsmesswert fehlt");
                continue;
            }

            if (!AlarmEvaluation.TryCalculateComparable(
                    actual,
                    condition.ValueMode,
                    reference,
                    out var comparable))
            {
                missingSource = true;
                values.Add(false);
                details.Add(
                    condition.EntityTitle + " · " +
                    condition.MetricLabel +
                    ": Bezug nicht berechenbar (ist " +
                    actual.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) +
                    ", Bezug " +
                    reference.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + ")");
                continue;
            }

            lastValue = comparable;
            var matches = AlarmEvaluation.Compare(
                comparable,
                condition.Comparison,
                condition.Threshold);
            values.Add(matches);
            if (condition.ValueMode ==
                ConditionValueMode.PercentOfReference)
            {
                var referenceLabel = string.IsNullOrWhiteSpace(
                    condition.ReferenceMetricLabel)
                    ? condition.ReferenceMetricPath
                    : condition.ReferenceMetricLabel;
                details.Add(
                    condition.EntityTitle + " · " +
                    condition.MetricLabel + " % von " + referenceLabel +
                    " " + OperatorText(condition.Comparison) + " " +
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + " % (ist " +
                    comparable.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + " %; " +
                    actual.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + " / " +
                    reference.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + ")");
            }
            else
            {
                details.Add(
                    condition.EntityTitle + " · " +
                    condition.MetricLabel + " " +
                    OperatorText(condition.Comparison) + " " +
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) +
                    " (ist " +
                    actual.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + ")");
            }
        }

        var isActive = AlarmEvaluation.Combine(values, rule.Logic);
        SetAlarm(
            "rule:" + rule.Id,
            rule.Name,
            string.Join(
                rule.Logic == AlarmLogic.All ? " UND " : " ODER ",
                details),
            "custom",
            rule.PanelId,
            rule.Severity,
            isActive,
            missingSource,
            rule.SoundId,
            rule.ActiveColor,
            lastValue,
            autoAcknowledgeOnClear:
                rule.AutoAcknowledgeOnClear,
            occurrenceId: rule.Id,
            slotId: "rule:" + rule.Id);
    }

    private void EvaluateExternalAlarms()
    {
        var wasSuppressed = m_suppressAlarmPersistence;
        m_suppressAlarmPersistence = true;
        try
        {
            EvaluateExternalAlarmsCore();
        }
        finally
        {
            m_suppressAlarmPersistence = wasSuppressed;
            if (!wasSuppressed && m_alarmPersistencePending)
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
                    "UNMA: C#-Alarmvorlage '" + template.OwnerModId +
                    ":" + template.Id +
                    "' kollidiert mit JSON; die JSON-Definition gilt.");
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
                    "UNMA: Entitäten für Fremdmod-Alarme konnten nicht " +
                    "gelesen werden: " + exception.Message);
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
                "Keine passende Entität aktiv")
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
                details.Add(label + ": Messwert fehlt");
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
                details.Add(label + ": Bezugsmesswert fehlt");
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
                details.Add(label + ": Bezug nicht berechenbar");
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
                details.Add(
                    label + " % von " + referenceLabel + " " +
                    condition.Operator + " " +
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) +
                    " % (ist " + comparable.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + " %; " +
                    actual.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + " / " +
                    reference.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + ")");
            }
            else
            {
                details.Add(
                    label + " " + condition.Operator + " " +
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) +
                    " (ist " + comparable.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture) + ")");
            }
        }

        result.IsActive = AlarmEvaluation.Combine(
            values,
            string.Equals(template.Logic, "any", StringComparison.Ordinal)
                ? AlarmLogic.Any
                : AlarmLogic.All);
        result.Detail = string.Join(
            string.Equals(template.Logic, "any", StringComparison.Ordinal)
                ? " ODER "
                : " UND ",
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
        if (!SustainedVanillaAlarmPolicy.ShouldProcessNotification(
                id,
                m_settlementsManager.LastPopulationDiff))
        {
            return;
        }
        var reconciledLegacyHistory =
            SustainedVanillaAlarmPolicy.IsSustainedPrototype(id) &&
            RestoreSustainedVanillaAlarmFromHistory(id);
        var overrideId = "vanilla:" + id;
        var slotId = overrideId;
        var message = notification.Message.Value;
        var severity = ClassifyNotification(notification);
        var detail = id;
        if (notification.Object.HasValue)
        {
            var notificationObject = notification.Object.Value;
            var objectTitle = notificationObject.DefaultTitle.Value;
            if (!string.IsNullOrWhiteSpace(objectTitle))
            {
                detail += " · " + objectTitle;
            }
            if (notificationObject is IEntity entity)
            {
                slotId += ":entity:" + entity.Id.Value;
            }
        }

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
            slotId: slotId);
        if (reconciledLegacyHistory)
        {
            PersistAlarmState();
        }
    }

    private void OnNotificationRemoved(INotification notification)
    {
        var prototypeId = notification.Proto.Id.Value;
        if (SustainedVanillaAlarmPolicy.IgnoresNotificationRemoval(
                prototypeId))
        {
            return;
        }
        var overrideId = "vanilla:" + prototypeId;
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
                    history.IsAcknowledged = true;
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
        return SustainedVanillaAlarmPolicy.AlarmKeyForNotification(
            notification.Proto.Id.Value,
            NotificationKey(notification));
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
        string slotId = "")
    {
        var shouldPersist = false;
        AlarmView slotCandidate;
        lock (m_gate)
        {
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
            state.View.Name = name ?? "MELDUNG";
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
            state.View.ActiveColor = string.IsNullOrWhiteSpace(activeColor)
                ? ColorFor(severity)
                : activeColor;
            state.View.LastValue = lastValue;

            if (transition.IsNewOccurrence)
            {
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
    }

    private void ClearAlarm(
        string key,
        bool autoAcknowledgeOnClear,
        bool persist = true)
    {
        var changed = false;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var state))
            {
                var wasActive = state.View.IsActive;
                var wasAcknowledged = state.View.IsAcknowledged;
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
                    wasGoneUnacknowledged !=
                    state.View.IsGoneUnacknowledged ||
                    historyChanged;
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
    }

    private void ForceNormal(string key, bool persist = true)
    {
        var changed = false;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var state))
            {
                changed = state.View.IsLatched ||
                          state.View.IsAcknowledged;
                var historyChanged = CloseHistoryLocked(
                    state.Sequence,
                    state.View.IsAcknowledged);
                state.View.IsActive = false;
                state.View.IsAcknowledged = false;
                state.View.IsGoneUnacknowledged = false;
                changed |= historyChanged;
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
        return history.SetState(true, acknowledged);
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
        history.Source = state.View.Source;
        history.PanelId = state.View.PanelId;
        history.Severity = state.View.Severity;
        return history.SetState(isGone, isAcknowledged) || changed;
    }

    private static AlarmHistoryDefinition CreateHistoryFromState(
        AlarmState state)
    {
        return new AlarmHistoryDefinition
        {
            Sequence = state.Sequence,
            AlarmKey = state.View.Key,
            Message = state.View.Name,
            Detail = state.View.Detail,
            Source = state.View.Source,
            PanelId = state.View.PanelId,
            Severity = state.View.Severity,
            IsGone = !state.View.IsActive,
            IsAcknowledged = state.View.IsActive &&
                             state.View.IsAcknowledged,
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
        };
    }

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
        var slot = PanelSlotProjection.CreateSlot(view);
        if (slot == null)
        {
            return false;
        }

        var changed = false;
        lock (m_configurationGate)
        {
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

    private static bool IsVisibleOnPanel(
        AlarmView view,
        PanelDefinition panel,
        IReadOnlyList<string> filters)
    {
        if (view.Source == "custom" || view.Source == "external")
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
            Sequence = sequence > 0 ? sequence : source.Sequence,
            Severity = source.Severity,
            IsActive = source.IsActive,
            IsAcknowledged = source.IsAcknowledged,
            IsGoneUnacknowledged = source.IsGoneUnacknowledged,
            IsMissingSource = source.IsMissingSource,
            LastValue = source.LastValue,
        };
    }

    private static AlarmRuleDefinition CloneRuleForEvaluation(
        AlarmRuleDefinition source)
    {
        return new AlarmRuleDefinition
        {
            Id = source.Id,
            PanelId = source.PanelId,
            Name = source.Name,
            Severity = source.Severity,
            Logic = source.Logic,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            Enabled = source.Enabled,
            AutoAcknowledgeOnClear = source.AutoAcknowledgeOnClear,
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
                    Conditions = stage.Conditions.Select(condition =>
                        new SystemConditionDefinition
                        {
                            MetricId = condition.MetricId,
                            Comparison = condition.Comparison,
                            Threshold = condition.Threshold,
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
}
