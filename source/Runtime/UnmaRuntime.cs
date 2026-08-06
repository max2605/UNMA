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
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.Simulation;
using UNMA.Domain;

namespace UNMA.Runtime;

public sealed class UnmaSettings
{
    public bool ShowOnGameStart = true;
    public bool EnableAudio = true;
    public int AudioVolumePercent = 65;
    public int PollIntervalMs = 500;
    public bool EnableSystemAlarms = true;
}

public sealed class UnmaRuntime : IDisposable
{
    private sealed class AlarmState
    {
        public readonly AlarmView View = new();
        public long Sequence;
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
    private readonly INotificationsManager m_notificationsManager;
    private readonly IEntitiesManager m_entitiesManager;
    private readonly IWorkersManager m_workersManager;
    private readonly SettlementsManager m_settlementsManager;
    private readonly PopsHealthManager m_healthManager;
    private readonly ISimLoopEvents m_simLoopEvents;
    private readonly UnmaStateStore m_store;
    private readonly Dictionary<string, AlarmState> m_alarms =
        new(StringComparer.Ordinal);

    private long m_sequence;
    private long m_nextEvaluationTimestamp;
    private long m_nextEvaluationErrorLogTimestamp;
    private volatile bool m_gameplayActive;
    private volatile UnmaSettings m_settings;
    private int m_requestedInspectionEntityId = -1;
    private long m_inspectionRequestGeneration;
    private EntityInspectionSnapshot m_completedInspection;
    private Dictionary<string, double> m_lastSystemMetrics =
        new(StringComparer.Ordinal);
    private bool m_simListenerAdded;
    private bool m_suppressAlarmPersistence;
    private bool m_disposed;

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
        UnmaSettings settings)
    {
        m_notificationsManager = notificationsManager;
        m_entitiesManager = entitiesManager;
        m_workersManager = workersManager;
        m_settlementsManager = settlementsManager;
        m_healthManager = healthManager;
        m_simLoopEvents = simLoopEvents;
        m_store = store;
        m_settings = settings ?? new UnmaSettings();
        Configuration = store.Load();
        RestoreAlarmMemories();
    }

