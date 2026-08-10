using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
        public double StartedAtTicks;
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
    private readonly ISimLoopEvents m_simLoopEvents;
    private readonly UnmaStateStore m_store;
    private readonly ExternalDisplayNotificationWriter m_externalDisplay =
        new();
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
    private readonly Dictionary<string, string> m_instrumentSignatures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SustainedConditionState>
        m_sustainedConditionStates = new(StringComparer.Ordinal);

    private const int MaximumInstrumentHistorySamples = 100000;
    private const double InstrumentHistorySampleIntervalTicks =
        GameTimeWindowPolicy.SimTicksPerDay;

    private sealed class NotificationEntityAlias
    {
        public int OwnerEntityId;
        public string OwnerEntityPrototypeId = "";
        public string OwnerEntityTitle = "";
    }

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
    private int m_alarmPersistenceSuppressionDepth;
    private bool m_alarmPersistencePending;
    private bool m_disposed;
    private ExternalDefinitionLoadResult m_externalDefinitions;

    public UnmaConfiguration Configuration { get; }
    public UnmaSettings Settings => m_settings;
    public string LastPersistenceError { get; private set; } = "";

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
        IEnumerable<ExternalProviderDescriptor> externalProviders = null)
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
        RefreshDisabledVanillaOverrideIds();
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
    }

    private void RestoreAlarmMemories()
    {
        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var closedSuppressedHistory = false;
        foreach (var memory in Configuration.AlarmMemories)
        {
            if (IsSuppressedVanillaAlarm(
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
                        isAcknowledged: true);
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
        lock (m_configurationGate)
        {
            slots = (panel.Slots ?? new List<PanelSlotDefinition>())
                .Select(PanelSlotProjection.CloneSlot)
                .Where(slot =>
                    slot != null &&
                    IsPersistedSlotAllowedOnPanelLocked(panel, slot) &&
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
        if (!Settings.EnableAudio)
        {
            return null;
        }

        var disabledVanillaOverrideIds =
            GetDisabledVanillaOverrideIds();
        var vanillaRules = GetVanillaNotificationRulesSnapshot();
        lock (m_gate)
        {
            AlarmState best = null;
            foreach (var candidate in m_alarms.Values)
            {
                if (!candidate.View.RequiresAcknowledgement ||
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

    private int AcknowledgeProjectedSlots(
        PanelDefinition panel,
        ISet<string> targetSlotIds)
    {
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

        var acknowledgedCount = 0;
        lock (m_gate)
        {
            foreach (var alarm in m_alarms.Values)
            {
                if (!alarm.View.RequiresAcknowledgement)
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

                if (AcknowledgeAlarmStateLocked(alarm))
                {
                    acknowledgedCount++;
                }
            }
            if (acknowledgedCount > 0)
            {
                m_alarmHistoryRevision++;
            }
        }

        var prunedExternal = PruneRetiredExternalAlarms();
        if (acknowledgedCount > 0 || prunedExternal)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
        return acknowledgedCount;
    }

    private bool AcknowledgeAlarmStateLocked(AlarmState alarm)
    {
        if (alarm == null || !alarm.View.RequiresAcknowledgement)
        {
            return false;
        }

        if (alarm.View.IsGoneUnacknowledged)
        {
            UpdateHistoryFromStateLocked(alarm, true, true);
            alarm.View.IsGoneUnacknowledged = false;
            alarm.View.IsAcknowledged = false;
            return true;
        }

        alarm.View.IsAcknowledged = true;
        UpdateHistoryFromStateLocked(alarm, false, true);
        return true;
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
                SanitizeEntityPanelSlotsLocked();
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
        Dictionary<PanelDefinition, List<PanelSlotDefinition>>
            previousPanelSlots;
        Dictionary<PanelDefinition, List<string>> previousExcludedAlarmIds;
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
            previousPanelSlots = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.Slots ?? new List<PanelSlotDefinition>())
                    .Select(PanelSlotProjection.CloneSlot)
                    .ToList());
            previousExcludedAlarmIds = Configuration.Panels.ToDictionary(
                panel => panel,
                panel => (panel.ExcludedAlarmIds ?? new List<string>())
                    .ToList());
            Configuration.Rules[ruleIndex] = updatedRule;
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Rules[ruleIndex] = previousRule;
                foreach (var pair in previousPanelSlots)
                {
                    pair.Key.Slots = pair.Value;
                }
                foreach (var pair in previousExcludedAlarmIds)
                {
                    pair.Key.ExcludedAlarmIds = pair.Value;
                }
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
                out var restoredHistory);
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = restoredMemories;
                Configuration.AlarmHistory = restoredHistory;
            }
            return false;
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

    public bool UpdatePanelSettings(
        string panelId,
        string name,
        int columns,
        bool includeVanilla,
        bool includeSystem,
        string notificationFilter)
    {
        panelId = panelId?.Trim() ?? "";
        if (panelId.Length == 0)
        {
            return false;
        }

        lock (m_persistenceGate)
        {
            PanelDefinition panel;
            string previousName;
            int previousColumns;
            bool previousIncludeVanilla;
            bool previousIncludeSystem;
            string previousFilter;
            Dictionary<PanelDefinition, List<PanelSlotDefinition>>
                previousPanelSlots;
            lock (m_configurationGate)
            {
                panel = Configuration.Panels.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate?.Id,
                        panelId,
                        StringComparison.Ordinal));
                if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
                {
                    return false;
                }

                previousName = panel.Name;
                previousColumns = panel.Columns;
                previousIncludeVanilla = panel.IncludeVanilla;
                previousIncludeSystem = panel.IncludeSystem;
                previousFilter = panel.NotificationFilter;
                previousPanelSlots = Configuration.Panels.ToDictionary(
                    candidate => candidate,
                    candidate => (candidate.Slots ??
                            new List<PanelSlotDefinition>())
                        .Select(PanelSlotProjection.CloneSlot)
                        .ToList());

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

            if (SaveConfiguration())
            {
                return true;
            }

            lock (m_configurationGate)
            {
                panel.Name = previousName;
                panel.Columns = previousColumns;
                panel.IncludeVanilla = previousIncludeVanilla;
                panel.IncludeSystem = previousIncludeSystem;
                panel.NotificationFilter = previousFilter;
                foreach (var slotSnapshot in previousPanelSlots)
                {
                    slotSnapshot.Key.Slots = slotSnapshot.Value;
                }
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
                candidate => candidate.EntityId >= 0
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
                    });
                    history.SetState(
                        isGone: true,
                        isAcknowledged: true);
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
                        pair.Value.View))
                .ToArray();
            if (matchingStates.Length == 0)
            {
                return;
            }

            var sequences = new HashSet<long>(
                matchingStates
                    .Select(pair => pair.Value.Sequence)
                    .Where(sequence => sequence > 0));
            foreach (var matchingState in matchingStates)
            {
                m_alarms.Remove(matchingState.Key);
            }
            m_alarmHistory.RemoveAll(history =>
                sequences.Contains(history.Sequence));
            m_alarmHistoryRevision++;
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
        m_entityRemovedEvent.RemoveNonSaveable(
            this,
            OnEntityRemoved);
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
            m_lastInstrumentValues.Clear();
            foreach (var instrument in instruments)
            {
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
            RemoveSustainedStatesForRule(rule.Id);
            ForceNormal("rule:" + rule.Id);
            return;
        }

        var values = new List<bool>(rule.Conditions.Count);
        var details = new List<string>(rule.Conditions.Count);
        var missingSource = false;
        var lastValue = 0d;

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
                    values.Add(false);
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
                        var sustained = EvaluateSustainedCondition(
                            sustainedStateKey,
                            condition,
                            instrumentValue,
                            windowTicks);
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
                        values.Add(false);
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
                values.Add(AlarmEvaluation.Compare(
                    instrumentValue,
                    condition.Comparison,
                    condition.Threshold));
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
                    values.Add(false);
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
                    values.Add(false);
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
                    values.Add(false);
                    details.Add(UnmaText.Format(
                        "runtime.condition.reference_not_calculable",
                        "{0}: reference cannot be calculated",
                        condition.MetricLabel));
                    continue;
                }

                lastValue = globalComparable;
                values.Add(AlarmEvaluation.Compare(
                    globalComparable,
                    condition.Comparison,
                    condition.Threshold));
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
                values.Add(false);
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
                values.Add(false);
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
                values.Add(false);
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
                values.Add(false);
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
                values.Add(false);
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
                values.Add(false);
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
                values.Add(false);
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

        var isActive = AlarmEvaluation.Combine(values, rule.Logic);
        SetAlarm(
            "rule:" + rule.Id,
            rule.Name,
            string.Join(
                rule.Logic == AlarmLogic.All ? UnmaText.Get("auto.a3f10eb98ea4") : UnmaText.Get("auto.5f15b34155a9"),
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

    private bool EvaluateSustainedCondition(
        string stateKey,
        ConditionDefinition condition,
        double currentValue,
        int windowTicks)
    {
        if (!AlarmEvaluation.Compare(
                currentValue,
                condition.Comparison,
                condition.Threshold))
        {
            m_sustainedConditionStates.Remove(stateKey);
            return false;
        }

        var signature = condition.InstrumentId + "|" +
                        (int)condition.Comparison + "|" +
                        condition.Threshold.ToString(
                            "R",
                            CultureInfo.InvariantCulture) + "|" +
                        windowTicks;
        var nowTicks = (double)m_calendar.RealTime.Ticks;
        if (!m_sustainedConditionStates.TryGetValue(
                stateKey,
                out var state) ||
            !string.Equals(
                state.Signature,
                signature,
                StringComparison.Ordinal) ||
            nowTicks < state.StartedAtTicks)
        {
            m_sustainedConditionStates[stateKey] =
                new SustainedConditionState
                {
                    Signature = signature,
                    StartedAtTicks = nowTicks,
                };
            return false;
        }
        return nowTicks - state.StartedAtTicks >= windowTicks;
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
        var slotId = overrideId;
        var message = notification.Message.Value;
        var severity = ClassifyNotification(notification);
        var detail = id;
        var entityId = -1;
        var entityPrototypeId = "";
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
            if (notificationObject is IEntity entity)
            {
                entityId = entity.Id.Value;
                entityPrototypeId = entity.Prototype.Id.Value;
                slotId += ":entity:" + entity.Id.Value;
            }
        }

        if (ResolveVanillaNotificationBehavior(
                overrideId,
                entityId,
                entityPrototypeId) == VanillaNotificationBehavior.Ignored)
        {
            return;
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
        if (!GetVanillaNotificationEnabled(overrideId))
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
        if (!GetVanillaNotificationEnabled(
                "vanilla:" + notification.Proto.Id.Value))
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
        string slotId = "",
        int entityId = -1,
        string entityPrototypeId = "",
        string entityTitle = "")
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
        if (shouldPublishExternal)
        {
            PublishExternalDisplayAlarm(slotCandidate, true);
        }
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
            out var alarmHistory);
        lock (m_configurationGate)
        {
            Configuration.AlarmMemories = alarmMemories;
            Configuration.AlarmHistory = alarmHistory;
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
