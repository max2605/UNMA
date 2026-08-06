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
    public int HealthWarningPercent = 65;
    public int HealthCriticalPercent = 45;
    public int HealthEmergencyPercent = 25;
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

    private readonly object m_gate = new();
    private readonly object m_configurationGate = new();
    private readonly object m_inspectionGate = new();
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
    private bool m_simListenerAdded;
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
    }

    public void Initialize()
    {
        m_notificationsManager.NotificationAdded += OnNotificationAdded;
        m_notificationsManager.NotificationRemoved += OnNotificationRemoved;
        m_notificationsManager.NotificationSuppressChanged +=
            OnNotificationSuppressChanged;

        foreach (var notification in
                 m_notificationsManager.FetchAllNotifications())
        {
            OnNotificationAdded(notification);
        }

        m_simLoopEvents.UpdateEndForUi.AddNonSaveable(
            this,
            OnUpdateEndForUi);
        m_simListenerAdded = true;
    }

    public void ApplySettings(UnmaSettings settings)
    {
        m_settings = settings ?? new UnmaSettings();
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        if (!m_settings.EnableSystemAlarms)
        {
            SetInactive("system:health");
            SetInactive("system:food");
            SetInactive("system:workers");
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
            EvaluateHealth();
            EvaluateFood();
            EvaluateWorkers();
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
                .OrderByDescending(state => state.View.IsActive)
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
                    item.View.IsActive &&
                    !item.View.IsAcknowledged)
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
                    state => state.View.IsActive &&
                             !state.View.IsAcknowledged);
            }
        }
    }

    public void AcknowledgeAll()
    {
        lock (m_gate)
        {
            foreach (var alarm in m_alarms.Values)
            {
                if (alarm.View.IsActive)
                {
                    alarm.View.IsAcknowledged = true;
                }
            }
        }
    }

    public bool SaveConfiguration()
    {
        bool saved;
        string error;
        lock (m_configurationGate)
        {
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

    public bool RemoveRule(string ruleId)
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

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Rules.Insert(removedIndex, removedRule);
            }
            return false;
        }

        lock (m_gate)
        {
            m_alarms.Remove("rule:" + ruleId);
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
            SetInactive("rule:" + ruleId);
        }
        Interlocked.Exchange(ref m_nextEvaluationTimestamp, 0L);
        return true;
    }

    public bool RemovePanel(string panelId)
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

        if (!SaveConfiguration())
        {
            lock (m_configurationGate)
            {
                Configuration.Panels.Insert(panelIndex, panel);
                Configuration.Rules.AddRange(removedRules);
            }
            return false;
        }

        lock (m_gate)
        {
            foreach (var rule in removedRules)
            {
                m_alarms.Remove("rule:" + rule.Id);
            }
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

        lock (m_gate)
        {
            foreach (var state in m_alarms.Values.Where(state =>
                         string.Equals(
                             state.View.OverrideId,
                             alarmId,
                             StringComparison.Ordinal)))
            {
                state.View.SoundId = soundId;
            }
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

    private void EvaluateHealth()
    {
        var health = m_healthManager.HealthStats.HealthThisMonth
            .ToIntPercentRounded();
        var population = m_settlementsManager.GetTotalPopulation();
        var warningThreshold = Math.Max(
            Settings.HealthWarningPercent,
            Math.Max(
                Settings.HealthCriticalPercent,
                Settings.HealthEmergencyPercent));
        var emergencyThreshold = Math.Min(
            Settings.HealthWarningPercent,
            Math.Min(
                Settings.HealthCriticalPercent,
                Settings.HealthEmergencyPercent));
        var criticalThreshold = Settings.HealthWarningPercent +
                                Settings.HealthCriticalPercent +
                                Settings.HealthEmergencyPercent -
                                warningThreshold -
                                emergencyThreshold;

        var severity = AlarmSeverity.Notice;
        var isActive = population > 0 && health < warningThreshold;
        if (isActive)
        {
            severity = AlarmSeverity.Warning;
        }
        if (population > 0 && health < criticalThreshold)
        {
            severity = AlarmSeverity.Critical;
        }
        if (population > 0 && health < emergencyThreshold)
        {
            severity = AlarmSeverity.Emergency;
        }

        SetAlarm(
            "system:health",
            "GESUNDHEIT NIEDRIG",
            "Gesundheit: " + health + " %",
            "system",
            "",
            severity,
            isActive,
            false,
            ResolveConfiguredSound("system:health"),
            ColorFor(severity),
            health,
            overrideId: "system:health");
    }

    private void EvaluateFood()
    {
        var months = m_settlementsManager.MonthsOfFood;
        var severity = AlarmSeverity.Notice;
        var isActive = false;
        var detail = "Nahrungsvorrat: " + months + " Monate";

        if (months <= 12)
        {
            severity = AlarmSeverity.Warning;
            isActive = true;
        }
        if (months <= 3)
        {
            severity = AlarmSeverity.Critical;
        }
        if (m_settlementsManager.ArePeopleStarving ||
            m_settlementsManager.AmountStarvedToDeathLastMonth > 0)
        {
            severity = AlarmSeverity.Emergency;
            isActive = true;
            detail = m_settlementsManager.AmountStarvedToDeathLastMonth > 0
                ? "Verhungert: " +
                  m_settlementsManager.AmountStarvedToDeathLastMonth
                : "Bevölkerung hungert";
        }

        SetAlarm(
            "system:food",
            "NAHRUNGSVERSORGUNG",
            detail,
            "system",
            "",
            severity,
            isActive,
            false,
            ResolveConfiguredSound("system:food"),
            ColorFor(severity),
            months,
            overrideId: "system:food");
    }

    private void EvaluateWorkers()
    {
        var workers = m_workersManager.AmountOfFreeWorkersOrMissing;
        var missing = Math.Max(0, -workers);
        var population = Math.Max(1, m_settlementsManager.GetTotalPopulation());
        var criticalThreshold = Math.Max(5, population / 20);
        var emergencyThreshold = Math.Max(20, population * 15 / 100);
        var severity = AlarmSeverity.Notice;
        var isActive = workers < 0;

        if (isActive)
        {
            severity = AlarmSeverity.Warning;
        }
        if (missing >= criticalThreshold)
        {
            severity = AlarmSeverity.Critical;
        }
        if (missing >= emergencyThreshold)
        {
            severity = AlarmSeverity.Emergency;
        }

        SetAlarm(
            "system:workers",
            "ARBEITER FEHLEN",
            workers >= 0
                ? "Freie Arbeiter: " + workers
                : "Fehlende Arbeiter: " + missing,
            "system",
            "",
            severity,
            isActive,
            false,
            ResolveConfiguredSound("system:workers"),
            ColorFor(severity),
            workers,
            overrideId: "system:workers");
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
            SetInactive("rule:" + rule.Id);
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
            lastValue);
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
            overrideId);
    }

    private void OnNotificationRemoved(INotification notification)
    {
        SetInactive(NotificationKey(notification));
        PruneInactiveVanillaHistory(500);
    }

    private void PruneInactiveVanillaHistory(int maximum)
    {
        lock (m_gate)
        {
            var inactive = m_alarms
                .Where(pair =>
                    pair.Value.View.Source == "vanilla" &&
                    !pair.Value.View.IsActive)
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
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(
                    NotificationKey(notification),
                    out var alarm))
            {
                alarm.View.IsAcknowledged = notification.IsSuppressed;
            }
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
        string overrideId = "")
    {
        lock (m_gate)
        {
            if (!m_alarms.TryGetValue(key, out var state))
            {
                state = new AlarmState();
                state.View.Key = key;
                state.View.IsAcknowledged = initiallyAcknowledged;
                m_alarms[key] = state;
            }

            var wasActive = state.View.IsActive;
            var previousSeverity = state.View.Severity;
            var wasAcknowledged = state.View.IsAcknowledged;
            state.View.Name = name ?? "MELDUNG";
            state.View.Detail = detail ?? "";
            state.View.Source = source ?? "";
            state.View.PanelId = panelId ?? "";
            var transition = AlarmEvaluation.Transition(
                wasActive,
                wasAcknowledged,
                previousSeverity,
                isActive,
                severity,
                initiallyAcknowledged);
            state.View.Severity = severity;
            state.View.IsActive = transition.IsActive;
            state.View.IsAcknowledged = transition.IsAcknowledged;
            state.View.IsMissingSource = missingSource;
            state.View.SoundId = string.IsNullOrWhiteSpace(soundId)
                ? "auto"
                : soundId;
            state.View.OverrideId = overrideId ?? "";
            state.View.ActiveColor = string.IsNullOrWhiteSpace(activeColor)
                ? ColorFor(severity)
                : activeColor;
            state.View.LastValue = lastValue;

            if (transition.IsNewOccurrence)
            {
                state.Sequence = ++m_sequence;
            }
        }
    }

    private void SetInactive(string key)
    {
        lock (m_gate)
        {
            if (m_alarms.TryGetValue(key, out var state))
            {
                state.View.IsActive = false;
                state.View.IsAcknowledged = false;
            }
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
            Severity = source.Severity,
            IsActive = source.IsActive,
            IsAcknowledged = source.IsAcknowledged,
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