    public void Initialize()
    {
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
            state.View.OccurrencePriority = memory.OccurrencePriority;
            state.View.Severity = memory.Severity;
            state.View.IsActive = memory.IsActive;
            state.View.IsAcknowledged = memory.IsAcknowledged;
            state.View.IsGoneUnacknowledged =
                memory.IsGoneUnacknowledged;
            state.View.IsMissingSource = memory.IsMissingSource;
            state.View.LastValue = memory.LastValue;
            m_alarms[memory.Key] = state;
            m_sequence = Math.Max(m_sequence, memory.Sequence);
        }
    }

    private void ClearRestoredVanillaAlarmsNoLongerPresent(
        IReadOnlyList<INotification> currentNotifications)
    {
        var currentKeys = new HashSet<string>(
            currentNotifications.Select(NotificationKey),
            StringComparer.Ordinal);
        AlarmView[] staleActiveViews;
        lock (m_gate)
        {
            staleActiveViews = m_alarms.Values
                .Where(state =>
                    state.View.Source == "vanilla" &&
                    state.View.IsActive &&
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
            EvaluateSystemAlarms();
        }
        EvaluateCustomRules();
    }

    public IReadOnlyList<AlarmView> GetViews(PanelDefinition panel)
    {
        if (panel == null)
        {
            return Array.Empty<AlarmView>();
        }

        var filters = SplitFilter(panel.NotificationFilter);
        lock (m_gate)
        {
            return m_alarms.Values
                .Where(state => IsVisibleOnPanel(
                    state.View,
                    panel,
                    filters))
                .OrderByDescending(state => state.View.IsLatched)
                .ThenByDescending(state =>
                    state.View.RequiresAcknowledgement)
                .ThenByDescending(state => state.View.IsActive)
                .ThenByDescending(state => state.View.Severity)
                .ThenByDescending(state => state.Sequence)
                .Select(state => Clone(state.View))
                .ToArray();
        }
    }

    public AlarmView GetAudibleAlarm()
    {
        if (!Settings.EnableAudio)
        {
            return null;
        }

        lock (m_gate)
        {
            var state = m_alarms.Values
                .Where(item =>
                    item.View.RequiresAcknowledgement &&
                    !string.Equals(
                        item.View.SoundId,
                        "none",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.View.Severity)
                .ThenByDescending(item => item.Sequence)
                .FirstOrDefault();
            return state == null ? null : Clone(state.View);
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
        }
        if (changed)
        {
            PruneInactiveVanillaHistory(500);
            PersistAlarmState();
        }
    }

    public bool SaveConfiguration()
    {
        lock (m_persistenceGate)
        {
            var alarmMemories = CaptureAlarmMemories();
            bool saved;
            string error;
            lock (m_configurationGate)
            {
                Configuration.AlarmMemories = alarmMemories;
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

    private List<AlarmMemoryDefinition> CaptureAlarmMemories()
    {
        lock (m_gate)
        {
            return m_alarms.Values
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
                })
                .ToList();
        }
    }

    private void PersistAlarmState()
    {
        if (!m_suppressAlarmPersistence)
        {
            SaveConfiguration();
        }
    }

    public bool AddRule(AlarmRuleDefinition rule)
    {
        if (rule == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            rule.Id = Guid.NewGuid().ToString("N");
        }
        lock (m_configurationGate)
        {
            Configuration.Rules.Add(rule);
        }
        if (SaveConfiguration())
        {
            Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
            return true;
        }

        lock (m_configurationGate)
        {
            Configuration.Rules.Remove(rule);
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
            return RemoveRuleWithPersistenceLock(ruleId);
        }
    }

    private bool RemoveRuleWithPersistenceLock(string ruleId)
    {
        AlarmRuleDefinition removedRule;
        var removedIndex = -1;
        lock (m_configurationGate)
        {
            removedIndex = Configuration.Rules.FindIndex(
                rule => string.Equals(
                    rule.Id,
                    ruleId,
                    StringComparison.Ordinal));
            if (removedIndex < 0)
            {
                return false;
            }
            removedRule = Configuration.Rules[removedIndex];
            Configuration.Rules.RemoveAt(removedIndex);
        }

        AlarmState removedAlarmState = null;
        lock (m_gate)
        {
            var alarmKey = "rule:" + ruleId;
            m_alarms.TryGetValue(alarmKey, out removedAlarmState);
            m_alarms.Remove(alarmKey);
        }

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Rules.Insert(removedIndex, removedRule);
            }
            if (removedAlarmState != null)
            {
                lock (m_gate)
                {
                    m_alarms["rule:" + ruleId] = removedAlarmState;
                }
            }
            return false;
        }
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
        lock (m_gate)
        {
            foreach (var rule in removedRules)
            {
                var alarmKey = "rule:" + rule.Id;
                if (m_alarms.TryGetValue(alarmKey, out var state))
                {
                    removedAlarmStates[alarmKey] = state;
                }
                m_alarms.Remove(alarmKey);
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

    public IReadOnlyList<AlarmView> GetSoundOverrideCandidates()
    {
        lock (m_gate)
        {
            return m_alarms.Values
                .Where(state =>
                    state.View.Source == "vanilla" &&
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
        return ResolveConfiguredSound(alarmId);
    }

    public bool SetConfiguredSound(string alarmId, string soundId)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return false;
        }

        soundId = string.IsNullOrWhiteSpace(soundId) ? "auto" : soundId;
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
        return ResolveAutoAcknowledgeOnClear(alarmId);
    }

    public bool SetConfiguredAutoAcknowledgeOnClear(
        string alarmId,
        bool autoAcknowledgeOnClear)
    {
        if (string.IsNullOrWhiteSpace(alarmId))
        {
            return false;
        }

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
                    SoundId = "auto",
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
                    state.View.IsGoneUnacknowledged = false;
                    state.View.IsAcknowledged = false;
                    clearedGoneAlarm = true;
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

    private void EvaluateSystemAlarms()
    {
        var metrics = CaptureSystemMetrics();
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
                occurrencePriority: stage.Priority);
        }
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

            lastValue = actual;
            var matches = AlarmEvaluation.Compare(
                actual,
                condition.Comparison,
                condition.Threshold);
            values.Add(matches);
            details.Add(
                condition.EntityTitle + " · " +
                condition.MetricLabel + " " +
                OperatorText(condition.Comparison) + " " +
                condition.Threshold.ToString("0.###", CultureInfo.CurrentCulture) +
                " (ist " +
                actual.ToString("0.###", CultureInfo.CurrentCulture) + ")");
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
            occurrenceId: rule.Id);
    }

    private void OnNotificationAdded(INotification notification)
    {
        var id = notification.Proto.Id.Value;
        var overrideId = "vanilla:" + id;
        var message = notification.Message.Value;
        var severity = ClassifyNotification(notification);
        var detail = id;
        if (notification.Object.HasValue)
        {
            var objectTitle = notification.Object.Value.DefaultTitle.Value;
            if (!string.IsNullOrWhiteSpace(objectTitle))
            {
                detail += " · " + objectTitle;
            }
        }

        SetAlarm(
            NotificationKey(notification),
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
            overrideId);
    }

    private void OnNotificationRemoved(INotification notification)
    {
        var overrideId = "vanilla:" + notification.Proto.Id.Value;
        ClearAlarm(
            NotificationKey(notification),
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
        var changed = false;
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(
                    NotificationKey(notification),
                    out var alarm))
            {
                changed = alarm.View.IsAcknowledged !=
                          notification.IsSuppressed;
                alarm.View.IsAcknowledged = notification.IsSuppressed;
            }
        }
        if (changed)
        {
            PersistAlarmState();
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
        int occurrencePriority = 0)
    {
        var shouldPersist = false;
        lock (m_gate)
        {
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
            state.View.OccurrencePriority = occurrencePriority;
            state.View.ActiveColor = string.IsNullOrWhiteSpace(activeColor)
                ? ColorFor(severity)
                : activeColor;
            state.View.LastValue = lastValue;

            if (transition.IsNewOccurrence)
            {
                state.Sequence = ++m_sequence;
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
                 previousOccurrencePriority !=
                 state.View.OccurrencePriority);
        }
        if (shouldPersist)
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
                changed =
                    wasActive != state.View.IsActive ||
                    wasAcknowledged != state.View.IsAcknowledged ||
                    wasGoneUnacknowledged !=
                    state.View.IsGoneUnacknowledged;
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
                state.View.IsActive = false;
                state.View.IsAcknowledged = false;
                state.View.IsGoneUnacknowledged = false;
            }
        }
        if (changed && persist)
        {
            PersistAlarmState();
        }
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

    private string ResolveConfiguredSound(string alarmId)
    {
        lock (m_configurationGate)
        {
            return Configuration.SoundOverrides.FirstOrDefault(item =>
                       string.Equals(
                           item.AlarmId,
                           alarmId,
                           StringComparison.Ordinal))?.SoundId ??
                   "auto";
        }
    }

    private bool ResolveAutoAcknowledgeOnClear(string alarmId)
    {
        lock (m_configurationGate)
        {
            return Configuration.SoundOverrides.FirstOrDefault(item =>
                string.Equals(
                    item.AlarmId,
                    alarmId,
                    StringComparison.Ordinal))?.AutoAcknowledgeOnClear ??
                   false;
        }
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

    private static bool IsVisibleOnPanel(
        AlarmView view,
        PanelDefinition panel,
        IReadOnlyList<string> filters)
    {
        if (view.Source == "custom")
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

    private static AlarmView Clone(AlarmView source)
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
            OccurrencePriority = source.OccurrencePriority,
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
