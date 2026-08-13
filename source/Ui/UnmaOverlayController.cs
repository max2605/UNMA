using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Mafi.Core.Entities;
using Mafi;
using Mafi.Unity;
using Mafi.Unity.Audio;
using Mafi.Unity.Camera;
using Mafi.Unity.Ui;
using Mafi.Unity.UiToolkit;
using UnityEngine;
using UNMA.Audio;
using UNMA.Domain;
using UNMA.Integration;
using UNMA.Localization;
using UNMA.Runtime;

namespace UNMA.Ui;

public sealed class UnmaOverlayController : MonoBehaviour
{
    private sealed class DetachedPanel
    {
        public string PanelId = "";
        public Rect Rect;
        public Rect LastPersistedRect;
        public Vector2 Scroll;
        public bool IsOpen = true;
        public bool LastPersistedOpen;
        public float PersistAt = -1f;
        public UnmaNativeDetachedPanelShell NativeShell;
    }

    private sealed class PanelViewCacheEntry
    {
        public int Frame = -1;
        public IReadOnlyList<AlarmView> Views = Array.Empty<AlarmView>();
    }

    private sealed class TransferRuleRow
    {
        public string Identity = "";
        public VanillaNotificationRule CurrentRule;
        public TransferNotificationRule ProfileRule;
        public VanillaNotificationRule ProfileDisplayRule;

        public VanillaNotificationRule DisplayRule =>
            CurrentRule ?? ProfileDisplayRule;
    }

    private const int TabBoard = 0;
    private const int TabHistory = 1;
    private const int TabSystem = 2;
    private const int TabSounds = 3;
    private const int TabOptions = 4;
    private const int TabInstruments = 5;
    // Leave a dedicated action strip below the tile detail. This keeps the
    // visible EDIT affordance from covering alarm information.
    private const float TileHeight = 142f;
    private const float HistoryRowHeight = 40f;
    private const int MaximumAlarmAreas = 64;
    private const int MaximumIncidentCards = 6;
    private const int MaximumIncidentMembersPerCard = 8;
    private const float WindowLayoutPersistenceDelaySeconds = 0.35f;
    private static readonly float[] s_instrumentPreviewSamples =
    {
        0.12f, 0.18f, 0.24f, 0.22f, 0.36f, 0.48f, 0.45f, 0.61f,
        0.70f, 0.66f, 0.79f,
    };
    private static readonly int[] s_historianRanges =
    {
        GameTimeWindowPolicy.SimTicksPerDay,
        GameTimeWindowPolicy.SimTicksPerMonth,
        GameTimeWindowPolicy.SimTicksPerYear,
        GameTimeWindowPolicy.SimTicksPerYear * 10,
        GameTimeWindowPolicy.SimTicksPerYear * 100,
        0,
    };
    private static readonly AlarmHistoryStateFilter[] s_historyStateFilters =
    {
        AlarmHistoryStateFilter.All,
        AlarmHistoryStateFilter.Open,
        AlarmHistoryStateFilter.Completed,
        AlarmHistoryStateFilter.K,
        AlarmHistoryStateFilter.KQ,
        AlarmHistoryStateFilter.KG,
        AlarmHistoryStateFilter.KGQ,
    };
    private static readonly AlarmSeverity?[] s_historySeverityFilters =
    {
        null,
        AlarmSeverity.Notice,
        AlarmSeverity.Warning,
        AlarmSeverity.Critical,
        AlarmSeverity.Emergency,
    };
    private enum EditorWindowMode
    {
        Rule,
        PanelCreation,
        PanelSettings,
        AlarmAreas,
    }

    private enum StatusSeverity
    {
        Info,
        Success,
        Warning,
        Error,
    }

    private enum TimingDisplayUnit
    {
        Tick,
        Day,
        Month,
        Year,
        Decade,
        Century,
    }

    private sealed class TimingDraftValue
    {
        public string AmountText = "0";
        public TimingDisplayUnit Unit = TimingDisplayUnit.Tick;
    }

    private static readonly FieldInfo s_menuDepthField =
        typeof(GlobalGfxSettings).GetField(
            "s_isInMenus",
            BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo s_loadingField =
        typeof(GlobalGfxSettings).GetField(
            "s_isInLoading",
            BindingFlags.Static | BindingFlags.NonPublic);
    private static bool s_gameplayStateFailureLogged;

    private readonly List<DetachedPanel> m_detachedPanels = new();
    private readonly Dictionary<string, Texture2D> m_colorTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConditionDefinition> m_draftConditions = new();
    private readonly List<string> m_draftConditionThresholdTexts = new();
    private readonly Dictionary<ConditionDefinition, string>
        m_draftTrendWindowTexts = new();
    private readonly Dictionary<ConditionDefinition, string>
        m_draftHysteresisTexts = new();
    private readonly Dictionary<string, PanelViewCacheEntry> m_panelViewCache =
        new(StringComparer.Ordinal);
    private int m_alarmAreaViewCacheFrame = -1;
    private AlarmAreaFilter m_alarmAreaViewCacheFilter = AlarmAreaFilter.All;
    private IReadOnlyList<AlarmView> m_alarmAreaViewCache =
        Array.Empty<AlarmView>();
    private bool m_alarmAreaViewCacheSucceeded;
    private int m_incidentSnapshotCacheFrame = -1;
    private AlarmAreaFilter m_incidentSnapshotCacheFilter =
        AlarmAreaFilter.All;
    private AlarmIncidentSnapshot m_incidentSnapshotCache;
    private bool m_incidentSnapshotCacheSucceeded;
    private readonly Dictionary<string, List<float>> m_instrumentSamples =
        new(StringComparer.Ordinal);
    private readonly List<InstrumentHistoryBucket>
        m_historianBucketScratch = new(4096);
    private readonly InstrumentPanelRenderer.HistorianTrace
        m_historianTrace = new();
    private readonly Dictionary<string, double> m_instrumentValues =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_invalidInstruments =
        new(StringComparer.Ordinal);

    private UnmaRuntime m_runtime;
    private UiRoot m_uiRoot;
    private InspectorsManager m_inspectorsManager;
    private CameraController m_cameraController;
    private UnmaInputBlocker m_inputBlocker;
    private UnmaAudioController m_audio;
    private InspectorAlarmButtonBridge m_inspectorAlarmButtons;
    private UnmaNativeLauncher m_nativeLauncher;
    private UnmaNativeWindowShell m_nativeWindowShell;
    private UnmaNativeEditorShell m_nativeEditorShell;
    private Rect m_windowRect;
    private Rect m_lastPersistedWindowRect;
    private Rect m_entityAlarmWindowRect = new(180f, 110f, 1080f, 720f);
    private Rect m_lastPersistedEditorWindowRect;
    private float m_windowRectPersistAt = -1f;
    private float m_editorWindowRectPersistAt = -1f;
    private Vector2 m_boardScroll;
    private Vector2 m_boardActionsScroll;
    private Vector2 m_incidentLensStatsScroll;
    private Vector2 m_incidentLensScroll;
    private Vector2 m_panelTabsScroll;
    private Vector2 m_alarmAreaTabsScroll;
    private Vector2 m_panelSettingsAreaScroll;
    private Vector2 m_historyScroll;
    private Vector2 m_editorScroll;
    private Vector2 m_entityAlarmScroll;
    private Vector2 m_metricPickerScroll;
    private Vector2 m_referenceMetricPickerScroll;
    private Vector2 m_soundOverrideScroll;
    private Vector2 m_systemAlarmScroll;
    private Vector2 m_optionsScroll;
    private bool m_optionsColorDraftInitialized;
    private string m_optionsWarningColor = "";
    private string m_optionsCriticalColor = "";
    private string m_optionsEmergencyColor = "";
    private string m_transferProfileName = "";
    private bool m_transferProfileUiInitialized;
    private bool m_transferNotificationBehaviors = true;
    private bool m_transferSoundSettings = true;
    private bool m_transferAppearance = true;
    private bool m_transferSystemAlarms = true;
    private bool m_transferWindowLayout;
    private readonly HashSet<string> m_transferSelectedRuleIdentities =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> m_transferKnownRuleIdentities =
        new(StringComparer.Ordinal);
    private TransferImportPreview m_transferImportPreview;
    private Vector2 m_instrumentScroll;
    private Vector2 m_instrumentPanelTabsScroll;
    private Vector2 m_instrumentMetricScroll;
    private string m_instrumentMetricFilter = "";
    private Vector2 m_instrumentTypePickerScroll;
    private IReadOnlyList<PanelSlotDefinition> m_panelSlotCandidates =
        Array.Empty<PanelSlotDefinition>();
    private float m_nextPanelSlotCandidateRefresh;
    private float m_nextInstrumentRefresh;
    private bool m_isOpen;
    private bool m_incidentLensExpanded;
    private bool m_entityAlarmWindowOpen;
    private bool m_editorClosePromptOpen;
    private EditorWindowMode m_editorWindowMode;
    private string m_panelSettingsPanelId = "";
    private string m_panelSettingsName = "";
    private int m_panelSettingsColumns = 3;
    private bool m_panelSettingsIncludeVanilla;
    private bool m_panelSettingsIncludeSystem;
    private string m_panelSettingsFilter = "";
    private string m_panelSettingsAreaId = "";
    private readonly List<AlarmAreaDefinition> m_alarmAreaDraft = new();
    private AlarmAreaFilter m_alarmAreaFilter = AlarmAreaFilter.All;
    private string m_newAlarmAreaName = "";
    private string m_pendingAlarmAreaDeleteId = "";
    private float m_pendingAlarmAreaDeleteUntil;
    private string m_activeEntityPanelId = "";
    private int m_openEntityPanelAfterInspectionId = -1;
    private bool m_openEntityAlarmAfterInspection;
    private int m_pendingInspectionEntityId = -1;
    private bool m_entityAssignmentPending;
    private int m_assignmentEntityId = -1;
    private EntityInspectionSnapshot m_assignmentEntity;
    private int m_draftPreferredSlotIndex = -1;
    private bool m_isAutomaticInspectionRefresh;
    private float m_nextEntityInspectionRefresh;
    private bool m_stylesReady;
    private int m_tab;
    private int m_currentPanelIndex;
    private bool m_gameplayWasActive;
    private bool m_isUiSuppressedByMenu;
    private bool m_clearGuiFocusPending;
    private bool m_nativeOverlayDrawLogged;
    private readonly HashSet<string> m_draftLinkedPanelIds =
        new(StringComparer.Ordinal);

    private EntityInspectionSnapshot m_selectedEntity;
    private IReadOnlyList<MetricDescriptor> m_selectedMetrics =
        Array.Empty<MetricDescriptor>();
    private int m_selectedMetricIndex;
    private int m_selectedReferenceMetricIndex;
    private string m_linkedInstrumentSourceId = "";
    private int m_selectedLinkedInstrumentIndex;
    private bool m_linkedInstrumentPickerOpen;
    private bool m_metricPickerOpen;
    private bool m_referenceMetricPickerOpen;
    private int m_conditionReferencePickerIndex = -1;
    private string m_metricPickerFilter = "";
    private string m_referenceMetricPickerFilter = "";
    private ConditionValueMode m_draftValueMode = ConditionValueMode.Absolute;
    private ComparisonOperator m_draftComparison =
        ComparisonOperator.Less;
    private AlarmSeverity m_draftSeverity = AlarmSeverity.Warning;
    private AlarmLogic m_draftLogic = AlarmLogic.All;
    private bool m_draftEnabled = true;
    private string m_draftThreshold = "0";
    private string m_draftRuleName = UnmaText.Get("auto.fe04a9d0e58c");
    private string m_draftColor = "#F0C541";
    private int m_draftSoundIndex;
    private string m_originalDraftSoundId = "auto";
    private bool m_draftSoundChanged;
    private bool m_draftAutoAcknowledgeOnClear;
    private readonly TimingDraftValue m_draftActivationDelay = new();
    private readonly TimingDraftValue m_draftResetDelay = new();
    private readonly TimingDraftValue m_draftMinimumActive = new();
    private bool m_ruleAdvancedOpen;
    private bool m_draftEscalationEnabled;
    private readonly TimingDraftValue m_draftEscalationAfter = new();
    private AlarmSeverity m_draftEscalationSeverity = AlarmSeverity.Critical;
    private string m_draftEscalationSoundId = "";
    private AlarmOperatorAction m_draftEscalationOperatorAction;
    private string m_editingRuleId = "";
    private string m_draftTargetPanelId = "";
    private string m_lastAlarmTileClickId = "";
    private float m_lastAlarmTileClickAt;
    private string m_lastNavigatedAlarmSlotId = "";
    private float m_audioMutedUntil;
    private string m_newPanelName = UnmaText.Get("auto.3f5c86818d70");
    private string m_panelSlotFilter = "";
    private string m_soundOverrideFilter = "";
    private EntityInspectionSnapshot m_instrumentDraftEntity;
    private readonly List<InstrumentSourceDefinition>
        m_instrumentDraftSources = new();
    private IReadOnlyList<MetricDescriptor> m_instrumentDraftMetrics =
        Array.Empty<MetricDescriptor>();
    private int m_instrumentDraftMetricIndex;
    private int m_currentInstrumentPanelIndex;
    private string m_instrumentDraftTitle = "";
    private string m_instrumentDraftMinimum = "0";
    private string m_instrumentDraftMaximum = "100";
    private InstrumentDisplayType m_instrumentDraftType =
        InstrumentDisplayType.RoundGauge;
    private InstrumentAggregationMode m_instrumentDraftAggregation =
        InstrumentAggregationMode.Single;
    private bool m_instrumentTypePickerOpen;
    private bool m_instrumentPanelCreationOpen;
    private string m_newInstrumentPanelName = UnmaText.Format(
        "ui.instrument.panel.default_name",
        "INSTRUMENT PANEL {0}",
        2);
    private string m_pendingInstrumentPanelDeleteId = "";
    private float m_pendingInstrumentPanelDeleteUntil;
    private string m_historianInstrumentId = "";
    private int m_historianRangeIndex = 1;
    private Vector2 m_historianPreviousWindowSize;
    private bool m_historianPreviousWindowSizeValid;
    private string m_historianCacheInstrumentId = "";
    private int m_historianCacheWindowTicks = -1;
    private int m_historianCachePixelColumns = -1;
    private bool m_historianCacheHasHistory;
    private InstrumentHistoryState m_historianCacheHistoryState;
    private double m_historianCacheScaleMinimum;
    private double m_historianCacheScaleMaximum;
    private SystemAlarmDefinition m_systemAlarmDraft;
    private readonly Dictionary<string, string> m_systemThresholdTexts =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> m_systemHysteresisTexts =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimingDraftValue> m_systemTimingDrafts =
        new(StringComparer.Ordinal);
    private string m_pendingSystemResetId = "";
    private float m_pendingSystemResetUntil;
    private string m_pendingPanelDeleteId = "";
    private float m_pendingPanelDeleteUntil;
    private float m_pendingHistoryDeleteUntil;
    private string m_pendingRuleDeleteId = "";
    private float m_pendingRuleDeleteUntil;
    private string m_draftConflictMessage = "";
    private float m_draftConflictMessageUntil;
    private string m_statusMessage = "";
    private float m_statusMessageUntil;
    private StatusSeverity m_statusSeverity;
    private bool m_statusPersistent;
    private AlarmView m_testAlarm;
    private float m_testAlarmUntil;
    private long m_historyCacheRevision = -1;
    private IReadOnlyList<AlarmHistoryDefinition> m_historyCache =
        Array.Empty<AlarmHistoryDefinition>();
    private string m_historySearchText = "";
    private AlarmHistoryStateFilter m_historyStateFilter =
        AlarmHistoryStateFilter.All;
    private AlarmSeverity? m_historySeverityFilter;
    private bool m_historyFilterPickerOpen;

    private GUIStyle m_panelStyle;
    private GUIStyle m_headerStyle;
    private GUIStyle m_sectionStyle;
    private GUIStyle m_labelStyle;
    private GUIStyle m_smallLabelStyle;
    private GUIStyle m_tileTitleStyle;
    private GUIStyle m_tileDetailStyle;
    private GUIStyle m_tileTitleLightStyle;
    private GUIStyle m_tileDetailLightStyle;
    private GUIStyle m_assignmentActionStyle;
    private GUIStyle m_buttonStyle;
    private GUIStyle m_primaryButtonStyle;
    private GUIStyle m_dangerButtonStyle;
    private GUIStyle m_warningBannerStyle;
    private GUIStyle m_statusInfoStyle;
    private GUIStyle m_statusSuccessStyle;
    private GUIStyle m_statusWarningStyle;
    private GUIStyle m_statusErrorStyle;
    private GUIStyle m_textFieldStyle;
    private GUIStyle m_historyHeaderStyle;
    private GUIStyle m_historyTextStyle;
    private GUIStyle m_historyStateStyle;
    private GUIStyle m_historyAlertTextStyle;
    private GUIStyle m_historyAlertStateStyle;

    public static UnmaOverlayController Create(
        UnmaRuntime runtime,
        InspectorsManager inspectorsManager,
        CameraController cameraController,
        IUnityInputMgr inputManager,
        AudioDb audioDb,
        UiRoot uiRoot,
        string modRoot)
    {
        var gameObject = new GameObject(UnmaText.Get("auto.b2d42e12e3c8"));
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
        var overlay = gameObject.AddComponent<UnmaOverlayController>();
        var audio = gameObject.AddComponent<UnmaAudioController>();
        audio.Configure(modRoot, audioDb);
        overlay.Configure(
            runtime,
            inspectorsManager,
            cameraController,
            inputManager,
            audio,
            uiRoot);
        return overlay;
    }

    public void Configure(
        UnmaRuntime runtime,
        InspectorsManager inspectorsManager,
        CameraController cameraController,
        IUnityInputMgr inputManager,
        UnmaAudioController audio,
        UiRoot uiRoot)
    {
        m_runtime = runtime;
        m_uiRoot = uiRoot;
        m_inspectorsManager = inspectorsManager;
        m_cameraController = cameraController;
        m_audio = audio;
        m_inputBlocker = new UnmaInputBlocker(
            inputManager,
            IsPointerOverAnyUnmaSurface);
        m_inspectorAlarmButtons = new InspectorAlarmButtonBridge(
            inspectorsManager,
            BeginEntityAlarmFromInspector);
        m_isOpen = runtime.Settings.ShowOnGameStart;
        var config = runtime.Configuration;
        m_draftTargetPanelId = config.Panels.FirstOrDefault()?.Id ?? "";
        m_windowRect = new Rect(
            config.WindowX,
            config.WindowY,
            Mathf.Max(700f, config.WindowWidth),
            Mathf.Max(520f, config.WindowHeight));
        m_entityAlarmWindowRect = new Rect(
            config.EditorWindowX,
            config.EditorWindowY,
            Mathf.Max(700f, config.EditorWindowWidth),
            Mathf.Max(520f, config.EditorWindowHeight));
        m_lastPersistedWindowRect = m_windowRect;
        m_lastPersistedEditorWindowRect = m_entityAlarmWindowRect;
        InitializeNativeLauncher(uiRoot, config);
        InitializeNativeWindowShell(uiRoot);
        InitializeNativeEditorShell(uiRoot);
        RestoreDetachedPanels(config);
    }

    public void ApplySettings(UnmaSettings settings)
    {
        m_runtime.ApplySettings(settings);
    }

    public void DisposeUi()
    {
        CaptureNativeWindowLayouts();
        PersistPendingWindowLayouts(force: true);
        m_nativeLauncher?.Dispose();
        m_nativeLauncher = null;
        m_nativeWindowShell?.Dispose();
        m_nativeWindowShell = null;
        m_nativeEditorShell?.Dispose();
        m_nativeEditorShell = null;
        foreach (var detached in m_detachedPanels)
        {
            detached.NativeShell?.Dispose();
            detached.NativeShell = null;
        }
        m_inspectorAlarmButtons?.Dispose();
        m_inspectorAlarmButtons = null;
        m_inputBlocker?.Dispose();
        m_inputBlocker = null;
        if (m_audio != null)
        {
            m_audio.StopAlarm();
        }
    }

    private void Update()
    {
        if (!m_gameplayWasActive)
        {
            if (!IsGameplayActive())
            {
                m_runtime.SetGameplayActive(false);
                m_audio.StopAlarm();
                m_nativeLauncher?.SetVisible(false);
                return;
            }
            m_gameplayWasActive = true;
            m_runtime.SetGameplayActive(true);
            m_audio.StopAlarm();
        }

        m_isUiSuppressedByMenu = !IsGameplayActive();
        // Read native focus before polling UNMA's reflection-based shortcuts.
        // COI's input controller blocks game shortcuts while a TextField owns
        // focus, but UNMA polls its configurable bindings directly and must
        // therefore honor the same capture explicitly.
        var nativeKeyboardFocused = IsNativeKeyboardFocused();
        m_inputBlocker?.SetKeyboardCaptured(nativeKeyboardFocused);
        var pointerOverUnma = !m_isUiSuppressedByMenu &&
                              IsPointerOverAnyUnmaSurface();
        m_inputBlocker?.SetBlockingEnabled(!m_isUiSuppressedByMenu);
        m_inputBlocker?.SetPointerState(pointerOverUnma);
        if (pointerOverUnma && m_cameraController != null)
        {
            m_cameraController.DisableZoomNextFrame = true;
        }

        if (!m_isUiSuppressedByMenu)
        {
            m_inspectorAlarmButtons?.Update();
            m_inputBlocker?.EnsureActive();
            if (m_runtime.TryTakeAttentionRequest(out var attentionRequest))
            {
                HandleAttentionRequest(attentionRequest);
            }
        }

        if (m_runtime.TryTakeCompletedInspection(out var inspection))
        {
            ApplyCompletedInspection(inspection);
        }

        if (!string.IsNullOrWhiteSpace(m_editingRuleId) &&
            !m_runtime.Configuration.Rules.Any(rule => string.Equals(
                rule.Id,
                m_editingRuleId,
                StringComparison.Ordinal)))
        {
            ResetDraftRule();
            CloseEditorWindow();
            SetStatus(
                UnmaText.Get("auto.ced08b6f8b50") +
                UnmaText.Get("auto.e24c442816b5"));
        }

        var alarmEditorVisible = m_entityAlarmWindowOpen &&
                                 m_editorWindowMode == EditorWindowMode.Rule;
        if (alarmEditorVisible &&
            !m_entityAssignmentPending &&
            string.IsNullOrWhiteSpace(m_linkedInstrumentSourceId) &&
            m_selectedEntity != null &&
            m_pendingInspectionEntityId < 0 &&
            Time.realtimeSinceStartup >= m_nextEntityInspectionRefresh)
        {
            if (m_selectedEntity.EntityId < 0)
            {
                SelectGlobalMetricSource(true);
            }
            else
            {
                m_pendingInspectionEntityId = m_selectedEntity.EntityId;
                m_isAutomaticInspectionRefresh = true;
                m_runtime.RequestEntityInspection(m_selectedEntity.EntityId);
            }
            m_nextEntityInspectionRefresh =
                Time.realtimeSinceStartup + 1f;
        }

        if (!m_isUiSuppressedByMenu &&
            !nativeKeyboardFocused &&
            KeybindFrameworkBridge.IsPressed(
                KeybindFrameworkBridge.ToggleWindowId,
                KeybindFrameworkBridge.ToggleWindowDefault))
        {
            m_isOpen = !m_isOpen;
            SynchronizeNativeWindowVisibility();
            SynchronizeNativeLauncher();
        }

        if (!m_isUiSuppressedByMenu &&
            !nativeKeyboardFocused &&
            KeybindFrameworkBridge.IsPressed(
                KeybindFrameworkBridge.AcknowledgeAllId,
                KeybindFrameworkBridge.AcknowledgeAllDefault))
        {
            AcknowledgeAllAlarms();
        }

        if (!m_isUiSuppressedByMenu &&
            !nativeKeyboardFocused &&
            KeybindFrameworkBridge.IsPressed(
                KeybindFrameworkBridge.NextUnacknowledgedAlarmId,
                KeybindFrameworkBridge.NextUnacknowledgedAlarmDefault))
        {
            NavigateToNextUnacknowledgedAlarm(CurrentPanel);
        }

        if (!m_isUiSuppressedByMenu &&
            !nativeKeyboardFocused &&
            KeybindFrameworkBridge.IsPressed(
                KeybindFrameworkBridge.MuteAudioFiveMinutesId,
                KeybindFrameworkBridge.MuteAudioFiveMinutesDefault))
        {
            m_audioMutedUntil = Time.realtimeSinceStartup + 300f;
            m_audio.StopAlarm();
            SetStatus(UnmaText.Get(
                "audio.muted_five_minutes",
                "Alarm audio muted for five minutes."));
        }

        var audible = Time.realtimeSinceStartup < m_audioMutedUntil
            ? null
            : m_testAlarm != null &&
                      Time.realtimeSinceStartup < m_testAlarmUntil
            ? m_testAlarm
            : m_runtime.GetAudibleAlarm();
        if (m_testAlarm != null &&
            Time.realtimeSinceStartup >= m_testAlarmUntil)
        {
            m_testAlarm = null;
        }
        m_audio.UpdateAlarm(
            audible,
            m_runtime.Settings.AudioVolumePercent);

        if (Time.realtimeSinceStartup >= m_nextInstrumentRefresh)
        {
            RefreshInstrumentValues();
            m_nextInstrumentRefresh = Time.realtimeSinceStartup + 0.5f;
        }

        SynchronizeNativeWindowVisibility();
        SynchronizeNativeEditorVisibility();
        SynchronizeNativeDetachedPanels();
        SynchronizeNativeLauncher();
        ClearPendingNativeFocus();
        RenderNativeBodies();
        UpdateNativeKeyboardInputCapture();
    }

    private void RenderNativeBodies()
    {
        if (!m_gameplayWasActive || m_isUiSuppressedByMenu)
        {
            return;
        }

        EnsureStyles();
        var scale = UiScale;
        if (m_isOpen && m_nativeWindowShell?.IsOpen == true)
        {
            m_nativeWindowShell.SetContentScale(scale);
            var currentSize = m_nativeWindowShell.CurrentSize;
            var preferredSize = m_nativeWindowShell.PreferredSize;
            m_windowRect.width = currentSize.x;
            m_windowRect.height = currentSize.y;
            try
            {
                m_nativeWindowShell.RenderBody(RenderMainBodyContent, scale);
            }
            finally
            {
                m_windowRect.width = preferredSize.x;
                m_windowRect.height = preferredSize.y;
            }
        }
        if (m_entityAlarmWindowOpen && m_nativeEditorShell?.IsOpen == true)
        {
            m_nativeEditorShell.SetContentScale(scale);
            var currentSize = m_nativeEditorShell.CurrentSize;
            var preferredSize = m_nativeEditorShell.PreferredSize;
            m_entityAlarmWindowRect.width = currentSize.x;
            m_entityAlarmWindowRect.height = currentSize.y;
            try
            {
                m_nativeEditorShell.RenderBody(RenderEditorBodyContent, scale);
            }
            finally
            {
                m_entityAlarmWindowRect.width = preferredSize.x;
                m_entityAlarmWindowRect.height = preferredSize.y;
            }
        }
        foreach (var detached in m_detachedPanels)
        {
            if (!detached.IsOpen || detached.NativeShell?.IsOpen != true)
            {
                continue;
            }
            var panel = m_runtime.Configuration.Panels.FirstOrDefault(
                item => item.Id == detached.PanelId);
            if (panel == null)
            {
                detached.IsOpen = false;
                continue;
            }

            detached.NativeShell.SetContentScale(scale);
            var currentSize = detached.NativeShell.CurrentSize;
            var preferredSize = detached.NativeShell.PreferredSize;
            detached.Rect.width = currentSize.x;
            detached.Rect.height = currentSize.y;
            try
            {
                detached.NativeShell.RenderBody(
                    () => RenderDetachedBodyContent(detached, panel),
                    scale);
            }
            finally
            {
                detached.Rect.width = preferredSize.x;
                detached.Rect.height = preferredSize.y;
            }
        }
        CaptureNativeWindowLayouts();
        PersistPendingWindowLayouts(force: false);

        if (!m_nativeOverlayDrawLogged)
        {
            m_nativeOverlayDrawLogged = true;
            Log.Info(
                "UNMA: all body content attached to native COI UI hierarchy");
        }
    }

    private void RenderMainBodyContent()
    {
        var scale = UiScale;
        m_windowRect.width /= scale;
        m_windowRect.height /= scale;
        try
        {
            DrawSelectedMainTab();
        }
        finally
        {
            if (m_nativeWindowShell == null)
            {
                m_windowRect.width *= scale;
                m_windowRect.height *= scale;
            }
        }
    }

    private void RenderEditorBodyContent()
    {
        DrawEditorBodyContent();
    }

    private void RenderDetachedBodyContent(
        DetachedPanel detached,
        PanelDefinition panel)
    {
        var scale = UiScale;
        var totalWidth = detached.Rect.width;
        var totalHeight = detached.Rect.height;
        detached.Rect.width = totalWidth / scale;
        detached.Rect.height = totalHeight / scale;
        try
        {
            DrawDetachedPanelContent(detached, panel);
        }
        finally
        {
            detached.Rect.width = totalWidth;
            detached.Rect.height = totalHeight;
        }
    }

    private void ClearPendingNativeFocus()
    {
        if (!m_clearGuiFocusPending)
        {
            return;
        }

        m_nativeWindowShell?.ClearBodyFocus();
        m_nativeEditorShell?.ClearBodyFocus();
        foreach (var detached in m_detachedPanels)
        {
            detached.NativeShell?.ClearBodyFocus();
        }
        m_clearGuiFocusPending = false;
    }

    private void UpdateNativeKeyboardInputCapture()
    {
        m_inputBlocker?.SetKeyboardCaptured(IsNativeKeyboardFocused());
    }

    private bool IsNativeKeyboardFocused()
    {
        return
            m_nativeWindowShell?.IsBodyKeyboardCaptured == true ||
            m_nativeEditorShell?.IsBodyKeyboardCaptured == true ||
            m_detachedPanels.Any(panel =>
                panel.NativeShell?.IsBodyKeyboardCaptured == true);
    }


    private void InitializeNativeLauncher(
        UiRoot uiRoot,
        UnmaConfiguration config)
    {
        try
        {
            m_nativeLauncher = new UnmaNativeLauncher(
                uiRoot,
                config.LauncherX < 0f ? 8f : config.LauncherX,
                config.LauncherY < 0f ? 160f : config.LauncherY,
                HandleNativeLauncherOpen,
                HandleNativeLauncherMoved);
            m_nativeLauncher.SetVisible(false);
            Log.Info("UNMA: native Captain of Industry launcher ready");
        }
        catch (Exception exception)
        {
            m_nativeLauncher?.Dispose();
            m_nativeLauncher = null;
            Log.Warning(
                "UNMA: native launcher could not be created. " +
                exception.GetType().Name + ": " + exception.Message);
        }
    }

    private void HandleNativeLauncherOpen()
    {
        if (m_isUiSuppressedByMenu || m_nativeWindowShell == null)
        {
            return;
        }

        m_isOpen = true;
        m_clearGuiFocusPending = true;
        SynchronizeNativeWindowVisibility();
        SynchronizeNativeLauncher();
    }

    private void HandleNativeLauncherMoved(float x, float y)
    {
        var config = m_runtime.Configuration;
        var previousX = config.LauncherX;
        var previousY = config.LauncherY;
        config.LauncherX = x;
        config.LauncherY = y;
        if (m_runtime.SaveConfiguration())
        {
            return;
        }

        config.LauncherX = previousX;
        config.LauncherY = previousY;
        m_nativeLauncher?.SetPosition(
            previousX < 0f ? 8f : previousX,
            previousY < 0f ? 160f : previousY);
        SetStatus(
            UnmaText.Get("auto.c1f0ffc84e81") +
            m_runtime.LastPersistenceError,
            StatusSeverity.Error,
            true);
    }

    private void SynchronizeNativeLauncher()
    {
        if (m_nativeLauncher == null)
        {
            return;
        }

        m_nativeLauncher.SetCount(m_runtime.UnacknowledgedCount);
        m_nativeLauncher.SetVisible(
            m_gameplayWasActive &&
            !m_isUiSuppressedByMenu &&
            (!m_isOpen || m_nativeWindowShell == null));
    }

    private void InitializeNativeWindowShell(UiRoot uiRoot)
    {
        try
        {
            m_nativeWindowShell = new UnmaNativeWindowShell(
                uiRoot,
                m_windowRect.width,
                m_windowRect.height,
                m_windowRect.x,
                m_windowRect.y,
                () => m_tab,
                SelectMainTab,
                HandleNativeWindowMinimized,
                HandleNativeWindowResized,
                HandleNativeSurfaceActivated);
            Log.Info(
                "UNMA: native Captain of Industry window shell ready");
        }
        catch (Exception exception)
        {
            m_nativeWindowShell?.Dispose();
            m_nativeWindowShell = null;
            Log.Warning(
                "UNMA: native window shell unavailable; window disabled. " +
                exception.GetType().Name +
                ": " + exception.Message);
        }
    }

    private void InitializeNativeEditorShell(UiRoot uiRoot)
    {
        try
        {
            m_nativeEditorShell = new UnmaNativeEditorShell(
                uiRoot,
                m_entityAlarmWindowRect.width,
                m_entityAlarmWindowRect.height,
                m_entityAlarmWindowRect.x,
                m_entityAlarmWindowRect.y,
                GetEditorWindowTitle(),
                RequestEditorClose,
                HandleNativeEditorResized,
                HandleNativeSurfaceActivated,
                HandleEditorEscapeShortcut,
                SaveDraftRuleFromShortcut);
            Log.Info("UNMA: native editor window shell ready");
        }
        catch (Exception exception)
        {
            m_nativeEditorShell?.Dispose();
            m_nativeEditorShell = null;
            Log.Warning(
                "UNMA: native editor shell unavailable; editor disabled. " +
                exception.GetType().Name +
                ": " + exception.Message);
        }
    }

    private void SynchronizeNativeWindowVisibility()
    {
        if (m_nativeWindowShell == null)
        {
            return;
        }

        try
        {
            m_nativeWindowShell.SetSuppressed(m_isUiSuppressedByMenu);
            if (m_isUiSuppressedByMenu)
            {
                return;
            }

            if (m_isOpen)
            {
                if (!m_nativeWindowShell.IsOpen)
                {
                    m_clearGuiFocusPending = true;
                }
                m_nativeWindowShell.Open();
            }
            else
            {
                m_nativeWindowShell.Close();
            }
        }
        catch (Exception exception)
        {
            m_nativeWindowShell.Dispose();
            m_nativeWindowShell = null;
            Log.Warning(
                "UNMA: native window could not be synchronized; " +
                "window disabled. " + exception.GetType().Name +
                ": " + exception.Message);
        }
    }

    private void SynchronizeNativeEditorVisibility()
    {
        if (m_nativeEditorShell == null)
        {
            return;
        }

        try
        {
            m_nativeEditorShell.SetSuppressed(m_isUiSuppressedByMenu);
            if (m_isUiSuppressedByMenu)
            {
                return;
            }

            if (m_entityAlarmWindowOpen)
            {
                if (!m_nativeEditorShell.IsOpen)
                {
                    m_clearGuiFocusPending = true;
                }
                m_nativeEditorShell.Open(GetEditorWindowTitle());
            }
            else
            {
                m_nativeEditorShell.Close();
            }
        }
        catch (Exception exception)
        {
            m_nativeEditorShell.Dispose();
            m_nativeEditorShell = null;
            Log.Warning(
                "UNMA: native editor could not be synchronized; " +
                "editor disabled. " + exception.GetType().Name +
                ": " + exception.Message);
        }
    }

    private void SynchronizeNativeDetachedPanels()
    {
        for (var index = m_detachedPanels.Count - 1; index >= 0; index--)
        {
            var detached = m_detachedPanels[index];
            if (!detached.IsOpen)
            {
                detached.NativeShell?.Dispose();
                detached.NativeShell = null;
                m_detachedPanels.RemoveAt(index);
                continue;
            }

            if (detached.NativeShell == null)
            {
                continue;
            }

            try
            {
                detached.NativeShell.SetSuppressed(m_isUiSuppressedByMenu);
                if (m_isUiSuppressedByMenu)
                {
                    continue;
                }

                var panel = m_runtime.Configuration.Panels.FirstOrDefault(
                    item => item.Id == detached.PanelId);
                if (panel == null)
                {
                    detached.IsOpen = false;
                    detached.NativeShell.Close();
                    continue;
                }

                if (!detached.NativeShell.IsOpen)
                {
                    m_clearGuiFocusPending = true;
                }
                detached.NativeShell.Open(GetDetachedPanelTitle(panel));
            }
            catch (Exception exception)
            {
                detached.NativeShell.Dispose();
                detached.NativeShell = null;
                detached.IsOpen = false;
                Log.Warning(
                    "UNMA: native detached panel unavailable; panel disabled. " +
                    exception.GetType().Name +
                    ": " + exception.Message);
            }
        }
    }

    private void HandleNativeWindowMinimized()
    {
        CaptureMainWindowLayout();
        PersistPendingWindowLayouts(force: true);
        m_isOpen = false;
        m_clearGuiFocusPending = true;
        SynchronizeNativeLauncher();
    }

    private void HandleNativeWindowResized(float width, float height)
    {
        m_windowRect.width = width;
        m_windowRect.height = height;
        m_windowRectPersistAt = Time.realtimeSinceStartup;
        PersistPendingWindowLayouts(force: true);
    }

    private void HandleNativeEditorResized(float width, float height)
    {
        m_entityAlarmWindowRect.width = width;
        m_entityAlarmWindowRect.height = height;
        m_editorWindowRectPersistAt = Time.realtimeSinceStartup;
        PersistPendingWindowLayouts(force: true);
    }

    private void HandleNativeSurfaceActivated(bool isBodyPoint)
    {
        if (!isBodyPoint)
        {
            m_clearGuiFocusPending = true;
        }
    }

    private void SelectMainTab(int tab)
    {
        if (tab != TabInstruments &&
            !string.IsNullOrWhiteSpace(m_historianInstrumentId))
        {
            ExitInstrumentHistorian();
        }
        m_tab = tab;
        m_clearGuiFocusPending = true;
    }


    private void DrawSelectedMainTab()
    {
        DrawStatusMessage();
        switch (m_tab)
        {
            case TabHistory:
                DrawHistory();
                break;
            case TabSystem:
                DrawSystemAlarms();
                break;
            case TabSounds:
                DrawSoundOverrides();
                break;
            case TabOptions:
                DrawOptions();
                break;
            case TabInstruments:
                DrawInstruments();
                break;
            default:
                DrawBoard();
                break;
        }
    }

    private void DrawBoard()
    {
        var panel = CurrentPanel;
        if (panel == null)
        {
            NativeGUILayout.Label(UnmaText.Get("auto.660051723bb3"), m_labelStyle);
            return;
        }

        var isEntityPanel = PanelTopologyPolicy.IsEntityPanel(panel);
        if (!isEntityPanel)
        {
            DrawAlarmAreaFilter();
            EnsureCurrentPanelVisibleInArea();
            panel = CurrentPanel;
            if (panel == null)
            {
                return;
            }
        }

        NativeGUILayout.BeginHorizontal();
        if (isEntityPanel)
        {
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.c76615f2e3a1"),
                    m_buttonStyle,
                    NativeGUILayout.Width(170f),
                    NativeGUILayout.Height(30f)))
            {
                m_activeEntityPanelId = "";
                m_boardScroll = Vector2.zero;
            }
            NativeGUILayout.Label(
                UnmaText.Get("auto.5a88ed325cbb") + panel.Name,
                m_primaryButtonStyle,
                NativeGUILayout.Height(30f));
        }
        else
        {
            var globalPanels = GlobalPanels;
            var visiblePanels = GetVisibleGlobalPanels();
            m_panelTabsScroll = NativeGUILayout.BeginScrollView(
                m_panelTabsScroll,
                false,
                false,
                NativeGUILayout.Height(52f),
                NativeGUILayout.ExpandWidth(true));
            NativeGUILayout.BeginHorizontal();
            foreach (var candidate in visiblePanels)
            {
                var globalIndex = globalPanels.FindIndex(item =>
                    string.Equals(
                        item.Id,
                        candidate.Id,
                        StringComparison.Ordinal));
                if (globalIndex < 0)
                {
                    continue;
                }
                var tabLabel = GetAreaAwarePanelTabLabel(candidate);
                var tabWidth = Mathf.Clamp(
                    (tabLabel?.Length ?? 0) *
                    Mathf.Max(7f, m_buttonStyle.fontSize * 0.58f) + 24f,
                    110f,
                    280f);
                if (NativeGUILayout.Button(
                        tabLabel,
                        globalIndex == m_currentPanelIndex
                            ? m_primaryButtonStyle
                            : m_buttonStyle,
                        NativeGUILayout.Width(tabWidth),
                        NativeGUILayout.Height(30f)))
                {
                    m_currentPanelIndex = globalIndex;
                    m_lastNavigatedAlarmSlotId = "";
                    m_boardScroll = Vector2.zero;
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.EndScrollView();
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.6f4982ecd932"),
                    m_buttonStyle,
                    NativeGUILayout.Width(88f),
                    NativeGUILayout.Height(30f)))
            {
                OpenPanelCreationEditor();
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get("ui.common.edit", "EDIT"),
                    m_buttonStyle,
                    NativeGUILayout.Width(72f),
                    NativeGUILayout.Height(30f)))
            {
                OpenPanelSettingsEditor(panel);
            }
        }
        NativeGUILayout.EndHorizontal();

        DrawEntityAssignmentBanner(panel);
        var alarms = GetBoardViews(panel);
        var activeCount = alarms.Count(alarm => alarm.IsActive);
        var unacknowledgedCount = alarms.Count(alarm =>
            alarm.RequiresAcknowledgement);
        var incidentLensAvailable = false;
        var incidentLensHeight = panel.IsDashboard
            ? DrawIncidentLens(panel, alarms, out incidentLensAvailable)
            : 0f;
        var compactActions = m_windowRect.width < 760f;
        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginHorizontal();
        var showBoardCountLabel = !panel.IsDashboard ||
                                  !incidentLensAvailable;
        if (showBoardCountLabel)
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.397544fe1d24") + activeCount +
                UnmaText.Get("auto.ac9ef4c5783a") + unacknowledgedCount,
                m_sectionStyle,
                NativeGUILayout.Height(34f));
        }
        if (compactActions && showBoardCountLabel)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        var scopedDashboard = IsAreaScopedDashboard(panel);
        var scopeActionWidth = compactActions
            ? Math.Max(118f, (m_windowRect.width - 70f) * 0.5f)
            : 160f;
        if (NativeGUILayout.Button(
                scopedDashboard
                    ? UnmaText.Get("board.acknowledge_area", "AREA ACK")
                    : UnmaText.Get("board.acknowledge_panel", "PANEL ACK"),
                m_dangerButtonStyle,
                NativeGUILayout.Width(scopeActionWidth),
                NativeGUILayout.Height(34f)))
        {
            AcknowledgePanelAlarms(panel);
        }
        if (NativeGUILayout.Button(
                scopedDashboard
                    ? UnmaText.Get("board.next_area_alarm", "AREA NEXT")
                    : UnmaText.Get("board.next_alarm", "NEXT ALARM"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(compactActions
                    ? scopeActionWidth
                    : 150f),
                NativeGUILayout.Height(34f)))
        {
            NavigateToNextUnacknowledgedAlarm(panel);
        }
        NativeGUILayout.EndHorizontal();

        if (compactActions)
        {
            m_boardActionsScroll = NativeGUILayout.BeginScrollView(
                m_boardActionsScroll,
                false,
                false,
                NativeGUILayout.Height(42f),
                NativeGUILayout.ExpandWidth(true));
        }
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("board.acknowledge_master", "MASTER ACK"),
                m_dangerButtonStyle,
                NativeGUILayout.Width(compactActions ? 140f : 160f),
                NativeGUILayout.Height(34f)))
        {
            AcknowledgeAllAlarms();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.c70a06d3a782"),
                m_buttonStyle,
                NativeGUILayout.Width(180f),
                NativeGUILayout.Height(34f)))
        {
            DetachPanel(panel.Id);
        }
        if (!panel.IsDashboard &&
            NativeGUILayout.Button(
                UnmaText.Get("auto.1cc8d34d4b3e"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(175f),
                NativeGUILayout.Height(34f)))
        {
            OpenNewRuleEditor(panel);
        }
        NativeGUILayout.EndHorizontal();
        if (compactActions)
        {
            NativeGUILayout.EndScrollView();
        }

        if (!m_entityAssignmentPending &&
            (!compactActions || m_windowRect.height >= 340f))
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.22344e5e1ac7"),
                m_smallLabelStyle);
        }
        m_boardScroll = NativeGUILayout.BeginScrollView(m_boardScroll);
        DrawAlarmGrid(
            alarms,
            panel.Columns,
            m_windowRect.width - 54f,
            m_boardScroll.y,
            Math.Max(
                m_windowRect.height < 360f ? 32f : 220f,
                m_windowRect.height -
                (isEntityPanel ? 190f : 232f) -
                (compactActions
                    ? panel.IsDashboard && incidentLensAvailable
                        ? 8f
                        : 42f
                    : 0f) -
                incidentLensHeight),
            panel.IsDashboard ? null : panel,
            panel,
            m_entityAssignmentPending && !panel.IsDashboard,
            panel.IsDashboard
                ? UnmaText.Get("auto.f895fe84e658")
                : UnmaText.Get("auto.e8bad0a4452b"),
            !panel.IsDashboard);
        NativeGUILayout.EndScrollView();
    }

    private float DrawIncidentLens(
        PanelDefinition dashboard,
        IReadOnlyList<AlarmView> visibleAlarms,
        out bool hasSnapshot)
    {
        const float barHeight = 42f;
        var filter = NormalizeAlarmAreaFilter();
        var available = TryGetCachedAlarmIncidentSnapshot(
            filter,
            out var snapshot) &&
                        snapshot?.IsTimeValid == true;
        hasSnapshot = available;
        var compact = m_windowRect.width < 760f;

        NativeGUILayout.Space(4f);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.incident.title", "INCIDENT LENS"),
            m_sectionStyle,
            NativeGUILayout.Width(compact ? 96f : 125f),
            NativeGUILayout.Height(34f));
        if (available)
        {
            NativeGUILayout.Label(
                StormLevelLabel(snapshot.StormLevel),
                IncidentStormStyle(snapshot.StormLevel),
                NativeGUILayout.Width(compact ? 84f : 105f),
                NativeGUILayout.Height(34f));
            var pressureText = UnmaText.Format(
                "ui.incident.pressure_summary",
                "READ-ONLY · GLOBAL PRESSURE {0} · RECENT {1}: " +
                "{2} OCCURRENCES · {3} DISTINCT ALARMS · " +
                "SCOPE {4}: {5} ACTIVE · {6} UNACK",
                snapshot.AlarmPressure,
                FormatIncidentDuration(snapshot.PressureWindowTicks),
                snapshot.RecentOccurrenceCount,
                snapshot.RecentDistinctAlarmCount,
                GetAlarmAreaFilterName(filter),
                snapshot.ActiveAlarmCount,
                snapshot.ActiveUnacknowledgedCount);
            m_incidentLensStatsScroll = NativeGUILayout.BeginScrollView(
                m_incidentLensStatsScroll,
                false,
                false,
                NativeGUILayout.Height(36f),
                NativeGUILayout.ExpandWidth(true));
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                pressureText,
                m_smallLabelStyle,
                NativeGUILayout.Width(Mathf.Clamp(
                    pressureText.Length * 7f + 20f,
                    360f,
                    820f)),
                NativeGUILayout.Height(30f));
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.EndScrollView();
        }
        else
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.incident.unavailable",
                    "INCIDENT ANALYSIS UNAVAILABLE"),
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(34f));
        }

        var guiWasEnabled = NativeGUI.enabled;
        NativeGUI.enabled = guiWasEnabled && available;
        if (NativeGUILayout.Button(
                m_incidentLensExpanded
                    ? UnmaText.Get("ui.incident.collapse", "COLLAPSE")
                    : UnmaText.Get("ui.incident.expand", "EXPAND"),
                m_incidentLensExpanded
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(compact ? 92f : 112f),
                NativeGUILayout.Height(34f)))
        {
            m_incidentLensExpanded = !m_incidentLensExpanded;
            m_incidentLensScroll = Vector2.zero;
        }
        NativeGUI.enabled = guiWasEnabled;
        NativeGUILayout.EndHorizontal();

        if (!available || !m_incidentLensExpanded)
        {
            return barHeight;
        }

        var detailHeight = Mathf.Clamp(
            m_windowRect.height * 0.32f,
            72f,
            300f);
        m_incidentLensScroll = NativeGUILayout.BeginScrollView(
            m_incidentLensScroll,
            NativeGUILayout.Height(detailHeight),
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.incident.heuristic_hint",
                "READ-ONLY HEURISTIC · Temporal correlation is not a confirmed cause."),
            m_warningBannerStyle,
            NativeGUILayout.Height(42f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.incident.focus_hint",
                "Member controls only focus visible alarms. They never acknowledge, hide, or silence alarms."),
            m_smallLabelStyle);
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.incident.scope_summary",
                "SCOPE {0} · {1} ACTIVE · {2} UNACK · {3} INCIDENTS",
                GetAlarmAreaFilterName(filter),
                snapshot.ActiveAlarmCount,
                snapshot.ActiveUnacknowledgedCount,
                snapshot.Incidents?.Count ?? 0),
            m_smallLabelStyle);
        NativeGUILayout.Space(4f);

        var incidents = snapshot.Incidents ?? Array.Empty<AlarmIncident>();
        if (incidents.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.incident.none",
                    "No active temporal incident clusters in this scope."),
                m_labelStyle);
        }
        var incidentCount = Math.Min(MaximumIncidentCards, incidents.Count);
        for (var index = 0; index < incidentCount; index++)
        {
            DrawIncidentCard(
                index,
                incidents[index],
                snapshot,
                dashboard,
                visibleAlarms);
        }
        if (incidents.Count > incidentCount)
        {
            NativeGUILayout.Label(
                UnmaText.Format(
                    "ui.incident.more_incidents",
                    "+ {0} MORE INCIDENTS",
                    incidents.Count - incidentCount),
                m_smallLabelStyle);
        }
        NativeGUILayout.EndScrollView();
        return barHeight + detailHeight + 4f;
    }

    private void DrawIncidentCard(
        int index,
        AlarmIncident incident,
        AlarmIncidentSnapshot snapshot,
        PanelDefinition dashboard,
        IReadOnlyList<AlarmView> visibleAlarms)
    {
        if (incident == null)
        {
            return;
        }

        NativeGUILayout.Space(6f);
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.incident.card_title",
                "INCIDENT {0} · {1}",
                index + 1,
                SeverityLabel(incident.Severity)),
            IncidentSeverityStyle(incident.Severity),
            NativeGUILayout.Height(30f));
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.incident.card_stats",
                "{0} MEMBERS · {1} UNACK · AGE {2} · SPAN {3}",
                incident.MemberCount,
                incident.UnacknowledgedCount,
                FormatIncidentDuration(
                    snapshot.CurrentGameTick - incident.FirstRaisedAtTicks),
                FormatIncidentDuration(
                    incident.LastRaisedAtTicks - incident.FirstRaisedAtTicks)),
            m_smallLabelStyle);

        var compactCard = m_windowRect.width < 760f;
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.incident.first_signal", "FIRST SIGNAL"),
            m_labelStyle,
            NativeGUILayout.Width(compactCard ? 112f : 145f));
        if (compactCard)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        if (incident.FirstSignal != null &&
            NativeGUILayout.Button(
                IncidentMemberLabel(
                    incident.FirstSignal,
                    snapshot.CurrentGameTick),
                m_primaryButtonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(30f)))
        {
            FocusIncidentMember(
                dashboard,
                incident.FirstSignal,
                visibleAlarms);
        }
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.Label(
            UnmaText.Get("ui.incident.members", "MEMBERS"),
            m_smallLabelStyle);
        var members = incident.Members ?? Array.Empty<AlarmIncidentMember>();
        var memberCount = Math.Min(
            MaximumIncidentMembersPerCard,
            members.Count);
        for (var memberIndex = 0;
             memberIndex < memberCount;
             memberIndex++)
        {
            var member = members[memberIndex];
            if (member == null)
            {
                continue;
            }
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                (memberIndex + 1) + ".",
                m_smallLabelStyle,
                NativeGUILayout.Width(28f));
            if (NativeGUILayout.Button(
                    IncidentMemberLabel(member, snapshot.CurrentGameTick),
                    m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(30f)))
            {
                FocusIncidentMember(dashboard, member, visibleAlarms);
            }
            NativeGUILayout.EndHorizontal();
        }
        if (members.Count > memberCount)
        {
            NativeGUILayout.Label(
                UnmaText.Format(
                    "ui.incident.more_members",
                    "+ {0} MORE MEMBERS",
                    members.Count - memberCount),
                m_smallLabelStyle);
        }
    }

    private bool TryGetCachedAlarmIncidentSnapshot(
        AlarmAreaFilter filter,
        out AlarmIncidentSnapshot snapshot)
    {
        if (m_incidentSnapshotCacheFrame == Time.frameCount &&
            AlarmAreaFiltersEqual(m_incidentSnapshotCacheFilter, filter))
        {
            snapshot = m_incidentSnapshotCache;
            return m_incidentSnapshotCacheSucceeded;
        }

        m_incidentSnapshotCacheFrame = Time.frameCount;
        m_incidentSnapshotCacheFilter = filter;
        m_incidentSnapshotCacheSucceeded =
            m_runtime.TryGetAlarmIncidentSnapshot(filter, out snapshot);
        m_incidentSnapshotCache = snapshot;
        return m_incidentSnapshotCacheSucceeded;
    }

    private void FocusIncidentMember(
        PanelDefinition dashboard,
        AlarmIncidentMember member,
        IReadOnlyList<AlarmView> visibleAlarms)
    {
        if (dashboard?.IsDashboard != true || member == null)
        {
            return;
        }
        var visible = visibleAlarms ?? Array.Empty<AlarmView>();
        var alarm = visible.FirstOrDefault(candidate =>
                        candidate != null &&
                        candidate.Sequence == member.Sequence &&
                        string.Equals(
                            PanelSlotProjection.StableAlarmId(candidate),
                            member.StableAlarmId,
                            StringComparison.Ordinal)) ??
                    visible.FirstOrDefault(candidate =>
                        candidate != null && string.Equals(
                            PanelSlotProjection.StableAlarmId(candidate),
                            member.StableAlarmId,
                            StringComparison.Ordinal));
        if (alarm == null)
        {
            SetStatus(UnmaText.Get(
                "ui.incident.focus_unavailable",
                "This incident member is no longer visible."));
            return;
        }

        m_lastNavigatedAlarmSlotId =
            PanelSlotProjection.StableAlarmId(alarm);
        SelectMainTab(TabBoard);
        m_isOpen = true;
        SelectGlobalPanel(dashboard, true);
        var alarmIndex = visible.ToList().FindIndex(candidate =>
            candidate != null &&
            candidate.Sequence == alarm.Sequence &&
            string.Equals(
                PanelSlotProjection.StableAlarmId(candidate),
                m_lastNavigatedAlarmSlotId,
                StringComparison.Ordinal));
        if (alarmIndex >= 0)
        {
            var columns = Math.Max(1, Math.Min(8, dashboard.Columns));
            m_boardScroll.y = Math.Max(
                0f,
                alarmIndex / columns * (TileHeight + 6f) - 12f);
        }
        if (m_runtime.TryResolveNavigationEntity(
                dashboard,
                alarm,
                out var entity))
        {
            NavigateToEntity(entity);
        }
        SetStatus(UnmaText.Format(
            "ui.incident.focused",
            "Focused incident member: {0}",
            alarm.Name ?? UnmaText.Get("ui.common.alarm", "ALARM")));
        SynchronizeNativeWindowVisibility();
        SynchronizeNativeLauncher();
    }

    private static string IncidentMemberLabel(
        AlarmIncidentMember member,
        double currentGameTick)
    {
        if (member == null)
        {
            return "";
        }
        return UnmaText.Get("ui.incident.focus", "FOCUS") + " · " +
               SeverityLabel(member.Severity) + " · " +
               (string.IsNullOrWhiteSpace(member.Name)
                   ? UnmaText.Get("ui.common.alarm", "ALARM")
                   : member.Name) + " · " +
               (member.RequiresAcknowledgement
                   ? UnmaText.Get("ui.incident.unack", "UNACK")
                   : UnmaText.Get("ui.incident.ack", "ACK")) + " · " +
               UnmaText.Format(
                   "ui.incident.member_age",
                   "AGE {0}",
                   FormatIncidentDuration(
                       currentGameTick - member.RaisedAtTicks));
    }

    private static string FormatIncidentDuration(double ticks)
    {
        if (double.IsNaN(ticks) || double.IsInfinity(ticks))
        {
            return UnmaText.Get("ui.incident.time_unavailable", "N/A");
        }
        ticks = Math.Max(0d, ticks);
        if (ticks >= GameTimeWindowPolicy.SimTicksPerYear)
        {
            return UnmaText.Format(
                "ui.incident.duration_years",
                "{0} YEARS",
                Math.Max(1, (int)Math.Floor(
                    ticks / GameTimeWindowPolicy.SimTicksPerYear)));
        }
        if (ticks >= GameTimeWindowPolicy.SimTicksPerMonth)
        {
            return UnmaText.Format(
                "ui.incident.duration_months",
                "{0} MONTHS",
                Math.Max(1, (int)Math.Floor(
                    ticks / GameTimeWindowPolicy.SimTicksPerMonth)));
        }
        if (ticks >= GameTimeWindowPolicy.SimTicksPerDay)
        {
            return UnmaText.Format(
                "ui.incident.duration_days",
                "{0} DAYS",
                Math.Max(1, (int)Math.Floor(
                    ticks / GameTimeWindowPolicy.SimTicksPerDay)));
        }
        return UnmaText.Format(
            "ui.incident.duration_ticks",
            "{0} TICKS",
            Math.Max(0, (int)Math.Floor(ticks)));
    }

    private static string StormLevelLabel(AlarmStormLevel level)
    {
        return level switch
        {
            AlarmStormLevel.Severe => UnmaText.Get(
                "ui.incident.level_severe",
                "SEVERE"),
            AlarmStormLevel.Storm => UnmaText.Get(
                "ui.incident.level_storm",
                "STORM"),
            AlarmStormLevel.Elevated => UnmaText.Get(
                "ui.incident.level_elevated",
                "ELEVATED"),
            _ => UnmaText.Get("ui.incident.level_normal", "NORMAL"),
        };
    }

    private GUIStyle IncidentStormStyle(AlarmStormLevel level)
    {
        return level == AlarmStormLevel.Storm ||
               level == AlarmStormLevel.Severe
            ? m_dangerButtonStyle
            : level == AlarmStormLevel.Elevated
                ? m_primaryButtonStyle
                : m_buttonStyle;
    }

    private GUIStyle IncidentSeverityStyle(AlarmSeverity severity)
    {
        return severity >= AlarmSeverity.Critical
            ? m_dangerButtonStyle
            : severity >= AlarmSeverity.Warning
                ? m_primaryButtonStyle
                : m_buttonStyle;
    }

    private void DrawAlarmAreaFilter()
    {
        var filter = NormalizeAlarmAreaFilter();
        var compact = m_windowRect.width < 820f;
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("board.area_filter", "AREA"),
            m_smallLabelStyle,
            NativeGUILayout.Width(compact ? 58f : 76f),
            NativeGUILayout.Height(30f));
        m_alarmAreaTabsScroll = NativeGUILayout.BeginScrollView(
            m_alarmAreaTabsScroll,
            false,
            false,
            NativeGUILayout.Height(42f),
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.BeginHorizontal();
        DrawAlarmAreaFilterButton(
            AlarmAreaFilter.All,
            UnmaText.Get("board.area_all", "ALL"),
            filter);
        DrawAlarmAreaFilterButton(
            AlarmAreaFilter.Unassigned,
            UnmaText.Get("board.area_unassigned", "UNASSIGNED"),
            filter);
        foreach (var area in m_runtime.Configuration.AlarmAreas ??
                     new List<AlarmAreaDefinition>())
        {
            if (area == null || string.IsNullOrWhiteSpace(area.Id))
            {
                continue;
            }
            DrawAlarmAreaFilterButton(
                AlarmAreaFilter.ForArea(area.Id),
                area.Name,
                filter);
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.EndScrollView();
        if (NativeGUILayout.Button(
                compact
                    ? UnmaText.Get("board.area_manage_short", "AREAS")
                    : UnmaText.Get("board.area_manage", "MANAGE AREAS"),
                m_buttonStyle,
                NativeGUILayout.Width(compact ? 82f : 142f),
                NativeGUILayout.Height(30f)))
        {
            OpenAlarmAreasEditor();
        }
        NativeGUILayout.EndHorizontal();
    }

    private void DrawAlarmAreaFilterButton(
        AlarmAreaFilter candidate,
        string name,
        AlarmAreaFilter selected)
    {
        var panelCount = AlarmAreaPolicy.Select(GlobalPanels, candidate)
            .Count(AlarmAreaPolicy.IsAssignablePanel);
        var isSelected = AlarmAreaFiltersEqual(candidate, selected);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? UnmaText.Get("ui.area.default_name", "AREA")
            : name.Trim();
        var label = UnmaText.Format(
            "board.area_filter_item",
            "{0} · {1} PANELS",
            displayName,
            panelCount);
        if (isSelected && TryGetCachedAlarmAreaDashboardViews(
                candidate,
                out var scopedViews))
        {
            scopedViews ??= Array.Empty<AlarmView>();
            label = UnmaText.Format(
                "board.area_filter_item_selected",
                "{0} · {1} PANELS · {2} ACTIVE · {3} UNACK",
                displayName,
                panelCount,
                scopedViews.Count(view => view.IsActive),
                scopedViews.Count(view => view.RequiresAcknowledgement));
        }
        var width = Mathf.Clamp(
            label.Length * Mathf.Max(7f, m_buttonStyle.fontSize * 0.58f) + 24f,
            116f,
            isSelected ? 380f : 290f);
        if (NativeGUILayout.Button(
                label,
                isSelected
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(width),
                NativeGUILayout.Height(30f)))
        {
            SelectAlarmAreaFilter(candidate);
        }
    }

    private void SelectAlarmAreaFilter(AlarmAreaFilter requested)
    {
        var next = AlarmAreaPolicy.NormalizeFilter(
            requested,
            m_runtime.Configuration.AlarmAreas);
        if (AlarmAreaFiltersEqual(next, NormalizeAlarmAreaFilter()))
        {
            return;
        }

        var current = CurrentPanel;
        m_alarmAreaFilter = next;
        var visiblePanels = GetVisibleGlobalPanels();
        if (current == null ||
            !visiblePanels.Any(panel => string.Equals(
                panel.Id,
                current.Id,
                StringComparison.Ordinal)))
        {
            var fallback = visiblePanels.FirstOrDefault(panel =>
                               panel.IsDashboard) ??
                           visiblePanels.FirstOrDefault();
            SelectGlobalPanel(fallback, false);
        }
        m_lastNavigatedAlarmSlotId = "";
        m_boardScroll = Vector2.zero;
        m_incidentLensScroll = Vector2.zero;
    }

    private void SelectGlobalPanel(
        PanelDefinition panel,
        bool revealOutsideCurrentArea)
    {
        if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return;
        }

        if (revealOutsideCurrentArea &&
            !panel.IsDashboard &&
            !GetVisibleGlobalPanels().Any(candidate => string.Equals(
                candidate.Id,
                panel.Id,
                StringComparison.Ordinal)))
        {
            var areaId = panel.AreaId?.Trim() ?? "";
            if (areaId.Length == 0)
            {
                m_alarmAreaFilter = AlarmAreaFilter.Unassigned;
            }
            else if ((m_runtime.Configuration.AlarmAreas ??
                      new List<AlarmAreaDefinition>()).Any(area =>
                         area != null && string.Equals(
                             area.Id,
                             areaId,
                             StringComparison.Ordinal)))
            {
                m_alarmAreaFilter = AlarmAreaFilter.ForArea(areaId);
            }
            else
            {
                m_alarmAreaFilter = AlarmAreaFilter.All;
            }
        }

        m_activeEntityPanelId = "";
        var panelIndex = GlobalPanels.FindIndex(candidate => string.Equals(
            candidate.Id,
            panel.Id,
            StringComparison.Ordinal));
        if (panelIndex >= 0)
        {
            m_currentPanelIndex = panelIndex;
        }
    }

    private void EnsureCurrentPanelVisibleInArea()
    {
        var current = CurrentPanel;
        if (current == null || PanelTopologyPolicy.IsEntityPanel(current))
        {
            return;
        }
        var visible = GetVisibleGlobalPanels();
        if (visible.Any(panel => string.Equals(
                panel.Id,
                current.Id,
                StringComparison.Ordinal)))
        {
            return;
        }
        SelectGlobalPanel(
            visible.FirstOrDefault(panel => panel.IsDashboard) ??
            visible.FirstOrDefault(),
            false);
        m_boardScroll = Vector2.zero;
    }

    private AlarmAreaFilter NormalizeAlarmAreaFilter()
    {
        m_alarmAreaFilter = AlarmAreaPolicy.NormalizeFilter(
            m_alarmAreaFilter,
            m_runtime.Configuration.AlarmAreas);
        return m_alarmAreaFilter;
    }

    private List<PanelDefinition> GetVisibleGlobalPanels()
    {
        var globalPanels = GlobalPanels;
        var filter = NormalizeAlarmAreaFilter();
        if (filter.Kind == AlarmAreaFilterKind.All)
        {
            return globalPanels;
        }

        var visible = AlarmAreaPolicy.Select(globalPanels, filter).ToList();
        var dashboard = globalPanels.FirstOrDefault(panel =>
            panel != null && panel.IsDashboard);
        if (dashboard != null)
        {
            visible.Insert(0, dashboard);
        }
        return visible;
    }

    private string GetAreaAwarePanelTabLabel(PanelDefinition panel)
    {
        if (panel == null || !panel.IsDashboard)
        {
            return panel?.Name ?? "";
        }
        var filter = NormalizeAlarmAreaFilter();
        if (filter.Kind == AlarmAreaFilterKind.All)
        {
            return panel.Name;
        }
        return UnmaText.Format(
            "board.dashboard_scope",
            "{0} · {1}",
            panel.Name,
            GetAlarmAreaFilterName(filter));
    }

    private string GetAlarmAreaFilterName(AlarmAreaFilter filter)
    {
        switch (filter.Kind)
        {
            case AlarmAreaFilterKind.Unassigned:
                return UnmaText.Get(
                    "board.area_unassigned",
                    "UNASSIGNED");
            case AlarmAreaFilterKind.Area:
                return m_runtime.Configuration.AlarmAreas?
                           .FirstOrDefault(area => area != null &&
                               string.Equals(
                                   area.Id,
                                   filter.AreaId,
                                   StringComparison.Ordinal))?.Name ??
                       UnmaText.Get("ui.area.default_name", "AREA");
            default:
                return UnmaText.Get("board.area_all", "ALL");
        }
    }

    private string GetCurrentConcreteAlarmAreaId()
    {
        var filter = NormalizeAlarmAreaFilter();
        return filter.Kind == AlarmAreaFilterKind.Area
            ? filter.AreaId ?? ""
            : "";
    }

    private IReadOnlyList<AlarmView> GetBoardViews(PanelDefinition panel)
    {
        if (!IsAreaScopedDashboard(panel))
        {
            return GetPanelViews(panel);
        }
        return TryGetCachedAlarmAreaDashboardViews(
            NormalizeAlarmAreaFilter(),
            out var views)
            ? views ?? Array.Empty<AlarmView>()
            : Array.Empty<AlarmView>();
    }

    private bool TryGetCachedAlarmAreaDashboardViews(
        AlarmAreaFilter filter,
        out IReadOnlyList<AlarmView> views)
    {
        if (m_alarmAreaViewCacheFrame == Time.frameCount &&
            AlarmAreaFiltersEqual(m_alarmAreaViewCacheFilter, filter))
        {
            views = m_alarmAreaViewCache;
            return m_alarmAreaViewCacheSucceeded;
        }

        m_alarmAreaViewCacheFrame = Time.frameCount;
        m_alarmAreaViewCacheFilter = filter;
        m_alarmAreaViewCacheSucceeded = m_runtime.TryGetDashboardViews(
            filter,
            out views);
        m_alarmAreaViewCache = views ?? Array.Empty<AlarmView>();
        views = m_alarmAreaViewCache;
        return m_alarmAreaViewCacheSucceeded;
    }

    private bool IsAreaScopedDashboard(PanelDefinition panel)
    {
        return panel?.IsDashboard == true &&
               NormalizeAlarmAreaFilter().Kind != AlarmAreaFilterKind.All;
    }

    private static bool AlarmAreaFiltersEqual(
        AlarmAreaFilter left,
        AlarmAreaFilter right)
    {
        return left.Kind == right.Kind &&
               string.Equals(
                   left.AreaId ?? "",
                   right.AreaId ?? "",
                   StringComparison.Ordinal);
    }

    private void DrawInstruments()
    {
        if (!string.IsNullOrWhiteSpace(m_historianInstrumentId))
        {
            DrawInstrumentHistorianView();
            return;
        }

        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.instrument.console_title",
                "MEASUREMENT AND RECORDING CONSOLE · SERIES 1974"),
            m_sectionStyle);
        NativeGUILayout.Space(6f);
        DrawInstrumentPanelTabs();
        var currentPanel = CurrentInstrumentPanel;
        if (currentPanel == null)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.instrument.no_panel",
                    "No instrument panel is available."),
                m_labelStyle);
            return;
        }
        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginVertical(m_panelStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.instrument.add_hint",
                "Add a measurement point: open a building in the game, take the source, and select a metric."),
            m_smallLabelStyle);
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.instrument.take_open_building",
                    "SOURCE FROM OPEN BUILDING"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(270f),
                NativeGUILayout.Height(30f)))
        {
            CaptureInstrumentEntity();
        }
        NativeGUILayout.Label(
            m_instrumentDraftEntity == null
                ? UnmaText.Get(
                    "ui.instrument.no_source_selected",
                    "No source selected")
                : m_instrumentDraftEntity.Title,
            m_labelStyle,
            NativeGUILayout.Height(30f));
        NativeGUILayout.EndHorizontal();

        if (m_instrumentDraftEntity != null &&
            m_instrumentDraftMetrics.Count > 0)
        {
            NativeGUILayout.Space(4f);
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.instrument.metric", "METRIC"),
                m_smallLabelStyle,
                NativeGUILayout.Width(82f),
                NativeGUILayout.ExpandWidth(false));
            var selectedMetric = m_instrumentDraftMetrics[
                Math.Max(0, Math.Min(
                    m_instrumentDraftMetricIndex,
                    m_instrumentDraftMetrics.Count - 1))];
            if (NativeGUILayout.Button(
                    selectedMetric.Label + "  ·  " +
                    FormatMetricValue(selectedMetric),
                    m_buttonStyle,
                    NativeGUILayout.Height(28f)))
            {
                m_metricPickerOpen = !m_metricPickerOpen;
            }
            NativeGUILayout.EndHorizontal();

            if (m_metricPickerOpen)
            {
                NativeGUILayout.BeginHorizontal();
                NativeGUILayout.Label(
                    UnmaText.Get("ui.common.search", "Search"),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(82f),
                    NativeGUILayout.ExpandWidth(false));
                var metricFilter = NativeGUILayout.TextField(
                    m_instrumentMetricFilter,
                    80,
                    m_textFieldStyle,
                    new NativeControlMetadata(
                        "instrument-metric-search",
                        UnmaText.Get("ui.common.search", "Search")),
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(28f));
                if (!string.Equals(
                        metricFilter,
                        m_instrumentMetricFilter,
                        StringComparison.Ordinal))
                {
                    m_instrumentMetricFilter = metricFilter;
                    m_instrumentMetricScroll = Vector2.zero;
                }
                var clearMetricSearchTooltip = UnmaText.Get(
                    "ui.common.clear",
                    "Clear search");
                if (NativeGUILayout.Button(
                        "\u00D7",
                        m_buttonStyle,
                        new NativeControlMetadata(
                            "instrument-metric-search-clear",
                            clearMetricSearchTooltip),
                        NativeGUILayout.Width(34f),
                        NativeGUILayout.ExpandWidth(false),
                        NativeGUILayout.Height(28f)))
                {
                    m_instrumentMetricFilter = "";
                    m_instrumentMetricScroll = Vector2.zero;
                }
                NativeGUILayout.EndHorizontal();

                m_instrumentMetricScroll = NativeGUILayout.BeginScrollView(
                    m_instrumentMetricScroll,
                    m_panelStyle,
                    NativeGUILayout.Height(145f));
                for (var index = 0;
                     index < m_instrumentDraftMetrics.Count;
                     index++)
                {
                    var metric = m_instrumentDraftMetrics[index];
                    if (!MetricPickerFilter.Matches(
                            metric.Label,
                            metric.Path,
                            m_instrumentMetricFilter))
                    {
                        continue;
                    }
                    if (NativeGUILayout.Button(
                            metric.Label + "  [" + metric.Path + "]  " +
                            FormatMetricValue(metric),
                            index == m_instrumentDraftMetricIndex
                                ? m_primaryButtonStyle
                                : m_buttonStyle,
                            NativeGUILayout.Height(26f)))
                    {
                        SelectInstrumentMetric(index);
                        m_metricPickerOpen = false;
                    }
                }
                NativeGUILayout.EndScrollView();
            }

            DrawInstrumentSourceEditor(selectedMetric);

            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.instrument.label", "LABEL"),
                m_smallLabelStyle,
                NativeGUILayout.Width(62f));
            m_instrumentDraftTitle = NativeGUILayout.TextField(
                m_instrumentDraftTitle ?? "",
                m_textFieldStyle,
                NativeGUILayout.MinWidth(130f),
                NativeGUILayout.Height(28f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.common.from", "FROM"),
                m_smallLabelStyle,
                NativeGUILayout.Width(34f));
            m_instrumentDraftMinimum = NativeGUILayout.TextField(
                m_instrumentDraftMinimum,
                m_textFieldStyle,
                NativeGUILayout.Width(76f),
                NativeGUILayout.Height(28f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.common.to", "TO"),
                m_smallLabelStyle,
                NativeGUILayout.Width(30f));
            m_instrumentDraftMaximum = NativeGUILayout.TextField(
                m_instrumentDraftMaximum,
                m_textFieldStyle,
                NativeGUILayout.Width(76f),
                NativeGUILayout.Height(28f));
            if (NativeGUILayout.Button(
                    UnmaText.Format(
                        "ui.instrument.type_button",
                        "TYPE: {0}  V",
                        InstrumentTypeLabel(m_instrumentDraftType)),
                    m_buttonStyle,
                    NativeGUILayout.Width(220f),
                    NativeGUILayout.Height(28f)))
            {
                m_instrumentTypePickerOpen = !m_instrumentTypePickerOpen;
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get(
                        "ui.instrument.install",
                        "INSTALL INSTRUMENT"),
                    m_primaryButtonStyle,
                    NativeGUILayout.Width(180f),
                    NativeGUILayout.Height(28f)))
            {
                AddInstrument();
            }
            NativeGUILayout.EndHorizontal();
            if (m_instrumentTypePickerOpen)
            {
                DrawInstrumentTypePicker(selectedMetric);
            }
        }
        NativeGUILayout.EndVertical();

        NativeGUILayout.Space(8f);
        var instruments = m_runtime.Configuration.Instruments
            .Where(instrument => string.Equals(
                instrument.PanelId,
                currentPanel.Id,
                StringComparison.Ordinal))
            .ToList();
        if (instruments.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.instrument.empty",
                    "No instruments have been installed yet. The console waits stoically."),
                m_labelStyle);
            return;
        }

        m_instrumentScroll = NativeGUILayout.BeginScrollView(m_instrumentScroll);
        const float cardGap = 7f;
        const float preferredCardWidth = 360f;
        var availableWidth = Mathf.Max(250f, m_windowRect.width - 62f);
        var columns = Math.Min(
            instruments.Count,
            Math.Max(
                1,
                Math.Min(
                    5,
                    Mathf.FloorToInt(
                        (availableWidth + cardGap) /
                        (preferredCardWidth + cardGap)))));
        var cardWidth = Mathf.Clamp(
            (availableWidth - cardGap * (columns - 1)) / columns,
            245f,
            480f);
        for (var start = 0; start < instruments.Count; start += columns)
        {
            var rowKey = "instrument-row:";
            for (var keyColumn = 0; keyColumn < columns; keyColumn++)
            {
                var keyIndex = start + keyColumn;
                rowKey += keyIndex < instruments.Count
                    ? "|" + instruments[keyIndex].Id
                    : "|empty";
            }
            NativeGUILayout.BeginHorizontal(rowKey);
            for (var column = 0; column < columns; column++)
            {
                var index = start + column;
                if (index >= instruments.Count)
                {
                    NativeGUILayout.FlexibleSpace();
                    continue;
                }
                var instrument = instruments[index];
                var rect = NativeGUILayoutUtility.GetRect(
                    cardWidth,
                    cardWidth,
                    225f,
                    225f,
                    NativeGUILayout.Width(cardWidth));
                m_instrumentValues.TryGetValue(instrument.Id, out var value);
                m_instrumentSamples.TryGetValue(instrument.Id, out var samples);
                InstrumentPanelRenderer.Draw(
                    rect,
                    instrument,
                    value,
                    !m_invalidInstruments.Contains(instrument.Id),
                    samples,
                    m_labelStyle,
                    m_smallLabelStyle,
                    reserveActionBar: true);
                if (NativeGUI.Button(
                        new Rect(rect.xMax - 27f, rect.y + 5f, 22f, 22f),
                        "X",
                        m_dangerButtonStyle))
                {
                    RemoveInstrument(instrument.Id);
                    NativeGUILayout.EndHorizontal();
                    NativeGUILayout.EndScrollView();
                    return;
                }
                if (NativeGUI.Button(
                        new Rect(rect.x + 6f, rect.y + 30f, 24f, 22f),
                        "↗",
                        m_primaryButtonStyle))
                {
                    NavigateToEntity(instrument.EntityId);
                }
                if (NativeGUI.Button(
                        new Rect(rect.x + 34f, rect.y + 30f, 68f, 22f),
                        UnmaText.Get("ui.historian.short", "HIST"),
                        m_buttonStyle))
                {
                    EnterInstrumentHistorian(instrument);
                }
                if (NativeGUI.Button(
                        new Rect(
                            rect.x + 106f,
                            rect.y + 30f,
                            58f,
                            22f),
                        UnmaText.Get("ui.instrument.alarm_short", "ALARM"),
                        m_buttonStyle))
                {
                    OpenInstrumentAlarmEditor(instrument);
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Space(7f);
        }
        NativeGUILayout.EndScrollView();
    }

    private void DrawInstrumentPanelTabs()
    {
        var panels = m_runtime.Configuration.InstrumentPanels;
        if (panels == null || panels.Count == 0)
        {
            return;
        }
        m_currentInstrumentPanelIndex = Math.Max(
            0,
            Math.Min(m_currentInstrumentPanelIndex, panels.Count - 1));
        NativeGUILayout.BeginHorizontal();
        m_instrumentPanelTabsScroll = NativeGUILayout.BeginScrollView(
            m_instrumentPanelTabsScroll,
            false,
            false,
            NativeGUILayout.Height(45f),
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.BeginHorizontal();
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            if (NativeGUILayout.Button(
                    panel.Name,
                    index == m_currentInstrumentPanelIndex
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.MinWidth(120f),
                    NativeGUILayout.MaxWidth(220f),
                    NativeGUILayout.Height(30f)))
            {
                m_currentInstrumentPanelIndex = index;
                m_instrumentScroll = Vector2.zero;
            }
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.EndScrollView();
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.instrument.panel.add", "+ PANEL"),
                m_buttonStyle,
                NativeGUILayout.Width(92f),
                NativeGUILayout.Height(30f)))
        {
            m_instrumentPanelCreationOpen = !m_instrumentPanelCreationOpen;
        }
        if (panels.Count > 1)
        {
            var current = panels[m_currentInstrumentPanelIndex];
            var confirmDelete = string.Equals(
                                    m_pendingInstrumentPanelDeleteId,
                                    current.Id,
                                    StringComparison.Ordinal) &&
                                Time.realtimeSinceStartup <
                                m_pendingInstrumentPanelDeleteUntil;
            if (NativeGUILayout.Button(
                    confirmDelete
                        ? UnmaText.Get("ui.common.really", "REALLY?")
                        : UnmaText.Get(
                            "ui.instrument.panel.remove",
                            "REMOVE PANEL"),
                    confirmDelete ? m_dangerButtonStyle : m_buttonStyle,
                    NativeGUILayout.Width(92f),
                    NativeGUILayout.Height(30f)))
            {
                if (confirmDelete)
                {
                    RemoveCurrentInstrumentPanel();
                }
                else
                {
                    m_pendingInstrumentPanelDeleteId = current.Id;
                    m_pendingInstrumentPanelDeleteUntil =
                        Time.realtimeSinceStartup + 5f;
                    SetStatus(
                        UnmaText.Get(
                            "ui.instrument.panel.confirm_remove",
                            "Press again to remove the panel; its instruments will be moved."));
                }
            }
        }
        NativeGUILayout.EndHorizontal();

        if (!m_instrumentPanelCreationOpen)
        {
            return;
        }
        NativeGUILayout.BeginHorizontal(m_panelStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.instrument.panel.new",
                "NEW INSTRUMENT PANEL"),
            m_smallLabelStyle,
            NativeGUILayout.Width(125f));
        m_newInstrumentPanelName = NativeGUILayout.TextField(
            m_newInstrumentPanelName ?? "",
            m_textFieldStyle,
            NativeGUILayout.MinWidth(180f),
            NativeGUILayout.Height(28f));
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.create", "CREATE"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(100f),
                NativeGUILayout.Height(28f)))
        {
            AddInstrumentPanel();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.cancel", "CANCEL"),
                m_buttonStyle,
                NativeGUILayout.Width(100f),
                NativeGUILayout.Height(28f)))
        {
            m_instrumentPanelCreationOpen = false;
        }
        NativeGUILayout.EndHorizontal();
    }

    private void AddInstrumentPanel()
    {
        var name = string.IsNullOrWhiteSpace(m_newInstrumentPanelName)
            ? UnmaText.Format(
                "ui.instrument.panel.default_name",
                "INSTRUMENT PANEL {0}",
                m_runtime.Configuration.InstrumentPanels.Count + 1)
            : m_newInstrumentPanelName.Trim().ToUpperInvariant();
        m_runtime.Configuration.InstrumentPanels.Add(
            new InstrumentPanelDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
            });
        m_currentInstrumentPanelIndex =
            m_runtime.Configuration.InstrumentPanels.Count - 1;
        m_instrumentPanelCreationOpen = false;
        m_newInstrumentPanelName = UnmaText.Format(
            "ui.instrument.panel.default_name",
            "INSTRUMENT PANEL {0}",
            m_currentInstrumentPanelIndex + 2);
        SaveConfiguration(UnmaText.Get(
            "ui.instrument.panel.created",
            "Instrument panel created."));
    }

    private void RemoveCurrentInstrumentPanel()
    {
        var panels = m_runtime.Configuration.InstrumentPanels;
        if (panels.Count <= 1)
        {
            return;
        }
        var removed = panels[m_currentInstrumentPanelIndex];
        panels.RemoveAt(m_currentInstrumentPanelIndex);
        m_currentInstrumentPanelIndex = Math.Max(
            0,
            Math.Min(m_currentInstrumentPanelIndex, panels.Count - 1));
        var destination = panels[m_currentInstrumentPanelIndex];
        foreach (var instrument in m_runtime.Configuration.Instruments.Where(
                     item => string.Equals(
                         item.PanelId,
                         removed.Id,
                         StringComparison.Ordinal)))
        {
            instrument.PanelId = destination.Id;
        }
        m_pendingInstrumentPanelDeleteId = "";
        SaveConfiguration(UnmaText.Format(
            "ui.instrument.panel.removed",
            "Instrument panel removed; its instruments were moved to {0}.",
            destination.Name));
    }

    private void DrawInstrumentTypePicker(MetricDescriptor selectedMetric)
    {
        NativeGUILayout.Space(5f);
        NativeGUILayout.BeginVertical(m_panelStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.instrument.type_picker_title",
                "SELECT INSTRUMENT TYPE · PREVIEW"),
            m_smallLabelStyle);
        m_instrumentTypePickerScroll = NativeGUILayout.BeginScrollView(
            m_instrumentTypePickerScroll,
            NativeGUILayout.Height(Mathf.Min(390f, m_windowRect.height * 0.48f)));
        var types = (InstrumentDisplayType[])Enum.GetValues(
            typeof(InstrumentDisplayType));
        const int columns = 3;
        for (var start = 0; start < types.Length; start += columns)
        {
            NativeGUILayout.BeginHorizontal();
            for (var column = 0; column < columns; column++)
            {
                var index = start + column;
                if (index >= types.Length)
                {
                    NativeGUILayout.FlexibleSpace();
                    continue;
                }
                var type = types[index];
                var rect = NativeGUILayoutUtility.GetRect(
                    180f,
                    280f,
                    164f,
                    164f,
                    NativeGUILayout.ExpandWidth(true));
                var preview = new InstrumentDefinition
                {
                    Title = InstrumentTypeLabel(type),
                    DisplayType = type,
                    EntityTitle = UnmaText.Get(
                        "ui.instrument.preview",
                        "PREVIEW"),
                    MetricLabel = selectedMetric.Label,
                    Unit = selectedMetric.Unit,
                    Minimum = 0d,
                    Maximum = string.Equals(
                        selectedMetric.Unit,
                        "%",
                        StringComparison.Ordinal)
                        ? 100d
                        : Math.Max(1d, selectedMetric.CurrentValue * 1.25d),
                };
                InstrumentPanelRenderer.Draw(
                    rect,
                    preview,
                    selectedMetric.CurrentValue,
                    true,
                    s_instrumentPreviewSamples,
                    m_labelStyle,
                    m_smallLabelStyle);
                if (NativeGUI.Button(
                        new Rect(
                            rect.x + 7f,
                            rect.yMax - 31f,
                            rect.width - 14f,
                            24f),
                        type == m_instrumentDraftType
                            ? UnmaText.Get("ui.common.selected", "SELECTED")
                            : UnmaText.Get("ui.common.select", "SELECT"),
                        type == m_instrumentDraftType
                            ? m_primaryButtonStyle
                            : m_buttonStyle))
                {
                    m_instrumentDraftType = type;
                    m_instrumentTypePickerOpen = false;
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Space(5f);
        }
        NativeGUILayout.EndScrollView();
        NativeGUILayout.EndVertical();
    }

    private void DrawInstrumentSourceEditor(MetricDescriptor selectedMetric)
    {
        NativeGUILayout.Space(5f);
        NativeGUILayout.BeginVertical(m_panelStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.instrument.calculation", "CALCULATION"),
            m_smallLabelStyle,
            NativeGUILayout.Width(86f),
            NativeGUILayout.Height(27f));
        foreach (InstrumentAggregationMode mode in Enum.GetValues(
                     typeof(InstrumentAggregationMode)))
        {
            if (NativeGUILayout.Button(
                    InstrumentAggregationLabel(mode),
                    mode == m_instrumentDraftAggregation
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.MinWidth(72f),
                    NativeGUILayout.Height(27f)))
            {
                m_instrumentDraftAggregation = mode;
            }
        }
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.instrument.sources_count",
                "SOURCES {0}",
                m_instrumentDraftSources.Count),
            m_smallLabelStyle,
            NativeGUILayout.Width(86f),
            NativeGUILayout.Height(27f));
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.instrument.add_matching_source",
                    "+ OPEN BUILDING WITH SAME METRIC"),
                m_buttonStyle,
                NativeGUILayout.Width(330f),
                NativeGUILayout.Height(27f)))
        {
            AddOpenEntityAsInstrumentSource(selectedMetric);
        }
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();

        for (var index = 0; index < m_instrumentDraftSources.Count; index++)
        {
            var source = m_instrumentDraftSources[index];
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Space(88f);
            NativeGUILayout.Label(
                UnmaText.Format(
                    "ui.instrument.source_row",
                    "{0} · {1} · ID {2}",
                    index + 1,
                    source.EntityTitle,
                    source.EntityId),
                m_labelStyle,
                NativeGUILayout.Height(24f));
            if (m_instrumentDraftSources.Count > 1 && NativeGUILayout.Button(
                    "X",
                    m_dangerButtonStyle,
                    NativeGUILayout.Width(34f),
                    NativeGUILayout.Height(24f)))
            {
                m_instrumentDraftSources.RemoveAt(index);
                if (m_instrumentDraftSources.Count == 1)
                {
                    m_instrumentDraftAggregation =
                        InstrumentAggregationMode.Single;
                }
                NativeGUILayout.EndHorizontal();
                break;
            }
            NativeGUILayout.EndHorizontal();
        }
        NativeGUILayout.EndVertical();
    }

    private void AddOpenEntityAsInstrumentSource(
        MetricDescriptor selectedMetric)
    {
        var entity = m_inspectorsManager.GetFirstActiveEntityOrNull();
        if (entity == null)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.open_additional_building",
                "Open the additional building in the game first."));
            return;
        }
        var entityId = entity.Id.Value;
        if (m_instrumentDraftSources.Any(source =>
                source.EntityId == entityId))
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.source_already_added",
                "This building has already been added as a source."));
            return;
        }
        if (!EntityMetricCatalog.TryRead(
                entity,
                selectedMetric.Path,
                out _))
        {
            SetStatus(UnmaText.Format(
                "ui.instrument.status.metric_not_available",
                "The open building does not provide the metric '{0}'.",
                selectedMetric.Label));
            return;
        }

        m_instrumentDraftSources.Add(new InstrumentSourceDefinition
        {
            EntityId = entityId,
            EntityTitle = EntityMetricCatalog.GetEntityTitle(entity),
            EntityPrototypeId = entity.Prototype.Id.Value,
        });
        if (m_instrumentDraftAggregation == InstrumentAggregationMode.Single)
        {
            m_instrumentDraftAggregation = InstrumentAggregationMode.Sum;
        }
        SetStatus(UnmaText.Format(
            "ui.instrument.status.source_added",
            "Calculated source added: {0}",
            EntityMetricCatalog.GetEntityTitle(entity)));
    }

    private static string InstrumentAggregationLabel(
        InstrumentAggregationMode mode)
    {
        return mode switch
        {
            InstrumentAggregationMode.Sum => UnmaText.Get(
                "ui.instrument.aggregation.sum",
                "SUM"),
            InstrumentAggregationMode.Average => UnmaText.Get(
                "ui.instrument.aggregation.average",
                "AVERAGE"),
            InstrumentAggregationMode.Minimum => UnmaText.Get(
                "ui.instrument.aggregation.minimum",
                "MIN"),
            InstrumentAggregationMode.Maximum => UnmaText.Get(
                "ui.instrument.aggregation.maximum",
                "MAX"),
            _ => UnmaText.Get(
                "ui.instrument.aggregation.single",
                "SINGLE"),
        };
    }

    private void CaptureInstrumentEntity()
    {
        var entity = m_inspectorsManager.GetFirstActiveEntityOrNull();
        if (entity == null)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.open_building",
                "Open a building in the game first."));
            return;
        }
        var metrics = EntityMetricCatalog.Discover(entity);
        if (metrics.Count == 0)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.no_metrics",
                "No metrics were found for this building."));
            return;
        }
        m_instrumentDraftEntity = new EntityInspectionSnapshot(
            entity.Id.Value,
            EntityMetricCatalog.GetEntityTitle(entity),
            entity.GetType().FullName,
            entity.Prototype.Id.Value,
            EntityMetricCatalog.TryGetStoredProductId(entity),
            metrics);
        m_instrumentDraftMetrics = metrics;
        m_instrumentDraftMetricIndex = 0;
        m_instrumentMetricFilter = "";
        m_instrumentMetricScroll = Vector2.zero;
        m_instrumentDraftSources.Clear();
        m_instrumentDraftSources.Add(new InstrumentSourceDefinition
        {
            EntityId = m_instrumentDraftEntity.EntityId,
            EntityTitle = m_instrumentDraftEntity.Title,
            EntityPrototypeId = m_instrumentDraftEntity.PrototypeId,
        });
        m_instrumentDraftAggregation = InstrumentAggregationMode.Single;
        m_metricPickerOpen = false;
        SelectInstrumentMetric(FindPreferredInstrumentMetric(metrics));
        SetStatus(UnmaText.Format(
            "ui.instrument.status.source_taken",
            "Measurement source selected: {0}",
            m_instrumentDraftEntity.Title));
    }

    private static int FindPreferredInstrumentMetric(
        IReadOnlyList<MetricDescriptor> metrics)
    {
        for (var index = 0; index < metrics.Count; index++)
        {
            if (string.Equals(
                    metrics[index].Path,
                    "$stored.percent",
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return 0;
    }

    private void SelectInstrumentMetric(int index)
    {
        if (m_instrumentDraftMetrics.Count == 0)
        {
            return;
        }
        m_instrumentDraftMetricIndex = Math.Max(
            0,
            Math.Min(index, m_instrumentDraftMetrics.Count - 1));
        var metric = m_instrumentDraftMetrics[m_instrumentDraftMetricIndex];
        m_instrumentDraftTitle = metric.Label.ToUpperInvariant();
        m_instrumentDraftMinimum = "0";
        var suggestedMaximum = string.Equals(metric.Unit, "%", StringComparison.Ordinal)
            ? 100d
            : Math.Max(1d, Math.Ceiling(metric.CurrentValue * 1.25d));
        m_instrumentDraftMaximum = suggestedMaximum.ToString(
            "0.###",
            CultureInfo.CurrentCulture);
    }

    private void AddInstrument()
    {
        if (m_instrumentDraftEntity == null ||
            m_instrumentDraftMetrics.Count == 0 ||
            !TryParseDouble(m_instrumentDraftMinimum, out var minimum) ||
            !TryParseDouble(m_instrumentDraftMaximum, out var maximum) ||
            maximum <= minimum)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.invalid_range",
                "Invalid measurement range: TO must be greater than FROM."));
            return;
        }
        var metric = m_instrumentDraftMetrics[m_instrumentDraftMetricIndex];
        if (m_instrumentDraftSources.Count == 0)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.source_required",
                "At least one measurement source is required."));
            return;
        }
        foreach (var source in m_instrumentDraftSources)
        {
            if (!m_runtime.TryGetLiveEntity(source.EntityId, out var entity) ||
                !EntityMetricCatalog.TryRead(entity, metric.Path, out _))
            {
                SetStatus(UnmaText.Format(
                    "ui.instrument.status.source_unavailable",
                    "Source unavailable or metric missing: {0}",
                    source.EntityTitle));
                return;
            }
        }
        var panel = CurrentInstrumentPanel;
        if (panel == null)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.no_target_panel",
                "No instrument panel is available as a target."));
            return;
        }
        var primarySource = m_instrumentDraftSources[0];
        var instrument = new InstrumentDefinition
        {
            Title = string.IsNullOrWhiteSpace(m_instrumentDraftTitle)
                ? metric.Label.ToUpperInvariant()
                : m_instrumentDraftTitle.Trim().ToUpperInvariant(),
            DisplayType = m_instrumentDraftType,
            EntityId = primarySource.EntityId,
            EntityTitle = m_instrumentDraftSources.Count > 1
                ? UnmaText.Format(
                    "ui.instrument.calculated_source_title",
                    "{0} SOURCES · {1}",
                    m_instrumentDraftSources.Count,
                    InstrumentAggregationLabel(m_instrumentDraftAggregation))
                : primarySource.EntityTitle,
            EntityPrototypeId = primarySource.EntityPrototypeId,
            MetricPath = metric.Path,
            MetricLabel = metric.Label,
            Unit = metric.Unit,
            Minimum = minimum,
            Maximum = maximum,
            PanelId = panel.Id,
            Sources = m_instrumentDraftSources.Select(source =>
                new InstrumentSourceDefinition
                {
                    EntityId = source.EntityId,
                    EntityTitle = source.EntityTitle,
                    EntityPrototypeId = source.EntityPrototypeId,
                }).ToList(),
            Aggregation = m_instrumentDraftAggregation,
            HistoryDurationSeconds = 21600,
            HistoryDurationAmount = 100,
            HistoryDurationUnit = GameTimeUnit.Year,
        };
        m_runtime.Configuration.Instruments.Add(instrument);
        SaveConfiguration(UnmaText.Get(
            "ui.instrument.status.installed",
            "Instrument installed."));
        RefreshInstrumentValues();
    }

    private void RemoveInstrument(string instrumentId)
    {
        var dependentRules = m_runtime.Configuration.Rules.Count(rule =>
            rule?.Conditions?.Any(condition =>
                condition != null && string.Equals(
                    condition.InstrumentId,
                    instrumentId,
                    StringComparison.Ordinal)) == true);
        var draftDependsOnInstrument = m_draftConditions.Any(condition =>
            condition != null && string.Equals(
                condition.InstrumentId,
                instrumentId,
                StringComparison.Ordinal));
        if (dependentRules > 0 || draftDependsOnInstrument)
        {
            SetStatus(draftDependsOnInstrument
                ? UnmaText.Get(
                    "ui.instrument.status.used_by_draft",
                    "The instrument is used by the open alarm draft. Save or discard the draft first.")
                : UnmaText.Format(
                    "ui.instrument.status.used_by_rules",
                    "The instrument is used by {0} alarm(s). Delete them in the alarm editor first.",
                    dependentRules));
            return;
        }

        m_runtime.Configuration.Instruments.RemoveAll(item => string.Equals(
            item.Id,
            instrumentId,
            StringComparison.Ordinal));
        m_instrumentSamples.Remove(instrumentId);
        m_instrumentValues.Remove(instrumentId);
        m_invalidInstruments.Remove(instrumentId);
        if (string.Equals(
                m_historianInstrumentId,
                instrumentId,
                StringComparison.Ordinal))
        {
            ExitInstrumentHistorian();
        }
        SaveConfiguration(UnmaText.Get(
            "ui.instrument.status.removed",
            "Instrument removed."));
    }

    private void RefreshInstrumentValues()
    {
        if (m_runtime?.Configuration?.Instruments == null)
        {
            return;
        }
        foreach (var instrument in m_runtime.Configuration.Instruments)
        {
            if (!m_runtime.TryReadInstrumentValue(
                    instrument,
                    out var value,
                    out _))
            {
                m_invalidInstruments.Add(instrument.Id);
                m_instrumentValues.Remove(instrument.Id);
                m_instrumentSamples.Remove(instrument.Id);
                continue;
            }
            m_invalidInstruments.Remove(instrument.Id);
            m_instrumentValues[instrument.Id] = value;
            if (!m_instrumentSamples.TryGetValue(instrument.Id, out var samples))
            {
                samples = new List<float>(240);
                m_instrumentSamples[instrument.Id] = samples;
            }
            samples.Add(Mathf.Clamp01((float)(
                (value - instrument.Minimum) /
                (instrument.Maximum - instrument.Minimum))));
            // Four times the former horizontal resolution keeps the strip
            // recorder's leftward feed below a pixel per poll on normal card
            // widths, so the paper advances continuously instead of jumping.
            if (samples.Count > 240)
            {
                samples.RemoveAt(0);
            }
        }
    }

    private void EnterInstrumentHistorian(InstrumentDefinition instrument)
    {
        if (instrument == null)
        {
            return;
        }

        m_historianInstrumentId = instrument.Id;
        m_historianRangeIndex = Math.Max(
            0,
            Math.Min(
                m_historianRangeIndex,
                s_historianRanges.Length - 1));
        if (m_nativeWindowShell != null)
        {
            m_historianPreviousWindowSize =
                m_nativeWindowShell.MaximizeTemporarily();
            m_historianPreviousWindowSizeValid = true;
            var maximizedSize = m_nativeWindowShell.CurrentSize;
            m_windowRect.width = maximizedSize.x;
            m_windowRect.height = maximizedSize.y;
        }
        m_instrumentScroll = Vector2.zero;
    }

    private void OpenInstrumentAlarmEditor(InstrumentDefinition instrument)
    {
        if (instrument == null)
        {
            return;
        }
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetStatus(UnmaText.Get(
                "ui.instrument.status.other_draft_open",
                "Another alarm draft is already open. Save or discard it first."));
            return;
        }
        var targetPanels = GlobalPanels.Where(panel =>
            panel != null &&
            !panel.IsDashboard &&
            !PanelTopologyPolicy.IsEntityPanel(panel)).ToArray();
        if (targetPanels.Length == 0)
        {
            SetStatus(UnmaText.Get(
                "ui.instrument.status.global_panel_required",
                "A global annunciator panel is required before creating this alarm."));
            return;
        }

        ResetDraftRule();
        SelectLinkedInstrumentSource(instrument.Id);
        // Instrument alarms require an explicit destination choice. This
        // prevents the first global panel (historically SUPPLY) from being
        // selected silently merely because it happens to be first.
        m_draftTargetPanelId = "";
        m_draftPreferredSlotIndex = -1;
        m_draftRuleName = UnmaText.Format(
            "ui.instrument.default_alarm_name",
            "{0} ALARM",
            instrument.Title);
        var condition = new ConditionDefinition
        {
            EntityId = instrument.EntityId,
            EntityTitle = instrument.EntityTitle,
            EntityPrototypeId = instrument.EntityPrototypeId,
            MetricPath = instrument.MetricPath,
            MetricLabel = instrument.Title,
            Comparison = ComparisonOperator.Less,
            Threshold = instrument.Minimum,
            InstrumentId = instrument.Id,
            TrendMode = InstrumentTrendMode.None,
            WindowSeconds = 60,
            WindowAmount = 1,
            WindowUnit = GameTimeUnit.Month,
            DeltaThreshold = Math.Max(
                1d,
                (instrument.Maximum - instrument.Minimum) * 0.05d),
        };
        m_draftConditions.Add(condition);
        m_draftConditionThresholdTexts.Add(
            condition.Threshold.ToString(
                "R",
                CultureInfo.CurrentCulture));
        m_draftTrendWindowTexts[condition] = "1";
        EnsureDraftHysteresisText(condition);
        OpenRuleEditorWindow();
        SetStatus(UnmaText.Format(
            "ui.instrument.status.alarm_prepared",
            "Alarm prepared for calculated metric: {0}",
            instrument.Title));
    }

    private void ExitInstrumentHistorian()
    {
        m_historianInstrumentId = "";
        if (!m_historianPreviousWindowSizeValid)
        {
            return;
        }

        if (m_nativeWindowShell != null)
        {
            m_nativeWindowShell.SetTemporarySize(
                m_historianPreviousWindowSize);
            var preferredSize = m_nativeWindowShell.PreferredSize;
            m_windowRect.width = preferredSize.x;
            m_windowRect.height = preferredSize.y;
        }
        m_historianPreviousWindowSizeValid = false;
    }

    private void DrawInstrumentHistorianView()
    {
        var instrument = m_runtime.Configuration.Instruments.FirstOrDefault(
            item => string.Equals(
                item.Id,
                m_historianInstrumentId,
                StringComparison.Ordinal));
        if (instrument == null)
        {
            ExitInstrumentHistorian();
            return;
        }

        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.historian.title",
                "INSTRUMENT HISTORIAN · {0}",
                instrument.Title),
            m_sectionStyle);
        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.historian.time_range", "GAME-TIME RANGE"),
            m_smallLabelStyle,
            NativeGUILayout.Width(90f),
            NativeGUILayout.Height(30f));
        for (var index = 0;
             index < s_historianRanges.Length;
             index++)
        {
            if (NativeGUILayout.Button(
                    GetHistorianRangeLabel(index),
                    index == m_historianRangeIndex
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Width(76f),
                    NativeGUILayout.Height(30f)))
            {
                m_historianRangeIndex = index;
            }
        }
        NativeGUILayout.FlexibleSpace();
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.historian.back_to_panel",
                    "BACK TO INSTRUMENT PANEL"),
                m_buttonStyle,
                NativeGUILayout.Width(190f),
                NativeGUILayout.Height(30f)))
        {
            ExitInstrumentHistorian();
            NativeGUILayout.EndHorizontal();
            return;
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Space(6f);

        var hasCurrentValue = m_instrumentValues.TryGetValue(
            instrument.Id,
            out var current);
        var isValid = !m_invalidInstruments.Contains(instrument.Id) &&
                      hasCurrentValue;
        var selectedRangeTicks = s_historianRanges[
            m_historianRangeIndex];
        var chartRect = NativeGUILayoutUtility.GetRect(
            520f,
            Mathf.Max(320f, m_windowRect.height - 180f),
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.ExpandHeight(true));
        // The chart has 24 horizontal pixels fewer than the allocated rect.
        // Capping the bucket count to that physical width keeps
        // rendering cost proportional to what can actually be seen.
        var pixelColumns = Mathf.Clamp(
            Mathf.FloorToInt(chartRect.width - 48f),
            1,
            4096);
        if (isValid)
        {
            RefreshHistorianCache(
                instrument,
                selectedRangeTicks,
                pixelColumns);
        }
        else
        {
            m_historianTrace.Clear();
            m_historianCacheInstrumentId = "";
        }
        var forecast = default(InstrumentForecastResult);
        var hasForecast = isValid && m_runtime.TryGetInstrumentForecast(
            instrument.Id,
            selectedRangeTicks,
            out forecast);
        if (hasForecast)
        {
            current = forecast.CurrentValue;
        }
        InstrumentPanelRenderer.DrawHistorian(
            chartRect,
            instrument,
            m_historianTrace,
            current,
            forecast,
            hasForecast,
            GetHistorianRangeLabel(m_historianRangeIndex),
            m_labelStyle,
            m_smallLabelStyle,
            isValid);
    }

    private static string GetHistorianRangeLabel(int index)
    {
        return index switch
        {
            0 => UnmaText.Get("ui.historian.range.one_day", "1 DAY"),
            1 => UnmaText.Get("ui.historian.range.one_month", "1 MONTH"),
            2 => UnmaText.Get("ui.historian.range.one_year", "1 YEAR"),
            3 => UnmaText.Get("ui.historian.range.ten_years", "10 YEARS"),
            4 => UnmaText.Get(
                "ui.historian.range.one_century",
                "100 YEARS"),
            _ => UnmaText.Get("ui.historian.range.maximum", "MAX"),
        };
    }

    private void RefreshHistorianCache(
        InstrumentDefinition instrument,
        int windowTicks,
        int pixelColumns)
    {
        var hasHistory = m_runtime.TryGetInstrumentHistoryState(
            instrument.Id,
            out var historyState);
        var cacheMatches = string.Equals(
                               m_historianCacheInstrumentId,
                               instrument.Id,
                               StringComparison.Ordinal) &&
                           m_historianCacheWindowTicks == windowTicks &&
                           m_historianCachePixelColumns ==
                           pixelColumns &&
                           m_historianCacheHasHistory == hasHistory &&
                           m_historianCacheScaleMinimum.Equals(
                               instrument.Minimum) &&
                           m_historianCacheScaleMaximum.Equals(
                               instrument.Maximum);
        if (cacheMatches && hasHistory)
        {
            cacheMatches = HistorianHistoryStateEquals(
                m_historianCacheHistoryState,
                historyState);
        }
        if (cacheMatches)
        {
            return;
        }

        m_historianTrace.Clear();
        m_historianCacheInstrumentId = instrument.Id;
        m_historianCacheWindowTicks = windowTicks;
        m_historianCachePixelColumns = pixelColumns;
        m_historianCacheHasHistory = hasHistory;
        m_historianCacheHistoryState = historyState;
        m_historianCacheScaleMinimum = instrument.Minimum;
        m_historianCacheScaleMaximum = instrument.Maximum;

        if (hasHistory && m_runtime.CopyDecimatedInstrumentHistory(
                instrument.Id,
                windowTicks,
                pixelColumns,
                m_historianBucketScratch,
                out historyState,
                out _,
                out _))
        {
            m_historianCacheHistoryState = historyState;
            foreach (var bucket in m_historianBucketScratch)
            {
                m_historianTrace.Add(
                    NormalizeHistorianValue(instrument, bucket.FirstValue),
                    NormalizeHistorianValue(instrument, bucket.MinimumValue),
                    NormalizeHistorianValue(instrument, bucket.MaximumValue),
                    NormalizeHistorianValue(instrument, bucket.LastValue));
            }
        }
    }

    private static bool HistorianHistoryStateEquals(
        InstrumentHistoryState left,
        InstrumentHistoryState right) =>
        left.SampleCount == right.SampleCount &&
        left.FirstTimestampSeconds.Equals(right.FirstTimestampSeconds) &&
        left.LastTimestampSeconds.Equals(right.LastTimestampSeconds) &&
        left.FirstValue.Equals(right.FirstValue) &&
        left.LastValue.Equals(right.LastValue);

    private static float NormalizeHistorianValue(
        InstrumentDefinition instrument,
        double value)
    {
        var span = instrument.Maximum - instrument.Minimum;
        return span > 0d
            ? Mathf.Clamp01((float)((value - instrument.Minimum) / span))
            : 0f;
    }

    private static string InstrumentTypeLabel(InstrumentDisplayType type)
    {
        return type switch
        {
            InstrumentDisplayType.EdgewiseVertical => UnmaText.Get(
                "ui.instrument.type.edgewise_vertical",
                "EDGEWISE · VERTICAL"),
            InstrumentDisplayType.EdgewiseHorizontal => UnmaText.Get(
                "ui.instrument.type.edgewise_horizontal",
                "EDGEWISE · HORIZONTAL"),
            InstrumentDisplayType.RoundGauge => UnmaText.Get(
                "ui.instrument.type.round_gauge",
                "ROUND GAUGE"),
            InstrumentDisplayType.SevenSegmentRed => UnmaText.Get(
                "ui.instrument.type.seven_segment_red",
                "7-SEGMENT · RED"),
            InstrumentDisplayType.SevenSegmentGreen => UnmaText.Get(
                "ui.instrument.type.seven_segment_green",
                "7-SEGMENT · GREEN"),
            InstrumentDisplayType.NixieTube => UnmaText.Get(
                "ui.instrument.type.nixie",
                "NIXIE TUBE"),
            InstrumentDisplayType.CrtAmber => UnmaText.Get(
                "ui.instrument.type.crt_amber",
                "CRT · AMBER"),
            InstrumentDisplayType.CrtGreen => UnmaText.Get(
                "ui.instrument.type.crt_green",
                "CRT · GREEN"),
            _ => UnmaText.Get(
                "ui.instrument.type.paper_recorder",
                "PAPER RECORDER"),
        };
    }

    private void DrawEntityAssignmentBanner(PanelDefinition panel)
    {
        if (!m_entityAssignmentPending)
        {
            return;
        }

        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginHorizontal();
        var entityText = m_assignmentEntity == null
            ? UnmaText.Get("auto.2623e678be24") + m_assignmentEntityId + UnmaText.Get("auto.76e7b0bbc88e")
            : UnmaText.Get("auto.9eb6dbd0927f") +
              m_assignmentEntity.Title.ToUpperInvariant() +
              UnmaText.Get("auto.9da04860d6fc") + m_assignmentEntity.EntityId;
        NativeGUILayout.Label(
            entityText,
            m_sectionStyle,
            NativeGUILayout.Height(34f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.71418af14024"),
                m_buttonStyle,
                NativeGUILayout.Width(190f),
                NativeGUILayout.Height(34f)))
        {
            CancelEntityAssignment();
            SetStatus(UnmaText.Get("auto.a0b453e90074"));
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            m_assignmentEntity == null
                ? UnmaText.Get("auto.ddaf88332415")
                : panel.IsDashboard
                    ? UnmaText.Get("auto.4d0bf7520637") +
                      UnmaText.Get("auto.a2b9fddc25ba")
                    : UnmaText.Get("auto.eb02fc0a8069") + panel.Name +
                  UnmaText.Get("ui.assignment.link_hint") +
                  UnmaText.Get("auto.3b6919024f08"),
            m_smallLabelStyle);
    }

    private void DrawHistory()
    {
        var allEntries = GetHistoryEntries();
        var entries = new AlarmHistoryQuery
        {
            SearchText = m_historySearchText,
            StateFilter = m_historyStateFilter,
            SeverityFilter = m_historySeverityFilter,
        }.Apply(allEntries);

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.history.filtered_count",
                "{0} OF {1} EVENTS",
                entries.Count,
                allEntries.Count),
            m_sectionStyle,
            NativeGUILayout.Height(34f));
        var confirmingDelete =
            Time.realtimeSinceStartup < m_pendingHistoryDeleteUntil;
        if (NativeGUILayout.Button(
                confirmingDelete
                    ? UnmaText.Get("auto.beb568ff57a3")
                    : UnmaText.Get("auto.3ecf169c4abf"),
                confirmingDelete
                    ? m_dangerButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(230f),
                NativeGUILayout.Height(34f)))
        {
            if (!confirmingDelete)
            {
                m_pendingHistoryDeleteUntil =
                    Time.realtimeSinceStartup + 5f;
                SetStatus(
                    UnmaText.Get("auto.af9b4e96e001"));
            }
            else if (m_runtime.DeleteCompletedAlarmHistory(
                         out var deletedCount))
            {
                m_pendingHistoryDeleteUntil = 0f;
                m_historyScroll = Vector2.zero;
                SetStatus(
                    deletedCount + UnmaText.Get("auto.fe46d61b2afe"));
            }
            else
            {
                SetStatus(
                    UnmaText.Get("auto.c1f0ffc84e81") +
                    m_runtime.LastPersistenceError);
            }
        }
        NativeGUILayout.EndHorizontal();

        DrawHistoryFilters(entries);

        NativeGUILayout.Label(
            UnmaText.Get("auto.546f06f29ca0"),
            m_smallLabelStyle);
        var showHistoryActions = entries.Any(entry => entry.CanDelete);
        DrawHistoryHeader(showHistoryActions);

        var historyViewportHeight =
            Math.Max(180f, m_windowRect.height - 258f);
        m_historyScroll.y = Mathf.Min(
            m_historyScroll.y,
            Math.Max(
                0f,
                entries.Count * (HistoryRowHeight + 4f) -
                historyViewportHeight));
        m_historyScroll = NativeGUILayout.BeginScrollView(
            m_historyScroll,
            NativeGUILayout.ExpandHeight(true));
        if (entries.Count == 0)
        {
            NativeGUILayout.Space(16f);
            NativeGUILayout.Label(
                UnmaText.Get("auto.d63794d49841"),
                m_labelStyle);
        }
        else
        {
            DrawHistoryRows(
                entries,
                m_historyScroll.y,
                historyViewportHeight,
                showHistoryActions);
        }
        NativeGUILayout.EndScrollView();
    }

    private void DrawHistoryFilters(
        IReadOnlyList<AlarmHistoryDefinition> filteredEntries)
    {
        var compact = m_windowRect.width < 900f;
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.history.search", "SEARCH"),
            m_smallLabelStyle,
            NativeGUILayout.Width(62f));
        var searchText = NativeGUILayout.TextField(
            m_historySearchText,
            256,
            m_textFieldStyle,
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(165f),
            NativeGUILayout.Height(30f));
        if (!string.Equals(
                searchText,
                m_historySearchText,
                StringComparison.Ordinal))
        {
            m_historySearchText = searchText;
            m_historyScroll = Vector2.zero;
        }
        if (NativeGUILayout.Button(
                new GUIContent(
                    "×",
                    UnmaText.Get("ui.common.clear", "Clear search")),
                m_buttonStyle,
                new NativeControlMetadata(
                    "history-search-clear",
                    UnmaText.Get("ui.common.clear", "Clear search")),
                NativeGUILayout.Width(34f),
                NativeGUILayout.Height(34f)))
        {
            m_historySearchText = "";
            m_historyScroll = Vector2.zero;
        }
        if (compact)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.history.filter", "FILTER") + " · " +
                HistoryStateFilterLabel(m_historyStateFilter) + " · " +
                HistorySeverityFilterLabel(m_historySeverityFilter),
                m_buttonStyle,
                compact
                    ? NativeGUILayout.ExpandWidth(true)
                    : NativeGUILayout.Width(245f),
                NativeGUILayout.Height(34f)))
        {
            m_historyFilterPickerOpen = !m_historyFilterPickerOpen;
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.history.export_csv", "CSV"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(58f),
                NativeGUILayout.Height(30f)))
        {
            ExportHistory(filteredEntries, json: false);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.history.export_json", "JSON"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(58f),
                NativeGUILayout.Height(30f)))
        {
            ExportHistory(filteredEntries, json: true);
        }
        NativeGUILayout.EndHorizontal();
        if (m_historyFilterPickerOpen)
        {
            DrawHistoryFilterPicker();
        }
    }

    private void DrawHistoryFilterPicker()
    {
        var compact = m_windowRect.width < 900f;
        NativeGUILayout.BeginVertical(m_panelStyle);
        NativeGUILayout.Label(
            UnmaText.Get("ui.history.state", "STATE"),
            m_smallLabelStyle);
        NativeGUILayout.BeginHorizontal();
        for (var index = 0; index < s_historyStateFilters.Length; index++)
        {
            if (compact && index == 4)
            {
                NativeGUILayout.EndHorizontal();
                NativeGUILayout.BeginHorizontal();
            }
            var filter = s_historyStateFilters[index];
            if (NativeGUILayout.Button(
                    HistoryStateFilterLabel(filter),
                    filter == m_historyStateFilter
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                m_historyStateFilter = filter;
                m_historyScroll = Vector2.zero;
            }
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.severity", "Severity"),
            m_smallLabelStyle);
        NativeGUILayout.BeginHorizontal();
        for (var index = 0; index < s_historySeverityFilters.Length; index++)
        {
            if (compact && index == 3)
            {
                NativeGUILayout.EndHorizontal();
                NativeGUILayout.BeginHorizontal();
            }
            var severity = s_historySeverityFilters[index];
            if (NativeGUILayout.Button(
                    HistorySeverityFilterLabel(severity),
                    severity == m_historySeverityFilter
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                m_historySeverityFilter = severity;
                m_historyScroll = Vector2.zero;
            }
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.EndVertical();
    }

    private static string HistoryStateFilterLabel(
        AlarmHistoryStateFilter filter)
    {
        return filter switch
        {
            AlarmHistoryStateFilter.Open => UnmaText.Get(
                "ui.history.state_open",
                "OPEN"),
            AlarmHistoryStateFilter.Completed => UnmaText.Get(
                "ui.history.state_completed",
                "COMPLETED"),
            AlarmHistoryStateFilter.K => "K",
            AlarmHistoryStateFilter.KQ => "KQ",
            AlarmHistoryStateFilter.KG => "KG",
            AlarmHistoryStateFilter.KGQ => "KGQ",
            _ => UnmaText.Get("ui.history.state_all", "ALL STATES"),
        };
    }

    private string HistorySeverityFilterLabel(AlarmSeverity? severity)
    {
        return severity.HasValue
            ? SeverityLabel(severity.Value)
            : UnmaText.Get("ui.history.severity_all", "ALL LEVELS");
    }

    private void ExportHistory(
        IReadOnlyList<AlarmHistoryDefinition> entries,
        bool json)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "UNMA",
                "exports");
            Directory.CreateDirectory(directory);
            var extension = json ? "json" : "csv";
            var path = Path.Combine(
                directory,
                "history-" + DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss-fff",
                    CultureInfo.InvariantCulture) + "." + extension);
            var content = json
                ? AlarmHistoryExport.ToJson(entries)
                : AlarmHistoryExport.ToCsv(entries);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            SetStatus(UnmaText.Format(
                "ui.history.exported",
                "History exported: {0}",
                path));
        }
        catch (Exception exception)
        {
            SetStatus(UnmaText.Format(
                "ui.history.export_failed",
                "History export failed: {0}",
                exception.Message));
        }
    }

    private IReadOnlyList<AlarmHistoryDefinition> GetHistoryEntries()
    {
        var revision = m_runtime.AlarmHistoryRevision;
        if (m_historyCacheRevision != revision)
        {
            m_historyCacheRevision = revision;
            m_historyCache = m_runtime.GetAlarmHistory();
        }
        return m_historyCache;
    }

    private void DrawHistoryHeader(bool showActions)
    {
        var rect = NativeGUILayoutUtility.GetRect(
            0f,
            30f,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(30f));
        DrawPanelRect(rect, CoiUiPalette.SurfaceRaised);
        var actionWidth = showActions ? 98f : 0f;
        var stateWidth = 92f;
        var timeWidth = 128f;
        NativeGUI.Label(
            new Rect(
                rect.x + 10f,
                rect.y,
                timeWidth - 10f,
                rect.height),
            UnmaText.Get("ui.history.time", "GAME TIME"),
            m_historyHeaderStyle);
        NativeGUI.Label(
            new Rect(
                rect.x + timeWidth,
                rect.y,
                rect.width - actionWidth - stateWidth - timeWidth - 10f,
                rect.height),
            UnmaText.Get("ui.history.message", "MESSAGE"),
            m_historyHeaderStyle);
        NativeGUI.Label(
            new Rect(
                rect.xMax - actionWidth - stateWidth,
                rect.y,
                stateWidth,
                rect.height),
            UnmaText.Get("ui.history.state", "STATE"),
            m_historyHeaderStyle);
        if (showActions)
        {
            NativeGUI.Label(
                new Rect(
                    rect.xMax - actionWidth,
                    rect.y,
                    actionWidth,
                    rect.height),
                UnmaText.Get("ui.history.action", "ACTION"),
                m_historyHeaderStyle);
        }
    }

    private void DrawHistoryRows(
        IReadOnlyList<AlarmHistoryDefinition> entries,
        float scrollY,
        float viewportHeight,
        bool showActions)
    {
        var rowStep = HistoryRowHeight + 4f;
        var firstVisible = Math.Max(
            0,
            Mathf.FloorToInt(scrollY / rowStep) - 2);
        var lastVisible = Math.Min(
            entries.Count,
            Mathf.CeilToInt((scrollY + viewportHeight) / rowStep) + 2);
        if (firstVisible > 0)
        {
            NativeGUILayout.Space(firstVisible * rowStep);
        }

        for (var index = firstVisible; index < lastVisible; index++)
        {
            var rect = NativeGUILayoutUtility.GetRect(
                "history-row:" + entries[index].Sequence.ToString(
                    CultureInfo.InvariantCulture),
                0f,
                HistoryRowHeight,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(HistoryRowHeight));
            DrawHistoryRow(rect, entries[index], showActions);
            NativeGUILayout.Space(4f);
        }

        if (lastVisible < entries.Count)
        {
            NativeGUILayout.Space((entries.Count - lastVisible) * rowStep);
        }
    }

    private void DrawHistoryRow(
        Rect rect,
        AlarmHistoryDefinition entry,
        bool showActions)
    {
        var background = CoiUiPalette.Surface;
        var textStyle = m_historyTextStyle;
        if (!entry.IsGone && !entry.IsAcknowledged)
        {
            background = new Color(0.55f, 0.035f, 0.035f, 1f);
            textStyle = m_historyAlertTextStyle;
        }
        else if (entry.IsGone && !entry.IsAcknowledged)
        {
            background = CoiUiPalette.SurfaceRaised;
        }

        DrawPanelRect(rect, Color.black);
        var inner = new Rect(
            rect.x + 2f,
            rect.y + 2f,
            rect.width - 4f,
            rect.height - 4f);
        DrawPanelRect(inner, background);

        var actionWidth = showActions ? 96f : 0f;
        var stateWidth = 90f;
        var timeWidth = 126f;
        NativeGUI.Label(
            new Rect(
                inner.x + 9f,
                inner.y,
                timeWidth - 9f,
                inner.height),
            FormatHistoryTime(entry),
            textStyle);
        NativeGUI.Label(
            new Rect(
                inner.x + timeWidth,
                inner.y,
                inner.width - actionWidth - stateWidth - timeWidth - 5f,
                inner.height),
            string.IsNullOrWhiteSpace(entry.Message)
                ? entry.AlarmKey
                : entry.Message,
            textStyle);
        NativeGUI.Label(
            new Rect(
                inner.xMax - actionWidth - stateWidth,
                inner.y,
                stateWidth,
                inner.height),
            entry.StateCode,
            entry.StateCode == "K"
                ? m_historyAlertStateStyle
                : m_historyStateStyle);

        if (showActions && entry.CanDelete && NativeGUI.Button(
                new Rect(
                    inner.xMax - actionWidth + 4f,
                    inner.y + 4f,
                    actionWidth - 8f,
                    inner.height - 8f),
                UnmaText.Get("auto.9cf94f11833b"),
                m_buttonStyle))
        {
            if (m_runtime.DeleteAlarmHistoryEntry(entry.Sequence))
            {
                SetStatus(UnmaText.Get("auto.ca50a93bd9e5"));
            }
            else
            {
                SetStatus(
                    UnmaText.Get("auto.c1f0ffc84e81") +
                    m_runtime.LastPersistenceError);
            }
        }
    }

    private static string FormatHistoryTime(AlarmHistoryDefinition entry)
    {
        if (!GameTimeStampPolicy.TryGetDate(
                GameTimeStampPolicy.LatestEventTicks(entry),
                out var date))
        {
            return UnmaText.Get("ui.history.time_unknown", "—");
        }
        return UnmaText.Format(
            "ui.history.game_date",
            "Y{0} M{1} D{2}",
            date.Year,
            date.Month,
            date.Day);
    }

    private void DrawEditor()
    {
        m_editorScroll = NativeGUILayout.BeginScrollView(m_editorScroll);
        DrawStatusMessage();
        DrawPanelManagement();

        NativeGUILayout.Space(12f);
        NativeGUILayout.Label(
            string.IsNullOrWhiteSpace(m_editingRuleId)
                ? UnmaText.Get("auto.3fc83596b4ef")
                : UnmaText.Get("auto.f8226d218f15"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.30893e3ab657") +
            UnmaText.Get("auto.a4af228f3574") +
            UnmaText.Get("auto.9053cc535627") +
            UnmaText.Get("auto.6232fc63f818"),
            m_smallLabelStyle);
        DrawAlarmRuleEditor(false);

        NativeGUILayout.Space(12f);
        DrawDefinedRules();
        NativeGUILayout.EndScrollView();
    }

    private void DrawPanelManagement()
    {
        if (Time.realtimeSinceStartup > m_pendingPanelDeleteUntil)
        {
            m_pendingPanelDeleteId = "";
        }
        NativeGUILayout.Label(UnmaText.Get("auto.251e714a80a6"), m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.8db078b96ea7"),
            m_smallLabelStyle);

        var panels = m_runtime.Configuration.Panels;
        if (panels.Count > 0)
        {
            m_currentPanelIndex = Math.Max(
                0,
                Math.Min(m_currentPanelIndex, panels.Count - 1));
            NativeGUILayout.BeginHorizontal();
            if (NativeGUILayout.Button("<", m_buttonStyle, NativeGUILayout.Width(38f)))
            {
                m_currentPanelIndex = Wrap(m_currentPanelIndex - 1, panels.Count);
            }
            NativeGUILayout.Label(
                panels[m_currentPanelIndex].Name +
                "   (" + (m_currentPanelIndex + 1) + "/" + panels.Count + ")",
                m_headerStyle,
                NativeGUILayout.Height(30f));
            if (NativeGUILayout.Button(">", m_buttonStyle, NativeGUILayout.Width(38f)))
            {
                m_currentPanelIndex = Wrap(m_currentPanelIndex + 1, panels.Count);
            }
            NativeGUILayout.EndHorizontal();
        }

        var panel = CurrentPanel;
        if (panel != null)
        {
            NativeGUILayout.Space(6f);
            NativeGUILayout.Label(UnmaText.Get("auto.d03a4752df6c"), m_sectionStyle);
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.common.name", "Name"),
                m_labelStyle,
                NativeGUILayout.Width(90f));
            panel.Name = NativeGUILayout.TextField(
                panel.Name,
                40,
                m_textFieldStyle,
                NativeGUILayout.Width(260f));
            NativeGUILayout.Label(
                UnmaText.Get("auto.7f6972b99a3e") + panel.Columns,
                m_labelStyle,
                NativeGUILayout.Width(90f));
            if (NativeGUILayout.Button("-", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                panel.Columns = Math.Max(1, panel.Columns - 1);
            }
            if (NativeGUILayout.Button("+", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                panel.Columns = Math.Min(8, panel.Columns + 1);
            }
            if (!panel.IsDashboard)
            {
                panel.IncludeVanilla = NativeGUILayout.Toggle(
                    panel.IncludeVanilla,
                    UnmaText.Get("auto.ef309fc5dd19"),
                    NativeGUILayout.Width(100f));
                panel.IncludeSystem = NativeGUILayout.Toggle(
                    panel.IncludeSystem,
                    UnmaText.Get("auto.025c249edeb5"),
                    NativeGUILayout.Width(100f));
            }
            else
            {
                NativeGUILayout.Label(
                    UnmaText.Get("auto.6e1d936caf5d"),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(205f));
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.d4efd9369153"),
                    m_primaryButtonStyle,
                    NativeGUILayout.Width(190f)))
            {
                SaveConfiguration(UnmaText.Get("auto.4bd5b213cd77"));
            }
            NativeGUILayout.EndHorizontal();

            if (panel.IsDashboard)
            {
                NativeGUILayout.Label(
                    UnmaText.Get("auto.e0e998aea68a") +
                    UnmaText.Get("auto.fee217fd8b0d") +
                    UnmaText.Get("auto.df66ce36493c") +
                    UnmaText.Get("ui.dashboard.not_deletable"),
                    m_smallLabelStyle);
            }
            else
            {
                NativeGUILayout.BeginHorizontal();
                NativeGUILayout.Label(
                    UnmaText.Get("ui.panel.auto_filter", "Auto-filter"),
                    m_labelStyle,
                    NativeGUILayout.Width(90f));
                panel.NotificationFilter = NativeGUILayout.TextField(
                    panel.NotificationFilter ?? "",
                    240,
                    m_textFieldStyle);
                NativeGUI.enabled = panels.Count > 1;
                var pendingDelete = string.Equals(
                    m_pendingPanelDeleteId,
                    panel.Id,
                    StringComparison.Ordinal);
                var affectedRules = m_runtime.Configuration.Rules.Count(rule =>
                    string.Equals(
                        rule.PanelId,
                        panel.Id,
                        StringComparison.Ordinal));
                if (NativeGUILayout.Button(
                        pendingDelete
                            ? UnmaText.Get("auto.2f4d2d64f711") + affectedRules + UnmaText.Get("auto.29b8add2ed8c")
                            : UnmaText.Get("auto.48a2c61d595d"),
                        m_dangerButtonStyle,
                        NativeGUILayout.Width(220f)))
                {
                    RemoveCurrentPanel();
                }
                NativeGUI.enabled = true;
                NativeGUILayout.EndHorizontal();

                DrawPanelSlots(panel);
            }
        }

        NativeGUILayout.Space(6f);
        NativeGUILayout.Label(UnmaText.Get("auto.ba2a4502c2e0"), m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.770ddae89d54"),
            m_labelStyle,
            NativeGUILayout.Width(205f));
        m_newPanelName = NativeGUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            NativeGUILayout.Width(300f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.1aedbc19e04e"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(190f)))
        {
            AddPanel();
        }
        NativeGUILayout.EndHorizontal();
    }

    private void DrawPanelSlots(PanelDefinition panel)
    {
        panel.Slots ??= new List<PanelSlotDefinition>();
        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(
            UnmaText.Get("auto.47b5a4a498c8") + panel.Slots.Count,
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.882f8bc83052"),
            m_smallLabelStyle);

        for (var index = 0; index < panel.Slots.Count; index++)
        {
            var slot = panel.Slots[index];
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                m_smallLabelStyle,
                NativeGUILayout.Width(28f));
            NativeGUILayout.Label(
                (slot.DisplayName ?? UnmaText.Get(
                    "ui.common.alarm",
                    "ALARM")) + "   ·   " +
                SlotSourceLabel(slot.Source),
                m_labelStyle);
            NativeGUI.enabled = index > 0;
            if (NativeGUILayout.Button("↑", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                panel.Slots.RemoveAt(index);
                panel.Slots.Insert(index - 1, slot);
                SaveConfiguration(UnmaText.Get("auto.e4e962c7b82e"));
                NativeGUI.enabled = true;
                NativeGUILayout.EndHorizontal();
                return;
            }
            NativeGUI.enabled = index < panel.Slots.Count - 1;
            if (NativeGUILayout.Button("↓", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                panel.Slots.RemoveAt(index);
                panel.Slots.Insert(index + 1, slot);
                SaveConfiguration(UnmaText.Get("auto.f0dec1316ddd"));
                NativeGUI.enabled = true;
                NativeGUILayout.EndHorizontal();
                return;
            }
            var isCustom = string.Equals(
                slot.Source,
                "custom",
                StringComparison.Ordinal);
            NativeGUI.enabled = !isCustom;
            if (NativeGUILayout.Button(
                    isCustom
                        ? UnmaText.Get("auto.063bd868b890")
                        : UnmaText.Get("ui.common.remove", "REMOVE"),
                    m_buttonStyle,
                    NativeGUILayout.Width(105f)))
            {
                panel.ExcludedAlarmIds ??= new List<string>();
                if (!panel.ExcludedAlarmIds.Contains(
                        slot.AlarmId,
                        StringComparer.Ordinal))
                {
                    panel.ExcludedAlarmIds.Add(slot.AlarmId);
                }
                panel.Slots.RemoveAt(index);
                SaveConfiguration(UnmaText.Get("auto.43a8099eaf2a"));
                NativeGUI.enabled = true;
                NativeGUILayout.EndHorizontal();
                return;
            }
            NativeGUI.enabled = true;
            NativeGUILayout.EndHorizontal();
        }

        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.02a7427b4413"),
            m_labelStyle,
            NativeGUILayout.Width(205f));
        m_panelSlotFilter = NativeGUILayout.TextField(
            m_panelSlotFilter,
            80,
            m_textFieldStyle);
        NativeGUILayout.EndHorizontal();

        var installed = new HashSet<string>(
            panel.Slots.Select(slot => slot.AlarmId),
            StringComparer.Ordinal);
        if (Time.realtimeSinceStartup >= m_nextPanelSlotCandidateRefresh)
        {
            m_panelSlotCandidates = m_runtime.GetPanelSlotCandidates();
            m_nextPanelSlotCandidateRefresh =
                Time.realtimeSinceStartup + 1f;
        }
        var available = m_panelSlotCandidates
            .Where(slot =>
                !installed.Contains(slot.AlarmId) &&
                !string.Equals(
                    slot.Source,
                    "custom",
                    StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(m_panelSlotFilter) ||
                 ((slot.DisplayName ?? "") + " " +
                  (slot.Detail ?? "") + " " + slot.AlarmId)
                 .IndexOf(
                     m_panelSlotFilter,
                     StringComparison.OrdinalIgnoreCase) >= 0))
            .Take(40)
            .ToArray();
        foreach (var slot in available)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                (slot.DisplayName ?? UnmaText.Get(
                    "ui.common.alarm",
                    "ALARM")) + "   ·   " +
                SlotSourceLabel(slot.Source),
                m_smallLabelStyle);
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.15a322e13c45"),
                    m_primaryButtonStyle,
                    NativeGUILayout.Width(105f)))
            {
                panel.ExcludedAlarmIds ??= new List<string>();
                var exclusionIds = new HashSet<string>(
                    StringComparer.Ordinal)
                {
                    slot.AlarmId,
                };
                var vanillaOverrideId = VanillaOverrideIdForSlot(slot);
                if (!string.IsNullOrWhiteSpace(vanillaOverrideId))
                {
                    exclusionIds.Add(vanillaOverrideId);
                    exclusionIds.Add(
                        PanelSlotProjection.LegacyVanillaSlotId(
                            vanillaOverrideId,
                            slot.Detail));
                }
                panel.ExcludedAlarmIds.RemoveAll(
                    exclusionIds.Contains);
                panel.Slots.Add(PanelSlotProjection.CloneSlot(slot));
                SaveConfiguration(UnmaText.Get("auto.8a0412c725ad"));
                NativeGUILayout.EndHorizontal();
                return;
            }
            NativeGUILayout.EndHorizontal();
        }
        if (available.Length == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.f7502479c781"),
                m_smallLabelStyle);
        }
    }

    private static string SlotSourceLabel(string source)
    {
        return source switch
        {
            "vanilla" => UnmaText.Get("ui.alarm.source.vanilla", "VANILLA"),
            "system" => UnmaText.Get("ui.alarm.source.system", "SYSTEM"),
            "custom" => UnmaText.Get("auto.5aa074c71bd3"),
            _ => UnmaText.Get("ui.common.alarm", "ALARM"),
        };
    }

    private static string VanillaOverrideIdForSlot(
        PanelSlotDefinition slot)
    {
        if (!string.Equals(
                slot.Source,
                "vanilla",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(slot.AlarmId))
        {
            return "";
        }
        var entityMarker = slot.AlarmId.IndexOf(
            ":entity:",
            StringComparison.Ordinal);
        if (entityMarker > 0)
        {
            return slot.AlarmId.Substring(0, entityMarker);
        }
        var legacyMarker = slot.AlarmId.IndexOf(
            ":legacy:",
            StringComparison.Ordinal);
        return legacyMarker > 0
            ? slot.AlarmId.Substring(0, legacyMarker)
            : slot.AlarmId;
    }

    private void DrawDefinedRules()
    {
        NativeGUILayout.Label(UnmaText.Get("auto.1d7281b62bea"), m_sectionStyle);
        var sounds = m_audio.GetSoundOptions();
        var panelId = CurrentPanel?.Id;
        foreach (var rule in m_runtime.Configuration.Rules
                     .Where(rule => rule.PanelId == panelId)
                     .ToArray())
        {
            NativeGUILayout.BeginHorizontal();
            if (NativeGUILayout.Button(
                    rule.Enabled
                        ? UnmaText.Get("ui.common.on", "ON")
                        : UnmaText.Get("ui.common.off", "OFF"),
                    rule.Enabled ? m_primaryButtonStyle : m_buttonStyle,
                    NativeGUILayout.Width(52f)))
            {
                if (m_runtime.SetRuleEnabled(rule.Id, !rule.Enabled))
                {
                    SetStatus(UnmaText.Get("auto.106717c1a131"));
                }
                else
                {
                    SetStatus(
                        UnmaText.Get("auto.5df942eb6687") +
                        m_runtime.LastPersistenceError);
                }
            }
            NativeGUILayout.Label(
                rule.Name + " · " + SeverityLabel(rule.Severity) +
                " · " + rule.Conditions.Count + UnmaText.Get("auto.05534195bbe5") +
                (rule.Logic == AlarmLogic.All
                    ? UnmaText.Get("ui.common.and", "AND")
                    : UnmaText.Get("ui.common.or", "OR")) + " · " +
                (rule.AutoAcknowledgeOnClear
                    ? UnmaText.Get("auto.367f30137868")
                    : UnmaText.Get("auto.c9097d398192")),
                m_labelStyle);
            if (NativeGUILayout.Button(
                    UnmaText.Get("ui.common.edit", "EDIT"),
                    m_buttonStyle,
                    NativeGUILayout.Width(105f)))
            {
                BeginEditingRule(rule, sounds);
                var firstCondition = rule.Conditions.FirstOrDefault();
                if (firstCondition == null)
                {
                    m_entityAlarmWindowOpen = true;
                }
                else
                {
                    OpenConditionSource(firstCondition, true);
                }
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.9cf94f11833b"),
                    m_dangerButtonStyle,
                    NativeGUILayout.Width(90f)))
            {
                if (m_runtime.RemoveRule(rule.Id))
                {
                    if (string.Equals(
                            m_editingRuleId,
                            rule.Id,
                            StringComparison.Ordinal))
                    {
                        ResetDraftRule();
                    }
                    SetStatus(UnmaText.Get("auto.61bea0138542"));
                }
                else
                {
                    SetStatus(
                        UnmaText.Get("auto.c1f0ffc84e81") +
                        m_runtime.LastPersistenceError);
                }
            }
            NativeGUILayout.EndHorizontal();
        }
    }


    private void DrawEditorBodyContent()
    {
        DrawStatusMessage();
        DrawDraftConflictBanner();
        m_entityAlarmScroll = NativeGUILayout.BeginScrollView(m_entityAlarmScroll);
        if (m_editorClosePromptOpen)
        {
            DrawEditorClosePrompt();
        }
        else if (m_editorWindowMode == EditorWindowMode.PanelCreation)
        {
            DrawNewPanelWindowContent();
        }
        else if (m_editorWindowMode == EditorWindowMode.PanelSettings)
        {
            DrawPanelSettingsWindowContent();
        }
        else if (m_editorWindowMode == EditorWindowMode.AlarmAreas)
        {
            DrawAlarmAreasWindowContent();
        }
        else
        {
            DrawAlarmRuleEditor(true);
        }
        NativeGUILayout.EndScrollView();
        if (!m_editorClosePromptOpen &&
            m_editorWindowMode == EditorWindowMode.Rule)
        {
            DrawRuleEditorActions(m_audio.GetSoundOptions());
        }
    }

    private string GetEditorWindowTitle()
    {
        var targetPanel = GetDraftTargetPanel();
        return m_editorWindowMode switch
        {
            EditorWindowMode.PanelCreation =>
                UnmaText.Get("auto.5e9e7c9addd9"),
            EditorWindowMode.PanelSettings =>
                UnmaText.Get("auto.0e8b76140a09"),
            EditorWindowMode.AlarmAreas =>
                UnmaText.Get("ui.area.editor_title", "MANAGE AREAS"),
            _ => UnmaText.Get("auto.b9ccafdfaef7") +
                 (targetPanel == null ? "" : " · " + targetPanel.Name),
        };
    }

    private void RequestEditorClose()
    {
        if (m_editorWindowMode == EditorWindowMode.AlarmAreas &&
            HasUnsavedAlarmAreas())
        {
            m_editorClosePromptOpen = true;
            m_clearGuiFocusPending = true;
            return;
        }
        if (m_editorWindowMode == EditorWindowMode.PanelSettings)
        {
            var panel = m_runtime.Configuration.Panels.FirstOrDefault(
                candidate => candidate != null && string.Equals(
                    candidate.Id,
                    m_panelSettingsPanelId,
                    StringComparison.Ordinal));
            if (HasUnsavedPanelSettings(panel))
            {
                m_editorClosePromptOpen = true;
                m_clearGuiFocusPending = true;
                return;
            }
        }
        if (m_editorWindowMode == EditorWindowMode.Rule &&
            HasDraftRuleWork())
        {
            m_editorClosePromptOpen = true;
            m_clearGuiFocusPending = true;
            return;
        }
        CloseEditorWindow();
    }

    private bool HandleEditorEscapeShortcut()
    {
        if (!m_entityAlarmWindowOpen)
        {
            return false;
        }
        if (m_editorClosePromptOpen)
        {
            m_editorClosePromptOpen = false;
            m_clearGuiFocusPending = true;
            return true;
        }
        RequestEditorClose();
        return true;
    }

    private bool SaveDraftRuleFromShortcut()
    {
        if (!m_entityAlarmWindowOpen ||
            m_editorWindowMode != EditorWindowMode.Rule ||
            m_editorClosePromptOpen)
        {
            return false;
        }
        SaveDraftRule(m_audio.GetSoundOptions());
        return true;
    }

    private void DrawEditorClosePrompt()
    {
        if (m_editorWindowMode == EditorWindowMode.AlarmAreas)
        {
            DrawAlarmAreasClosePrompt();
            return;
        }
        if (m_editorWindowMode == EditorWindowMode.PanelSettings)
        {
            DrawPanelSettingsClosePrompt();
            return;
        }
        NativeGUILayout.Space(24f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.close_draft_title",
                "CLOSE ALARM EDITOR?"),
            m_warningBannerStyle,
            NativeGUILayout.Height(58f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.close_draft_description",
                "The current draft is still open. You can save it, minimize it and continue later, or discard it."),
            m_labelStyle);
        NativeGUILayout.Space(18f);
        NativeGUILayout.BeginHorizontal();
        NativeGUI.enabled = string.IsNullOrEmpty(
            GetRuleDraftValidationMessage());
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.editor.save_and_close",
                    "SAVE & CLOSE"),
                m_primaryButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            if (SaveDraftRule(m_audio.GetSoundOptions()))
            {
                CloseEditorWindow();
            }
        }
        NativeGUI.enabled = true;
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.minimize", "MINIMIZE"),
                m_buttonStyle,
                NativeGUILayout.Height(42f)))
        {
            CloseEditorWindow();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.discard", "DISCARD"),
                m_dangerButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            ResetDraftRule();
            CloseEditorWindow();
            SetStatus(UnmaText.Get(
                "ui.editor.status.draft_discarded",
                "Draft discarded."));
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Space(12f);
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.editor.back_to_editor",
                    "BACK TO EDITOR"),
                m_buttonStyle,
                NativeGUILayout.Width(230f),
                NativeGUILayout.Height(34f)))
        {
            m_editorClosePromptOpen = false;
        }
    }

    private void CloseEditorWindow()
    {
        CaptureEditorWindowLayout();
        PersistPendingWindowLayouts(force: true);
        m_editorClosePromptOpen = false;
        m_entityAlarmWindowOpen = false;
        m_openEntityAlarmAfterInspection = false;
        m_clearGuiFocusPending = true;
    }

    private void DrawNewPanelWindowContent()
    {
        NativeGUILayout.Label(UnmaText.Get("auto.ba2a4502c2e0"), m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.05a309b6f1bd") +
            UnmaText.Get("auto.61fdafb643aa"),
            m_smallLabelStyle);
        NativeGUILayout.Space(8f);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.common.name", "Name"),
            m_labelStyle,
            NativeGUILayout.Width(120f));
        m_newPanelName = NativeGUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            NativeGUILayout.Width(360f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.ea4da1cee467"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(180f),
                NativeGUILayout.Height(32f)))
        {
            if (AddPanel())
            {
                CloseEditorWindow();
            }
        }
        NativeGUILayout.EndHorizontal();
    }

    private void DrawAlarmAreasWindowContent()
    {
        if (Time.realtimeSinceStartup > m_pendingAlarmAreaDeleteUntil)
        {
            m_pendingAlarmAreaDeleteId = "";
        }

        NativeGUILayout.Label(
            UnmaText.Get("ui.area.editor_title", "MANAGE AREAS"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.area.editor_hint",
                "Areas group global panels without changing their alarms. Deleting an area moves its panels to UNASSIGNED."),
            m_smallLabelStyle);
        NativeGUILayout.Space(8f);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.area.new_name", "NEW AREA"),
            m_labelStyle,
            NativeGUILayout.Width(120f));
        m_newAlarmAreaName = NativeGUILayout.TextField(
            m_newAlarmAreaName,
            AlarmAreaPolicy.MaximumDraftNameLength,
            m_textFieldStyle,
            NativeGUILayout.ExpandWidth(true));
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.area.add", "ADD"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(110f),
                NativeGUILayout.Height(32f)))
        {
            AddAlarmAreaDraft();
        }
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.Space(10f);
        if (m_alarmAreaDraft.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.area.empty",
                    "No custom areas. Panels are currently UNASSIGNED."),
                m_labelStyle);
        }

        var compact = m_entityAlarmWindowRect.width /
                      Math.Max(0.75f, UiScale) < 900f;
        for (var index = 0; index < m_alarmAreaDraft.Count; index++)
        {
            var area = m_alarmAreaDraft[index];
            if (area == null)
            {
                continue;
            }
            var panelCount = m_runtime.Configuration.Panels.Count(panel =>
                AlarmAreaPolicy.IsAssignablePanel(panel) &&
                string.Equals(
                    panel.AreaId,
                    area.Id,
                    StringComparison.Ordinal));

            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                (index + 1) + ".",
                m_smallLabelStyle,
                NativeGUILayout.Width(32f));
            area.Name = NativeGUILayout.TextField(
                area.Name ?? "",
                AlarmAreaPolicy.MaximumDraftNameLength,
                m_textFieldStyle,
                NativeGUILayout.ExpandWidth(true));
            if (!compact)
            {
                NativeGUILayout.Label(
                    UnmaText.Format(
                        "ui.area.panel_count",
                        "{0} PANEL(S)",
                        panelCount),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(120f));
            }
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            if (compact)
            {
                NativeGUILayout.Label(
                    UnmaText.Format(
                        "ui.area.panel_count",
                        "{0} PANEL(S)",
                        panelCount),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(130f));
            }
            else
            {
                NativeGUILayout.Space(32f);
            }
            var guiWasEnabled = NativeGUI.enabled;
            NativeGUI.enabled = guiWasEnabled && index > 0;
            var moveUp = NativeGUILayout.Button(
                "↑",
                m_buttonStyle,
                NativeGUILayout.Width(42f),
                NativeGUILayout.Height(30f));
            NativeGUI.enabled = guiWasEnabled &&
                                index < m_alarmAreaDraft.Count - 1;
            var moveDown = NativeGUILayout.Button(
                "↓",
                m_buttonStyle,
                NativeGUILayout.Width(42f),
                NativeGUILayout.Height(30f));
            NativeGUI.enabled = guiWasEnabled;

            var confirmingDelete = string.Equals(
                                       m_pendingAlarmAreaDeleteId,
                                       area.Id,
                                       StringComparison.Ordinal) &&
                                   Time.realtimeSinceStartup <=
                                   m_pendingAlarmAreaDeleteUntil;
            var delete = NativeGUILayout.Button(
                confirmingDelete
                    ? UnmaText.Get(
                        "ui.area.delete_confirm",
                        "CONFIRM DELETE")
                    : UnmaText.Get("ui.area.delete", "DELETE"),
                confirmingDelete ? m_dangerButtonStyle : m_buttonStyle,
                NativeGUILayout.Width(confirmingDelete ? 170f : 110f),
                NativeGUILayout.Height(30f));
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Space(4f);

            if (moveUp || moveDown)
            {
                AlarmAreaPolicy.TryMove(
                    m_alarmAreaDraft,
                    area.Id,
                    moveUp ? index - 1 : index + 1,
                    out _);
                m_pendingAlarmAreaDeleteId = "";
                return;
            }
            if (delete)
            {
                if (!confirmingDelete)
                {
                    m_pendingAlarmAreaDeleteId = area.Id;
                    m_pendingAlarmAreaDeleteUntil =
                        Time.realtimeSinceStartup + 6f;
                    SetStatus(UnmaText.Format(
                        "ui.area.delete_impact",
                        "Delete this area? {0} panel(s) will become UNASSIGNED.",
                        panelCount));
                }
                else
                {
                    m_alarmAreaDraft.RemoveAt(index);
                    m_pendingAlarmAreaDeleteId = "";
                    SetStatus(UnmaText.Get(
                        "ui.area.delete_pending",
                        "Area marked for deletion. Save to apply."));
                }
                return;
            }
        }

        NativeGUILayout.Space(12f);
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.save", "SAVE"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(150f),
                NativeGUILayout.Height(34f)))
        {
            SaveAlarmAreas();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.cancel", "CANCEL"),
                m_buttonStyle,
                NativeGUILayout.Width(150f),
                NativeGUILayout.Height(34f)))
        {
            ReloadAlarmAreaDraft();
            CloseEditorWindow();
        }
        NativeGUILayout.EndHorizontal();
    }

    private void AddAlarmAreaDraft()
    {
        if (m_alarmAreaDraft.Count >= MaximumAlarmAreas)
        {
            SetAlarmAreaFailure(AlarmAreaMutationFailure.TooManyAreas);
            return;
        }
        if (!AlarmAreaPolicy.TryCreate(
                m_alarmAreaDraft,
                m_newAlarmAreaName,
                () => Guid.NewGuid().ToString("N"),
                out _,
                out var failure))
        {
            SetAlarmAreaFailure(failure);
            return;
        }
        m_newAlarmAreaName = "";
        m_pendingAlarmAreaDeleteId = "";
        SetStatus(UnmaText.Get(
            "ui.area.added_to_draft",
            "Area added to the draft."));
    }

    private bool SaveAlarmAreas()
    {
        if (!string.IsNullOrWhiteSpace(m_newAlarmAreaName))
        {
            SetStatus(UnmaText.Get(
                "ui.area.error_add_pending",
                "Add the entered area name to the draft before saving."));
            return false;
        }
        if (!AlarmAreaPolicy.ValidateReplacement(
                m_alarmAreaDraft,
                out var normalized,
                out var failure))
        {
            SetAlarmAreaFailure(failure);
            return false;
        }

        var selectedAreaWillBeDeleted =
            NormalizeAlarmAreaFilter().Kind == AlarmAreaFilterKind.Area &&
            !normalized.Any(area => string.Equals(
                area.Id,
                m_alarmAreaFilter.AreaId,
                StringComparison.Ordinal));
        if (!m_runtime.ReplaceAlarmAreas(
                normalized,
                out var unassignedPanelCount))
        {
            SetStatus(UnmaText.Format(
                "ui.area.save_failed",
                "Areas could not be saved: {0}",
                m_runtime.LastPersistenceError));
            return false;
        }

        m_alarmAreaFilter = selectedAreaWillBeDeleted
            ? AlarmAreaFilter.Unassigned
            : AlarmAreaPolicy.NormalizeFilter(
                m_alarmAreaFilter,
                m_runtime.Configuration.AlarmAreas);
        ReloadAlarmAreaDraft();
        EnsureCurrentPanelVisibleInArea();
        SetStatus(unassignedPanelCount > 0
            ? UnmaText.Format(
                "ui.area.saved_unassigned",
                "Areas saved. {0} panel(s) are now UNASSIGNED.",
                unassignedPanelCount)
            : UnmaText.Get("ui.area.saved", "Areas saved."));
        return true;
    }

    private void ReloadAlarmAreaDraft()
    {
        m_alarmAreaDraft.Clear();
        foreach (var area in m_runtime.Configuration.AlarmAreas ??
                     new List<AlarmAreaDefinition>())
        {
            if (area == null)
            {
                continue;
            }
            m_alarmAreaDraft.Add(new AlarmAreaDefinition
            {
                Id = area.Id,
                Name = area.Name,
            });
        }
        m_newAlarmAreaName = "";
        m_pendingAlarmAreaDeleteId = "";
        m_pendingAlarmAreaDeleteUntil = 0f;
    }

    private bool HasUnsavedAlarmAreas()
    {
        if (!string.IsNullOrWhiteSpace(m_newAlarmAreaName))
        {
            return true;
        }
        var stored = m_runtime.Configuration.AlarmAreas ??
                     new List<AlarmAreaDefinition>();
        if (stored.Count != m_alarmAreaDraft.Count)
        {
            return true;
        }
        for (var index = 0; index < stored.Count; index++)
        {
            var original = stored[index];
            var draft = m_alarmAreaDraft[index];
            if (original == null || draft == null ||
                !string.Equals(
                    original.Id,
                    draft.Id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    original.Name,
                    draft.Name,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private void SetAlarmAreaFailure(AlarmAreaMutationFailure failure)
    {
        SetStatus(failure switch
        {
            AlarmAreaMutationFailure.InvalidName => UnmaText.Get(
                "ui.area.error_name_required",
                "Enter an area name."),
            AlarmAreaMutationFailure.NameTooLong => UnmaText.Get(
                "ui.area.error_name_too_long",
                "Area names may contain at most 40 characters."),
            AlarmAreaMutationFailure.DuplicateName => UnmaText.Get(
                "ui.area.error_name_duplicate",
                "Area names must be unique."),
            AlarmAreaMutationFailure.TooManyAreas => UnmaText.Get(
                "ui.area.error_limit",
                "A maximum of 64 areas is supported."),
            _ => UnmaText.Get(
                "ui.area.error_invalid",
                "The area draft is invalid."),
        });
    }

    private void DrawAlarmAreasClosePrompt()
    {
        NativeGUILayout.Space(24f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.area.close_title",
                "CLOSE AREA EDITOR?"),
            m_warningBannerStyle,
            NativeGUILayout.Height(58f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.area.close_description",
                "The area draft has unsaved changes."),
            m_labelStyle);
        NativeGUILayout.Space(18f);
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.editor.save_and_close", "SAVE & CLOSE"),
                m_primaryButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            if (SaveAlarmAreas())
            {
                CloseEditorWindow();
            }
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.discard", "DISCARD"),
                m_dangerButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            ReloadAlarmAreaDraft();
            CloseEditorWindow();
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Space(12f);
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.editor.back_to_editor", "BACK TO EDITOR"),
                m_buttonStyle,
                NativeGUILayout.Width(230f),
                NativeGUILayout.Height(34f)))
        {
            m_editorClosePromptOpen = false;
        }
    }

    private void DrawPanelSettingsClosePrompt()
    {
        var panel = m_runtime.Configuration.Panels.FirstOrDefault(candidate =>
            candidate != null && string.Equals(
                candidate.Id,
                m_panelSettingsPanelId,
                StringComparison.Ordinal));
        NativeGUILayout.Space(24f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.panel.close_title",
                "CLOSE PANEL SETTINGS?"),
            m_warningBannerStyle,
            NativeGUILayout.Height(58f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.panel.close_description",
                "The panel settings have unsaved changes."),
            m_labelStyle);
        NativeGUILayout.Space(18f);
        NativeGUILayout.BeginHorizontal();
        var guiWasEnabled = NativeGUI.enabled;
        NativeGUI.enabled = guiWasEnabled && panel != null;
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.editor.save_and_close", "SAVE & CLOSE"),
                m_primaryButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            if (SavePanelSettings(panel))
            {
                CloseEditorWindow();
            }
        }
        NativeGUI.enabled = guiWasEnabled;
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.discard", "DISCARD"),
                m_dangerButtonStyle,
                NativeGUILayout.Height(42f)))
        {
            CloseEditorWindow();
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Space(12f);
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.editor.back_to_editor", "BACK TO EDITOR"),
                m_buttonStyle,
                NativeGUILayout.Width(230f),
                NativeGUILayout.Height(34f)))
        {
            m_editorClosePromptOpen = false;
        }
    }

    private void DrawPanelSettingsWindowContent()
    {
        var panel = m_runtime.Configuration.Panels.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                m_panelSettingsPanelId,
                StringComparison.Ordinal));
        if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.0e35fa3ee857"),
                m_labelStyle);
            return;
        }

        var compactSettings = m_entityAlarmWindowRect.width /
                              Math.Max(0.75f, UiScale) < 840f;
        NativeGUILayout.Label(UnmaText.Get("auto.63a4d85953f8"), m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.common.name", "Name"),
            m_labelStyle,
            NativeGUILayout.Width(90f));
        m_panelSettingsName = NativeGUILayout.TextField(
            m_panelSettingsName,
            40,
            m_textFieldStyle,
            compactSettings
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(300f));
        if (!compactSettings)
        {
            DrawPanelSettingsColumnsAndSave(panel, false);
        }
        NativeGUILayout.EndHorizontal();
        if (compactSettings)
        {
            NativeGUILayout.BeginHorizontal();
            DrawPanelSettingsColumnsAndSave(panel, true);
            NativeGUILayout.EndHorizontal();
        }

        if (panel.IsDashboard)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.panel.area", "AREA"),
                m_labelStyle,
                NativeGUILayout.Width(90f));
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.panel.area_dashboard",
                    "AUTOMATIC — CURRENT BOARD FILTER"),
                m_smallLabelStyle);
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("auto.2eb2c75b7d87") +
                UnmaText.Get("auto.a1af7061ed28"),
                m_smallLabelStyle);
            return;
        }

        DrawPanelSettingsAreaSelector();
        var hasUnsavedPanelSettings = HasUnsavedPanelSettings(panel);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            hasUnsavedPanelSettings
                ? UnmaText.Get(
                    "ui.panel.clone_save_first",
                    "Save the visible panel settings before duplicating it.")
                : UnmaText.Get(
                    "ui.panel.clone_hint",
                    "Create an independent copy. Cloned custom alarms start disabled."),
            m_smallLabelStyle);
        if (compactSettings)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        var guiWasEnabled = NativeGUI.enabled;
        NativeGUI.enabled = guiWasEnabled && !hasUnsavedPanelSettings;
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.panel.clone", "DUPLICATE PANEL"),
                m_primaryButtonStyle,
                compactSettings
                    ? NativeGUILayout.ExpandWidth(true)
                    : NativeGUILayout.Width(210f),
                NativeGUILayout.Height(30f)))
        {
            ClonePanel(panel);
            NativeGUILayout.EndHorizontal();
            return;
        }
        NativeGUI.enabled = guiWasEnabled;
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        m_panelSettingsIncludeVanilla = NativeGUILayout.Toggle(
            m_panelSettingsIncludeVanilla,
            UnmaText.Get("auto.d696777f43cd"),
            compactSettings
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(170f));
        m_panelSettingsIncludeSystem = NativeGUILayout.Toggle(
            m_panelSettingsIncludeSystem,
            UnmaText.Get("auto.e71a0cea7772"),
            compactSettings
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(170f));
        if (compactSettings)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        NativeGUILayout.Label(
            UnmaText.Get("ui.panel.auto_filter", "Auto-filter"),
            m_labelStyle,
            NativeGUILayout.Width(90f));
        m_panelSettingsFilter = NativeGUILayout.TextField(
            m_panelSettingsFilter,
            240,
            m_textFieldStyle);
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.Label(
            UnmaText.Get("auto.fe1185445958"),
            m_smallLabelStyle);
        DrawPanelSlots(panel);

        NativeGUILayout.Space(12f);
        var confirmingDelete = string.Equals(
                                   m_pendingPanelDeleteId,
                                   panel.Id,
                                   StringComparison.Ordinal) &&
                               Time.realtimeSinceStartup <=
                               m_pendingPanelDeleteUntil;
        if (NativeGUILayout.Button(
                confirmingDelete
                    ? UnmaText.Get("auto.df65358a4dae")
                    : UnmaText.Get("auto.74d628988b87"),
                confirmingDelete ? m_dangerButtonStyle : m_buttonStyle,
                NativeGUILayout.Width(220f)))
        {
            if (!confirmingDelete)
            {
                m_pendingPanelDeleteId = panel.Id;
                m_pendingPanelDeleteUntil =
                    Time.realtimeSinceStartup + 6f;
                SetStatus(
                    UnmaText.Get("auto.2f5dc812ca48"));
            }
            else if (m_runtime.RemovePanel(panel.Id))
            {
                CloseDetachedPanelsForPanel(panel.Id);
                CloseEditorWindow();
                m_pendingPanelDeleteId = "";
                m_currentPanelIndex = Math.Max(
                    0,
                    Math.Min(m_currentPanelIndex, GlobalPanels.Count - 1));
                SetStatus(UnmaText.Get("auto.05ac06816324"));
            }
            else
            {
                SetStatus(
                    UnmaText.Get("auto.0fb0c7def7d9") +
                    m_runtime.LastPersistenceError);
            }
        }
    }

    private void DrawPanelSettingsColumnsAndSave(
        PanelDefinition panel,
        bool expandSave)
    {
        NativeGUILayout.Label(
            UnmaText.Get("auto.7f6972b99a3e") + m_panelSettingsColumns,
            m_labelStyle,
            NativeGUILayout.Width(90f));
        if (NativeGUILayout.Button(
                "−",
                m_buttonStyle,
                NativeGUILayout.Width(36f)))
        {
            m_panelSettingsColumns = Math.Max(
                1,
                m_panelSettingsColumns - 1);
        }
        if (NativeGUILayout.Button(
                "+",
                m_buttonStyle,
                NativeGUILayout.Width(36f)))
        {
            m_panelSettingsColumns = Math.Min(
                8,
                m_panelSettingsColumns + 1);
        }
        if (expandSave)
        {
            if (NativeGUILayout.Button(
                    UnmaText.Get("ui.common.save", "SAVE"),
                    m_primaryButtonStyle,
                    NativeGUILayout.ExpandWidth(true)))
            {
                SavePanelSettings(panel);
            }
        }
        else if (NativeGUILayout.Button(
                     UnmaText.Get("ui.common.save", "SAVE"),
                     m_primaryButtonStyle,
                     NativeGUILayout.Width(150f)))
        {
            SavePanelSettings(panel);
        }
    }

    private void DrawPanelSettingsAreaSelector()
    {
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.panel.area", "AREA"),
            m_labelStyle,
            NativeGUILayout.Width(90f));
        m_panelSettingsAreaScroll = NativeGUILayout.BeginScrollView(
            m_panelSettingsAreaScroll,
            false,
            false,
            NativeGUILayout.Height(42f),
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.BeginHorizontal();
        DrawPanelSettingsAreaButton(
            "",
            UnmaText.Get("board.area_unassigned", "UNASSIGNED"));
        foreach (var area in m_runtime.Configuration.AlarmAreas ??
                     new List<AlarmAreaDefinition>())
        {
            if (area == null || string.IsNullOrWhiteSpace(area.Id))
            {
                continue;
            }
            DrawPanelSettingsAreaButton(area.Id, area.Name);
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.EndScrollView();
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.panel.area_hint",
                "Assign this panel to an operational area."),
            m_smallLabelStyle);
    }

    private void DrawPanelSettingsAreaButton(string areaId, string name)
    {
        areaId = areaId?.Trim() ?? "";
        name = string.IsNullOrWhiteSpace(name)
            ? UnmaText.Get("ui.area.default_name", "AREA")
            : name.Trim();
        var width = Mathf.Clamp(
            name.Length * Mathf.Max(7f, m_buttonStyle.fontSize * 0.58f) + 24f,
            120f,
            260f);
        if (NativeGUILayout.Button(
                name,
                string.Equals(
                    m_panelSettingsAreaId ?? "",
                    areaId,
                    StringComparison.Ordinal)
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(width),
                NativeGUILayout.Height(30f)))
        {
            m_panelSettingsAreaId = areaId;
        }
    }

    private bool SavePanelSettings(PanelDefinition panel)
    {
        if (m_runtime.UpdatePanelSettings(
                panel.Id,
                m_panelSettingsName,
                m_panelSettingsColumns,
                m_panelSettingsIncludeVanilla,
                m_panelSettingsIncludeSystem,
                m_panelSettingsFilter,
                m_panelSettingsAreaId))
        {
            m_panelSettingsName = panel.Name;
            m_panelSettingsColumns = panel.Columns;
            m_panelSettingsIncludeVanilla = panel.IncludeVanilla;
            m_panelSettingsIncludeSystem = panel.IncludeSystem;
            m_panelSettingsFilter = panel.NotificationFilter ?? "";
            m_panelSettingsAreaId = panel.AreaId ?? "";
            EnsureCurrentPanelVisibleInArea();
            SetStatus(UnmaText.Get("auto.4bd5b213cd77"));
            return true;
        }

        SetStatus(
            UnmaText.Get("auto.27f10f6dc69e") +
            m_runtime.LastPersistenceError);
        return false;
    }

    private bool HasUnsavedPanelSettings(PanelDefinition panel)
    {
        if (panel == null)
        {
            return false;
        }

        var normalizedName = string.IsNullOrWhiteSpace(m_panelSettingsName)
            ? UnmaText.Get("default.panel", "PANEL")
            : m_panelSettingsName.Trim();
        return !string.Equals(
                   panel.Name ?? "",
                   normalizedName,
                   StringComparison.Ordinal) ||
               panel.Columns != Math.Max(
                   1,
                   Math.Min(8, m_panelSettingsColumns)) ||
               panel.IncludeVanilla != m_panelSettingsIncludeVanilla ||
               panel.IncludeSystem != m_panelSettingsIncludeSystem ||
               !string.Equals(
                   panel.AreaId ?? "",
                   m_panelSettingsAreaId ?? "",
                   StringComparison.Ordinal) ||
               !string.Equals(
                   panel.NotificationFilter ?? "",
                   m_panelSettingsFilter ?? "",
                   StringComparison.Ordinal);
    }

    private void ClonePanel(PanelDefinition panel)
    {
        if (!m_runtime.TryCloneGlobalPanel(
                panel?.Id,
                requestedName: "",
                out var clonedPanel,
                out var clonedRuleCount,
                out var skippedSlotCount))
        {
            SetStatus(UnmaText.Format(
                "ui.panel.clone_failed",
                "Panel copy failed: {0}",
                GetPanelCloneFailureDetail()));
            return;
        }

        m_activeEntityPanelId = "";
        m_currentPanelIndex = GlobalPanels.FindIndex(candidate =>
            string.Equals(
                candidate.Id,
                clonedPanel.Id,
                StringComparison.Ordinal));
        m_currentPanelIndex = Math.Max(0, m_currentPanelIndex);
        if (!HasDraftRuleWork())
        {
            m_draftTargetPanelId = clonedPanel.Id;
        }
        CloseEditorWindow();
        SetStatus(skippedSlotCount > 0
            ? UnmaText.Format(
                "ui.panel.clone_success_skipped",
                "Panel copied. {0} custom alarm(s) start disabled; " +
                "{1} broken rule slot(s) were skipped.",
                clonedRuleCount,
                skippedSlotCount)
            : UnmaText.Format(
                "ui.panel.clone_success",
                "Panel copied. {0} custom alarm(s) start disabled.",
                clonedRuleCount));
    }

    private string GetPanelCloneFailureDetail()
    {
        return m_runtime.LastPanelCloneFailure switch
        {
            PanelCloneFailure.InvalidSource => UnmaText.Get(
                "ui.panel.clone_error_invalid_source",
                "The source panel is unavailable."),
            PanelCloneFailure.DashboardNotSupported => UnmaText.Get(
                "ui.panel.clone_error_dashboard",
                "The dashboard cannot be copied."),
            PanelCloneFailure.EntityPanelNotSupported => UnmaText.Get(
                "ui.panel.clone_error_entity",
                "Entity panels cannot be copied."),
            PanelCloneFailure.InvalidSourceData => UnmaText.Get(
                "ui.panel.clone_error_invalid_data",
                "The source panel data is invalid."),
            PanelCloneFailure.IdGenerationFailed => UnmaText.Get(
                "ui.panel.clone_error_id_generation",
                "Unique IDs could not be generated."),
            _ => string.IsNullOrWhiteSpace(m_runtime.LastPersistenceError)
                ? UnmaText.Get(
                    "ui.panel.clone_error_persistence",
                    "The configuration could not be saved.")
                : m_runtime.LastPersistenceError,
        };
    }

    private void DrawAlarmRuleEditor(bool inEntityWindow)
    {
        DrawAlarmTitleField();
        DrawAlarmEnabledField();
        NativeGUILayout.Space(6f);
        DrawTargetPanelSelector(inEntityWindow);
        NativeGUILayout.Space(6f);
        DrawEntitySourceSelector(inEntityWindow);
        if (TryGetLinkedInstrumentSource(out _))
        {
            NativeGUILayout.Space(6f);
            DrawLinkedInstrumentConditionForm();
        }
        else if (m_selectedEntity != null && m_selectedMetrics.Count > 0)
        {
            NativeGUILayout.Space(6f);
            DrawNewConditionForm();
        }

        NativeGUILayout.Space(8f);
        DrawConditionTable();
        NativeGUILayout.Space(8f);
        DrawAlarmProperties();
    }

    private void DrawAlarmTitleField()
    {
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.message_title", "MESSAGE TITLE"),
            m_sectionStyle);
        m_draftRuleName = NativeGUILayout.TextField(
            m_draftRuleName,
            80,
            m_textFieldStyle,
            NativeGUILayout.Height(34f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.message_title_hint",
                "Shown as the title in the alarm slot and in history."),
            m_smallLabelStyle);
    }

    private void DrawAlarmEnabledField()
    {
        var compact = IsCompactRuleEditor();
        if (!compact)
        {
            NativeGUILayout.BeginHorizontal();
        }
        m_draftEnabled = NativeGUILayout.Toggle(
            m_draftEnabled,
            UnmaText.Get(
                "ui.editor.alarm_enabled",
                "ALARM ENABLED"),
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(210f),
            NativeGUILayout.Height(30f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.alarm_enabled_hint",
                "Inactive alarms remain configured but are not evaluated."),
            m_smallLabelStyle);
        if (!compact)
        {
            NativeGUILayout.EndHorizontal();
        }
    }

    private bool IsCompactRuleEditor(float breakpoint = 900f) =>
        m_entityAlarmWindowRect.width / Math.Max(0.75f, UiScale) < breakpoint;

    private void DrawTargetPanelSelector(bool allowCreate)
    {
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.target_panel", "TARGET ANNUNCIATOR PANEL"),
            m_sectionStyle);
        if (TryGetLinkedInstrumentSource(out _))
        {
            DrawInstrumentTargetPanelSelector();
            return;
        }
        var panel = GetDraftTargetPanel();
        if (panel == null)
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.ebe65b2ddfb6") +
                UnmaText.Get("auto.193650f56055"),
                m_labelStyle);
            return;
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            PanelTopologyPolicy.IsEntityPanel(panel)
                ? UnmaText.Get("auto.ef933adc4bdb")
                : UnmaText.Get("auto.3ed702323b47"),
            m_labelStyle,
            NativeGUILayout.Width(160f));
        NativeGUILayout.Label(
            panel.Name,
            m_headerStyle,
            NativeGUILayout.Height(30f));
        NativeGUILayout.EndHorizontal();

        if (PanelTopologyPolicy.IsEntityPanel(panel))
        {
            DrawGlobalPanelLinks();
        }
    }

    private void DrawInstrumentTargetPanelSelector()
    {
        var targets = GlobalPanels
            .Where(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(panel))
            .ToArray();
        if (targets.Length == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.editor.target_panel_required",
                    "At least one annunciator panel is required."),
                m_warningBannerStyle);
            return;
        }

        if (GetSelectedDraftTargetPanelIds().Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.editor.target_panel_choose_required",
                    "Choose at least one target panel before saving."),
                m_warningBannerStyle);
        }

        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.instrument_target_hint",
                "Select every panel on which this instrument alarm should appear. At least one panel must remain selected."),
            m_smallLabelStyle);
        var logicalWidth = m_entityAlarmWindowRect.width /
                           Math.Max(0.75f, UiScale);
        var columns = Math.Max(
            1,
            Math.Min(4, Mathf.FloorToInt((logicalWidth - 20f) / 226f)));
        for (var start = 0; start < targets.Length; start += columns)
        {
            NativeGUILayout.BeginHorizontal();
            for (var offset = 0;
                 offset < columns && start + offset < targets.Length;
                 offset++)
            {
                var target = targets[start + offset];
                var selected = IsDraftTargetPanelSelected(target.Id);
                if (NativeGUILayout.Button(
                        (selected ? "✓ " : "+ ") + target.Name,
                        selected ? m_primaryButtonStyle : m_buttonStyle,
                        NativeGUILayout.Width(220f),
                        NativeGUILayout.Height(30f)))
                {
                    SetDraftTargetPanelSelected(target, !selected);
                }
            }
            NativeGUILayout.FlexibleSpace();
            NativeGUILayout.EndHorizontal();
        }
    }

    private bool IsDraftTargetPanelSelected(string panelId)
    {
        return string.Equals(
                   m_draftTargetPanelId,
                   panelId,
                   StringComparison.Ordinal) ||
               m_draftLinkedPanelIds.Contains(panelId);
    }

    private void SetDraftTargetPanelSelected(
        PanelDefinition panel,
        bool selected)
    {
        if (panel == null || panel.IsDashboard ||
            PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return;
        }

        if (selected)
        {
            if (GetDraftTargetPanel() == null)
            {
                m_draftTargetPanelId = panel.Id;
                m_draftPreferredSlotIndex = panel.Slots?.Count ?? 0;
            }
            else if (!string.Equals(
                         m_draftTargetPanelId,
                         panel.Id,
                         StringComparison.Ordinal))
            {
                m_draftLinkedPanelIds.Add(panel.Id);
            }
            return;
        }

        var selectedIds = GetSelectedDraftTargetPanelIds();
        if (selectedIds.Count <= 1)
        {
            SetStatus(UnmaText.Get(
                "ui.editor.status.target_panel_required",
                "At least one target panel must remain selected."));
            return;
        }

        if (!string.Equals(
                m_draftTargetPanelId,
                panel.Id,
                StringComparison.Ordinal))
        {
            m_draftLinkedPanelIds.Remove(panel.Id);
            return;
        }

        var promotedId = m_draftLinkedPanelIds.FirstOrDefault(id =>
            !string.Equals(id, panel.Id, StringComparison.Ordinal) &&
            m_runtime.Configuration.Panels.Any(candidate =>
                candidate != null &&
                !candidate.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(candidate) &&
                string.Equals(candidate.Id, id, StringComparison.Ordinal)));
        if (string.IsNullOrWhiteSpace(promotedId))
        {
            SetStatus(UnmaText.Get(
                "ui.editor.status.target_panel_required",
                "At least one target panel must remain selected."));
            return;
        }
        m_draftLinkedPanelIds.Remove(promotedId);
        m_draftLinkedPanelIds.Remove(panel.Id);
        m_draftTargetPanelId = promotedId;
        var promoted = GetDraftTargetPanel();
        m_draftPreferredSlotIndex = promoted?.Slots?.Count ?? 0;
    }

    private List<string> GetSelectedDraftTargetPanelIds()
    {
        var result = new List<string>();
        var primary = GetDraftTargetPanel();
        if (primary != null &&
            !primary.IsDashboard &&
            !PanelTopologyPolicy.IsEntityPanel(primary))
        {
            result.Add(m_draftTargetPanelId);
        }
        result.AddRange(m_draftLinkedPanelIds.Where(id =>
            !result.Contains(id, StringComparer.Ordinal) &&
            m_runtime.Configuration.Panels.Any(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(panel) &&
                string.Equals(panel.Id, id, StringComparison.Ordinal))));
        return result;
    }

    private void DrawGlobalPanelLinks()
    {
        NativeGUILayout.Space(6f);
        NativeGUILayout.Label(
            UnmaText.Get("auto.c350c4d6b1d5"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.7237b12624f3") +
            UnmaText.Get("auto.e4505264649b"),
            m_smallLabelStyle);

        var globalTargets = GlobalPanels
            .Where(panel => !panel.IsDashboard)
            .ToArray();
        if (globalTargets.Length == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.637c3fbb4c15"),
                m_smallLabelStyle);
            return;
        }

        foreach (var globalPanel in globalTargets)
        {
            NativeGUILayout.BeginHorizontal();
            var linked = m_draftLinkedPanelIds.Contains(globalPanel.Id);
            if (NativeGUILayout.Button(
                    linked
                        ? "✓ " + globalPanel.Name
                        : "+ " + globalPanel.Name,
                    linked ? m_primaryButtonStyle : m_buttonStyle,
                    NativeGUILayout.Width(420f),
                    NativeGUILayout.Height(30f)))
            {
                if (linked)
                {
                    m_draftLinkedPanelIds.Remove(globalPanel.Id);
                }
                else
                {
                    m_draftLinkedPanelIds.Add(globalPanel.Id);
                }
            }
            NativeGUILayout.FlexibleSpace();
            NativeGUILayout.EndHorizontal();
        }
    }

    private void DrawCreateTargetPanelRow(bool slotPositionLocked)
    {
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.96cad36109c7"),
            m_labelStyle,
            NativeGUILayout.Width(205f));
        var guiWasEnabled = NativeGUI.enabled;
        NativeGUI.enabled = guiWasEnabled && !slotPositionLocked;
        m_newPanelName = NativeGUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            NativeGUILayout.Width(310f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.af812ec572bb"),
                m_buttonStyle,
                NativeGUILayout.Width(205f)))
        {
            AddPanel();
        }
        NativeGUI.enabled = guiWasEnabled;
        NativeGUILayout.Label(
            slotPositionLocked
                ? UnmaText.Get("auto.da45fd0a048f")
                : UnmaText.Get("auto.83f9628c70ab"),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();
    }

    private void DrawEntitySourceSelector(bool inEntityWindow)
    {
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.source", "SOURCE"),
            m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.7edb47ed7ea9"),
                string.IsNullOrWhiteSpace(m_linkedInstrumentSourceId) &&
                m_selectedEntity != null && m_selectedEntity.EntityId >= 0
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(315f),
                NativeGUILayout.Height(30f)))
        {
            CaptureSelectedEntity(inEntityWindow);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.editor.global_variables",
                    "GLOBAL VARIABLES"),
                string.IsNullOrWhiteSpace(m_linkedInstrumentSourceId) &&
                m_selectedEntity?.EntityId < 0
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(220f),
                NativeGUILayout.Height(30f)))
        {
            SelectGlobalMetricSource(false);
        }
        var hasLinkedSource = TryGetLinkedInstrumentSource(
            out var linkedSource);
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.editor.instrument_source",
                    "INSTRUMENT"),
                hasLinkedSource
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(200f),
                NativeGUILayout.Height(30f)))
        {
            if (hasLinkedSource)
            {
                m_linkedInstrumentPickerOpen = !m_linkedInstrumentPickerOpen;
            }
            else
            {
                var firstInstrument = m_runtime.Configuration.Instruments
                    .FirstOrDefault(item => item != null);
                if (firstInstrument == null ||
                    !SelectLinkedInstrumentSource(firstInstrument.Id))
                {
                    SetStatus(UnmaText.Get(
                        "ui.editor.status.no_instruments",
                        "No instruments are available as a source."));
                }
            }
        }
        NativeGUILayout.Label(
            linkedSource != null
                ? UnmaText.Format(
                    "ui.editor.linked_values_source",
                    "LINKED VALUES: {0}",
                    linkedSource.Title)
                : m_selectedEntity == null
                ? UnmaText.Get("auto.51f6d86aa271") +
                  UnmaText.Get("auto.3ebeb0f6f700") +
                  UnmaText.Get("ui.entity.take_selection")
                : m_selectedEntity.Title + " · " +
                  ShortTypeName(m_selectedEntity.EntityType) +
                  UnmaText.Get("auto.9da04860d6fc") + m_selectedEntity.EntityId +
                  " · " + m_selectedMetrics.Count + UnmaText.Get("auto.c8b47a039c3f"),
            m_labelStyle);
        NativeGUILayout.EndHorizontal();
    }

    private void DrawLinkedInstrumentConditionForm()
    {
        var instruments = GetLinkedInstruments();
        if (instruments.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.editor.linked_values_unavailable",
                    "No linked values are available."),
                m_smallLabelStyle);
            return;
        }

        m_selectedLinkedInstrumentIndex = Math.Max(
            0,
            Math.Min(m_selectedLinkedInstrumentIndex, instruments.Count - 1));
        var instrument = instruments[m_selectedLinkedInstrumentIndex];
        var hasValue = m_runtime.TryGetInstrumentCurrentValue(
            instrument.Id,
            out var currentValue);

        NativeGUILayout.Label(UnmaText.Get("auto.d7ee9125f8f1"), m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.6bb4e33de37c"),
            m_labelStyle,
            NativeGUILayout.Width(150f));
        var valueText = hasValue
            ? currentValue.ToString(
                "0.###",
                CultureInfo.CurrentCulture) +
              (string.IsNullOrWhiteSpace(instrument.Unit)
                  ? ""
                  : " " + instrument.Unit)
            : UnmaText.Get(
                "ui.instrument.value_unavailable",
                "NOT AVAILABLE");
        if (NativeGUILayout.Button(
                instrument.Title + "  [" + valueText + "]",
                m_linkedInstrumentPickerOpen
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Height(30f)))
        {
            m_linkedInstrumentPickerOpen = !m_linkedInstrumentPickerOpen;
        }
        NativeGUILayout.EndHorizontal();

        if (m_linkedInstrumentPickerOpen)
        {
            foreach (var candidate in instruments)
            {
                var selected = string.Equals(
                    candidate.Id,
                    instrument.Id,
                    StringComparison.Ordinal);
                if (!NativeGUILayout.Button(
                        candidate.Title,
                        selected ? m_primaryButtonStyle : m_buttonStyle,
                        NativeGUILayout.Height(28f)))
                {
                    continue;
                }
                m_selectedLinkedInstrumentIndex = instruments.IndexOf(candidate);
                m_linkedInstrumentPickerOpen = false;
            }
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("editor.comparison"),
            m_labelStyle,
            NativeGUILayout.Width(150f));
        DrawComparisonSelector(ref m_draftComparison);
        NativeGUILayout.Space(12f);
        NativeGUILayout.Label(
            UnmaText.Get("editor.target_value"),
            m_labelStyle,
            NativeGUILayout.Width(105f));
        m_draftThreshold = NativeGUILayout.TextField(
            m_draftThreshold,
            24,
            m_textFieldStyle,
            NativeGUILayout.Width(105f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.3cb2b0054d58"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(190f),
                NativeGUILayout.Height(30f)))
        {
            AddLinkedInstrumentCondition(instrument);
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.linked_values_hint",
                "After adding the row, value, change and duration modes can be configured below."),
            m_smallLabelStyle);
    }

    private void DrawNewConditionForm()
    {
        m_selectedMetricIndex = Math.Max(
            0,
            Math.Min(m_selectedMetricIndex, m_selectedMetrics.Count - 1));
        var metric = m_selectedMetrics[m_selectedMetricIndex];

        NativeGUILayout.Label(UnmaText.Get("auto.d7ee9125f8f1"), m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(UnmaText.Get("auto.6bb4e33de37c"), m_labelStyle, NativeGUILayout.Width(150f));
        if (NativeGUILayout.Button(
                metric.Label + UnmaText.Get("auto.e824707b8b2d") + FormatMetricValue(metric) + "]",
                m_metricPickerOpen ? m_primaryButtonStyle : m_buttonStyle,
                NativeGUILayout.Height(30f)))
        {
            m_metricPickerOpen = !m_metricPickerOpen;
            m_referenceMetricPickerOpen = false;
        }
        NativeGUILayout.EndHorizontal();

        if (m_metricPickerOpen)
        {
            DrawMetricPicker(false);
            metric = m_selectedMetrics[m_selectedMetricIndex];
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.calculation", "Calculation"),
            m_labelStyle,
            NativeGUILayout.Width(150f));
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.editor.absolute", "ABSOLUTE"),
                m_draftValueMode == ConditionValueMode.Absolute
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(125f)))
        {
            m_draftValueMode = ConditionValueMode.Absolute;
            m_referenceMetricPickerOpen = false;
        }
        if (NativeGUILayout.Button(
                metric.Path.StartsWith(
                    "$input.product:",
                    StringComparison.Ordinal)
                    ? UnmaText.Get("auto.b3ada244026c")
                    : UnmaText.Get("auto.9424124c3537"),
                m_draftValueMode == ConditionValueMode.PercentOfReference
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(125f)))
        {
            m_draftValueMode = ConditionValueMode.PercentOfReference;
            SelectSuggestedReferenceMetric(metric);
        }

        if (m_draftValueMode == ConditionValueMode.PercentOfReference)
        {
            m_selectedReferenceMetricIndex = Math.Max(
                0,
                Math.Min(
                    m_selectedReferenceMetricIndex,
                    m_selectedMetrics.Count - 1));
            var reference = m_selectedMetrics[m_selectedReferenceMetricIndex];
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.cbe287253675") + reference.Label +
                    " [" + FormatMetricValue(reference) + "]",
                    m_referenceMetricPickerOpen
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Height(30f)))
            {
                m_referenceMetricPickerOpen = !m_referenceMetricPickerOpen;
                m_metricPickerOpen = false;
            }
        }
        NativeGUILayout.EndHorizontal();

        if (metric.Path.StartsWith(
                "$input.product:",
                StringComparison.Ordinal))
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.104537c3e0ed") +
                UnmaText.Get("auto.0c0050a05708"),
                m_smallLabelStyle);
        }

        if (m_referenceMetricPickerOpen &&
            m_draftValueMode == ConditionValueMode.PercentOfReference)
        {
            DrawMetricPicker(true);
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("editor.comparison"),
            m_labelStyle,
            NativeGUILayout.Width(150f));
        DrawComparisonSelector(ref m_draftComparison);
        NativeGUILayout.Space(12f);
        NativeGUILayout.Label(
            m_draftValueMode == ConditionValueMode.PercentOfReference
                ? UnmaText.Get("auto.23a9b1f4773d")
                : UnmaText.Get("editor.target_value"),
            m_labelStyle,
            NativeGUILayout.Width(105f));
        m_draftThreshold = NativeGUILayout.TextField(
            m_draftThreshold,
            24,
            m_textFieldStyle,
            NativeGUILayout.Width(105f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.3cb2b0054d58"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(190f),
                NativeGUILayout.Height(30f)))
        {
            AddDraftCondition();
        }
        NativeGUILayout.EndHorizontal();
    }

    private void DrawMetricPicker(bool referencePicker)
    {
        var filter = referencePicker
            ? m_referenceMetricPickerFilter
            : m_metricPickerFilter;
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Space(150f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.common.search", "Search"),
            m_smallLabelStyle,
            NativeGUILayout.Width(60f));
        filter = NativeGUILayout.TextField(
            filter,
            60,
            m_textFieldStyle,
            NativeGUILayout.Width(280f));
        NativeGUILayout.Label(
            UnmaText.Get("auto.84d283754bde"),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();
        if (referencePicker)
        {
            m_referenceMetricPickerFilter = filter;
        }
        else
        {
            m_metricPickerFilter = filter;
        }

        var scroll = referencePicker
            ? m_referenceMetricPickerScroll
            : m_metricPickerScroll;
        scroll = NativeGUILayout.BeginScrollView(scroll, NativeGUILayout.Height(170f));
        var shown = 0;
        for (var index = 0; index < m_selectedMetrics.Count; index++)
        {
            var candidate = m_selectedMetrics[index];
            if (!string.IsNullOrWhiteSpace(filter) &&
                candidate.Label.IndexOf(
                    filter,
                    StringComparison.CurrentCultureIgnoreCase) < 0 &&
                candidate.Path.IndexOf(
                    filter,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (++shown > 80)
            {
                NativeGUILayout.Label(
                    UnmaText.Get("auto.7a9d07fa642b"),
                    m_smallLabelStyle);
                break;
            }

            var selected = referencePicker
                ? index == m_selectedReferenceMetricIndex
                : index == m_selectedMetricIndex;
            if (NativeGUILayout.Button(
                    candidate.Label + UnmaText.Get("auto.fe59854f2cdf") +
                    FormatMetricValue(candidate),
                    selected ? m_primaryButtonStyle : m_buttonStyle,
                    NativeGUILayout.Height(27f)))
            {
                if (referencePicker)
                {
                    m_selectedReferenceMetricIndex = index;
                    m_referenceMetricPickerOpen = false;
                }
                else
                {
                    m_selectedMetricIndex = index;
                    m_metricPickerOpen = false;
                    SelectSuggestedReferenceMetric(candidate);
                }
            }
        }
        NativeGUILayout.EndScrollView();
        if (referencePicker)
        {
            m_referenceMetricPickerScroll = scroll;
        }
        else
        {
            m_metricPickerScroll = scroll;
        }
    }

    private void DrawComparisonSelector(ref ComparisonOperator comparison)
    {
        foreach (ComparisonOperator candidate in Enum.GetValues(
                     typeof(ComparisonOperator)))
        {
            if (NativeGUILayout.Button(
                    UnmaRuntime.OperatorText(candidate),
                    comparison == candidate
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Width(42f),
                    NativeGUILayout.Height(28f)))
            {
                comparison = candidate;
            }
        }
    }

    private void DrawConditionTable()
    {
        NativeGUILayout.Label(UnmaText.Get("auto.6dc84400fbd4"), m_sectionStyle);
        var compact = IsCompactRuleEditor();
        if (!compact)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.actual_value", "ACTUAL VALUE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(135f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.identifier", "IDENTIFIER"),
                m_smallLabelStyle,
                NativeGUILayout.Width(330f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.operator", "OPERATOR"),
                m_smallLabelStyle,
                NativeGUILayout.Width(265f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.target_value", "TARGET VALUE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(115f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.condition", "CONDITION"),
                m_smallLabelStyle,
                NativeGUILayout.Width(90f));
            NativeGUILayout.EndHorizontal();
        }

        if (m_draftConditions.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get("auto.71931e3b5361"),
                m_smallLabelStyle);
            return;
        }

        if (compact)
        {
            DrawCompactConditionCards();
            DrawCompactConditionLogicSelector();
            return;
        }

        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            var condition = m_draftConditions[index];
            while (m_draftConditionThresholdTexts.Count <= index)
            {
                m_draftConditionThresholdTexts.Add(
                    (UsesComparisonThreshold(condition.TrendMode)
                        ? condition.Threshold
                        : condition.DeltaThreshold).ToString(
                        "R",
                        CultureInfo.CurrentCulture));
            }
            EnsureDraftHysteresisText(condition);

            if (!string.IsNullOrWhiteSpace(condition.InstrumentId))
            {
                if (DrawInstrumentConditionRow(index, condition))
                {
                    index--;
                }
                continue;
            }

            NativeGUILayout.BeginHorizontal(
                "condition:" + RuntimeHelpers.GetHashCode(condition).ToString(
                    CultureInfo.InvariantCulture));
            NativeGUILayout.Label(
                ConditionActualText(condition),
                m_labelStyle,
                NativeGUILayout.Width(135f),
                NativeGUILayout.Height(42f));
            NativeGUILayout.BeginVertical(NativeGUILayout.Width(330f));
            NativeGUILayout.Label(
                condition.EntityTitle + " #" + condition.EntityId +
                " · " + condition.MetricLabel,
                m_labelStyle);
            NativeGUILayout.BeginHorizontal();
            if (NativeGUILayout.Button(
                    condition.ValueMode == ConditionValueMode.Absolute
                        ? UnmaText.Get("ui.editor.absolute", "ABSOLUTE")
                        : UnmaText.Get("auto.9424124c3537"),
                    m_buttonStyle,
                    NativeGUILayout.Width(85f)))
            {
                condition.ValueMode =
                    condition.ValueMode == ConditionValueMode.Absolute
                        ? ConditionValueMode.PercentOfReference
                        : ConditionValueMode.Absolute;
                if (condition.ValueMode == ConditionValueMode.PercentOfReference &&
                    string.IsNullOrWhiteSpace(condition.ReferenceMetricPath))
                {
                    condition.ReferenceMetricPath =
                        SuggestedReferencePath(condition.MetricPath);
                    condition.ReferenceMetricLabel =
                        FindSelectedMetric(condition.ReferenceMetricPath)?.Label ??
                        condition.ReferenceMetricPath;
                }
            }
            if (condition.ValueMode == ConditionValueMode.PercentOfReference)
            {
                if (NativeGUILayout.Button(
                        string.IsNullOrWhiteSpace(condition.ReferenceMetricLabel)
                            ? UnmaText.Get("auto.72b40251b34b")
                            : UnmaText.Get("auto.cbe287253675") + condition.ReferenceMetricLabel,
                        m_conditionReferencePickerIndex == index
                            ? m_primaryButtonStyle
                            : m_buttonStyle))
                {
                    m_conditionReferencePickerIndex =
                        m_conditionReferencePickerIndex == index ? -1 : index;
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.EndVertical();

            var comparison = condition.Comparison;
            NativeGUILayout.BeginHorizontal(NativeGUILayout.Width(265f));
            DrawComparisonSelector(ref comparison);
            NativeGUILayout.EndHorizontal();
            condition.Comparison = comparison;

            m_draftConditionThresholdTexts[index] = NativeGUILayout.TextField(
                m_draftConditionThresholdTexts[index],
                24,
                m_textFieldStyle,
                NativeGUILayout.Width(105f),
                NativeGUILayout.Height(30f));
            NativeGUILayout.Label(
                    index == 0
                    ? UnmaText.Get("ui.editor.start", "START")
                    : m_draftLogic == AlarmLogic.All
                        ? UnmaText.Get("ui.common.and", "AND")
                        : UnmaText.Get("ui.common.or", "OR"),
                m_headerStyle,
                NativeGUILayout.Width(70f),
                NativeGUILayout.Height(30f));
            if (NativeGUILayout.Button(
                    "X",
                    m_dangerButtonStyle,
                    NativeGUILayout.Width(38f),
                    NativeGUILayout.Height(30f)))
                {
                    m_draftTrendWindowTexts.Remove(condition);
                    m_draftHysteresisTexts.Remove(condition);
                    m_draftConditions.RemoveAt(index);
                m_draftConditionThresholdTexts.RemoveAt(index);
                if (m_conditionReferencePickerIndex == index)
                {
                    m_conditionReferencePickerIndex = -1;
                }
                else if (m_conditionReferencePickerIndex > index)
                {
                    m_conditionReferencePickerIndex--;
                }
                index--;
            }
            NativeGUILayout.EndHorizontal();

            if (index >= 0 &&
                index < m_draftConditions.Count &&
                ReferenceEquals(m_draftConditions[index], condition))
            {
                DrawDraftHysteresisRow(condition, 465f);
            }

            if (index >= 0 && m_conditionReferencePickerIndex == index)
            {
                DrawConditionReferencePicker(condition);
            }
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(UnmaText.Get("auto.956d69c9e3ca"), m_labelStyle, NativeGUILayout.Width(210f));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.76efbe95b3a4"),
                m_draftLogic == AlarmLogic.All
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(290f)))
        {
            m_draftLogic = AlarmLogic.All;
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.556080cfb23f"),
                m_draftLogic == AlarmLogic.Any
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(300f)))
        {
            m_draftLogic = AlarmLogic.Any;
        }
        NativeGUILayout.Label(
            UnmaText.Get("auto.9a99ea646292"),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();
    }

    private void DrawCompactConditionCards()
    {
        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            var condition = m_draftConditions[index];
            while (m_draftConditionThresholdTexts.Count <= index)
            {
                m_draftConditionThresholdTexts.Add(
                    (UsesComparisonThreshold(condition.TrendMode)
                        ? condition.Threshold
                        : condition.DeltaThreshold).ToString(
                        "R",
                        CultureInfo.CurrentCulture));
            }
            EnsureDraftHysteresisText(condition);

            var removed = !string.IsNullOrWhiteSpace(condition.InstrumentId)
                ? DrawCompactInstrumentConditionCard(index, condition)
                : DrawCompactMetricConditionCard(index, condition);
            if (removed)
            {
                index--;
            }
        }
    }

    private bool DrawCompactMetricConditionCard(
        int index,
        ConditionDefinition condition)
    {
        NativeGUILayout.BeginVertical(
            "compact-condition:" + RuntimeHelpers.GetHashCode(condition)
                .ToString(CultureInfo.InvariantCulture),
            m_panelStyle);

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            index == 0
                ? UnmaText.Get("ui.editor.start", "START")
                : m_draftLogic == AlarmLogic.All
                    ? UnmaText.Get("ui.common.and", "AND")
                    : UnmaText.Get("ui.common.or", "OR"),
            m_headerStyle,
            NativeGUILayout.Width(70f),
            NativeGUILayout.Height(36f));
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.actual_value", "ACTUAL VALUE"),
            m_smallLabelStyle,
            NativeGUILayout.Width(105f));
        NativeGUILayout.Label(
            ConditionActualText(condition),
            m_headerStyle,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(36f));
        var remove = NativeGUILayout.Button(
            "X",
            m_dangerButtonStyle,
            NativeGUILayout.Width(36f),
            NativeGUILayout.Height(36f));
        NativeGUILayout.EndHorizontal();

        if (remove)
        {
            NativeGUILayout.EndVertical();
            RemoveDraftConditionAt(index, condition);
            return true;
        }

        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.identifier", "IDENTIFIER"),
            m_smallLabelStyle);
        NativeGUILayout.Label(
            condition.EntityTitle + " #" + condition.EntityId +
            " · " + condition.MetricLabel,
            m_labelStyle);

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.calculation", "Calculation"),
            m_smallLabelStyle,
            NativeGUILayout.Width(105f));
        if (NativeGUILayout.Button(
                condition.ValueMode == ConditionValueMode.Absolute
                    ? UnmaText.Get("ui.editor.absolute", "ABSOLUTE")
                    : UnmaText.Get("auto.9424124c3537"),
                m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(34f)))
        {
            condition.ValueMode =
                condition.ValueMode == ConditionValueMode.Absolute
                    ? ConditionValueMode.PercentOfReference
                    : ConditionValueMode.Absolute;
            if (condition.ValueMode == ConditionValueMode.PercentOfReference &&
                string.IsNullOrWhiteSpace(condition.ReferenceMetricPath))
            {
                condition.ReferenceMetricPath =
                    SuggestedReferencePath(condition.MetricPath);
                condition.ReferenceMetricLabel =
                    FindSelectedMetric(condition.ReferenceMetricPath)?.Label ??
                    condition.ReferenceMetricPath;
            }
        }
        NativeGUILayout.EndHorizontal();

        if (condition.ValueMode == ConditionValueMode.PercentOfReference)
        {
            if (NativeGUILayout.Button(
                    string.IsNullOrWhiteSpace(condition.ReferenceMetricLabel)
                        ? UnmaText.Get("auto.72b40251b34b")
                        : UnmaText.Get("auto.cbe287253675") +
                          condition.ReferenceMetricLabel,
                    m_conditionReferencePickerIndex == index
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                m_conditionReferencePickerIndex =
                    m_conditionReferencePickerIndex == index ? -1 : index;
            }
        }

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.operator", "OPERATOR"),
            m_smallLabelStyle,
            NativeGUILayout.Width(105f));
        var comparison = condition.Comparison;
        DrawCompactComparisonSelector(ref comparison);
        condition.Comparison = comparison;
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.target_value", "TARGET VALUE"),
            m_smallLabelStyle,
            NativeGUILayout.Width(105f));
        m_draftConditionThresholdTexts[index] = NativeGUILayout.TextField(
            m_draftConditionThresholdTexts[index],
            24,
            m_textFieldStyle,
            NativeGUILayout.Width(125f),
            NativeGUILayout.Height(34f));
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        DrawDraftHysteresisInline(condition, 34f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.timing.hysteresis_hint",
                "0 disables the dead band around the threshold."),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();

        if (m_conditionReferencePickerIndex == index)
        {
            DrawConditionReferencePicker(condition);
        }

        NativeGUILayout.EndVertical();
        return false;
    }

    private bool DrawCompactInstrumentConditionCard(
        int index,
        ConditionDefinition condition)
    {
        var instrument = m_runtime.Configuration.Instruments.FirstOrDefault(
            item => string.Equals(
                item.Id,
                condition.InstrumentId,
                StringComparison.Ordinal));
        var currentText = m_runtime.TryGetInstrumentCurrentValue(
            condition.InstrumentId,
            out var currentValue)
            ? currentValue.ToString("0.###", CultureInfo.CurrentCulture)
            : "—";

        NativeGUILayout.BeginVertical(
            "compact-instrument-condition:" +
            RuntimeHelpers.GetHashCode(condition).ToString(
                CultureInfo.InvariantCulture),
            m_panelStyle);

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            index == 0
                ? UnmaText.Get("ui.editor.start", "START")
                : m_draftLogic == AlarmLogic.All
                    ? UnmaText.Get("ui.common.and", "AND")
                    : UnmaText.Get("ui.common.or", "OR"),
            m_headerStyle,
            NativeGUILayout.Width(70f),
            NativeGUILayout.Height(36f));
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.actual_value", "ACTUAL VALUE"),
            m_smallLabelStyle,
            NativeGUILayout.Width(105f));
        NativeGUILayout.Label(
            currentText,
            m_headerStyle,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(36f));
        var remove = NativeGUILayout.Button(
            "X",
            m_dangerButtonStyle,
            NativeGUILayout.Width(36f),
            NativeGUILayout.Height(36f));
        NativeGUILayout.EndHorizontal();

        if (remove)
        {
            NativeGUILayout.EndVertical();
            RemoveDraftConditionAt(index, condition);
            return true;
        }

        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.instrument.condition_title",
                "{0} · CALCULATED METRIC",
                instrument?.Title ?? condition.MetricLabel),
            m_labelStyle);
        NativeGUILayout.Label(
            instrument == null
                ? UnmaText.Get(
                    "ui.instrument.condition_missing",
                    "Instrument no longer exists")
                : UnmaText.Format(
                    "ui.instrument.condition_sources",
                    "{0} · {1} SOURCE(S)",
                    InstrumentAggregationLabel(instrument.Aggregation),
                    instrument.Sources.Count),
            m_smallLabelStyle);

        NativeGUILayout.BeginHorizontal();
        DrawCompactInstrumentModeButton(
            index,
            condition,
            UnmaText.Get("ui.instrument.condition.value", "VALUE"),
            condition.TrendMode == InstrumentTrendMode.None,
            InstrumentTrendMode.None);
        DrawCompactInstrumentModeButton(
            index,
            condition,
            UnmaText.Get(
                "ui.instrument.condition.decrease",
                "DECREASE"),
            IsDecreaseMode(condition.TrendMode),
            InstrumentTrendMode.DecreaseAbsolute);
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.BeginHorizontal();
        DrawCompactInstrumentModeButton(
            index,
            condition,
            UnmaText.Get(
                "ui.instrument.condition.increase",
                "INCREASE"),
            IsIncreaseMode(condition.TrendMode),
            InstrumentTrendMode.IncreaseAbsolute);
        DrawCompactInstrumentModeButton(
            index,
            condition,
            UnmaText.Get(
                "ui.instrument.condition.sustain",
                "SUSTAIN"),
            condition.TrendMode == InstrumentTrendMode.SustainComparison,
            InstrumentTrendMode.SustainComparison);
        NativeGUILayout.EndHorizontal();

        if (UsesComparisonThreshold(condition.TrendMode))
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.instrument.condition.compare",
                    "COMPARE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(105f));
            var comparison = condition.Comparison;
            DrawCompactComparisonSelector(ref comparison);
            condition.Comparison = comparison;
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.instrument.condition.target_value",
                    "TARGET VALUE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(105f));
            m_draftConditionThresholdTexts[index] =
                NativeGUILayout.TextField(
                    m_draftConditionThresholdTexts[index],
                    24,
                    m_textFieldStyle,
                    NativeGUILayout.Width(125f),
                    NativeGUILayout.Height(34f));
            NativeGUILayout.FlexibleSpace();
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            DrawDraftHysteresisInline(condition, 34f);
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.timing.hysteresis_hint",
                    "0 disables the dead band around the threshold."),
                m_smallLabelStyle);
            NativeGUILayout.EndHorizontal();
        }
        else
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                IsPercentChangeMode(condition.TrendMode)
                    ? UnmaText.Get(
                        "ui.instrument.condition.minimum_percent",
                        "AT LEAST %")
                    : UnmaText.Get(
                        "ui.instrument.condition.minimum_amount",
                        "AT LEAST AMOUNT"),
                m_smallLabelStyle,
                NativeGUILayout.Width(125f));
            m_draftConditionThresholdTexts[index] =
                NativeGUILayout.TextField(
                    m_draftConditionThresholdTexts[index],
                    24,
                    m_textFieldStyle,
                    NativeGUILayout.Width(125f),
                    NativeGUILayout.Height(34f));
            NativeGUILayout.FlexibleSpace();
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            if (NativeGUILayout.Button(
                    UnmaText.Get(
                        "ui.instrument.condition.amount",
                        "AMOUNT"),
                    !IsPercentChangeMode(condition.TrendMode)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                SetInstrumentTrendMode(
                    index,
                    condition,
                    IsDecreaseMode(condition.TrendMode)
                        ? InstrumentTrendMode.DecreaseAbsolute
                        : InstrumentTrendMode.IncreaseAbsolute);
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get(
                        "ui.instrument.condition.percent",
                        "PERCENT"),
                    IsPercentChangeMode(condition.TrendMode)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                SetInstrumentTrendMode(
                    index,
                    condition,
                    IsDecreaseMode(condition.TrendMode)
                        ? InstrumentTrendMode.DecreasePercent
                        : InstrumentTrendMode.IncreasePercent);
            }
            NativeGUILayout.EndHorizontal();
        }

        if (condition.TrendMode != InstrumentTrendMode.None)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get(
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "ui.instrument.condition.for_time"
                        : "ui.instrument.condition.within_time",
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "FOR"
                        : "WITHIN"),
                m_smallLabelStyle,
                NativeGUILayout.Width(105f));
            if (!m_draftTrendWindowTexts.TryGetValue(
                    condition,
                    out var windowText))
            {
                windowText = condition.WindowAmount.ToString(
                    CultureInfo.CurrentCulture);
            }
            windowText = NativeGUILayout.TextField(
                windowText,
                8,
                m_textFieldStyle,
                NativeGUILayout.Width(125f),
                NativeGUILayout.Height(34f));
            m_draftTrendWindowTexts[condition] = windowText;
            NativeGUILayout.FlexibleSpace();
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            DrawCompactGameTimeUnitButton(
                condition,
                GameTimeUnit.Day,
                UnmaText.Get("ui.time.day", "DAY"));
            DrawCompactGameTimeUnitButton(
                condition,
                GameTimeUnit.Month,
                UnmaText.Get("ui.time.month", "MONTH"));
            DrawCompactGameTimeUnitButton(
                condition,
                GameTimeUnit.Year,
                UnmaText.Get("ui.time.year", "YEAR"));
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
            DrawCompactGameTimeUnitButton(
                condition,
                GameTimeUnit.Decade,
                UnmaText.Get("ui.time.decade", "10 YEARS"));
            DrawCompactGameTimeUnitButton(
                condition,
                GameTimeUnit.Century,
                UnmaText.Get("ui.time.century", "100 YEARS"));
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get(
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "ui.instrument.condition.sustain_hint"
                        : "ui.instrument.condition.window_hint",
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "The comparison must remain true for the complete game-time window."
                        : "Compared with the value at the start of the game-time window."),
                m_smallLabelStyle);
        }

        NativeGUILayout.EndVertical();
        return false;
    }

    private void DrawCompactInstrumentModeButton(
        int index,
        ConditionDefinition condition,
        string label,
        bool selected,
        InstrumentTrendMode mode)
    {
        if (NativeGUILayout.Button(
                label,
                selected ? m_primaryButtonStyle : m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(34f)))
        {
            SetInstrumentTrendMode(index, condition, mode);
        }
    }

    private void DrawCompactComparisonSelector(
        ref ComparisonOperator comparison)
    {
        foreach (ComparisonOperator candidate in Enum.GetValues(
                     typeof(ComparisonOperator)))
        {
            if (NativeGUILayout.Button(
                    UnmaRuntime.OperatorText(candidate),
                    comparison == candidate
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f)))
            {
                comparison = candidate;
            }
        }
    }

    private void DrawCompactGameTimeUnitButton(
        ConditionDefinition condition,
        GameTimeUnit unit,
        string label)
    {
        if (!NativeGUILayout.Button(
                label,
                condition.WindowUnit == unit
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(34f)))
        {
            return;
        }

        condition.WindowUnit = unit;
        condition.WindowAmount = GameTimeWindowPolicy.ClampAmount(
            condition.WindowAmount,
            unit);
        m_draftTrendWindowTexts[condition] =
            condition.WindowAmount.ToString(CultureInfo.CurrentCulture);
    }

    private void DrawCompactConditionLogicSelector()
    {
        NativeGUILayout.Label(
            UnmaText.Get("auto.956d69c9e3ca"),
            m_labelStyle);
        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.76efbe95b3a4"),
                m_draftLogic == AlarmLogic.All
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(36f)))
        {
            m_draftLogic = AlarmLogic.All;
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.556080cfb23f"),
                m_draftLogic == AlarmLogic.Any
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(36f)))
        {
            m_draftLogic = AlarmLogic.Any;
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.9a99ea646292"),
            m_smallLabelStyle);
    }

    private void RemoveDraftConditionAt(
        int index,
        ConditionDefinition condition)
    {
        m_draftTrendWindowTexts.Remove(condition);
        m_draftHysteresisTexts.Remove(condition);
        m_draftConditions.RemoveAt(index);
        m_draftConditionThresholdTexts.RemoveAt(index);
        if (m_conditionReferencePickerIndex == index)
        {
            m_conditionReferencePickerIndex = -1;
        }
        else if (m_conditionReferencePickerIndex > index)
        {
            m_conditionReferencePickerIndex--;
        }
    }

    private bool DrawInstrumentConditionRow(
        int index,
        ConditionDefinition condition)
    {
        var instrument = m_runtime.Configuration.Instruments.FirstOrDefault(
            item => string.Equals(
                item.Id,
                condition.InstrumentId,
                StringComparison.Ordinal));
        var currentText = m_runtime.TryGetInstrumentCurrentValue(
            condition.InstrumentId,
            out var currentValue)
            ? currentValue.ToString("0.###", CultureInfo.CurrentCulture)
            : "—";

        NativeGUILayout.BeginVertical(
            "instrument-condition:" + RuntimeHelpers.GetHashCode(condition)
                .ToString(CultureInfo.InvariantCulture),
            m_panelStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            currentText,
            m_headerStyle,
            NativeGUILayout.Width(78f),
            NativeGUILayout.Height(30f));
        NativeGUILayout.BeginVertical(NativeGUILayout.MinWidth(150f));
        NativeGUILayout.Label(
            UnmaText.Format(
                "ui.instrument.condition_title",
                "{0} · CALCULATED METRIC",
                instrument?.Title ?? condition.MetricLabel),
            m_labelStyle);
        NativeGUILayout.Label(
            instrument == null
                ? UnmaText.Get(
                    "ui.instrument.condition_missing",
                    "Instrument no longer exists")
                : UnmaText.Format(
                    "ui.instrument.condition_sources",
                    "{0} · {1} SOURCE(S)",
                    InstrumentAggregationLabel(instrument.Aggregation),
                    instrument.Sources.Count),
            m_smallLabelStyle);
        NativeGUILayout.EndVertical();

        if (NativeGUILayout.Button(
                UnmaText.Get("ui.instrument.condition.value", "VALUE"),
                condition.TrendMode == InstrumentTrendMode.None
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(68f),
                NativeGUILayout.Height(30f)))
        {
            SetInstrumentTrendMode(
                index,
                condition,
                InstrumentTrendMode.None);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.instrument.condition.decrease",
                    "DECREASE"),
                IsDecreaseMode(condition.TrendMode)
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(88f),
                NativeGUILayout.Height(30f)))
        {
            SetInstrumentTrendMode(
                index,
                condition,
                InstrumentTrendMode.DecreaseAbsolute);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.instrument.condition.increase",
                    "INCREASE"),
                IsIncreaseMode(condition.TrendMode)
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(82f),
                NativeGUILayout.Height(30f)))
        {
            SetInstrumentTrendMode(
                index,
                condition,
                InstrumentTrendMode.IncreaseAbsolute);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.instrument.condition.sustain",
                    "SUSTAIN"),
                condition.TrendMode ==
                InstrumentTrendMode.SustainComparison
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(78f),
                NativeGUILayout.Height(30f)))
        {
            SetInstrumentTrendMode(
                index,
                condition,
                InstrumentTrendMode.SustainComparison);
        }
        NativeGUILayout.FlexibleSpace();
        if (NativeGUILayout.Button(
                "X",
                m_dangerButtonStyle,
                NativeGUILayout.Width(34f),
                NativeGUILayout.Height(30f)))
        {
            m_draftConditions.RemoveAt(index);
            m_draftConditionThresholdTexts.RemoveAt(index);
            m_draftTrendWindowTexts.Remove(condition);
            m_draftHysteresisTexts.Remove(condition);
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.EndVertical();
            return true;
        }
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Space(78f);
        if (UsesComparisonThreshold(condition.TrendMode))
        {
            NativeGUILayout.Label(
                UnmaText.Get("ui.instrument.condition.compare", "COMPARE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(82f));
            var comparison = condition.Comparison;
            DrawComparisonSelector(ref comparison);
            condition.Comparison = comparison;
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.instrument.condition.target_value",
                    "TARGET VALUE"),
                m_smallLabelStyle,
                NativeGUILayout.Width(76f));
            m_draftConditionThresholdTexts[index] = NativeGUILayout.TextField(
                m_draftConditionThresholdTexts[index],
                24,
                m_textFieldStyle,
                NativeGUILayout.Width(110f),
                NativeGUILayout.Height(28f));
            DrawDraftHysteresisInline(condition);
        }
        else
        {
            NativeGUILayout.Label(
                IsPercentChangeMode(condition.TrendMode)
                    ? UnmaText.Get(
                        "ui.instrument.condition.minimum_percent",
                        "AT LEAST %")
                    : UnmaText.Get(
                        "ui.instrument.condition.minimum_amount",
                        "AT LEAST AMOUNT"),
                m_smallLabelStyle,
                NativeGUILayout.Width(125f));
            m_draftConditionThresholdTexts[index] = NativeGUILayout.TextField(
                m_draftConditionThresholdTexts[index],
                24,
                m_textFieldStyle,
                NativeGUILayout.Width(110f),
                NativeGUILayout.Height(28f));
            if (NativeGUILayout.Button(
                    UnmaText.Get(
                        "ui.instrument.condition.amount",
                        "AMOUNT"),
                    !IsPercentChangeMode(condition.TrendMode)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Width(82f),
                    NativeGUILayout.Height(28f)))
            {
                SetInstrumentTrendMode(
                    index,
                    condition,
                    IsDecreaseMode(condition.TrendMode)
                        ? InstrumentTrendMode.DecreaseAbsolute
                        : InstrumentTrendMode.IncreaseAbsolute);
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get(
                        "ui.instrument.condition.percent",
                        "PERCENT"),
                    IsPercentChangeMode(condition.TrendMode)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Width(86f),
                    NativeGUILayout.Height(28f)))
            {
                SetInstrumentTrendMode(
                    index,
                    condition,
                    IsDecreaseMode(condition.TrendMode)
                        ? InstrumentTrendMode.DecreasePercent
                        : InstrumentTrendMode.IncreasePercent);
            }
        }
        NativeGUILayout.EndHorizontal();

        if (condition.TrendMode != InstrumentTrendMode.None)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Space(78f);
            NativeGUILayout.Label(
                UnmaText.Get(
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "ui.instrument.condition.for_time"
                        : "ui.instrument.condition.within_time",
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "FOR"
                        : "WITHIN"),
                m_smallLabelStyle,
                NativeGUILayout.Width(80f));
            if (!m_draftTrendWindowTexts.TryGetValue(
                    condition,
                    out var windowText))
            {
                windowText = condition.WindowAmount.ToString(
                    CultureInfo.CurrentCulture);
            }
            windowText = NativeGUILayout.TextField(
                windowText,
                8,
                m_textFieldStyle,
                NativeGUILayout.Width(72f),
                NativeGUILayout.Height(28f));
            m_draftTrendWindowTexts[condition] = windowText;
            DrawGameTimeUnitSelector(condition);
            NativeGUILayout.Label(
                UnmaText.Get(
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "ui.instrument.condition.sustain_hint"
                        : "ui.instrument.condition.window_hint",
                    condition.TrendMode ==
                    InstrumentTrendMode.SustainComparison
                        ? "The comparison must remain true for the complete game-time window."
                        : "Compared with the value at the start of the game-time window."),
                m_smallLabelStyle);
            NativeGUILayout.EndHorizontal();
        }
        NativeGUILayout.EndVertical();
        return false;
    }

    private void EnsureDraftHysteresisText(ConditionDefinition condition)
    {
        if (condition == null || m_draftHysteresisTexts.ContainsKey(condition))
        {
            return;
        }
        m_draftHysteresisTexts[condition] = condition.Hysteresis.ToString(
            "R",
            CultureInfo.CurrentCulture);
    }

    private void DrawDraftHysteresisRow(
        ConditionDefinition condition,
        float leadingSpace)
    {
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Space(leadingSpace);
        DrawDraftHysteresisInline(condition);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.timing.hysteresis_hint",
                "0 disables the dead band around the threshold."),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();
    }

    private void DrawDraftHysteresisInline(
        ConditionDefinition condition,
        float fieldHeight = 28f)
    {
        EnsureDraftHysteresisText(condition);
        NativeGUILayout.Label(
            UnmaText.Get("ui.timing.hysteresis", "HYSTERESIS"),
            m_smallLabelStyle,
            NativeGUILayout.Width(88f));
        m_draftHysteresisTexts[condition] = NativeGUILayout.TextField(
            m_draftHysteresisTexts[condition],
            24,
            m_textFieldStyle,
            NativeGUILayout.Width(82f),
            NativeGUILayout.Height(fieldHeight));
    }

    private void SetInstrumentTrendMode(
        int index,
        ConditionDefinition condition,
        InstrumentTrendMode mode)
    {
        if (condition.TrendMode == mode)
        {
            return;
        }

        // Preserve the unfinished text of the mode being left. Otherwise a
        // Mode round-trips must not silently restore the last saved
        // model values and discards what the player just typed.
        if (index >= 0 && index < m_draftConditionThresholdTexts.Count &&
            TryParseDouble(
                m_draftConditionThresholdTexts[index],
                out var currentThreshold))
        {
            if (UsesComparisonThreshold(condition.TrendMode))
            {
                condition.Threshold = currentThreshold;
            }
            else
            {
                condition.DeltaThreshold = currentThreshold;
            }
        }
        if (condition.TrendMode != InstrumentTrendMode.None &&
            m_draftTrendWindowTexts.TryGetValue(
                condition,
                out var currentWindowText) &&
            int.TryParse(
                currentWindowText,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var currentWindowAmount) &&
            currentWindowAmount > 0)
        {
            condition.WindowAmount = GameTimeWindowPolicy.ClampAmount(
                currentWindowAmount,
                condition.WindowUnit);
        }

        condition.TrendMode = mode;
        m_draftConditionThresholdTexts[index] =
            (UsesComparisonThreshold(mode)
                ? condition.Threshold
                : condition.DeltaThreshold).ToString(
                "R",
                CultureInfo.CurrentCulture);
        m_draftTrendWindowTexts[condition] =
            condition.WindowAmount.ToString(CultureInfo.CurrentCulture);
    }

    private void DrawGameTimeUnitSelector(ConditionDefinition condition)
    {
        DrawGameTimeUnitButton(
            condition,
            GameTimeUnit.Day,
            UnmaText.Get("ui.time.day", "DAY"));
        DrawGameTimeUnitButton(
            condition,
            GameTimeUnit.Month,
            UnmaText.Get("ui.time.month", "MONTH"));
        DrawGameTimeUnitButton(
            condition,
            GameTimeUnit.Year,
            UnmaText.Get("ui.time.year", "YEAR"));
        DrawGameTimeUnitButton(
            condition,
            GameTimeUnit.Decade,
            UnmaText.Get("ui.time.decade", "10 YEARS"));
        DrawGameTimeUnitButton(
            condition,
            GameTimeUnit.Century,
            UnmaText.Get("ui.time.century", "100 YEARS"));
    }

    private void DrawGameTimeUnitButton(
        ConditionDefinition condition,
        GameTimeUnit unit,
        string label)
    {
        if (NativeGUILayout.Button(
                label,
                condition.WindowUnit == unit
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                NativeGUILayout.Width(unit >= GameTimeUnit.Decade ? 90f : 76f),
                NativeGUILayout.Height(28f)))
        {
            condition.WindowUnit = unit;
            condition.WindowAmount = GameTimeWindowPolicy.ClampAmount(
                condition.WindowAmount,
                unit);
            m_draftTrendWindowTexts[condition] =
                condition.WindowAmount.ToString(
                    CultureInfo.CurrentCulture);
        }
    }

    private static bool UsesComparisonThreshold(InstrumentTrendMode mode)
    {
        return mode == InstrumentTrendMode.None ||
               mode == InstrumentTrendMode.SustainComparison;
    }

    private static bool IsDecreaseMode(InstrumentTrendMode mode)
    {
        return mode == InstrumentTrendMode.DecreaseAbsolute ||
               mode == InstrumentTrendMode.DecreasePercent;
    }

    private static bool IsIncreaseMode(InstrumentTrendMode mode)
    {
        return mode == InstrumentTrendMode.IncreaseAbsolute ||
               mode == InstrumentTrendMode.IncreasePercent;
    }

    private static bool IsPercentChangeMode(InstrumentTrendMode mode)
    {
        return mode == InstrumentTrendMode.DecreasePercent ||
               mode == InstrumentTrendMode.IncreasePercent;
    }

    private void DrawConditionReferencePicker(ConditionDefinition condition)
    {
        var compact = IsCompactRuleEditor();
        if (m_selectedEntity == null ||
            m_selectedEntity.EntityId != condition.EntityId)
        {
            if (compact)
            {
                NativeGUILayout.BeginVertical();
            }
            else
            {
                NativeGUILayout.BeginHorizontal();
                NativeGUILayout.Space(135f);
            }
            NativeGUILayout.Label(
                UnmaText.Get("auto.af0f45a59557"),
                m_smallLabelStyle);
            var inspect = compact
                ? NativeGUILayout.Button(
                    UnmaText.Get("auto.c29601081242"),
                    m_buttonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(34f))
                : NativeGUILayout.Button(
                    UnmaText.Get("auto.c29601081242"),
                    m_buttonStyle,
                    NativeGUILayout.Width(190f));
            if (inspect)
            {
                RequestEntityInspection(condition.EntityId, false);
            }
            if (compact)
            {
                NativeGUILayout.EndVertical();
            }
            else
            {
                NativeGUILayout.EndHorizontal();
            }
            return;
        }

        if (compact)
        {
            NativeGUILayout.BeginVertical();
            NativeGUILayout.Label(
                UnmaText.Get("auto.bb45057d02f0"),
                m_smallLabelStyle);
        }
        else
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Space(135f);
            NativeGUILayout.Label(
                UnmaText.Get("auto.bb45057d02f0"),
                m_smallLabelStyle,
                NativeGUILayout.Width(90f));
        }
        m_referenceMetricPickerFilter = compact
            ? NativeGUILayout.TextField(
                m_referenceMetricPickerFilter,
                60,
                m_textFieldStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(34f))
            : NativeGUILayout.TextField(
                m_referenceMetricPickerFilter,
                60,
                m_textFieldStyle,
                NativeGUILayout.Width(280f));
        NativeGUILayout.Label(
            UnmaText.Get("auto.d47099108ed4"),
            m_smallLabelStyle);
        if (compact)
        {
            NativeGUILayout.EndVertical();
        }
        else
        {
            NativeGUILayout.EndHorizontal();
        }

        m_referenceMetricPickerScroll = NativeGUILayout.BeginScrollView(
            m_referenceMetricPickerScroll,
            NativeGUILayout.Height(170f));
        foreach (var metric in m_selectedMetrics)
        {
            if (string.Equals(
                    condition.MetricPath,
                    metric.Path,
                    StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(m_referenceMetricPickerFilter) &&
                metric.Label.IndexOf(
                    m_referenceMetricPickerFilter,
                    StringComparison.CurrentCultureIgnoreCase) < 0 &&
                metric.Path.IndexOf(
                    m_referenceMetricPickerFilter,
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.64762227fbd5") + metric.Label + UnmaText.Get("auto.f583d8b1f88d") +
                    FormatMetricValue(metric),
                    string.Equals(
                        condition.ReferenceMetricPath,
                        metric.Path,
                        StringComparison.Ordinal)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    NativeGUILayout.Height(compact ? 34f : 26f)))
            {
                condition.ReferenceMetricPath = metric.Path;
                condition.ReferenceMetricLabel = metric.Label;
                m_conditionReferencePickerIndex = -1;
            }
        }
        NativeGUILayout.EndScrollView();
    }

    private void DrawAlarmProperties()
    {
        var compact = IsCompactRuleEditor();
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.appearance_and_sound",
                "APPEARANCE & SOUND"),
            m_sectionStyle);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.editor.severity", "Severity"),
            m_labelStyle,
            NativeGUILayout.Width(105f));
        foreach (AlarmSeverity severity in Enum.GetValues(typeof(AlarmSeverity)))
        {
            if (NativeGUILayout.Button(
                    SeverityLabel(severity),
                    m_draftSeverity == severity
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    compact
                        ? NativeGUILayout.ExpandWidth(true)
                        : NativeGUILayout.Width(125f)))
            {
                m_draftSeverity = severity;
                m_draftColor = DefaultColorFor(severity);
                EnsureDraftEscalationTarget();
            }
        }
        if (!compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.active_color", "Active color"),
                m_labelStyle,
                NativeGUILayout.Width(85f));
            m_draftColor = NativeGUILayout.TextField(
                m_draftColor,
                9,
                m_textFieldStyle,
                NativeGUILayout.Width(95f));
        }
        NativeGUILayout.EndHorizontal();
        if (compact)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.active_color", "Active color"),
                m_labelStyle,
                NativeGUILayout.Width(105f));
            m_draftColor = NativeGUILayout.TextField(
                m_draftColor,
                9,
                m_textFieldStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(30f));
            NativeGUILayout.EndHorizontal();
        }

        var sounds = m_audio.GetSoundOptions();
        if (sounds.Count > 0)
        {
            m_draftSoundIndex = Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1));
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("ui.editor.sound", "Sound"),
                m_labelStyle,
                NativeGUILayout.Width(105f));
            if (NativeGUILayout.Button("<", m_buttonStyle, NativeGUILayout.Width(38f)))
            {
                m_draftSoundIndex = Wrap(m_draftSoundIndex - 1, sounds.Count);
                m_draftSoundChanged = true;
            }
            var originalSoundMissing =
                !string.IsNullOrWhiteSpace(m_editingRuleId) &&
                !m_draftSoundChanged &&
                !sounds.Any(sound => string.Equals(
                    sound.Id,
                    m_originalDraftSoundId,
                    StringComparison.OrdinalIgnoreCase));
            NativeGUILayout.Label(
                originalSoundMissing
                    ? UnmaText.Get("auto.40bffd508dbf") + m_originalDraftSoundId
                    : sounds[m_draftSoundIndex].Label,
                m_labelStyle,
                compact
                    ? NativeGUILayout.ExpandWidth(true)
                    : NativeGUILayout.Width(310f));
            if (NativeGUILayout.Button(">", m_buttonStyle, NativeGUILayout.Width(38f)))
            {
                m_draftSoundIndex = Wrap(m_draftSoundIndex + 1, sounds.Count);
                m_draftSoundChanged = true;
            }
            if (compact)
            {
                NativeGUILayout.EndHorizontal();
                NativeGUILayout.BeginHorizontal();
                NativeGUILayout.Space(105f);
            }
            NativeGUI.enabled = !originalSoundMissing;
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.775da082f4c5"),
                    m_buttonStyle,
                    compact
                        ? NativeGUILayout.ExpandWidth(true)
                        : NativeGUILayout.Width(125f)))
            {
                TestSound(sounds[m_draftSoundIndex].Id, m_draftSeverity);
            }
            NativeGUI.enabled = true;
            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.ae84ac2ff8ca"),
                    m_buttonStyle,
                    compact
                        ? NativeGUILayout.ExpandWidth(true)
                        : NativeGUILayout.Width(105f)))
            {
                StopTestSound();
            }
            NativeGUILayout.EndHorizontal();
        }

        if (!compact)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Space(105f);
        }
        m_draftAutoAcknowledgeOnClear = NativeGUILayout.Toggle(
            m_draftAutoAcknowledgeOnClear,
            UnmaText.Get("auto.19a7e6f7335e"),
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(340f));
        NativeGUILayout.Label(
            UnmaText.Get("auto.f8daf4186ab9"),
            m_smallLabelStyle);
        if (!compact)
        {
            NativeGUILayout.EndHorizontal();
        }

        DrawAlarmAdvancedSection(sounds);
    }

    private void DrawAlarmAdvancedSection(
        IReadOnlyList<SoundOption> sounds)
    {
        var compact = IsCompactRuleEditor(760f);
        var configured = HasAdvancedAlarmSettings();
        var invalid = HasAdvancedAlarmValidationError();
        var title = UnmaText.Get(
            "ui.editor.advanced.title",
            "ADVANCED SETTINGS");
        var scope = UnmaText.Get(
            "ui.editor.advanced.scope",
            "Timing and escalation");
        var summary = invalid
            ? UnmaText.Get(
                "ui.editor.advanced.needs_attention",
                "Needs attention")
            : configured
                ? UnmaText.Get(
                    "ui.editor.advanced.configured",
                    "Configured")
                : UnmaText.Get(
                    "ui.editor.advanced.defaults",
                    "Defaults");
        var tooltip = m_ruleAdvancedOpen
            ? UnmaText.Get(
                "ui.editor.advanced.collapse",
                "Collapse timing and escalation settings")
            : UnmaText.Get(
                "ui.editor.advanced.expand",
                "Expand timing and escalation settings");
        var buttonText = (invalid ? "!  " : "") +
                         (m_ruleAdvancedOpen ? "[-] " : "[+] ") +
                         title +
                         (compact ? "" : "  ·  " + scope + "  ·  " + summary);
        var headerStyle = invalid
            ? m_dangerButtonStyle
            : m_ruleAdvancedOpen
                ? m_primaryButtonStyle
                : m_buttonStyle;

        NativeGUILayout.Space(10f);
        if (NativeGUILayout.Button(
                new GUIContent(buttonText, tooltip),
                headerStyle,
                new NativeControlMetadata(
                    "alarm-advanced-toggle",
                    tooltip,
                    focusable: true),
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(40f)))
        {
            m_ruleAdvancedOpen = !m_ruleAdvancedOpen;
        }
        if (compact)
        {
            NativeGUILayout.Label(
                scope + "  ·  " + summary,
                m_smallLabelStyle);
        }
        if (!m_ruleAdvancedOpen)
        {
            return;
        }

        NativeGUILayout.BeginVertical(m_panelStyle);
        DrawAlarmTimingDraft();
        DrawAlarmEscalationDraft(sounds);
        NativeGUILayout.EndVertical();
    }

    private bool HasAdvancedAlarmSettings() =>
        TimingDraftHasInput(m_draftActivationDelay) ||
        TimingDraftHasInput(m_draftResetDelay) ||
        TimingDraftHasInput(m_draftMinimumActive) ||
        m_draftEscalationEnabled ||
        TimingDraftHasInput(m_draftEscalationAfter) ||
        !string.IsNullOrWhiteSpace(m_draftEscalationSoundId) ||
        m_draftEscalationOperatorAction != AlarmOperatorAction.None;

    private bool HasAdvancedAlarmValidationError()
    {
        if (!TryGetTimingTicks(m_draftActivationDelay, out _) ||
            !TryGetTimingTicks(m_draftResetDelay, out _) ||
            !TryGetTimingTicks(m_draftMinimumActive, out _))
        {
            return true;
        }
        if (!m_draftEscalationEnabled)
        {
            return false;
        }
        return !TryGetTimingTicks(
                   m_draftEscalationAfter,
                   out var escalationAfter) ||
               escalationAfter <= 0 ||
               m_draftSeverity >= AlarmSeverity.Emergency ||
               m_draftEscalationSeverity <= m_draftSeverity;
    }

    private void DrawRuleEditorActions(IReadOnlyList<SoundOption> sounds)
    {
        var compact = IsCompactRuleEditor(760f);
        var extremeCompact =
            m_entityAlarmWindowRect.height / Math.Max(0.75f, UiScale) < 600f;
        NativeGUILayout.Space(6f);
        NativeGUILayout.BeginVertical(m_panelStyle);
        var validationMessage = GetRuleDraftValidationMessage();
        var ready = string.IsNullOrEmpty(validationMessage);
        if (extremeCompact)
        {
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                ready
                    ? UnmaText.Get(
                        "ui.editor.footer.ready",
                        "READY TO SAVE")
                    : UnmaText.Get(
                        "ui.editor.footer.not_ready",
                        "NOT READY") + " · " + validationMessage,
                ready ? m_smallLabelStyle : m_statusErrorStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(30f));
            NativeGUI.enabled = ready;
            if (NativeGUILayout.Button(
                    new GUIContent(
                        UnmaText.Get("ui.common.save", "SAVE"),
                        m_draftEnabled
                            ? UnmaText.Get(
                                "ui.editor.save_and_activate",
                                "SAVE & ACTIVATE")
                            : UnmaText.Get(
                                "ui.editor.save_inactive",
                                "SAVE INACTIVE")),
                    m_primaryButtonStyle,
                    new NativeControlMetadata(
                        "alarm-editor-save-compact",
                        m_draftEnabled
                            ? UnmaText.Get(
                                "ui.editor.save_and_activate",
                                "SAVE & ACTIVATE")
                            : UnmaText.Get(
                                "ui.editor.save_inactive",
                                "SAVE INACTIVE")),
                    NativeGUILayout.Width(84f),
                    NativeGUILayout.Height(36f)))
            {
                SaveDraftRule(sounds);
            }
            NativeGUI.enabled = true;
            if (NativeGUILayout.Button(
                    new GUIContent(
                        UnmaText.Get("ui.common.discard", "DISCARD"),
                        UnmaText.Get(
                            "ui.editor.discard_changes",
                            "DISCARD CHANGES")),
                    m_buttonStyle,
                    new NativeControlMetadata(
                        "alarm-editor-discard-compact",
                        UnmaText.Get(
                            "ui.editor.discard_changes",
                            "DISCARD CHANGES")),
                    NativeGUILayout.Width(104f),
                    NativeGUILayout.Height(36f)))
            {
                ResetDraftRule();
                SetStatus(UnmaText.Get("auto.8df90cb55cac"));
            }
            if (!string.IsNullOrWhiteSpace(m_editingRuleId))
            {
                var confirmingDelete = string.Equals(
                        m_pendingRuleDeleteId,
                        m_editingRuleId,
                        StringComparison.Ordinal) &&
                    Time.realtimeSinceStartup <= m_pendingRuleDeleteUntil;
                var deleteLabel = confirmingDelete
                    ? UnmaText.Get(
                        "ui.editor.delete_alarm_confirm",
                        "AGAIN: DELETE ALARM")
                    : UnmaText.Get(
                        "ui.editor.delete_alarm",
                        "DELETE ALARM");
                if (NativeGUILayout.Button(
                        new GUIContent("×", deleteLabel),
                        m_dangerButtonStyle,
                        new NativeControlMetadata(
                            "alarm-editor-delete-compact",
                            deleteLabel),
                        NativeGUILayout.Width(40f),
                        NativeGUILayout.Height(36f)))
                {
                    DeleteEditedRule(confirmingDelete);
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.EndVertical();
            return;
        }
        if (!compact)
        {
            NativeGUILayout.BeginHorizontal();
        }
        NativeGUILayout.Label(
            ready
                ? UnmaText.Get(
                    "ui.editor.footer.ready",
                    "READY TO SAVE")
                : UnmaText.Get(
                    "ui.editor.footer.not_ready",
                    "NOT READY") + " · " + validationMessage,
            ready ? m_smallLabelStyle : m_statusErrorStyle,
            NativeGUILayout.Height(30f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.editor.keyboard_shortcut_hint",
                "Ctrl+Enter saves · Esc closes"),
            m_smallLabelStyle,
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(230f),
            NativeGUILayout.Height(30f));
        if (!compact)
        {
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.BeginHorizontal();
        }
        else
        {
            NativeGUILayout.BeginVertical();
        }
        NativeGUI.enabled = ready;
        if (NativeGUILayout.Button(
                m_draftEnabled
                    ? UnmaText.Get(
                        "ui.editor.save_and_activate",
                        "SAVE & ACTIVATE")
                    : UnmaText.Get(
                        "ui.editor.save_inactive",
                        "SAVE INACTIVE"),
                m_primaryButtonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(40f)))
        {
            SaveDraftRule(sounds);
        }
        NativeGUI.enabled = true;
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "ui.editor.discard_changes",
                    "DISCARD CHANGES"),
                m_buttonStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.Height(40f)))
        {
            ResetDraftRule();
            SetStatus(UnmaText.Get("auto.8df90cb55cac"));
        }
        if (!string.IsNullOrWhiteSpace(m_editingRuleId))
        {
            var confirmingDelete = string.Equals(
                    m_pendingRuleDeleteId,
                    m_editingRuleId,
                    StringComparison.Ordinal) &&
                Time.realtimeSinceStartup <= m_pendingRuleDeleteUntil;
            if (NativeGUILayout.Button(
                    confirmingDelete
                        ? UnmaText.Get(
                            "ui.editor.delete_alarm_confirm",
                            "AGAIN: DELETE ALARM")
                        : UnmaText.Get(
                            "ui.editor.delete_alarm",
                            "DELETE ALARM"),
                    m_dangerButtonStyle,
                    NativeGUILayout.ExpandWidth(true),
                    NativeGUILayout.Height(40f)))
            {
                DeleteEditedRule(confirmingDelete);
            }
        }
        if (!compact)
        {
            NativeGUILayout.EndHorizontal();
        }
        else
        {
            NativeGUILayout.EndVertical();
        }
        NativeGUILayout.EndVertical();
    }

    private string GetRuleDraftValidationMessage()
    {
        if (string.IsNullOrWhiteSpace(m_draftRuleName))
        {
            return UnmaText.Get(
                "ui.editor.validation.title_required",
                "Enter a message title.");
        }
        if (GetDraftTargetPanel() == null)
        {
            return UnmaText.Get(
                "ui.editor.validation.panel_required",
                "Choose a target panel.");
        }
        if (m_draftConditions.Count == 0)
        {
            return UnmaText.Get(
                "ui.editor.validation.conditions_required",
                "Add at least one condition.");
        }
        if (!AlarmUiErgonomics.IsValidHtmlColor(m_draftColor))
        {
            return UnmaText.Get(
                "ui.editor.validation.color_invalid",
                "Enter a color in #RRGGBB format.");
        }
        foreach (var condition in m_draftConditions)
        {
            EnsureDraftHysteresisText(condition);
        }
        if (m_draftEscalationEnabled &&
            (!TryGetTimingTicks(
                 m_draftEscalationAfter,
                 out var escalation) ||
             escalation <= 0))
        {
            return UnmaText.Get(
                "ui.editor.validation.timing_invalid",
                "Check the timing values.");
        }
        if (!TryGetTimingTicks(m_draftActivationDelay, out _) ||
            !TryGetTimingTicks(m_draftResetDelay, out _) ||
            !TryGetTimingTicks(m_draftMinimumActive, out _))
        {
            return UnmaText.Get(
                "ui.editor.validation.timing_invalid",
                "Check the timing values.");
        }
        if (m_draftEscalationEnabled &&
            (m_draftSeverity >= AlarmSeverity.Emergency ||
             m_draftEscalationSeverity <= m_draftSeverity))
        {
            return UnmaText.Get(
                "ui.escalation.invalid_severity",
                "Escalation severity must be strictly higher than the base severity.");
        }
        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            var condition = m_draftConditions[index];
            if (index >= m_draftConditionThresholdTexts.Count ||
                !TryParseDouble(m_draftConditionThresholdTexts[index], out _) ||
                !m_draftHysteresisTexts.TryGetValue(
                    condition,
                    out var hysteresisText) ||
                !TryParseDouble(hysteresisText, out var hysteresis) ||
                hysteresis < 0d)
            {
                return UnmaText.Get(
                    "ui.editor.validation.conditions_required",
                    "Complete every condition.");
            }
            if (!string.IsNullOrWhiteSpace(condition.InstrumentId))
            {
                if (!m_runtime.Configuration.Instruments.Any(instrument =>
                        instrument != null && string.Equals(
                            instrument.Id,
                            condition.InstrumentId,
                            StringComparison.Ordinal)))
                {
                    return UnmaText.Format(
                        "ui.instrument.status.condition_missing",
                        "Condition {0}: The associated instrument no longer exists.",
                        index + 1);
                }
                if (condition.TrendMode != InstrumentTrendMode.None &&
                    (!UsesComparisonThreshold(condition.TrendMode) &&
                     (!TryParseDouble(
                          m_draftConditionThresholdTexts[index],
                          out var trendThreshold) ||
                      trendThreshold < 0d) ||
                     !m_draftTrendWindowTexts.TryGetValue(
                         condition,
                         out var windowText) ||
                     !int.TryParse(
                         windowText,
                         NumberStyles.Integer,
                         CultureInfo.CurrentCulture,
                         out var windowAmount) ||
                     windowAmount < 1))
                {
                    return UnmaText.Format(
                        "ui.instrument.status.invalid_time_condition",
                        "Time condition {0}: Enter a valid amount and game-time range.",
                        index + 1);
                }
                continue;
            }
            if (condition.ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.IsNullOrWhiteSpace(condition.ReferenceMetricPath))
            {
                return UnmaText.Get("auto.21ca7079c12b") + (index + 1) +
                       UnmaText.Get("auto.115b04808134");
            }
            if (condition.ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.Equals(
                    condition.MetricPath,
                    condition.ReferenceMetricPath,
                    StringComparison.Ordinal))
            {
                return UnmaText.Get("auto.53c26ac33af4") + (index + 1) +
                       UnmaText.Get("auto.be2c20cf5599");
            }
        }
        return "";
    }

    private void DrawAlarmTimingDraft()
    {
        var compact = IsCompactRuleEditor(760f);
        NativeGUILayout.Space(8f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.timing.title", "ALARM TIMING"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.timing.hint",
                "All durations use the COI game calendar. Zero activates and resets immediately; zero minimum time is disabled."),
            m_smallLabelStyle);
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.activation", "ACTIVATION DELAY"),
            m_draftActivationDelay,
            UnmaText.Get("ui.timing.zero.instant", "INSTANT"),
            compact);
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.reset", "RESET DELAY"),
            m_draftResetDelay,
            UnmaText.Get("ui.timing.zero.instant", "INSTANT"),
            compact);
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.minimum_active", "MINIMUM ACTIVE"),
            m_draftMinimumActive,
            UnmaText.Get("ui.timing.zero.off", "OFF"),
            compact);
    }

    private void DrawAlarmEscalationDraft(
        IReadOnlyList<SoundOption> sounds)
    {
        var compact = IsCompactRuleEditor(760f);
        var controlHeight = compact ? 34f : 28f;
        NativeGUILayout.Space(8f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.escalation.title", "ESCALATION"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.escalation.hint",
                "Escalation raises severity and can change the sound when an alarm remains active for the configured game time."),
            m_smallLabelStyle);

        var canEscalate = m_draftSeverity < AlarmSeverity.Emergency;
        NativeGUI.enabled = canEscalate;
        m_draftEscalationEnabled = NativeGUILayout.Toggle(
            m_draftEscalationEnabled,
            UnmaText.Get("ui.escalation.enabled", "ENABLE ESCALATION"),
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(250f),
            NativeGUILayout.Height(controlHeight));
        NativeGUI.enabled = true;
        if (!canEscalate)
        {
            m_draftEscalationEnabled = false;
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.unavailable_emergency",
                    "Emergency is already the highest severity."),
                m_smallLabelStyle);
            return;
        }
        if (!m_draftEscalationEnabled)
        {
            return;
        }

        EnsureDraftEscalationTarget();
        DrawTimingDraftValue(
            UnmaText.Get("ui.escalation.after", "AFTER"),
            m_draftEscalationAfter,
            UnmaText.Get("ui.escalation.required", "REQUIRED"),
            compact);

        if (compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.target_severity",
                    "TARGET SEVERITY"),
                m_smallLabelStyle);
        }
        NativeGUILayout.BeginHorizontal();
        if (!compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.target_severity",
                    "TARGET SEVERITY"),
                m_smallLabelStyle,
                NativeGUILayout.Width(190f));
        }
        foreach (AlarmSeverity severity in Enum.GetValues(
                     typeof(AlarmSeverity)))
        {
            if (severity <= m_draftSeverity)
            {
                continue;
            }
            if (NativeGUILayout.Button(
                    SeverityLabel(severity),
                    m_draftEscalationSeverity == severity
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    compact
                        ? NativeGUILayout.ExpandWidth(true)
                        : NativeGUILayout.Width(125f),
                    NativeGUILayout.Height(controlHeight)))
            {
                m_draftEscalationSeverity = severity;
            }
        }
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();

        if (compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.sound",
                    "ESCALATION SOUND"),
                m_smallLabelStyle);
        }
        NativeGUILayout.BeginHorizontal();
        if (!compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get("ui.escalation.sound", "ESCALATION SOUND"),
                m_smallLabelStyle,
                NativeGUILayout.Width(190f));
        }
        if (NativeGUILayout.Button(
                "<",
                m_buttonStyle,
                NativeGUILayout.Width(38f),
                NativeGUILayout.Height(controlHeight)))
        {
            CycleDraftEscalationSound(sounds, -1);
        }
        NativeGUILayout.Label(
            EscalationSoundLabel(sounds, m_draftEscalationSoundId),
            m_labelStyle,
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(260f));
        if (NativeGUILayout.Button(
                ">",
                m_buttonStyle,
                NativeGUILayout.Width(38f),
                NativeGUILayout.Height(controlHeight)))
        {
            CycleDraftEscalationSound(sounds, 1);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.test", "TEST"),
                m_buttonStyle,
                NativeGUILayout.Width(65f),
                NativeGUILayout.Height(controlHeight)))
        {
            TestSound(
                ResolveDraftEscalationTestSound(sounds),
                m_draftEscalationSeverity);
        }
        NativeGUILayout.EndHorizontal();

        if (compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.operator_action",
                    "OPERATOR ACTION"),
                m_smallLabelStyle);
        }
        NativeGUILayout.BeginHorizontal();
        if (!compact)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "ui.escalation.operator_action",
                    "OPERATOR ACTION"),
                m_smallLabelStyle,
                NativeGUILayout.Width(190f));
        }
        if (NativeGUILayout.Button(
                OperatorActionLabel(m_draftEscalationOperatorAction),
                m_buttonStyle,
                compact
                    ? NativeGUILayout.ExpandWidth(true)
                    : NativeGUILayout.Width(330f),
                NativeGUILayout.Height(controlHeight)))
        {
            m_draftEscalationOperatorAction = NextEnum(
                m_draftEscalationOperatorAction);
        }
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.escalation.operator_action_hint",
                "Operator actions only open the matching UNMA panel. The mute option ends only the temporary five-minute mute."),
            m_smallLabelStyle);
    }

    private void EnsureDraftEscalationTarget()
    {
        if (m_draftSeverity >= AlarmSeverity.Emergency)
        {
            m_draftEscalationEnabled = false;
            m_draftEscalationSeverity = AlarmSeverity.Emergency;
            return;
        }
        if (m_draftEscalationSeverity <= m_draftSeverity)
        {
            m_draftEscalationSeverity =
                (AlarmSeverity)((int)m_draftSeverity + 1);
        }
    }

    private void CycleDraftEscalationSound(
        IReadOnlyList<SoundOption> sounds,
        int direction)
    {
        var ids = new List<string> { "" };
        if (sounds != null)
        {
            foreach (var sound in sounds)
            {
                if (sound != null && !ids.Any(id => string.Equals(
                        id,
                        sound.Id,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    ids.Add(sound.Id);
                }
            }
        }
        var current = ids.FindIndex(id => string.Equals(
            id,
            m_draftEscalationSoundId,
            StringComparison.OrdinalIgnoreCase));
        m_draftEscalationSoundId = ids[Wrap(
            Math.Max(0, current) + direction,
            ids.Count)];
    }

    private static string EscalationSoundLabel(
        IReadOnlyList<SoundOption> sounds,
        string soundId)
    {
        if (string.IsNullOrEmpty(soundId))
        {
            return UnmaText.Get(
                "ui.escalation.sound_inherit",
                "INHERIT BASE SOUND");
        }
        var sound = sounds?.FirstOrDefault(option =>
            option != null && string.Equals(
                option.Id,
                soundId,
                StringComparison.OrdinalIgnoreCase));
        return sound?.Label ??
               UnmaText.Get("auto.40bffd508dbf") + soundId;
    }

    private string ResolveDraftEscalationTestSound(
        IReadOnlyList<SoundOption> sounds)
    {
        if (!string.IsNullOrEmpty(m_draftEscalationSoundId))
        {
            return m_draftEscalationSoundId;
        }
        if (!string.IsNullOrWhiteSpace(m_editingRuleId) &&
            !m_draftSoundChanged)
        {
            return m_originalDraftSoundId;
        }
        return sounds != null && sounds.Count > 0
            ? sounds[Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1))].Id
            : "auto";
    }

    private static string OperatorActionLabel(AlarmOperatorAction action)
    {
        return action switch
        {
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute =>
                UnmaText.Get(
                    "ui.operator_action.open_panel_cancel_mute",
                    "OPEN PANEL + END 5-MIN MUTE"),
            AlarmOperatorAction.OpenPanel => UnmaText.Get(
                "ui.operator_action.open_panel",
                "OPEN PANEL"),
            _ => UnmaText.Get("ui.operator_action.none", "NONE"),
        };
    }

    private void DrawTimingDraftValue(
        string label,
        TimingDraftValue draft,
        string zeroLabel,
        bool compact = false)
    {
        if (compact)
        {
            NativeGUILayout.BeginVertical();
            NativeGUILayout.Label(label, m_smallLabelStyle);
        }
        NativeGUILayout.BeginHorizontal();
        if (!compact)
        {
            NativeGUILayout.Label(
                label,
                m_smallLabelStyle,
                NativeGUILayout.Width(190f));
        }
        draft.AmountText = NativeGUILayout.TextField(
            draft.AmountText ?? "0",
            9,
            m_textFieldStyle,
            compact
                ? NativeGUILayout.ExpandWidth(true)
                : NativeGUILayout.Width(72f),
            NativeGUILayout.Height(compact ? 34f : 28f));
        if (NativeGUILayout.Button(
                TimingUnitLabel(draft.Unit),
                m_buttonStyle,
                NativeGUILayout.Width(105f),
                NativeGUILayout.Height(compact ? 34f : 28f)))
        {
            draft.Unit = NextTimingDisplayUnit(draft.Unit);
        }
        NativeGUILayout.Label(
            TimingDraftIsZero(draft) ? zeroLabel : "",
            m_smallLabelStyle,
            NativeGUILayout.Width(compact ? 85f : 105f));
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();
        if (compact)
        {
            NativeGUILayout.EndVertical();
        }
    }

    private static TimingDisplayUnit NextTimingDisplayUnit(
        TimingDisplayUnit unit)
    {
        return unit == TimingDisplayUnit.Century
            ? TimingDisplayUnit.Tick
            : (TimingDisplayUnit)((int)unit + 1);
    }

    private static bool TimingDraftIsZero(TimingDraftValue draft)
    {
        return draft != null && int.TryParse(
            draft.AmountText,
            NumberStyles.Integer,
            CultureInfo.CurrentCulture,
            out var amount) && amount == 0;
    }

    private static bool TimingDraftHasInput(TimingDraftValue draft)
    {
        return draft != null && !string.Equals(
            draft.AmountText?.Trim(),
            "0",
            StringComparison.Ordinal);
    }

    private static bool TryGetTimingTicks(
        TimingDraftValue draft,
        out int ticks)
    {
        ticks = 0;
        if (draft == null ||
            !int.TryParse(
                draft.AmountText,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var amount) ||
            amount < 0)
        {
            return false;
        }
        var total = (long)amount * TimingTicksPerUnit(draft.Unit);
        if (total > AlarmTimingPolicy.MaximumTimingTicks)
        {
            return false;
        }
        ticks = (int)total;
        return true;
    }

    private static void LoadTimingDraft(TimingDraftValue draft, int ticks)
    {
        ticks = Math.Max(
            0,
            Math.Min(AlarmTimingPolicy.MaximumTimingTicks, ticks));
        draft.AmountText = ticks.ToString(CultureInfo.CurrentCulture);
        draft.Unit = TimingDisplayUnit.Tick;
        if (ticks == 0)
        {
            return;
        }
        foreach (var unit in new[]
                 {
                     TimingDisplayUnit.Century,
                     TimingDisplayUnit.Decade,
                     TimingDisplayUnit.Year,
                     TimingDisplayUnit.Month,
                     TimingDisplayUnit.Day,
                 })
        {
            var divisor = TimingTicksPerUnit(unit);
            if (ticks % divisor != 0)
            {
                continue;
            }
            draft.AmountText = (ticks / divisor).ToString(
                CultureInfo.CurrentCulture);
            draft.Unit = unit;
            return;
        }
    }

    private static int TimingTicksPerUnit(TimingDisplayUnit unit)
    {
        return unit switch
        {
            TimingDisplayUnit.Day => GameTimeWindowPolicy.SimTicksPerDay,
            TimingDisplayUnit.Month => GameTimeWindowPolicy.SimTicksPerMonth,
            TimingDisplayUnit.Year => GameTimeWindowPolicy.SimTicksPerYear,
            TimingDisplayUnit.Decade =>
                GameTimeWindowPolicy.SimTicksPerYear * 10,
            TimingDisplayUnit.Century =>
                GameTimeWindowPolicy.SimTicksPerYear * 100,
            _ => 1,
        };
    }

    private static string TimingUnitLabel(TimingDisplayUnit unit)
    {
        return unit switch
        {
            TimingDisplayUnit.Day => UnmaText.Get("ui.time.day", "DAY"),
            TimingDisplayUnit.Month => UnmaText.Get("ui.time.month", "MONTH"),
            TimingDisplayUnit.Year => UnmaText.Get("ui.time.year", "YEAR"),
            TimingDisplayUnit.Decade =>
                UnmaText.Get("ui.time.decade", "10 YEARS"),
            TimingDisplayUnit.Century =>
                UnmaText.Get("ui.time.century", "100 YEARS"),
            _ => UnmaText.Get("ui.time.tick", "TICK"),
        };
    }

    private void DrawSystemAlarms()
    {
        if (Time.realtimeSinceStartup > m_pendingSystemResetUntil)
        {
            m_pendingSystemResetId = "";
        }
        NativeGUILayout.Label(UnmaText.Get("auto.2d1f579a5d01"), m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.2092938a7b0b"),
            m_smallLabelStyle);

        m_systemAlarmScroll = NativeGUILayout.BeginScrollView(m_systemAlarmScroll);
        if (m_systemAlarmDraft == null)
        {
            var compactRows = m_windowRect.width < 760f;
            foreach (var alarm in m_runtime.GetSystemAlarmDefinitions())
            {
                DrawSystemAlarmSummaryRow(alarm, compactRows);
            }
        }
        else
        {
            DrawSystemAlarmDraft();
        }
        NativeGUILayout.EndScrollView();
    }

    private void DrawSystemAlarmSummaryRow(
        SystemAlarmDefinition alarm,
        bool compact)
    {
        NativeGUILayout.BeginVertical(
            "system-summary:" + alarm.Id,
            m_panelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (!compact)
        {
            NativeGUILayout.BeginHorizontal();
        }
        NativeGUILayout.Label(
            alarm.DisplayName + " · " +
            alarm.Stages.Count(stage => stage.Enabled) +
            UnmaText.Get("auto.da08863fac44") +
            (alarm.AutoAcknowledgeOnClear
                ? UnmaText.Get("auto.367f30137868")
                : UnmaText.Get("auto.c9097d398192")),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(32f));
        if (compact)
        {
            NativeGUILayout.BeginHorizontal();
        }

        if (NativeGUILayout.Button(
                alarm.Enabled
                    ? UnmaText.Get("ui.common.on", "ON")
                    : UnmaText.Get("ui.common.off", "OFF"),
                alarm.Enabled ? m_primaryButtonStyle : m_buttonStyle,
                NativeGUILayout.Width(compact ? 72f : 84f),
                NativeGUILayout.Height(32f)))
        {
            alarm.Enabled = !alarm.Enabled;
            if (m_runtime.UpdateSystemAlarm(alarm))
            {
                SetStatus(UnmaText.Get("auto.1145c5c75960"));
            }
            else
            {
                SetStatus(
                    UnmaText.Get("auto.5df942eb6687") +
                    m_runtime.LastPersistenceError);
            }
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.edit", "EDIT"),
                m_buttonStyle,
                NativeGUILayout.Width(compact ? 88f : 112f),
                NativeGUILayout.Height(32f)))
        {
            BeginEditingSystemAlarm(alarm);
        }

        var confirmingReset = string.Equals(
            m_pendingSystemResetId,
            alarm.Id,
            StringComparison.Ordinal);
        if (NativeGUILayout.Button(
                confirmingReset
                    ? UnmaText.Get("auto.91d331fc1397")
                    : UnmaText.Get(
                        "ui.system.factory_default",
                        "FACTORY DEFAULT"),
                confirmingReset ? m_dangerButtonStyle : m_buttonStyle,
                NativeGUILayout.Width(compact ? 132f : 154f),
                NativeGUILayout.Height(32f)))
        {
            if (!confirmingReset)
            {
                m_pendingSystemResetId = alarm.Id;
                m_pendingSystemResetUntil = Time.realtimeSinceStartup + 5f;
                SetStatus(
                    UnmaText.Get("auto.cd3ff75f956e") +
                    UnmaText.Get("auto.a63963cdfdad"));
            }
            else
            {
                m_pendingSystemResetId = "";
                if (m_runtime.ResetSystemAlarm(alarm.Id))
                {
                    SetStatus(UnmaText.Get("auto.85ff6c110b12"));
                }
                else
                {
                    SetStatus(
                        UnmaText.Get("auto.1e0dd8281824") +
                        m_runtime.LastPersistenceError);
                }
            }
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.EndVertical();
        NativeGUILayout.Space(4f);
    }

    private void DrawSystemAlarmDraft()
    {
        var draft = m_systemAlarmDraft;
        var sounds = m_audio.GetSoundOptions();
        var metrics = SystemMetricCatalog.All;
        var currentValues = m_runtime.GetSystemMetricValues();

        NativeGUILayout.BeginHorizontal();
        draft.Enabled = NativeGUILayout.Toggle(
            draft.Enabled,
            UnmaText.Get("auto.9bb40c22f772"),
            NativeGUILayout.Width(170f));
        NativeGUILayout.Label(
            UnmaText.Get("ui.common.name", "Name"),
            m_labelStyle,
            NativeGUILayout.Width(45f));
        draft.DisplayName = NativeGUILayout.TextField(
            draft.DisplayName ?? "",
            60,
            m_textFieldStyle);
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.BeginHorizontal();
        draft.AutoAcknowledgeOnClear = NativeGUILayout.Toggle(
            draft.AutoAcknowledgeOnClear,
            UnmaText.Get("auto.19a7e6f7335e"),
            NativeGUILayout.Width(340f));
        NativeGUILayout.Label(
            UnmaText.Get("auto.e330dc16dd70"),
            m_smallLabelStyle);
        NativeGUILayout.EndHorizontal();

        foreach (var stageEntry in draft.Stages
                     .Select((stage, index) => new
                     {
                         Stage = stage,
                         Index = index,
                     })
                     .OrderBy(entry => entry.Stage.Priority)
                     .ToArray())
        {
            var stage = stageEntry.Stage;
            var stageIndex = stageEntry.Index;
            NativeGUILayout.BeginVertical(m_panelStyle);
            NativeGUILayout.BeginHorizontal();
            stage.Enabled = NativeGUILayout.Toggle(
                stage.Enabled,
                UnmaText.Get("auto.6477bc93951f"),
                NativeGUILayout.Width(105f));
            NativeGUILayout.Label(
                UnmaText.Get("ui.common.text", "Text"),
                m_labelStyle,
                NativeGUILayout.Width(38f));
            stage.Message = NativeGUILayout.TextField(
                stage.Message ?? "",
                100,
                m_textFieldStyle);
            if (NativeGUILayout.Button(
                    SeverityLabel(stage.Severity),
                    m_buttonStyle,
                    NativeGUILayout.Width(105f)))
            {
                stage.Severity = NextEnum(stage.Severity);
            }
            NativeGUILayout.EndHorizontal();

            NativeGUILayout.BeginHorizontal();
            if (NativeGUILayout.Button(
                    stage.Logic == AlarmLogic.All
                        ? UnmaText.Get("auto.77bbd577fc42")
                        : UnmaText.Get("auto.7c378839a7f0"),
                    m_buttonStyle,
                    NativeGUILayout.Width(115f)))
            {
                stage.Logic = stage.Logic == AlarmLogic.All
                    ? AlarmLogic.Any
                    : AlarmLogic.All;
            }
            NativeGUILayout.Label(
                UnmaText.Get("ui.common.color", "Color"),
                m_labelStyle,
                NativeGUILayout.Width(48f));
            stage.ActiveColor = NativeGUILayout.TextField(
                stage.ActiveColor ?? "auto",
                9,
                m_textFieldStyle,
                NativeGUILayout.Width(92f));

            if (sounds.Count > 0)
            {
                var soundIndex = FindSoundIndex(sounds, stage.SoundId);
                var soundAvailable = sounds.Any(sound => string.Equals(
                    sound.Id,
                    stage.SoundId,
                    StringComparison.OrdinalIgnoreCase));
                if (NativeGUILayout.Button("◀", m_buttonStyle, NativeGUILayout.Width(30f)))
                {
                    soundIndex = Wrap(soundIndex - 1, sounds.Count);
                    stage.SoundId = sounds[soundIndex].Id;
                }
                NativeGUILayout.Label(
                    soundAvailable
                        ? sounds[soundIndex].Label
                        : UnmaText.Get("auto.40bffd508dbf") + stage.SoundId,
                    m_smallLabelStyle,
                    NativeGUILayout.Width(190f));
                if (NativeGUILayout.Button("▶", m_buttonStyle, NativeGUILayout.Width(30f)))
                {
                    soundIndex = Wrap(soundIndex + 1, sounds.Count);
                    stage.SoundId = sounds[soundIndex].Id;
                }
                if (NativeGUILayout.Button(
                        UnmaText.Get("ui.common.test", "TEST"),
                        m_buttonStyle,
                        NativeGUILayout.Width(55f)))
                {
                    TestSound(stage.SoundId, stage.Severity);
                }
            }
            NativeGUILayout.EndHorizontal();

            DrawSystemStageTiming(stageIndex, stage);
            DrawSystemStageOperatorAction(stage);

            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var condition = stage.Conditions[index];
                var metricIndex = SystemMetricCatalog.FindIndex(
                    condition.MetricId);
                var metric = metricIndex >= 0
                    ? metrics[metricIndex]
                    : new SystemMetricDescriptor(
                        condition.MetricId ?? "",
                        UnmaText.Get("auto.516699304b80") + (condition.MetricId ?? ""),
                        UnmaText.Get("auto.9a98b3d0d737"));
                var thresholdKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "threshold");
                var hysteresisKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "hysteresis");
                if (!m_systemThresholdTexts.TryGetValue(
                        thresholdKey,
                        out var thresholdText))
                {
                    thresholdText = condition.Threshold.ToString(
                        "R",
                        CultureInfo.CurrentCulture);
                    m_systemThresholdTexts[thresholdKey] = thresholdText;
                }
                if (!m_systemHysteresisTexts.TryGetValue(
                        hysteresisKey,
                        out var hysteresisText))
                {
                    hysteresisText = condition.Hysteresis.ToString(
                        "R",
                        CultureInfo.CurrentCulture);
                    m_systemHysteresisTexts[hysteresisKey] = hysteresisText;
                }

                NativeGUILayout.BeginHorizontal();
                if (NativeGUILayout.Button("◀", m_buttonStyle, NativeGUILayout.Width(30f)))
                {
                    metricIndex = metricIndex < 0
                        ? metrics.Count - 1
                        : Wrap(metricIndex - 1, metrics.Count);
                    condition.MetricId = metrics[metricIndex].Id;
                    metric = metrics[metricIndex];
                }
                NativeGUILayout.Label(
                    metric.Label + " · " + metric.Unit +
                    (currentValues.TryGetValue(metric.Id, out var current)
                        ? UnmaText.Get("auto.aa3d8483c2cc") + current.ToString(
                            "0.##",
                            CultureInfo.CurrentCulture) + "]"
                        : ""),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(260f));
                if (NativeGUILayout.Button("▶", m_buttonStyle, NativeGUILayout.Width(30f)))
                {
                    metricIndex = metricIndex < 0
                        ? 0
                        : Wrap(metricIndex + 1, metrics.Count);
                    condition.MetricId = metrics[metricIndex].Id;
                }
                if (NativeGUILayout.Button(
                        UnmaRuntime.OperatorText(condition.Comparison),
                        m_buttonStyle,
                        NativeGUILayout.Width(45f)))
                {
                    condition.Comparison = NextEnum(condition.Comparison);
                }
                thresholdText = NativeGUILayout.TextField(
                    thresholdText,
                    24,
                    m_textFieldStyle,
                    NativeGUILayout.Width(90f));
                m_systemThresholdTexts[thresholdKey] = thresholdText;
                NativeGUILayout.Label(
                    UnmaText.Get("ui.timing.hysteresis", "HYSTERESIS"),
                    m_smallLabelStyle,
                    NativeGUILayout.Width(88f));
                hysteresisText = NativeGUILayout.TextField(
                    hysteresisText,
                    24,
                    m_textFieldStyle,
                    NativeGUILayout.Width(78f));
                m_systemHysteresisTexts[hysteresisKey] = hysteresisText;
                if (NativeGUILayout.Button(
                        UnmaText.Get("ui.common.remove", "REMOVE"),
                        m_dangerButtonStyle,
                        NativeGUILayout.Width(95f)))
                {
                    if (TryApplySystemDraftTexts())
                    {
                        stage.Conditions.RemoveAt(index);
                        RebuildSystemThresholdTexts();
                        index--;
                    }
                }
                NativeGUILayout.EndHorizontal();
            }

            if (NativeGUILayout.Button(
                    UnmaText.Get("auto.d6c391b41588"),
                    m_buttonStyle,
                    NativeGUILayout.Width(135f)))
            {
                if (TryApplySystemDraftTexts())
                {
                    stage.Conditions.Add(new SystemConditionDefinition
                    {
                        MetricId = metrics[0].Id,
                        Comparison = ComparisonOperator.Less,
                        Threshold = 0d,
                    });
                    RebuildSystemThresholdTexts();
                }
            }
            NativeGUILayout.EndVertical();
        }

        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.2cf14a67c208"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(235f),
                NativeGUILayout.Height(30f)))
        {
            SaveSystemAlarmDraft();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("ui.common.cancel", "CANCEL"),
                m_buttonStyle,
                NativeGUILayout.Width(115f),
                NativeGUILayout.Height(30f)))
        {
            m_systemAlarmDraft = null;
            ClearSystemDraftTexts();
            SetStatus(UnmaText.Get("auto.6b89012b7c85"));
        }
        NativeGUILayout.EndHorizontal();
    }

    private void DrawSystemStageTiming(
        int stageIndex,
        SystemAlarmStageDefinition stage)
    {
        NativeGUILayout.Label(
            UnmaText.Get("ui.timing.stage_title", "STAGE TIMING"),
            m_smallLabelStyle);
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.activation", "ACTIVATION DELAY"),
            GetSystemTimingDraft(
                stageIndex,
                "activation",
                stage.ActivationDelayTicks),
            UnmaText.Get("ui.timing.zero.instant", "INSTANT"));
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.reset", "RESET DELAY"),
            GetSystemTimingDraft(
                stageIndex,
                "reset",
                stage.ResetDelayTicks),
            UnmaText.Get("ui.timing.zero.instant", "INSTANT"));
        DrawTimingDraftValue(
            UnmaText.Get("ui.timing.minimum_active", "MINIMUM ACTIVE"),
            GetSystemTimingDraft(
                stageIndex,
                "minimum-active",
                stage.MinimumActiveTicks),
            UnmaText.Get("ui.timing.zero.off", "OFF"));
    }

    private void DrawSystemStageOperatorAction(
        SystemAlarmStageDefinition stage)
    {
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("ui.escalation.operator_action", "OPERATOR ACTION"),
            m_smallLabelStyle,
            NativeGUILayout.Width(190f));
        if (NativeGUILayout.Button(
                OperatorActionLabel(stage.OperatorAction),
                m_buttonStyle,
                NativeGUILayout.Width(330f),
                NativeGUILayout.Height(28f)))
        {
            stage.OperatorAction = NextEnum(stage.OperatorAction);
        }
        NativeGUILayout.FlexibleSpace();
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.system.operator_action_hint",
                "Runs once when an already-active alarm advances to this stage. It never moves the camera or changes per-alarm snoozes."),
            m_smallLabelStyle);
    }

    private TimingDraftValue GetSystemTimingDraft(
        int stageIndex,
        string field,
        int fallbackTicks)
    {
        var key = SystemStageDraftKey(stageIndex, field);
        if (m_systemTimingDrafts.TryGetValue(key, out var draft))
        {
            return draft;
        }
        draft = new TimingDraftValue();
        LoadTimingDraft(draft, fallbackTicks);
        m_systemTimingDrafts[key] = draft;
        return draft;
    }

    private void BeginEditingSystemAlarm(SystemAlarmDefinition alarm)
    {
        m_systemAlarmDraft = alarm;
        RebuildSystemThresholdTexts();
        m_systemAlarmScroll = Vector2.zero;
        SetStatus(UnmaText.Get("auto.f57478cbd5c5"));
    }

    private void SaveSystemAlarmDraft()
    {
        for (var stageIndex = 0;
             stageIndex < m_systemAlarmDraft.Stages.Count;
             stageIndex++)
        {
            var stage = m_systemAlarmDraft.Stages[stageIndex];
            if (!TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "activation",
                        stage.ActivationDelayTicks),
                    out var activationDelayTicks) ||
                !TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "reset",
                        stage.ResetDelayTicks),
                    out var resetDelayTicks) ||
                !TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "minimum-active",
                        stage.MinimumActiveTicks),
                    out var minimumActiveTicks))
            {
                SetStatus(UnmaText.Format(
                    "ui.timing.invalid_stage_duration",
                    "Stage '{0}': Enter non-negative whole timing values within 100 game years.",
                    stage.Message));
                return;
            }
            stage.ActivationDelayTicks = activationDelayTicks;
            stage.ResetDelayTicks = resetDelayTicks;
            stage.MinimumActiveTicks = minimumActiveTicks;
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var thresholdKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "threshold");
                var hysteresisKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "hysteresis");
                if (!m_systemThresholdTexts.TryGetValue(
                        thresholdKey,
                        out var text) ||
                    !TryParseDouble(text, out var threshold))
                {
                    SetStatus(
                        UnmaText.Get("auto.85b8b6dcd53e") +
                        stage.Message + "'.");
                    return;
                }
                if (!m_systemHysteresisTexts.TryGetValue(
                        hysteresisKey,
                        out var hysteresisText) ||
                    !TryParseDouble(hysteresisText, out var hysteresis) ||
                    hysteresis < 0d)
                {
                    SetStatus(UnmaText.Format(
                        "ui.timing.invalid_stage_hysteresis",
                        "Stage '{0}', condition {1}: Enter a non-negative hysteresis value.",
                        stage.Message,
                        index + 1));
                    return;
                }
                stage.Conditions[index].Threshold = threshold;
                stage.Conditions[index].Hysteresis = hysteresis;
            }
            stage.ActiveColor = NormalizeSystemColor(stage.ActiveColor);
            stage.SoundId = string.IsNullOrWhiteSpace(stage.SoundId)
                ? "auto"
                : stage.SoundId;
        }

        if (!m_runtime.UpdateSystemAlarm(m_systemAlarmDraft))
        {
            SetStatus(
                UnmaText.Get("auto.5df942eb6687") +
                m_runtime.LastPersistenceError);
            return;
        }
        m_systemAlarmDraft = null;
        ClearSystemDraftTexts();
        SetStatus(UnmaText.Get("auto.a62e7b126c0b"));
    }

    private void RebuildSystemThresholdTexts()
    {
        ClearSystemDraftTexts();
        if (m_systemAlarmDraft == null)
        {
            return;
        }
        for (var stageIndex = 0;
             stageIndex < m_systemAlarmDraft.Stages.Count;
             stageIndex++)
        {
            var stage = m_systemAlarmDraft.Stages[stageIndex];
            GetSystemTimingDraft(
                stageIndex,
                "activation",
                stage.ActivationDelayTicks);
            GetSystemTimingDraft(
                stageIndex,
                "reset",
                stage.ResetDelayTicks);
            GetSystemTimingDraft(
                stageIndex,
                "minimum-active",
                stage.MinimumActiveTicks);
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var condition = stage.Conditions[index];
                m_systemThresholdTexts[SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "threshold")] = condition.Threshold.ToString(
                        "R",
                        CultureInfo.CurrentCulture);
                m_systemHysteresisTexts[SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "hysteresis")] = condition.Hysteresis.ToString(
                        "R",
                        CultureInfo.CurrentCulture);
            }
        }
    }

    private bool TryApplySystemDraftTexts()
    {
        if (m_systemAlarmDraft == null)
        {
            return false;
        }

        // Validate every text draft before changing the model. ADD/REMOVE
        // rebuilds the index-based draft keys, so proceeding with even one
        // invalid value would otherwise silently discard the player's input.
        for (var stageIndex = 0;
             stageIndex < m_systemAlarmDraft.Stages.Count;
             stageIndex++)
        {
            var stage = m_systemAlarmDraft.Stages[stageIndex];
            if (!TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "activation",
                        stage.ActivationDelayTicks),
                    out _) ||
                !TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "reset",
                        stage.ResetDelayTicks),
                    out _) ||
                !TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "minimum-active",
                        stage.MinimumActiveTicks),
                    out _))
            {
                SetStatus(UnmaText.Format(
                    "ui.timing.invalid_stage_duration",
                    "Stage '{0}': Enter non-negative whole timing values within 100 game years.",
                    stage.Message));
                return false;
            }

            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var thresholdKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "threshold");
                var hysteresisKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "hysteresis");
                if (!m_systemThresholdTexts.TryGetValue(
                        thresholdKey,
                        out var thresholdText) ||
                    !TryParseDouble(thresholdText, out _))
                {
                    SetStatus(UnmaText.Format(
                        "ui.timing.invalid_stage_threshold",
                        "Stage '{0}', condition {1}: Enter a valid threshold value.",
                        stage.Message,
                        index + 1));
                    return false;
                }
                if (!m_systemHysteresisTexts.TryGetValue(
                        hysteresisKey,
                        out var hysteresisText) ||
                    !TryParseDouble(hysteresisText, out var hysteresis) ||
                    hysteresis < 0d)
                {
                    SetStatus(UnmaText.Format(
                        "ui.timing.invalid_stage_hysteresis",
                        "Stage '{0}', condition {1}: Enter a non-negative hysteresis value.",
                        stage.Message,
                        index + 1));
                    return false;
                }
            }
        }

        for (var stageIndex = 0;
             stageIndex < m_systemAlarmDraft.Stages.Count;
             stageIndex++)
        {
            var stage = m_systemAlarmDraft.Stages[stageIndex];
            if (TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "activation",
                        stage.ActivationDelayTicks),
                    out var activationDelayTicks))
            {
                stage.ActivationDelayTicks = activationDelayTicks;
            }
            if (TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "reset",
                        stage.ResetDelayTicks),
                    out var resetDelayTicks))
            {
                stage.ResetDelayTicks = resetDelayTicks;
            }
            if (TryGetTimingTicks(
                    GetSystemTimingDraft(
                        stageIndex,
                        "minimum-active",
                        stage.MinimumActiveTicks),
                    out var minimumActiveTicks))
            {
                stage.MinimumActiveTicks = minimumActiveTicks;
            }
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var condition = stage.Conditions[index];
                var thresholdKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "threshold");
                var hysteresisKey = SystemConditionDraftKey(
                    stageIndex,
                    index,
                    "hysteresis");
                if (m_systemThresholdTexts.TryGetValue(
                        thresholdKey,
                        out var text) &&
                    TryParseDouble(text, out var threshold))
                {
                    condition.Threshold = threshold;
                }
                if (m_systemHysteresisTexts.TryGetValue(
                        hysteresisKey,
                        out var hysteresisText) &&
                    TryParseDouble(hysteresisText, out var hysteresis) &&
                    hysteresis >= 0d)
                {
                    condition.Hysteresis = hysteresis;
                }
            }
        }
        return true;
    }

    private void ClearSystemDraftTexts()
    {
        m_systemThresholdTexts.Clear();
        m_systemHysteresisTexts.Clear();
        m_systemTimingDrafts.Clear();
    }

    private static string SystemStageDraftKey(int stageIndex, string field)
    {
        return "stage:" + stageIndex.ToString(CultureInfo.InvariantCulture) +
               "|" + field;
    }

    private static string SystemConditionDraftKey(
        int stageIndex,
        int conditionIndex,
        string field)
    {
        return "stage:" + stageIndex.ToString(CultureInfo.InvariantCulture) +
               "|condition:" +
               conditionIndex.ToString(CultureInfo.InvariantCulture) +
               "|" + field;
    }

    private void DrawSoundOverrides()
    {
        NativeGUILayout.Label(
            UnmaText.Get(
                "sounds.override.title",
                UnmaText.Get("auto.8d7c9716a814")),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "sounds.override.description",
                UnmaText.Get("auto.858b267a7513") +
                UnmaText.Get("auto.fe4117aa6b38") +
                UnmaText.Get("auto.f65fbbbc7afc") +
                UnmaText.Get("auto.5f6d3281fc28") +
                UnmaText.Get("auto.0d87a97f32cd") +
                UnmaText.Get("auto.6083c2c35caf") +
                UnmaText.Get("ui.vanilla.notification_unchanged")),
            m_smallLabelStyle);

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get(
                "sounds.override.filter_label",
                UnmaText.Get("auto.8567d6ad7823")),
            m_labelStyle,
            NativeGUILayout.Width(155f));
        m_soundOverrideFilter = NativeGUILayout.TextField(
            m_soundOverrideFilter,
            100,
            m_textFieldStyle,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(30f));
        NativeGUILayout.EndHorizontal();

        var sounds = m_audio.GetSoundOptions();
        var candidates = m_runtime.GetSoundOverrideCandidates()
            .Where(MatchesSoundOverrideFilter)
            .ToArray();

        m_soundOverrideScroll = NativeGUILayout.BeginVerticalScrollView(
            m_soundOverrideScroll,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.ExpandHeight(true));
        if (candidates.Length == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "sounds.override.empty",
                    UnmaText.Get("auto.24b5ad869385") +
                    UnmaText.Get("auto.2a5f3853286c") +
                    UnmaText.Get("auto.2a8545158425")),
                m_labelStyle);
        }

        foreach (var candidate in candidates)
        {
            var configured = m_runtime.GetConfiguredSound(
                candidate.OverrideId);
            var soundIndex = FindSoundIndex(sounds, configured);
            var autoAcknowledgeOnClear =
                m_runtime.GetConfiguredAutoAcknowledgeOnClear(
                    candidate.OverrideId);
            var isVanilla = string.Equals(
                candidate.Source,
                "vanilla",
                StringComparison.Ordinal);

            NativeGUILayout.BeginVertical(
                "sound-override:" +
                PanelSlotProjection.StableViewIdentity(candidate),
                m_panelStyle,
                NativeGUILayout.ExpandWidth(true));
            NativeGUILayout.Label(
                candidate.Name,
                m_headerStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.MinHeight(28f));
            NativeGUILayout.Label(
                candidate.Detail,
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true),
                NativeGUILayout.MinHeight(22f));

            if (isVanilla)
            {
                DrawVanillaBehaviorControls(candidate);
            }

            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                UnmaText.Get("sounds.override.sound_label", "Ton"),
                m_smallLabelStyle,
                NativeGUILayout.MinWidth(190f),
                NativeGUILayout.ExpandWidth(true));
            if (NativeGUILayout.Button("◀", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex - 1, sounds.Count)]);
            }
            NativeGUILayout.Label(
                sounds[soundIndex].Label,
                m_smallLabelStyle,
                NativeGUILayout.Width(245f),
                NativeGUILayout.Height(30f));
            if (NativeGUILayout.Button("▶", m_buttonStyle, NativeGUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex + 1, sounds.Count)]);
            }
            NativeGUILayout.EndHorizontal();

            var updatedAutoAcknowledgeOnClear = NativeGUILayout.Toggle(
                autoAcknowledgeOnClear,
                UnmaText.Get(
                    "sounds.override.auto_acknowledge",
                    UnmaText.Get("auto.19a7e6f7335e")));
            if (updatedAutoAcknowledgeOnClear != autoAcknowledgeOnClear)
            {
                SaveAutoAcknowledgeOnClear(
                    candidate.OverrideId,
                    updatedAutoAcknowledgeOnClear);
            }
            NativeGUILayout.EndVertical();
            NativeGUILayout.Space(5f);
        }
        NativeGUILayout.EndScrollView();
    }

    private bool MatchesSoundOverrideFilter(AlarmView candidate)
    {
        if (candidate == null ||
            string.IsNullOrWhiteSpace(m_soundOverrideFilter))
        {
            return candidate != null;
        }

        var isVanilla = string.Equals(
            candidate.Source,
            "vanilla",
            StringComparison.Ordinal);
        var enabled = !isVanilla ||
                      m_runtime.GetVanillaNotificationEnabled(
                          candidate.OverrideId);
        var sourceTokens = isVanilla
            ? UnmaText.Get(
                "sounds.override.filter_tokens_vanilla",
                "Vanilla")
            : UnmaText.Get(
                "sounds.override.filter_tokens_external",
                UnmaText.Get("auto.1b06e318a052"));
        var statusTokens = !isVanilla
            ? ""
            : enabled
                ? UnmaText.Get(
                    "sounds.override.filter_tokens_enabled",
                    UnmaText.Get("auto.995781363573"))
                : UnmaText.Get(
                    "sounds.override.filter_tokens_disabled",
                    UnmaText.Get("auto.db3bac7e3811"));
        var haystack = string.Join(
            " ",
            candidate.Name,
            candidate.Detail,
            candidate.OverrideId,
            sourceTokens,
            statusTokens);
        return m_soundOverrideFilter
            .Split(
                new[] { ' ', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries)
            .All(token => haystack.IndexOf(
                token,
                StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private void DrawVanillaBehaviorControls(AlarmView candidate)
    {
        if (candidate.EntityId >= 0)
        {
            DrawVanillaBehaviorRow(
                candidate,
                VanillaNotificationScope.Entity,
                UnmaText.Get(
                    "sounds.override.scope_entity",
                    "NUR DIESES OBJEKT"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            DrawVanillaBehaviorRow(
                candidate,
                VanillaNotificationScope.EntityPrototype,
                UnmaText.Format(
                    "sounds.override.scope_prototype",
                    "ALLE GLEICHEN OBJEKTE ({0})",
                    candidate.EntityPrototypeId));
        }
        DrawVanillaBehaviorRow(
            candidate,
            VanillaNotificationScope.NotificationType,
            UnmaText.Get(
                "sounds.override.scope_notification",
                "DIESER MELDUNGSTYP"));
    }

    private void DrawVanillaBehaviorRow(
        AlarmView candidate,
        VanillaNotificationScope scope,
        string scopeLabel)
    {
        var behavior = m_runtime.GetVanillaNotificationBehavior(
            candidate.OverrideId,
            scope,
            candidate.EntityId,
            candidate.EntityPrototypeId);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            scopeLabel,
            m_smallLabelStyle,
            NativeGUILayout.MinWidth(190f),
            NativeGUILayout.ExpandWidth(true));
        if (NativeGUILayout.Button(
                VanillaBehaviorLabel(behavior),
                behavior == VanillaNotificationBehavior.Hidden ||
                behavior == VanillaNotificationBehavior.Ignored
                    ? m_dangerButtonStyle
                    : behavior == VanillaNotificationBehavior.Silent
                        ? m_buttonStyle
                        : m_primaryButtonStyle,
                NativeGUILayout.Width(245f),
                NativeGUILayout.Height(30f)))
        {
            SaveVanillaNotificationBehavior(
                candidate,
                scope,
                NextVanillaBehavior(behavior));
        }
        NativeGUILayout.EndHorizontal();
    }

    private static VanillaNotificationBehavior NextVanillaBehavior(
        VanillaNotificationBehavior behavior)
    {
        return behavior switch
        {
            VanillaNotificationBehavior.Normal =>
                VanillaNotificationBehavior.Silent,
            VanillaNotificationBehavior.Silent =>
                VanillaNotificationBehavior.Hidden,
            VanillaNotificationBehavior.Hidden =>
                VanillaNotificationBehavior.Ignored,
            _ => VanillaNotificationBehavior.Normal,
        };
    }

    private static string VanillaBehaviorLabel(
        VanillaNotificationBehavior behavior)
    {
        return behavior switch
        {
            VanillaNotificationBehavior.Silent => UnmaText.Get(
                "sounds.override.behavior_silent",
                "LOGGEN · TON AUS"),
            VanillaNotificationBehavior.Hidden => UnmaText.Get(
                "sounds.override.behavior_hidden",
                "LOGGEN · TON AUS · AUSBLENDEN"),
            VanillaNotificationBehavior.Ignored => UnmaText.Get(
                "sounds.override.behavior_ignored",
                "NICHT LOGGEN · KOMPLETT IGNORIEREN"),
            _ => UnmaText.Get(
                "sounds.override.behavior_normal",
                "NORMAL"),
        };
    }

    private void SaveVanillaNotificationBehavior(
        AlarmView candidate,
        VanillaNotificationScope scope,
        VanillaNotificationBehavior behavior)
    {
        SaveVanillaNotificationBehavior(
            candidate.OverrideId,
            candidate.Name,
            scope,
            behavior,
            candidate.EntityId,
            candidate.EntityPrototypeId);
    }

    private void SaveVanillaNotificationBehavior(
        string overrideId,
        string alarmName,
        VanillaNotificationScope scope,
        VanillaNotificationBehavior behavior,
        int entityId,
        string entityPrototypeId)
    {
        if (m_runtime.SetVanillaNotificationBehavior(
                overrideId,
                scope,
                behavior,
                entityId,
                entityPrototypeId))
        {
            SetStatus(UnmaText.Format(
                "sounds.override.status_behavior_saved",
                "Regel gespeichert: {0} · {1}",
                alarmName,
                VanillaBehaviorLabel(behavior)));
        }
        else
        {
            SetStatus(UnmaText.Format(
                "sounds.override.status_behavior_error",
                "Regel konnte nicht gespeichert werden: {0}",
                m_runtime.LastPersistenceError));
        }
    }

    private void SaveVanillaNotificationEnabled(
        string overrideId,
        bool enabled)
    {
        if (m_runtime.SetVanillaNotificationEnabled(overrideId, enabled))
        {
            SetStatus(UnmaText.Format(
                enabled
                    ? "sounds.override.status_global_enabled"
                    : "sounds.override.status_global_disabled",
                enabled
                    ? UnmaText.Get("auto.c9c235dca05e")
                    : UnmaText.Get("auto.b2db5c92e05b"),
                overrideId));
        }
        else
        {
            SetStatus(UnmaText.Format(
                "sounds.override.status_global_error",
                UnmaText.Get("auto.ea482c77e2dc"),
                overrideId,
                m_runtime.LastPersistenceError));
        }
    }

    private void SaveSoundOverride(string alarmId, SoundOption sound)
    {
        if (m_runtime.SetConfiguredSound(alarmId, sound.Id))
        {
            SetStatus(UnmaText.Get("auto.27294bb6c18a") + sound.Label);
        }
        else
        {
            SetStatus(
                UnmaText.Get("auto.b379f0ea571f") +
                m_runtime.LastPersistenceError);
        }
    }

    private void SaveAutoAcknowledgeOnClear(
        string alarmId,
        bool autoAcknowledgeOnClear)
    {
        if (m_runtime.SetConfiguredAutoAcknowledgeOnClear(
                alarmId,
                autoAcknowledgeOnClear))
        {
            SetStatus(
                autoAcknowledgeOnClear
                    ? UnmaText.Get("auto.3e6ba3666853")
                    : UnmaText.Get("auto.5cf22fd5cda5"));
        }
        else
        {
            SetStatus(
                UnmaText.Get("auto.a1eb5fd04b07") +
                m_runtime.LastPersistenceError);
        }
    }

    private void DrawOptions()
    {
        m_optionsScroll = NativeGUILayout.BeginVerticalScrollView(
            m_optionsScroll,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.ExpandHeight(true));

        NativeGUILayout.Label(UnmaText.Get("options.display"), m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.05e9f359f2e3"),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("options.ui_scale"),
            m_labelStyle,
            NativeGUILayout.Width(120f));
        var scaleChanged = false;
        if (NativeGUILayout.Button("−", m_buttonStyle, NativeGUILayout.Width(38f)))
        {
            m_runtime.Configuration.UiScalePercent = Math.Max(
                75,
                m_runtime.Configuration.UiScalePercent - 25);
            scaleChanged = true;
        }
        NativeGUILayout.Label(
            m_runtime.Configuration.UiScalePercent + " %",
            m_headerStyle,
            NativeGUILayout.Width(90f));
        if (NativeGUILayout.Button("+", m_buttonStyle, NativeGUILayout.Width(38f)))
        {
            m_runtime.Configuration.UiScalePercent = Math.Min(
                200,
                m_runtime.Configuration.UiScalePercent + 25);
            scaleChanged = true;
        }
        if (NativeGUILayout.Button(
                "100 %",
                m_buttonStyle,
                NativeGUILayout.Width(80f)))
        {
            m_runtime.Configuration.UiScalePercent = 100;
            scaleChanged = true;
        }
        NativeGUILayout.EndHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.df85f85313da"),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (scaleChanged)
        {
            SaveConfiguration(
                UnmaText.Get("auto.9f37ceb925ab") +
                m_runtime.Configuration.UiScalePercent + " %.");
        }

        if (!m_optionsColorDraftInitialized)
        {
            m_optionsColorDraftInitialized = true;
            m_optionsWarningColor = m_runtime.Configuration.WarningColor;
            m_optionsCriticalColor = m_runtime.Configuration.CriticalColor;
            m_optionsEmergencyColor = m_runtime.Configuration.EmergencyColor;
        }
        var compactColorSettings = m_windowRect.width < 760f;
        if (!compactColorSettings)
        {
            NativeGUILayout.BeginHorizontal();
        }
        m_optionsWarningColor = DrawOptionsColorField(
            UnmaText.Get("options.warning_color"),
            m_optionsWarningColor,
            compactColorSettings);
        m_optionsCriticalColor = DrawOptionsColorField(
            UnmaText.Get("severity.critical"),
            m_optionsCriticalColor,
            compactColorSettings);
        m_optionsEmergencyColor = DrawOptionsColorField(
            UnmaText.Get("severity.emergency"),
            m_optionsEmergencyColor,
            compactColorSettings);
        var colorsValid =
            AlarmUiErgonomics.IsValidHtmlColor(m_optionsWarningColor) &&
            AlarmUiErgonomics.IsValidHtmlColor(m_optionsCriticalColor) &&
            AlarmUiErgonomics.IsValidHtmlColor(m_optionsEmergencyColor);
        if (compactColorSettings)
        {
            NativeGUILayout.BeginHorizontal();
        }
        NativeGUI.enabled = colorsValid;
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.373d6df29cf1"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(175f),
                NativeGUILayout.Height(30f)))
        {
            var configuration = m_runtime.Configuration;
            var oldWarning = configuration.WarningColor;
            var oldCritical = configuration.CriticalColor;
            var oldEmergency = configuration.EmergencyColor;
            configuration.WarningColor = m_optionsWarningColor.Trim();
            configuration.CriticalColor = m_optionsCriticalColor.Trim();
            configuration.EmergencyColor = m_optionsEmergencyColor.Trim();
            if (m_runtime.SaveConfiguration())
            {
                SetStatus(
                    UnmaText.Get("auto.f7bb0c5b2c6c"),
                    StatusSeverity.Success);
            }
            else
            {
                configuration.WarningColor = oldWarning;
                configuration.CriticalColor = oldCritical;
                configuration.EmergencyColor = oldEmergency;
                SetStatus(
                    UnmaText.Get("auto.5df942eb6687") +
                    m_runtime.LastPersistenceError,
                    StatusSeverity.Error,
                    true);
            }
        }
        NativeGUI.enabled = true;
        NativeGUILayout.EndHorizontal();
        if (!colorsValid)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "options.color.invalid",
                    "Enter every color in #RRGGBB format."),
                m_statusErrorStyle);
        }

        NativeGUILayout.Space(8f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "ui.options.accessibility",
                "ACCESSIBILITY"),
            m_sectionStyle);
        var reducedMotion = NativeGUILayout.Toggle(
            m_runtime.Configuration.ReducedMotion,
            UnmaText.Get(
                "options.reduced_motion",
                "Reduced motion"),
            NativeGUILayout.Height(30f));
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.reduced_motion_hint",
                "Uses stable alarm highlighting instead of flashing."),
            m_smallLabelStyle);
        if (reducedMotion != m_runtime.Configuration.ReducedMotion)
        {
            var previous = m_runtime.Configuration.ReducedMotion;
            m_runtime.Configuration.ReducedMotion = reducedMotion;
            if (!m_runtime.SaveConfiguration())
            {
                m_runtime.Configuration.ReducedMotion = previous;
                SetStatus(
                    UnmaText.Get("auto.5df942eb6687") +
                    m_runtime.LastPersistenceError,
                    StatusSeverity.Error,
                    true);
            }
            else
            {
                SetStatus(UnmaText.Get(
                    reducedMotion
                        ? "options.reduced_motion_hint"
                        : "auto.f7bb0c5b2c6c",
                    reducedMotion
                        ? "Uses stable alarm highlighting instead of flashing."
                        : "Appearance saved."), StatusSeverity.Success);
            }
        }

        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.options.audio", "AUDIO"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.f98a9c516625"),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            UnmaText.Get("auto.665123745b97") +
            m_audio.SoundsDirectory,
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            UnmaText.Get("auto.b4f0fa6a9f20"),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (NativeGUILayout.Button(
                UnmaText.Get("auto.3ac4c11a94ac"),
                m_buttonStyle,
                NativeGUILayout.Width(220f)))
        {
            m_audio.RefreshSoundOptions();
            SetStatus(UnmaText.Get("auto.48d4265633fa"));
        }

        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.options.system_alarms", "SYSTEM ALARMS"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.5ca97b0efd51"),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(UnmaText.Get("auto.461c23ce7edb"), m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.a183668aa2b3"),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(
            UnmaText.Get("ui.options.state_model", "STATE MODEL"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get("auto.fdea5764a7c1"),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.Space(10f);
        DrawTransferProfileOptions();

        NativeGUILayout.Space(10f);
        NativeGUILayout.Label(
            UnmaText.Get("options.integration.title", "FREMDMOD-API"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.integration.description",
                UnmaText.Get("auto.a67711e569a9") +
                UnmaText.Get("auto.ae53894897ea")),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));
        var integration = m_runtime.GetExternalIntegrationStatus();
        NativeGUILayout.Label(
            UnmaText.Format(
                "options.integration.status",
                UnmaText.Get("auto.824596e450d8") +
                UnmaText.Get("auto.7365af489dd8") +
                UnmaText.Get("auto.f7593a8c54ea"),
                integration.ActiveProviderCount,
                integration.LoadedFileCount,
                integration.ScannedFileCount,
                integration.JsonAlarmCount,
                integration.ApiMetricCount,
                integration.ApiAlarmCount,
                integration.ApiStateCount,
                integration.DiagnosticCount),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "options.integration.reload",
                    UnmaText.Get("auto.6a0576853198")),
                m_buttonStyle,
                NativeGUILayout.Width(260f)))
        {
            var clean = m_runtime.ReloadExternalDefinitions();
            m_audio.RefreshSoundOptions();
            SetStatus(clean
                ? UnmaText.Get(
                    "options.integration.reload_ok",
                    UnmaText.Get("auto.872181b517a4"))
                : UnmaText.Get(
                    "options.integration.reload_partial",
                    UnmaText.Get("auto.b7537ab622a9") +
                    UnmaText.Get("auto.6b9b10331ab0")));
        }
        foreach (var diagnostic in m_runtime
                     .GetExternalIntegrationDiagnostics()
                     .Take(3))
        {
            NativeGUILayout.Label(
                diagnostic.ProviderId + " · " + diagnostic.Code + " · " +
                LocalizeExternalDiagnosticMessage(diagnostic.Code),
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true));
        }
        NativeGUILayout.EndScrollView();
    }

    private void DrawTransferProfileOptions()
    {
        var profile = m_runtime.GetTransferProfile();
        var ruleRows = BuildTransferRuleRows(profile);
        InitializeTransferProfileUi(profile, ruleRows);
        TrackNewTransferRules(ruleRows);

        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.title",
                "SPIELSTANDSÜBERGREIFENDES PROFIL"),
            m_sectionStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.description",
                "Ausgewählte Einstellungen global speichern und in andere " +
                "Spielstände übernehmen."),
            m_labelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("options.transfer.profile_name", "Profilname"),
            m_labelStyle,
            NativeGUILayout.Width(150f));
        m_transferProfileName = NativeGUILayout.TextField(
            m_transferProfileName,
            80,
            m_textFieldStyle,
            NativeGUILayout.ExpandWidth(true),
            NativeGUILayout.Height(30f));
        NativeGUILayout.EndHorizontal();

        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.categories",
                "ÜBERTRAGBARE BEREICHE"),
            m_headerStyle);
        DrawTransferCategory(
            ref m_transferNotificationBehaviors,
            "options.transfer.category.notifications",
            "Meldungsverhalten",
            "options.transfer.category.notifications_hint",
            "Portable Meldungsregeln übertragen.");
        DrawTransferCategory(
            ref m_transferSoundSettings,
            "options.transfer.category.sounds",
            "Töne und Auto-Quittierung",
            "options.transfer.category.sounds_hint",
            "Tonzuordnungen und Auto-Quittierung übertragen.");
        DrawTransferCategory(
            ref m_transferAppearance,
            "options.transfer.category.appearance",
            "Farben und UI-Skalierung",
            "options.transfer.category.appearance_hint",
            "Farben und Skalierung übertragen.");
        DrawTransferCategory(
            ref m_transferSystemAlarms,
            "options.transfer.category.system_alarms",
            "Systemalarme",
            "options.transfer.category.system_alarms_hint",
            "Systemalarm-Konfiguration übertragen.");
        DrawTransferCategory(
            ref m_transferWindowLayout,
            "options.transfer.category.window_layout",
            "Fensterlayout",
            "options.transfer.category.window_layout_hint",
            "Fensterpositionen und -größen übertragen.");

        NativeGUILayout.Space(6f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.rules_title",
                "MELDUNGSREGELN EINZELN AUSWÄHLEN"),
            m_headerStyle);
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.rules_hint",
                "Einzelobjekt-Regeln sind spielstandsgebunden und werden " +
                "übersprungen."),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));

        NativeGUILayout.BeginHorizontal();
        var previousEnabled = NativeGUI.enabled;
        NativeGUI.enabled = previousEnabled &&
                            m_transferNotificationBehaviors;
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "options.transfer.rules_select_all",
                    "ALLE PORTABLEN AUSWÄHLEN"),
                m_buttonStyle,
                NativeGUILayout.Width(220f)))
        {
            foreach (var row in ruleRows.Where(IsPortableTransferRule))
            {
                m_transferSelectedRuleIdentities.Add(row.Identity);
            }
            InvalidateTransferPreview();
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "options.transfer.rules_clear",
                    "AUSWAHL AUFHEBEN"),
                m_buttonStyle,
                NativeGUILayout.Width(190f)))
        {
            m_transferSelectedRuleIdentities.Clear();
            InvalidateTransferPreview();
        }
        NativeGUI.enabled = previousEnabled;
        NativeGUILayout.EndHorizontal();

        if (ruleRows.Count == 0)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "options.transfer.rules_empty",
                    "Noch keine Meldungsregeln vorhanden."),
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true));
        }
        else
        {
            foreach (var row in ruleRows)
            {
                DrawTransferRuleRow(row);
            }
        }

        NativeGUILayout.Space(6f);
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.existing_title",
                "VORHANDENES STANDARDPROFIL"),
            m_headerStyle);
        if (profile == null)
        {
            NativeGUILayout.Label(
                UnmaText.Get(
                    "options.transfer.existing_none",
                    "Noch kein Profil gespeichert."),
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true));
        }
        else
        {
            var profileName = profile.Metadata?.Name;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                profileName = UnmaText.Get(
                    "options.transfer.default_name",
                    "Standard");
            }
            NativeGUILayout.Label(
                UnmaText.Format(
                    "options.transfer.existing_summary",
                    "{0} · Schema {1} · {2} Meldungsregeln",
                    profileName,
                    profile.ProfileSchemaVersion,
                    profile.NotificationRules?.Count ?? 0),
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true));
            if (!string.IsNullOrWhiteSpace(profile.Metadata?.SourceVersion))
            {
                NativeGUILayout.Label(
                    UnmaText.Format(
                        "options.transfer.source_version",
                        "UNMA-Quellversion: {0}",
                        profile.Metadata.SourceVersion),
                    m_smallLabelStyle,
                    NativeGUILayout.ExpandWidth(true));
            }
            if (!string.IsNullOrWhiteSpace(profile.Metadata?.CreatedUtc))
            {
                NativeGUILayout.Label(
                    UnmaText.Format(
                        "options.transfer.created_utc",
                        "Gespeichert (UTC): {0}",
                        profile.Metadata.CreatedUtc),
                    m_smallLabelStyle,
                    NativeGUILayout.ExpandWidth(true));
            }
            if ((profile.Metadata?.SkippedItems ?? 0) > 0)
            {
                NativeGUILayout.Label(
                    UnmaText.Format(
                        "options.transfer.export_skipped",
                        "Beim Speichern übersprungen: {0}",
                        profile.Metadata.SkippedItems),
                    m_warningBannerStyle,
                    NativeGUILayout.ExpandWidth(true));
            }
            foreach (var diagnostic in
                     (profile.Metadata?.Diagnostics ?? new List<string>())
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Take(3))
            {
                NativeGUILayout.Label(
                    diagnostic,
                    m_smallLabelStyle,
                    NativeGUILayout.ExpandWidth(true));
            }
        }
        NativeGUILayout.Label(
            UnmaText.Format(
                "options.transfer.path",
                "Ablage: {0}",
                m_runtime.TransferProfilePath),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (!string.IsNullOrWhiteSpace(m_runtime.LastTransferProfileError))
        {
            NativeGUILayout.Label(
                UnmaText.Format(
                    "options.transfer.last_error",
                    "Letzter Profilfehler: {0}",
                    m_runtime.LastTransferProfileError),
                m_warningBannerStyle,
                NativeGUILayout.ExpandWidth(true));
        }

        NativeGUILayout.BeginHorizontal();
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    profile == null
                        ? "options.transfer.save"
                        : "options.transfer.update",
                    profile == null
                        ? "PROFIL SPEICHERN"
                        : "PROFIL AKTUALISIEREN"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(220f),
                NativeGUILayout.Height(30f)))
        {
            SaveTransferProfileFromOptions();
        }

        previousEnabled = NativeGUI.enabled;
        NativeGUI.enabled = previousEnabled && profile != null;
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "options.transfer.preview",
                    "IMPORT-VORSCHAU"),
                m_buttonStyle,
                NativeGUILayout.Width(220f),
                NativeGUILayout.Height(30f)))
        {
            PreviewTransferProfileFromOptions();
        }
        NativeGUI.enabled = previousEnabled;
        NativeGUILayout.EndHorizontal();

        DrawTransferImportPreview();
    }

    private void DrawTransferCategory(
        ref bool enabled,
        string labelKey,
        string fallbackLabel,
        string hintKey,
        string fallbackHint)
    {
        var updated = NativeGUILayout.Toggle(
            enabled,
            UnmaText.Get(labelKey, fallbackLabel));
        if (updated != enabled)
        {
            enabled = updated;
            InvalidateTransferPreview();
        }
        NativeGUILayout.Label(
            UnmaText.Get(hintKey, fallbackHint),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
    }

    private List<TransferRuleRow> BuildTransferRuleRows(
        UnmaTransferProfile profile)
    {
        var rows = new Dictionary<string, TransferRuleRow>(
            StringComparer.Ordinal);
        AddCurrentTransferRuleRows(
            rows,
            m_runtime.Configuration.VanillaNotificationRules);
        AddProfileTransferRuleRows(
            rows,
            profile?.NotificationRules);
        return rows.Values
            .OrderBy(row =>
                row.DisplayRule?.Scope == VanillaNotificationScope.Entity
                    ? 1
                    : 0)
            .ThenBy(
                row => row.DisplayRule?.AlarmId ?? "",
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Identity, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddCurrentTransferRuleRows(
        IDictionary<string, TransferRuleRow> rows,
        IEnumerable<VanillaNotificationRule> rules)
    {
        if (rules == null)
        {
            return;
        }
        foreach (var rule in rules.Where(rule => rule != null))
        {
            var identity = VanillaNotificationSuppressionPolicy.RuleIdentity(
                rule);
            if (string.IsNullOrWhiteSpace(identity))
            {
                continue;
            }
            if (!rows.TryGetValue(identity, out var row))
            {
                row = new TransferRuleRow
                {
                    Identity = identity,
                };
                rows.Add(identity, row);
            }
            row.CurrentRule = rule;
        }
    }

    private static void AddProfileTransferRuleRows(
        IDictionary<string, TransferRuleRow> rows,
        IEnumerable<TransferNotificationRule> rules)
    {
        if (rules == null)
        {
            return;
        }
        foreach (var rule in rules.Where(rule => rule != null))
        {
            var displayRule = new VanillaNotificationRule
            {
                AlarmId = rule.AlarmId,
                Scope = rule.Scope,
                Behavior = rule.Behavior,
                EntityPrototypeId = rule.EntityPrototypeId,
            };
            var identity = VanillaNotificationSuppressionPolicy.RuleIdentity(
                displayRule);
            if (string.IsNullOrWhiteSpace(identity))
            {
                continue;
            }
            if (!rows.TryGetValue(identity, out var row))
            {
                row = new TransferRuleRow
                {
                    Identity = identity,
                };
                rows.Add(identity, row);
            }
            row.ProfileRule = rule;
            row.ProfileDisplayRule = displayRule;
        }
    }

    private void InitializeTransferProfileUi(
        UnmaTransferProfile profile,
        IReadOnlyList<TransferRuleRow> ruleRows)
    {
        if (m_transferProfileUiInitialized)
        {
            return;
        }

        m_transferProfileUiInitialized = true;
        m_transferProfileName = profile?.Metadata?.Name;
        if (string.IsNullOrWhiteSpace(m_transferProfileName))
        {
            m_transferProfileName = UnmaText.Get(
                "options.transfer.default_name",
                "Standard");
        }

        var storedSelection = profile?.Selection;
        if (storedSelection != null)
        {
            m_transferNotificationBehaviors =
                storedSelection.NotificationBehaviors;
            m_transferSoundSettings = storedSelection.SoundSettings;
            m_transferAppearance = storedSelection.Appearance;
            m_transferSystemAlarms = storedSelection.SystemAlarms;
            m_transferWindowLayout = storedSelection.WindowLayout;
        }

        var exactRuleSelection = storedSelection?.NotificationRuleIdentities;
        foreach (var row in ruleRows)
        {
            m_transferKnownRuleIdentities.Add(row.Identity);
            if (IsPortableTransferRule(row) &&
                (exactRuleSelection == null ||
                 exactRuleSelection.Contains(row.Identity)))
            {
                m_transferSelectedRuleIdentities.Add(row.Identity);
            }
        }
    }

    private void TrackNewTransferRules(
        IReadOnlyList<TransferRuleRow> ruleRows)
    {
        foreach (var row in ruleRows)
        {
            if (!m_transferKnownRuleIdentities.Add(row.Identity) ||
                !IsPortableTransferRule(row))
            {
                continue;
            }
            m_transferSelectedRuleIdentities.Add(row.Identity);
            InvalidateTransferPreview();
        }
    }

    private void DrawTransferRuleRow(TransferRuleRow row)
    {
        var rule = row.DisplayRule;
        if (rule == null)
        {
            return;
        }
        var portable = IsPortableTransferRule(row);
        NativeGUILayout.BeginVertical(
            "transfer-rule:" + row.Identity,
            m_panelStyle,
            NativeGUILayout.ExpandWidth(true));
        if (portable)
        {
            var previousEnabled = NativeGUI.enabled;
            NativeGUI.enabled = previousEnabled &&
                                m_transferNotificationBehaviors;
            var selected = m_transferSelectedRuleIdentities.Contains(
                row.Identity);
            var updated = NativeGUILayout.Toggle(
                selected,
                string.IsNullOrWhiteSpace(rule.AlarmId)
                    ? row.Identity
                    : rule.AlarmId);
            NativeGUI.enabled = previousEnabled;
            if (updated != selected)
            {
                if (updated)
                {
                    m_transferSelectedRuleIdentities.Add(row.Identity);
                }
                else
                {
                    m_transferSelectedRuleIdentities.Remove(row.Identity);
                }
                InvalidateTransferPreview();
            }
        }
        else
        {
            NativeGUILayout.Label(
                string.IsNullOrWhiteSpace(rule.AlarmId)
                    ? row.Identity
                    : rule.AlarmId,
                m_headerStyle,
                NativeGUILayout.ExpandWidth(true));
        }

        NativeGUILayout.Label(
            TransferRuleScopeLabel(rule) + " · " +
            (portable
                ? UnmaText.Get(
                    "options.transfer.rule_portable",
                    "ÜBERTRAGBAR")
                : UnmaText.Get(
                    "options.transfer.rule_not_portable",
                    "NICHT ÜBERTRAGBAR · WIRD ÜBERSPRUNGEN")),
            portable ? m_smallLabelStyle : m_warningBannerStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            TransferRuleSourceAndBehaviorLabel(row),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.EndVertical();
        NativeGUILayout.Space(3f);
    }

    private static bool IsPortableTransferRule(TransferRuleRow row)
    {
        return row?.DisplayRule != null &&
               row.DisplayRule.Scope != VanillaNotificationScope.Entity;
    }

    private static string TransferRuleScopeLabel(
        VanillaNotificationRule rule)
    {
        return rule.Scope switch
        {
            VanillaNotificationScope.EntityPrototype => UnmaText.Format(
                "options.transfer.rule_scope_prototype",
                "OBJEKTTYP · {0}",
                rule.EntityPrototypeId),
            VanillaNotificationScope.Entity => UnmaText.Format(
                "options.transfer.rule_scope_entity",
                "EINZELOBJEKT · ENTITY {0}",
                rule.EntityId),
            _ => UnmaText.Get(
                "options.transfer.rule_scope_notification",
                "MELDUNGSTYP"),
        };
    }

    private static string TransferRuleSourceAndBehaviorLabel(
        TransferRuleRow row)
    {
        var parts = new List<string>();
        if (row.CurrentRule != null)
        {
            parts.Add(
                UnmaText.Get(
                    "options.transfer.rule_from_current_save",
                    "AKTUELLER SPIELSTAND") + " · " +
                VanillaBehaviorLabel(row.CurrentRule.Behavior));
        }
        if (row.ProfileRule != null)
        {
            parts.Add(
                UnmaText.Get(
                    "options.transfer.rule_from_profile",
                    "IM PROFIL") + " · " +
                VanillaBehaviorLabel(row.ProfileRule.Behavior));
        }
        return string.Join(" · ", parts);
    }

    private TransferProfileSelection BuildTransferProfileSelection()
    {
        var notificationRuleIdentities = new HashSet<string>(
            m_transferSelectedRuleIdentities,
            StringComparer.Ordinal);
        if (m_transferNotificationBehaviors)
        {
            foreach (var rule in m_runtime.Configuration
                         .VanillaNotificationRules ??
                     Enumerable.Empty<VanillaNotificationRule>())
            {
                if (rule?.Scope != VanillaNotificationScope.Entity)
                {
                    continue;
                }
                var identity =
                    VanillaNotificationSuppressionPolicy.RuleIdentity(rule);
                if (identity.Length > 0)
                {
                    notificationRuleIdentities.Add(identity);
                }
            }
        }

        return new TransferProfileSelection
        {
            NotificationBehaviors = m_transferNotificationBehaviors,
            SoundSettings = m_transferSoundSettings,
            Appearance = m_transferAppearance,
            SystemAlarms = m_transferSystemAlarms,
            WindowLayout = m_transferWindowLayout,
            NotificationRuleIdentities = notificationRuleIdentities
                .OrderBy(identity => identity, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private void SaveTransferProfileFromOptions()
    {
        var name = m_transferProfileName?.Trim() ?? "";
        if (name.Length == 0)
        {
            SetStatus(UnmaText.Get(
                "options.transfer.name_required",
                "Gib einen Profilnamen ein."));
            return;
        }
        if (m_runtime.SaveTransferProfile(
                name,
                BuildTransferProfileSelection()))
        {
            m_transferProfileName = name;
            m_transferImportPreview = null;
            var savedProfile = m_runtime.GetTransferProfile();
            var skipped = savedProfile?.Metadata?.SkippedItems ?? 0;
            SetStatus(
                skipped > 0
                    ? UnmaText.Format(
                        "options.transfer.save_ok_skipped",
                        "Profil '{0}' gespeichert; {1} nicht übertragbare " +
                        "Einträge übersprungen.",
                        name,
                        skipped)
                    : UnmaText.Format(
                        "options.transfer.save_ok",
                        "Profil '{0}' wurde spielstandsübergreifend gespeichert.",
                        name),
                skipped > 0
                    ? StatusSeverity.Warning
                    : StatusSeverity.Success);
            return;
        }
        SetStatus(
            UnmaText.Format(
                "options.transfer.save_failed",
                "Profil konnte nicht gespeichert werden: {0}",
                TransferProfileError()),
            StatusSeverity.Error,
            true);
    }

    private void PreviewTransferProfileFromOptions()
    {
        if (m_runtime.GetTransferProfile() == null)
        {
            SetStatus(UnmaText.Get(
                "options.transfer.no_profile",
                "Speichere zuerst ein Profil."));
            return;
        }
        var selection = BuildTransferProfileSelection();
        var preview = m_runtime.PreviewTransferProfile(selection);
        if (preview == null)
        {
            SetStatus(
                UnmaText.Format(
                    "options.transfer.preview_failed",
                    "Import-Vorschau konnte nicht erstellt werden: {0}",
                    TransferProfileError()),
                StatusSeverity.Error,
                true);
            return;
        }
        AppendMissingTransferSoundDiagnostics(
            preview,
            m_runtime.GetTransferProfile(),
            selection);
        m_transferImportPreview = preview;
    }

    private void DrawTransferImportPreview()
    {
        if (m_transferImportPreview == null)
        {
            return;
        }
        NativeGUILayout.BeginVertical(
            "transfer-import-preview",
            m_panelStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.preview_title",
                "IMPORT-VORSCHAU · NOCH NICHT ANGEWENDET"),
            m_headerStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            TransferPreviewSummary(m_transferImportPreview),
            m_warningBannerStyle,
            NativeGUILayout.ExpandWidth(true));
        NativeGUILayout.Label(
            UnmaText.Get(
                "options.transfer.preview_hint",
                "Bilanz prüfen und den Merge-Import anschließend bestätigen."),
            m_smallLabelStyle,
            NativeGUILayout.ExpandWidth(true));
        foreach (var diagnostic in
                 (m_transferImportPreview.Diagnostics ?? new List<string>())
                 .Where(item => !string.IsNullOrWhiteSpace(item))
                 .Take(5))
        {
            NativeGUILayout.Label(
                diagnostic,
                m_smallLabelStyle,
                NativeGUILayout.ExpandWidth(true));
        }
        if (NativeGUILayout.Button(
                UnmaText.Get(
                    "options.transfer.import_confirm",
                    "MERGE-IMPORT BESTÄTIGEN"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(260f),
                NativeGUILayout.Height(32f)))
        {
            ImportTransferProfileFromOptions();
        }
        NativeGUILayout.EndVertical();
    }

    private void ImportTransferProfileFromOptions()
    {
        var selection = BuildTransferProfileSelection();
        if (m_runtime.ImportTransferProfile(
                selection,
                out var appliedPreview))
        {
            if (selection.WindowLayout)
            {
                ApplyWindowLayoutFromConfiguration();
            }
            appliedPreview ??= m_transferImportPreview;
            SetStatus(
                UnmaText.Format(
                    "options.transfer.import_ok",
                    "Profil importiert: {0} neu, {1} geändert, " +
                    "{2} unverändert, {3} übersprungen.",
                    appliedPreview?.Added ?? 0,
                    appliedPreview?.Changed ?? 0,
                    appliedPreview?.Unchanged ?? 0,
                    appliedPreview?.Skipped ?? 0),
                (appliedPreview?.Skipped ?? 0) > 0
                    ? StatusSeverity.Warning
                    : StatusSeverity.Success);
            m_transferImportPreview = null;
            m_optionsColorDraftInitialized = false;
            return;
        }
        SetStatus(
            UnmaText.Format(
                "options.transfer.import_failed",
                "Profil konnte nicht importiert werden: {0}",
                TransferProfileError()),
            StatusSeverity.Error,
            true);
    }

    private static string TransferPreviewSummary(
        TransferImportPreview preview)
    {
        return UnmaText.Format(
            "options.transfer.preview_summary",
            "{0} neu · {1} geändert · {2} unverändert · " +
            "{3} übersprungen",
            preview.Added,
            preview.Changed,
            preview.Unchanged,
            preview.Skipped);
    }

    private void AppendMissingTransferSoundDiagnostics(
        TransferImportPreview preview,
        UnmaTransferProfile profile,
        TransferProfileSelection selection)
    {
        if (preview == null || profile == null || selection == null)
        {
            return;
        }
        var availableSoundIds = new HashSet<string>(
            (m_audio?.GetSoundOptions() ?? Array.Empty<SoundOption>())
            .Where(option => option != null &&
                             !string.IsNullOrWhiteSpace(option.Id))
            .Select(option => option.Id),
            StringComparer.OrdinalIgnoreCase);
        var requestedSoundIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (selection.SoundSettings)
        {
            foreach (var soundId in (profile.SoundSettings ??
                         new List<TransferSoundSetting>())
                     .Select(setting => setting?.SoundId))
            {
                if (!string.IsNullOrWhiteSpace(soundId))
                {
                    requestedSoundIds.Add(soundId.Trim());
                }
            }
        }
        if (selection.SystemAlarms)
        {
            foreach (var soundId in (profile.SystemAlarms ??
                         new List<SystemAlarmDefinition>())
                     .Where(alarm => alarm != null)
                     .SelectMany(alarm => alarm.Stages ??
                         new List<SystemAlarmStageDefinition>())
                     .Where(stage => stage != null)
                     .Select(stage => stage.SoundId))
            {
                if (!string.IsNullOrWhiteSpace(soundId))
                {
                    requestedSoundIds.Add(soundId.Trim());
                }
            }
        }
        foreach (var soundId in requestedSoundIds
                     .Where(soundId => !availableSoundIds.Contains(soundId))
                     .OrderBy(soundId => soundId, StringComparer.OrdinalIgnoreCase))
        {
            var diagnostic = UnmaText.Format(
                "options.transfer.sound_missing",
                "Sound '{0}' is unavailable; UNMA will use its normal " +
                "fallback until the sound is installed.",
                soundId);
            if (!preview.Diagnostics.Contains(diagnostic))
            {
                preview.Diagnostics.Add(diagnostic);
            }
        }
    }

    private string TransferProfileError()
    {
        return string.IsNullOrWhiteSpace(m_runtime.LastTransferProfileError)
            ? UnmaText.Get(
                "options.transfer.unknown_error",
                "Unbekannter Fehler")
            : m_runtime.LastTransferProfileError;
    }

    private void InvalidateTransferPreview()
    {
        if (m_transferImportPreview == null)
        {
            return;
        }
        m_transferImportPreview = null;
        SetStatus(UnmaText.Get(
            "options.transfer.preview_invalidated",
            "Auswahl geändert. Bitte Vorschau erneut erstellen."));
    }

    private string DrawOptionsColorField(
        string label,
        string value,
        bool ownRow)
    {
        if (ownRow)
        {
            NativeGUILayout.BeginHorizontal();
        }
        NativeGUILayout.Label(
            label,
            m_labelStyle,
            NativeGUILayout.Width(110f),
            NativeGUILayout.Height(30f));
        var updated = NativeGUILayout.TextField(
            value,
            9,
            m_textFieldStyle,
            NativeGUILayout.Width(112f),
            NativeGUILayout.Height(30f));
        if (ownRow)
        {
            NativeGUILayout.EndHorizontal();
        }
        return updated;
    }

    private static string LocalizeExternalDiagnosticMessage(string code)
    {
        return code switch
        {
            "provider.null" => UnmaText.Get(
                "options.integration.diagnostic.provider.null",
                "Null provider descriptor was ignored."),
            "provider.invalid_id" => UnmaText.Get(
                "options.integration.diagnostic.provider.invalid_id",
                "Provider ID is invalid."),
            "provider.invalid_root" => UnmaText.Get(
                "options.integration.diagnostic.provider.invalid_root",
                "Provider root directory is invalid or unavailable."),
            "provider.duplicate" => UnmaText.Get(
                "options.integration.diagnostic.provider.duplicate",
                "Duplicate provider descriptor was ignored."),
            "provider.scan_failed" => UnmaText.Get(
                "options.integration.diagnostic.provider.scan_failed",
                "JSON files could not be enumerated."),
            "provider.file_limit" => UnmaText.Get(
                "options.integration.diagnostic.provider.file_limit",
                "Provider contains too many JSON files; excess files were ignored."),
            "provider.alarm_limit" => UnmaText.Get(
                "options.integration.diagnostic.provider.alarm_limit",
                "Provider declares too many alarms; excess declarations were ignored."),
            "alarm.invalid" => UnmaText.Get(
                "options.integration.diagnostic.alarm.invalid",
                "Alarm definition is invalid."),
            "alarm.duplicate" => UnmaText.Get(
                "options.integration.diagnostic.alarm.duplicate",
                "Duplicate alarm ID was ignored."),
            "alarm.localization_namespace_conflict" => UnmaText.Get(
                "options.integration.diagnostic.alarm.localization_namespace_conflict",
                "Localization namespace is already owned by another provider."),
            "file.too_large" => UnmaText.Get(
                "options.integration.diagnostic.file.too_large",
                "Definition file exceeds the size limit."),
            "file.invalid_json" => UnmaText.Get(
                "options.integration.diagnostic.file.invalid_json",
                "Definition file is invalid JSON or could not be read."),
            "file.empty" => UnmaText.Get(
                "options.integration.diagnostic.file.empty",
                "Definition file does not contain a JSON object."),
            "file.unsupported_schema" => UnmaText.Get(
                "options.integration.diagnostic.file.unsupported_schema",
                "Definition file uses an unsupported schema version."),
            "file.provider_mismatch" => UnmaText.Get(
                "options.integration.diagnostic.file.provider_mismatch",
                "mod_id does not match the provider mod ID."),
            "file.alarms_required" => UnmaText.Get(
                "options.integration.diagnostic.file.alarms_required",
                "alarms must be a JSON array."),
            "file.alarm_limit" => UnmaText.Get(
                "options.integration.diagnostic.file.alarm_limit",
                "Definition file contains too many alarms."),
            _ => UnmaText.Get(
                "options.integration.diagnostic.unknown",
                "See the game log for diagnostic details."),
        };
    }


    private void DrawDetachedPanelContent(
        DetachedPanel detached,
        PanelDefinition panel)
    {
        DrawStatusMessage();
        var alarms = GetPanelViews(panel);
        var activeCount = alarms.Count(alarm => alarm.IsActive);
        var unacknowledgedCount = alarms.Count(alarm =>
            alarm.RequiresAcknowledgement);
        NativeGUILayout.BeginHorizontal();
        NativeGUILayout.Label(
            UnmaText.Get("auto.397544fe1d24") + activeCount +
            UnmaText.Get("auto.ddc0834bf463") + unacknowledgedCount,
            m_smallLabelStyle);
        if (NativeGUILayout.Button(
                UnmaText.Get("board.acknowledge_panel", "PANEL ACK"),
                m_dangerButtonStyle,
                NativeGUILayout.Width(130f)))
        {
            AcknowledgePanelAlarms(panel);
        }
        if (NativeGUILayout.Button(
                UnmaText.Get("board.next_alarm", "NEXT ALARM"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(120f)))
        {
            NavigateToNextUnacknowledgedAlarm(panel);
        }
        if (!panel.IsDashboard && NativeGUILayout.Button(
                UnmaText.Get("auto.d5302ca93907"),
                m_primaryButtonStyle,
                NativeGUILayout.Width(120f)))
        {
            OpenNewRuleEditor(panel);
        }
        NativeGUILayout.EndHorizontal();

        detached.Scroll = NativeGUILayout.BeginScrollView(detached.Scroll);
        DrawAlarmGrid(
            alarms,
            Math.Max(1, Math.Min(panel.Columns, 5)),
            detached.Rect.width - 54f,
            detached.Scroll.y,
            Math.Max(180f, detached.Rect.height - 100f),
            null,
            panel,
            false,
            panel.IsDashboard
                ? UnmaText.Get("auto.f895fe84e658")
                : UnmaText.Get("auto.e8bad0a4452b"),
            !panel.IsDashboard);
        NativeGUILayout.EndScrollView();
    }

    private static string GetDetachedPanelTitle(PanelDefinition panel)
    {
        return UnmaText.Get("auto.528ebd6136c2") + panel.Name;
    }

    private void DrawAlarmGrid(
        IReadOnlyList<AlarmView> alarms,
        int columns,
        float availableWidth,
        float scrollY,
        float viewportHeight,
        PanelDefinition interactionPanel,
        PanelDefinition displayPanel,
        bool assignmentPending,
        string emptyMessage,
        bool drawEmptyCells)
    {
        columns = Math.Max(1, Math.Min(8, columns));
        var showCreationTarget = assignmentPending &&
                                 interactionPanel != null;
        var itemCount = alarms.Count + (showCreationTarget ? 1 : 0);
        if (itemCount == 0)
        {
            NativeGUILayout.Space(20f);
            NativeGUILayout.Label(
                emptyMessage,
                m_labelStyle);
            return;
        }

        var tileWidth = Math.Max(140f, (availableWidth -
            (columns - 1) * 6f) / columns);
        var rowHeight = TileHeight + 6f;
        var rowCount = (itemCount + columns - 1) / columns;
        var firstVisibleRow = Math.Max(
            0,
            Mathf.FloorToInt(scrollY / rowHeight) - 2);
        var lastVisibleRow = Math.Min(
            rowCount,
            Mathf.CeilToInt((scrollY + viewportHeight) / rowHeight) + 2);
        if (firstVisibleRow > 0)
        {
            NativeGUILayout.Space(firstVisibleRow * rowHeight);
        }

        for (var row = firstVisibleRow; row < lastVisibleRow; row++)
        {
            var rowStart = row * columns;
            var columnsInRow = drawEmptyCells
                ? columns
                : Math.Min(columns, itemCount - rowStart);
            var rowKey = "alarm-grid:" + (displayPanel?.Id ?? "") + ":";
            for (var keyColumn = 0;
                 keyColumn < columnsInRow;
                 keyColumn++)
            {
                var keyIndex = rowStart + keyColumn;
                if (keyIndex < alarms.Count)
                {
                    var alarm = alarms[keyIndex];
                    rowKey += "|" + (
                        string.IsNullOrWhiteSpace(alarm.Key)
                            ? alarm.Sequence.ToString(
                                CultureInfo.InvariantCulture)
                            : alarm.Key);
                }
                else
                {
                    rowKey += showCreationTarget && keyIndex == alarms.Count
                        ? "|creation-target"
                        : "|empty-" + keyIndex.ToString(
                            CultureInfo.InvariantCulture);
                }
            }
            NativeGUILayout.BeginHorizontal(rowKey);
            for (var column = 0; column < columnsInRow; column++)
            {
                var index = rowStart + column;
                var rect = NativeGUILayoutUtility.GetRect(
                    tileWidth,
                    TileHeight,
                    NativeGUILayout.Width(tileWidth),
                    NativeGUILayout.Height(TileHeight));
                if (index < alarms.Count)
                {
                    var alarm = alarms[index];
                    var isAudioSnoozed =
                        alarm.RequiresAcknowledgement &&
                        displayPanel != null &&
                        m_runtime.IsAlarmAudioSnoozed(
                            displayPanel.Id,
                            PanelSlotProjection.StableAlarmId(alarm));
                    DrawAlarmTile(
                        rect,
                        alarm,
                        displayPanel,
                        isAudioSnoozed);
                    if (assignmentPending && interactionPanel != null)
                    {
                        DrawExistingAssignmentTarget(
                            rect,
                            interactionPanel,
                            alarm);
                    }
                    else if (!m_entityAssignmentPending)
                    {
                        var hasEntityVanillaControls =
                            IsEntityVanillaTile(
                                displayPanel,
                                alarm);
                        var hasNavigationButton =
                            !hasEntityVanillaControls &&
                            m_runtime.TryResolveNavigationEntity(
                                displayPanel,
                                alarm,
                                out _);
                        var hasRightActionColumn =
                            alarm.RequiresAcknowledgement ||
                            hasNavigationButton;
                        var tileClickRect = hasRightActionColumn
                            ? new Rect(
                                rect.x,
                                rect.y,
                                rect.width - 35f,
                                rect.height)
                            : rect;
                        if (hasEntityVanillaControls)
                        {
                            tileClickRect.height = Math.Max(
                                0f,
                                tileClickRect.height - 31f);
                        }
                        var customAlarm =
                            PanelSlotProjection.TryGetCustomRuleId(
                                alarm,
                                out _);
                        var editTooltip = customAlarm
                            ? UnmaText.Get(
                                "alarm_tile.edit_tooltip",
                                "Edit this custom alarm.")
                            : "";
                        if (NativeGUI.Button(
                                tileClickRect,
                                new GUIContent("", editTooltip),
                                GUIStyle.none,
                                new NativeControlMetadata(
                                    "alarm-tile-overlay-" +
                                    PanelSlotProjection.StableAlarmId(alarm),
                                    editTooltip,
                                    false,
                                    -1)))
                        {
                            HandleAlarmTileClick(alarm);
                        }
                        if (!hasEntityVanillaControls)
                        {
                            DrawAlarmNavigationButton(
                                rect,
                                alarm,
                                displayPanel);
                        }
                        DrawAlarmEditButton(rect, alarm);
                        DrawAlarmAudioSnoozeButton(
                            rect,
                            alarm,
                            displayPanel,
                            isAudioSnoozed);
                        DrawAlarmAcknowledgeButton(
                            rect,
                            alarm,
                            displayPanel);
                    }
                }
                else if (showCreationTarget && index == alarms.Count)
                {
                    DrawNewAssignmentTarget(
                        rect,
                        interactionPanel,
                        index);
                }
                else if (drawEmptyCells)
                {
                    DrawEmptyTile(rect);
                }
                if (column < columnsInRow - 1)
                {
                    NativeGUILayout.Space(6f);
                }
            }
            NativeGUILayout.EndHorizontal();
            NativeGUILayout.Space(6f);
        }
        if (lastVisibleRow < rowCount)
        {
            NativeGUILayout.Space((rowCount - lastVisibleRow) * rowHeight);
        }
    }

    private IReadOnlyList<AlarmView> GetPanelViews(PanelDefinition panel)
    {
        if (!m_panelViewCache.TryGetValue(panel.Id, out var entry))
        {
            entry = new PanelViewCacheEntry();
            m_panelViewCache[panel.Id] = entry;
        }
        if (entry.Frame != Time.frameCount)
        {
            entry.Frame = Time.frameCount;
            entry.Views = m_runtime.GetViews(panel);
        }
        return entry.Views;
    }

    private void HandleAlarmTileClick(AlarmView alarm)
    {
        if (m_entityAssignmentPending)
        {
            return;
        }

        var alarmId = PanelSlotProjection.StableAlarmId(alarm);
        var now = Time.realtimeSinceStartup;
        if (!string.Equals(
                m_lastAlarmTileClickId,
                alarmId,
                StringComparison.Ordinal) ||
            now - m_lastAlarmTileClickAt > 0.5f)
        {
            m_lastAlarmTileClickId = alarmId;
            m_lastAlarmTileClickAt = now;
            return;
        }

        m_lastAlarmTileClickId = "";
        m_lastAlarmTileClickAt = 0f;
        OpenRuleFromAlarmTile(alarm);
    }

    private bool OpenRuleFromAlarmTile(AlarmView alarm)
    {
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return true;
        }
        if (!PanelSlotProjection.TryGetCustomRuleId(alarm, out var ruleId))
        {
            return false;
        }

        var rule = m_runtime.Configuration.Rules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        if (rule == null)
        {
            m_isOpen = true;
            SetStatus(
                UnmaText.Get("auto.b4e21cd402a4"));
            return true;
        }

        var alreadyEditing = string.Equals(
                m_editingRuleId,
                rule.Id,
                StringComparison.Ordinal);
        if (!alreadyEditing)
        {
            if (HasDraftRuleWork())
            {
                OpenRuleEditorWindow();
                SetDraftConflictStatus(
                    UnmaText.Get("auto.67e43fb81ece") +
                    UnmaText.Get("auto.90f638ce60bc"));
                return true;
            }
            BeginEditingRule(rule, m_audio.GetSoundOptions());
        }

        var primaryPanel = m_runtime.Configuration.Panels.FirstOrDefault(
            panel => string.Equals(
                panel.Id,
                rule.PanelId,
                StringComparison.Ordinal));
        if (PanelTopologyPolicy.IsEntityPanel(primaryPanel))
        {
            m_activeEntityPanelId = primaryPanel.Id;
        }
        else
        {
            SelectGlobalPanel(primaryPanel, true);
        }
        m_isOpen = true;
        SelectMainTab(TabBoard);
        OpenRuleEditorWindow();

        var firstCondition = rule.Conditions.FirstOrDefault();
        if (alreadyEditing)
        {
            SetStatus(UnmaText.Get("auto.7f308a23243a"));
        }
        else if (firstCondition != null)
        {
            OpenConditionSource(firstCondition, true);
        }
        else
        {
            SetStatus(UnmaText.Get("auto.93f638d82e65"));
        }
        return true;
    }

    private void DrawAlarmTile(
        Rect rect,
        AlarmView alarm,
        PanelDefinition displayPanel,
        bool isAudioSnoozed)
    {
        var customRule = TryGetCustomRule(alarm);
        var inactive = customRule?.Enabled == false;
        var background = CoiUiPalette.Text;
        if (inactive)
        {
            background = CoiUiPalette.SurfaceRaised;
        }
        else if (alarm.IsActive || alarm.IsGoneUnacknowledged)
        {
            var active = ParseColor(alarm.ActiveColor, Color.yellow);
            var blinkOn = m_runtime.Configuration.ReducedMotion ||
                          !alarm.RequiresAcknowledgement ||
                          Mathf.FloorToInt(Time.realtimeSinceStartup * 2.2f) %
                          2 == 0;
            background = blinkOn
                ? active
                : CoiUiPalette.Surface;
        }
        else if (alarm.IsMissingSource)
        {
            background = new Color(0.76f, 0.70f, 0.60f, 1f);
        }

        DrawPanelRect(rect, Color.black);
        var inner = new Rect(
            rect.x + 4f,
            rect.y + 4f,
            rect.width - 8f,
            rect.height - 8f);
        DrawPanelRect(inner, background);

        var useLightText = AlarmUiErgonomics.ShouldUseLightText(
            background.r,
            background.g,
            background.b);
        var titleStyle = useLightText
            ? m_tileTitleLightStyle
            : m_tileTitleStyle;
        var detailStyle = useLightText
            ? m_tileDetailLightStyle
            : m_tileDetailStyle;

        var badge = inactive
            ? UnmaText.Get("ui.editor.inactive_badge", "INACTIVE")
            : alarm.IsGoneUnacknowledged
            ? UnmaText.Get("auto.3f6e1a7c5590")
            : alarm.IsActive
                ? alarm.IsAcknowledged
                    ? UnmaText.Get("ui.alarm.state.active_acknowledged", "ACTIVE")
                    : UnmaText.Get("ui.alarm.state.incoming", "INCOMING")
                : alarm.IsMissingSource
                    ? UnmaText.Get("auto.6a49896902cb")
                    : UnmaText.Get("ui.alarm.state.normal", "NORMAL");
        if ((alarm.IsActive || alarm.IsGoneUnacknowledged) &&
            alarm.IsMissingSource)
        {
            badge += UnmaText.Get("auto.70ab47b6f195");
        }
        if (isAudioSnoozed)
        {
            badge = UnmaText.Get(
                "alarm_tile.audio_snoozed_badge",
                "Z · 1M") + " · " + badge;
        }
        var acknowledgementInset = alarm.RequiresAcknowledgement ? 32f : 0f;
        NativeGUI.Label(
            new Rect(
                inner.x + 7f,
                inner.y + 5f,
                inner.width - 14f - acknowledgementInset,
                18f),
            badge + " · " + SeverityLabel(alarm.Severity),
            detailStyle);
        NativeGUI.Label(
            new Rect(
                inner.x + 7f,
                inner.y + 24f,
                inner.width - 14f - acknowledgementInset,
                48f),
            (alarm.Name ?? UnmaText.Get(
                "ui.common.alarm",
                "ALARM")).ToUpperInvariant(),
            titleStyle);
        NativeGUI.Label(
            new Rect(
                inner.x + 7f,
                inner.y + 72f,
                inner.width - 14f,
                IsEntityVanillaTile(displayPanel, alarm) ? 13f : 25f),
            alarm.Detail ?? "",
            detailStyle);
        DrawEntityVanillaBehaviorButtons(inner, alarm, displayPanel);
    }

    private AlarmRuleDefinition TryGetCustomRule(AlarmView alarm)
    {
        if (!PanelSlotProjection.TryGetCustomRuleId(alarm, out var ruleId))
        {
            return null;
        }
        return m_runtime.Configuration.Rules.FirstOrDefault(rule =>
            string.Equals(rule?.Id, ruleId, StringComparison.Ordinal));
    }

    private static bool IsEntityVanillaTile(
        PanelDefinition panel,
        AlarmView alarm)
    {
        return PanelTopologyPolicy.IsEntityPanel(panel) &&
               alarm != null &&
               string.Equals(
                   alarm.Source,
                   "vanilla",
                   StringComparison.Ordinal) &&
               VanillaNotificationSuppressionPolicy.IsVanillaOverrideId(
                   VanillaNotificationSuppressionPolicy
                       .GetOverrideIdForSlotId(
                           PanelSlotProjection.StableAlarmId(alarm)));
    }

    private void DrawEntityVanillaBehaviorButtons(
        Rect inner,
        AlarmView alarm,
        PanelDefinition panel)
    {
        if (!IsEntityVanillaTile(panel, alarm))
        {
            return;
        }
        var overrideId = VanillaNotificationSuppressionPolicy
            .GetOverrideIdForSlotId(
                PanelSlotProjection.StableAlarmId(alarm));
        var entityBehavior = m_runtime.GetVanillaNotificationBehavior(
            overrideId,
            VanillaNotificationScope.Entity,
            panel.OwnerEntityId,
            panel.OwnerEntityPrototypeId);
        var prototypeBehavior = m_runtime.GetVanillaNotificationBehavior(
            overrideId,
            VanillaNotificationScope.EntityPrototype,
            panel.OwnerEntityId,
            panel.OwnerEntityPrototypeId);
        var gap = 4f;
        var width = (inner.width - 14f - gap) / 2f;
        var y = inner.yMax - 24f;
        var entityRect = new Rect(inner.x + 7f, y, width, 20f);
        var prototypeRect = new Rect(
            entityRect.xMax + gap,
            y,
            width,
            20f);
        if (NativeGUI.Button(
                entityRect,
                UnmaText.Get("alarm_tile.object", "OBJECT") + ": " +
                CompactVanillaBehaviorLabel(entityBehavior),
                VanillaBehaviorButtonStyle(entityBehavior)))
        {
            SaveVanillaNotificationBehavior(
                overrideId,
                alarm.Name,
                VanillaNotificationScope.Entity,
                NextVanillaBehavior(entityBehavior),
                panel.OwnerEntityId,
                panel.OwnerEntityPrototypeId);
        }
        if (!string.IsNullOrWhiteSpace(panel.OwnerEntityPrototypeId) &&
            NativeGUI.Button(
                prototypeRect,
                UnmaText.Get("alarm_tile.type", "TYPE") + ": " +
                CompactVanillaBehaviorLabel(prototypeBehavior),
                VanillaBehaviorButtonStyle(prototypeBehavior)))
        {
            SaveVanillaNotificationBehavior(
                overrideId,
                alarm.Name,
                VanillaNotificationScope.EntityPrototype,
                NextVanillaBehavior(prototypeBehavior),
                panel.OwnerEntityId,
                panel.OwnerEntityPrototypeId);
        }
    }

    private GUIStyle VanillaBehaviorButtonStyle(
        VanillaNotificationBehavior behavior)
    {
        return behavior == VanillaNotificationBehavior.Hidden ||
               behavior == VanillaNotificationBehavior.Ignored
            ? m_dangerButtonStyle
            : behavior == VanillaNotificationBehavior.Normal
                ? m_primaryButtonStyle
                : m_buttonStyle;
    }

    private static string CompactVanillaBehaviorLabel(
        VanillaNotificationBehavior behavior)
    {
        return behavior switch
        {
            VanillaNotificationBehavior.Silent => UnmaText.Get(
                "alarm_tile.behavior_silent",
                "SILENT"),
            VanillaNotificationBehavior.Hidden => UnmaText.Get(
                "alarm_tile.behavior_hidden",
                "HIDDEN"),
            VanillaNotificationBehavior.Ignored => UnmaText.Get(
                "alarm_tile.behavior_ignored",
                "IGNORED"),
            _ => UnmaText.Get("alarm_tile.behavior_normal", "ON"),
        };
    }

    private void AcknowledgeAllAlarms()
    {
        var count = m_runtime.UnacknowledgedCount;
        m_runtime.AcknowledgeAll();
        m_audio.StopAlarm();
        SetStatus(count > 0
            ? UnmaText.Format(
                "board.acknowledged_count",
                "Acknowledged {0} alarm(s).",
                count)
            : UnmaText.Get(
                "board.no_unacknowledged",
                "No unacknowledged alarms."));
    }

    private void AcknowledgePanelAlarms(PanelDefinition panel)
    {
        if (panel == null)
        {
            return;
        }

        var isAreaScope = IsAreaScopedDashboard(panel);
        int count;
        if (isAreaScope)
        {
            var visibleSlotIds = GetBoardViews(panel)
                .Select(PanelSlotProjection.StableAlarmId)
                .Where(slotId => !string.IsNullOrWhiteSpace(slotId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (!m_runtime.TryAcknowledgeDashboard(
                    NormalizeAlarmAreaFilter(),
                    visibleSlotIds,
                    out count))
            {
                SetStatus(UnmaText.Get(
                    "board.area_unavailable",
                    "The selected area is unavailable."));
                return;
            }
        }
        else
        {
            count = m_runtime.AcknowledgePanel(panel.Id);
        }
        if (count > 0)
        {
            m_audio.StopAlarm();
        }
        SetStatus(count > 0
            ? UnmaText.Format(
                isAreaScope
                    ? "board.area_acknowledged_count"
                    : "board.acknowledged_count",
                isAreaScope
                    ? "Acknowledged {0} alarm(s) in this area."
                    : "Acknowledged {0} alarm(s).",
                count)
            : UnmaText.Get(
                isAreaScope
                    ? "board.area_no_unacknowledged"
                    : "board.no_unacknowledged",
                isAreaScope
                    ? "No unacknowledged alarms in this area."
                    : "No unacknowledged alarms."));
    }

    private void NavigateToNextUnacknowledgedAlarm(PanelDefinition panel)
    {
        if (panel == null)
        {
            SetStatus(UnmaText.Get(
                "board.no_unacknowledged",
                "No unacknowledged alarms."));
            return;
        }

        AlarmView alarm;
        var isAreaScope = IsAreaScopedDashboard(panel);
        if (isAreaScope)
        {
            if (!m_runtime.TryGetNextDashboardUnacknowledged(
                    NormalizeAlarmAreaFilter(),
                    m_lastNavigatedAlarmSlotId,
                    out alarm))
            {
                SetStatus(UnmaText.Get(
                    "board.area_unavailable",
                    "The selected area is unavailable."));
                return;
            }
        }
        else
        {
            alarm = m_runtime.GetNextUnacknowledged(
                panel.Id,
                m_lastNavigatedAlarmSlotId);
        }
        if (alarm == null)
        {
            m_lastNavigatedAlarmSlotId = "";
            SetStatus(UnmaText.Get(
                isAreaScope
                    ? "board.area_no_unacknowledged"
                    : "board.no_unacknowledged",
                isAreaScope
                    ? "No unacknowledged alarms in this area."
                    : "No unacknowledged alarms."));
            return;
        }

        m_lastNavigatedAlarmSlotId =
            PanelSlotProjection.StableAlarmId(alarm);
        SelectMainTab(TabBoard);
        m_isOpen = true;
        if (PanelTopologyPolicy.IsEntityPanel(panel))
        {
            m_activeEntityPanelId = panel.Id;
        }
        else
        {
            SelectGlobalPanel(panel, true);
        }

        var visible = GetBoardViews(panel);
        var alarmIndex = visible.ToList().FindIndex(candidate =>
            string.Equals(
                PanelSlotProjection.StableAlarmId(candidate),
                m_lastNavigatedAlarmSlotId,
                StringComparison.Ordinal));
        if (alarmIndex >= 0)
        {
            var columns = Math.Max(1, Math.Min(8, panel.Columns));
            m_boardScroll.y = Math.Max(
                0f,
                alarmIndex / columns * (TileHeight + 6f) - 12f);
        }

        if (m_runtime.TryResolveNavigationEntity(
                panel,
                alarm,
                out var entity))
        {
            NavigateToEntity(entity);
        }
        SetStatus(UnmaText.Format(
            "board.next_alarm_selected",
            "Selected: {0}",
            alarm.Name ?? UnmaText.Get("ui.common.alarm", "ALARM")));
        SynchronizeNativeWindowVisibility();
        SynchronizeNativeLauncher();
    }

    private void HandleAttentionRequest(AlarmAttentionRequest request)
    {
        if (!request.IsValid || m_isUiSuppressedByMenu)
        {
            return;
        }

        var panel = m_runtime.Configuration.Panels.FirstOrDefault(candidate =>
            candidate != null && string.Equals(
                candidate.Id,
                request.PanelId,
                StringComparison.Ordinal));
        if (panel != null && !GetPanelViews(panel).Any(alarm => string.Equals(
                PanelSlotProjection.StableAlarmId(alarm),
                request.SlotId,
                StringComparison.Ordinal)))
        {
            panel = null;
        }
        if (panel == null)
        {
            panel = m_runtime.Configuration.Panels.FirstOrDefault(candidate =>
                candidate != null && GetPanelViews(candidate).Any(alarm =>
                    string.Equals(
                        PanelSlotProjection.StableAlarmId(alarm),
                        request.SlotId,
                        StringComparison.Ordinal)));
        }
        if (panel == null)
        {
            SetStatus(UnmaText.Get(
                "board.operator_attention_panel_missing",
                "Escalation requested an unavailable panel."));
            return;
        }

        var cancelTemporaryMute =
            request.OperatorAction ==
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute;
        var temporaryMuteEnded = cancelTemporaryMute &&
                                 Time.realtimeSinceStartup <
                                 m_audioMutedUntil;
        if (cancelTemporaryMute)
        {
            m_audioMutedUntil = 0f;
        }

        m_isOpen = true;
        SelectMainTab(TabBoard);
        if (PanelTopologyPolicy.IsEntityPanel(panel))
        {
            m_activeEntityPanelId = panel.Id;
        }
        else
        {
            if (panel.IsDashboard &&
                IsAreaScopedDashboard(panel) &&
                !GetBoardViews(panel).Any(alarm => string.Equals(
                    PanelSlotProjection.StableAlarmId(alarm),
                    request.SlotId,
                    StringComparison.Ordinal)))
            {
                m_alarmAreaFilter = AlarmAreaFilter.All;
            }
            SelectGlobalPanel(panel, true);
        }

        var visible = GetBoardViews(panel);
        var alarmIndex = visible.ToList().FindIndex(candidate => string.Equals(
            PanelSlotProjection.StableAlarmId(candidate),
            request.SlotId,
            StringComparison.Ordinal));
        if (alarmIndex >= 0)
        {
            var columns = Math.Max(1, Math.Min(8, panel.Columns));
            m_boardScroll.y = Math.Max(
                0f,
                alarmIndex / columns * (TileHeight + 6f) - 12f);
        }
        else
        {
            m_boardScroll = Vector2.zero;
        }

        SetStatus(UnmaText.Format(
            temporaryMuteEnded
                ? "board.operator_attention_mute_cancelled"
                : "board.operator_attention",
            temporaryMuteEnded
                ? "Escalation opened panel and ended the temporary five-minute mute: {0}"
                : "Escalation opened panel: {0}",
            panel.Name));
        SynchronizeNativeWindowVisibility();
        SynchronizeNativeLauncher();
    }

    private void DrawAlarmNavigationButton(
        Rect tileRect,
        AlarmView alarm,
        PanelDefinition panel)
    {
        if (!m_runtime.TryResolveNavigationEntity(
                panel,
                alarm,
                out var entity))
        {
            return;
        }

        var buttonRect = new Rect(
            tileRect.xMax - 40f,
            tileRect.yMax - 40f,
            36f,
            36f);
        if (NativeGUI.Button(
                buttonRect,
                new GUIContent(
                    "↗",
                    UnmaText.Get(
                        "alarm_tile.open_object_tooltip",
                        "Open the associated object.")),
                m_primaryButtonStyle,
                new NativeControlMetadata(
                    "alarm-open-object-" +
                    PanelSlotProjection.StableAlarmId(alarm),
                    UnmaText.Get(
                        "alarm_tile.open_object_tooltip",
                        "Open the associated object."))))
        {
            NavigateToEntity(entity);
        }
    }

    private void DrawAlarmEditButton(Rect tileRect, AlarmView alarm)
    {
        if (!PanelSlotProjection.TryGetCustomRuleId(alarm, out _))
        {
            return;
        }
        var buttonRect = new Rect(
            tileRect.x + 4f,
            tileRect.yMax - 34f,
            Math.Max(76f, tileRect.width - 48f),
            30f);
        if (NativeGUI.Button(
                buttonRect,
                new GUIContent(
                    UnmaText.Get("alarm_tile.edit", "EDIT"),
                    UnmaText.Get(
                        "alarm_tile.edit_tooltip",
                        "Edit this custom alarm.")),
                m_buttonStyle,
                new NativeControlMetadata(
                    "alarm-edit-" +
                    PanelSlotProjection.StableAlarmId(alarm),
                    UnmaText.Get(
                        "alarm_tile.edit_tooltip",
                        "Edit this custom alarm."))))
        {
            OpenRuleFromAlarmTile(alarm);
        }
    }

    private void DrawAlarmAcknowledgeButton(
        Rect tileRect,
        AlarmView alarm,
        PanelDefinition panel)
    {
        if (alarm?.RequiresAcknowledgement != true || panel == null)
        {
            return;
        }

        var buttonRect = new Rect(
            tileRect.xMax - 40f,
            tileRect.y + 4f,
            36f,
            36f);
        if (!NativeGUI.Button(
                buttonRect,
                new GUIContent(
                    UnmaText.Get("alarm_tile.acknowledge", "Q"),
                    UnmaText.Get(
                        "alarm_tile.acknowledge_tooltip",
                        "Acknowledge this alarm.")),
                m_dangerButtonStyle,
                new NativeControlMetadata(
                    "alarm-acknowledge-" +
                    PanelSlotProjection.StableAlarmId(alarm),
                    UnmaText.Get(
                        "alarm_tile.acknowledge_tooltip",
                        "Acknowledge this alarm."))))
        {
            return;
        }

        var slotId = PanelSlotProjection.StableAlarmId(alarm);
        var acknowledged = IsAreaScopedDashboard(panel)
            ? m_runtime.TryAcknowledgeDashboard(
                  NormalizeAlarmAreaFilter(),
                  new[] { slotId },
                  out var count) && count > 0
            : m_runtime.AcknowledgeAlarm(panel.Id, slotId);
        if (acknowledged)
        {
            m_audio.StopAlarm();
            SetStatus(UnmaText.Get(
                "board.acknowledged_one",
                "Alarm acknowledged."));
        }
    }

    private void DrawAlarmAudioSnoozeButton(
        Rect tileRect,
        AlarmView alarm,
        PanelDefinition panel,
        bool isAudioSnoozed)
    {
        if (alarm?.RequiresAcknowledgement != true || panel == null)
        {
            return;
        }

        var buttonRect = new Rect(
            tileRect.xMax - 40f,
            tileRect.y + (tileRect.height - 36f) * 0.5f,
            36f,
            36f);
        if (!NativeGUI.Button(
                buttonRect,
                new GUIContent(
                    isAudioSnoozed
                        ? UnmaText.Get("alarm_tile.audio_resume", "R")
                        : UnmaText.Get("alarm_tile.audio_snooze", "Z"),
                    UnmaText.Get(
                        isAudioSnoozed
                            ? "alarm_tile.audio_resume_tooltip"
                            : "alarm_tile.audio_snooze_tooltip",
                        isAudioSnoozed
                            ? "Resume audio for this alarm."
                            : "Snooze audio for one game month.")),
                isAudioSnoozed ? m_primaryButtonStyle : m_buttonStyle,
                new NativeControlMetadata(
                    "alarm-audio-" +
                    PanelSlotProjection.StableAlarmId(alarm),
                    UnmaText.Get(
                        isAudioSnoozed
                            ? "alarm_tile.audio_resume_tooltip"
                            : "alarm_tile.audio_snooze_tooltip",
                        isAudioSnoozed
                            ? "Resume audio for this alarm."
                            : "Snooze audio for one game month."))))
        {
            return;
        }

        m_lastAlarmTileClickId = "";
        m_lastAlarmTileClickAt = 0f;
        var slotId = PanelSlotProjection.StableAlarmId(alarm);
        var changed = isAudioSnoozed
            ? m_runtime.UnsnoozeAlarmAudio(panel.Id, slotId)
            : m_runtime.SnoozeAlarmAudio(
                panel.Id,
                slotId,
                GameTimeWindowPolicy.SimTicksPerMonth);
        if (changed <= 0)
        {
            SetStatus(UnmaText.Get(
                "board.audio_snooze_no_change",
                "Alarm audio state did not change."));
            return;
        }
        if (!isAudioSnoozed)
        {
            m_audio.StopAlarm();
        }
        SetStatus(isAudioSnoozed
            ? UnmaText.Format(
                "board.audio_resumed",
                "Alarm audio resumed ({0} occurrence(s)).",
                changed)
            : UnmaText.Format(
                "board.audio_snoozed",
                "Alarm audio snoozed for 1 game month ({0} occurrence(s)).",
                changed));
    }

    private void NavigateToEntity(int entityId)
    {
        if (!m_runtime.TryGetLiveEntity(entityId, out var entity))
        {
            SetStatus(UnmaText.Get("auto.28a2ba9ec3eb"));
            return;
        }

        NavigateToEntity(entity);
    }

    private void NavigateToEntity(IEntity entity)
    {
        if (entity == null || entity.IsDestroyed)
        {
            SetStatus(UnmaText.Get("auto.28a2ba9ec3eb"));
            return;
        }

        if (entity is IEntityWithPosition positioned)
        {
            m_cameraController?.PanTo(positioned.Position2f);
        }
        m_inspectorsManager?.TryActivateFor(entity);
        SetStatus(UnmaText.Get("auto.75b7d485418f"));
    }

    private void DrawEmptyTile(Rect rect)
    {
        DrawPanelRect(rect, Color.black);
        DrawPanelRect(
            new Rect(
                rect.x + 4f,
                rect.y + 4f,
                rect.width - 8f,
                rect.height - 8f),
            CoiUiPalette.Text);
    }

    private void DrawExistingAssignmentTarget(
        Rect rect,
        PanelDefinition panel,
        AlarmView alarm)
    {
        var canLink = PanelSlotProjection.TryGetCustomRuleId(
            alarm,
            out _);
        var actionRect = new Rect(
            rect.x + 4f,
            rect.y + rect.height - 28f,
            rect.width - 8f,
            24f);
        DrawPanelRect(
            actionRect,
            canLink
                ? CoiUiPalette.Blue
                : CoiUiPalette.Control);
        NativeGUI.Label(
            actionRect,
            canLink
                ? UnmaText.Get("auto.fe5cfa5cedb5")
                : UnmaText.Get("auto.dcc40b537b28"),
            m_assignmentActionStyle);
        if (NativeGUI.Button(
                rect,
                GUIContent.none,
                GUIStyle.none,
                new NativeControlMetadata(
                    "assignment-existing-" +
                    PanelSlotProjection.StableAlarmId(alarm),
                    focusable: false,
                    tabIndex: -1)))
        {
            HandleExistingAssignmentTarget(panel, alarm);
        }
    }

    private void DrawNewAssignmentTarget(
        Rect rect,
        PanelDefinition panel,
        int slotIndex)
    {
        DrawEmptyTile(rect);
        var inner = new Rect(
            rect.x + 4f,
            rect.y + 4f,
            rect.width - 8f,
            rect.height - 8f);
        DrawPanelRect(inner, CoiUiPalette.Symbol);
        NativeGUI.Label(
            new Rect(inner.x + 7f, inner.y + 17f, inner.width - 14f, 52f),
            UnmaText.Get("auto.1cc8d34d4b3e"),
            m_tileTitleStyle);
        NativeGUI.Label(
            new Rect(inner.x + 7f, inner.y + 73f, inner.width - 14f, 25f),
            m_assignmentEntity == null
                ? UnmaText.Get("auto.7c06a5edce22")
                : UnmaText.Get("auto.36a818f7f3f3") +
                  m_assignmentEntity.Title.ToUpperInvariant(),
            m_tileDetailStyle);
        if (NativeGUI.Button(
                rect,
                GUIContent.none,
                GUIStyle.none,
                new NativeControlMetadata(
                    "assignment-new-" + panel.Id + "-" + slotIndex,
                    focusable: false,
                    tabIndex: -1)))
        {
            HandleNewAssignmentTarget(panel, slotIndex);
        }
    }

    private void HandleExistingAssignmentTarget(
        PanelDefinition panel,
        AlarmView alarm)
    {
        if (!IsEntityAssignmentReady())
        {
            SetStatus(UnmaText.Get("auto.23d65250f6f8"));
            return;
        }
        if (!PanelSlotProjection.TryGetCustomRuleId(
                alarm,
                out var ruleId))
        {
            SetStatus(
                UnmaText.Get("auto.437d5121f36c") +
                UnmaText.Get("auto.27567c838396") +
                UnmaText.Get("auto.422bced1880e"));
            return;
        }

        var rule = m_runtime.Configuration.Rules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        if (rule == null)
        {
            SetStatus(
                UnmaText.Get("auto.a8773cef8433") +
                UnmaText.Get("auto.60fcc3b8049f"));
            return;
        }
        if (!string.Equals(
                m_editingRuleId,
                rule.Id,
                StringComparison.Ordinal))
        {
            if (HasDraftRuleWork())
            {
                SetDraftConflictStatus(
                    UnmaText.Get("auto.4683ec5b7d62") +
                    UnmaText.Get("auto.8dd3eb1fc170") +
                    UnmaText.Get("auto.bdbb3a94176a") +
                    UnmaText.Get("auto.9c6bb5adc23c"));
                return;
            }
            BeginEditingRule(rule, m_audio.GetSoundOptions());
        }

        m_draftPreferredSlotIndex = -1;
        OpenAssignmentEntityEditor(
            m_assignmentEntity.Title + UnmaText.Get("auto.b2ce0caec171") +
            rule.Name + UnmaText.Get("auto.a885f14507a3") +
            UnmaText.Get("auto.27846549c7aa"));
    }

    private void HandleNewAssignmentTarget(
        PanelDefinition panel,
        int slotIndex)
    {
        if (!IsEntityAssignmentReady())
        {
            SetStatus(UnmaText.Get("auto.23d65250f6f8"));
            return;
        }
        if (HasDraftRuleWork())
        {
            SetDraftConflictStatus(
                UnmaText.Get("auto.4683ec5b7d62") +
                UnmaText.Get("auto.2f8c34a0d52c") +
                UnmaText.Get("auto.3b8557a9f488") +
                UnmaText.Get("auto.2937f45b1021"));
            return;
        }

        ResetDraftRule();
        m_draftTargetPanelId = panel.Id;
        m_draftPreferredSlotIndex = Math.Max(
            0,
            Math.Min(slotIndex, panel.Slots?.Count ?? 0));
        OpenAssignmentEntityEditor(
            UnmaText.Get("auto.e6ff6d9861e1") + m_assignmentEntity.Title +
            UnmaText.Get("auto.0dcaccd035ba"));
    }

    private bool IsEntityAssignmentReady()
    {
        return m_entityAssignmentPending &&
               m_assignmentEntity != null &&
               m_assignmentEntity.Metrics != null &&
               m_assignmentEntity.Metrics.Count > 0;
    }

    private void OpenAssignmentEntityEditor(string status)
    {
        var entity = m_assignmentEntity;
        if (entity == null)
        {
            SetStatus(UnmaText.Get("auto.21ced0635377"));
            return;
        }

        m_entityAssignmentPending = false;
        m_assignmentEntityId = -1;
        m_assignmentEntity = null;
        m_selectedEntity = entity;
        m_selectedMetrics = entity.Metrics ??
                            Array.Empty<MetricDescriptor>();
        m_selectedMetricIndex = 0;
        m_selectedReferenceMetricIndex = 0;
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        m_editorWindowMode = EditorWindowMode.Rule;
        m_entityAlarmWindowOpen = true;
        m_openEntityAlarmAfterInspection = false;
        m_entityAlarmScroll = Vector2.zero;
        m_nextEntityInspectionRefresh =
            Time.realtimeSinceStartup + 1f;
        SetStatus(status);
    }

    private void CancelEntityAssignment()
    {
        if (m_pendingInspectionEntityId == m_assignmentEntityId)
        {
            m_runtime.CancelEntityInspectionRequest();
            m_pendingInspectionEntityId = -1;
            m_isAutomaticInspectionRefresh = false;
            m_openEntityAlarmAfterInspection = false;
        }
        m_entityAssignmentPending = false;
        m_assignmentEntityId = -1;
        m_assignmentEntity = null;
    }

    private bool HasDraftRuleWork()
    {
        return !string.IsNullOrWhiteSpace(m_editingRuleId) ||
               m_draftPreferredSlotIndex >= 0 ||
               m_draftLinkedPanelIds.Count > 0 ||
               m_draftConditions.Count > 0 ||
               !string.Equals(
                   m_draftRuleName?.Trim(),
                   UnmaText.Get("auto.fe04a9d0e58c"),
                   StringComparison.Ordinal) ||
               m_draftSeverity != AlarmSeverity.Warning ||
               !m_draftEnabled ||
               m_draftLogic != AlarmLogic.All ||
               !string.Equals(
                   m_draftColor?.Trim(),
                   "#F0C541",
                   StringComparison.OrdinalIgnoreCase) ||
               m_draftSoundIndex != 0 ||
               m_draftSoundChanged ||
               m_draftAutoAcknowledgeOnClear ||
               TimingDraftHasInput(m_draftActivationDelay) ||
               TimingDraftHasInput(m_draftResetDelay) ||
               TimingDraftHasInput(m_draftMinimumActive) ||
               m_draftEscalationEnabled ||
               TimingDraftHasInput(m_draftEscalationAfter) ||
               m_draftEscalationSeverity != AlarmSeverity.Critical ||
               !string.IsNullOrEmpty(m_draftEscalationSoundId) ||
               m_draftEscalationOperatorAction != AlarmOperatorAction.None ||
               m_draftValueMode != ConditionValueMode.Absolute ||
               m_draftComparison != ComparisonOperator.Less ||
               !string.Equals(
                   m_draftThreshold?.Trim(),
                   "0",
                   StringComparison.Ordinal);
    }

    private void DrawPanelRect(Rect rect, Color color)
    {
        var previous = NativeGUI.color;
        NativeGUI.color = color;
        NativeGUI.DrawTexture(rect, Texture2D.whiteTexture);
        NativeGUI.color = previous;
    }

    private void BeginEntityAlarmFromInspector(IEntityInspector inspector)
    {
        var entity = inspector?.EntityUntyped;
        if (entity == null)
        {
            SetStatus(
                UnmaText.Get("auto.9de6b84cdfae"));
            return;
        }

        CancelEntityAssignment();
        m_openEntityPanelAfterInspectionId = entity.Id.Value;
        m_isOpen = true;
        SelectMainTab(TabBoard);
        RequestEntityInspection(
            entity.Id.Value,
            false,
            preserveCurrentSelection: true);
        SetStatus(
            UnmaText.Get("auto.a3f0c28806d6"));
    }

    private void CaptureSelectedEntity()
    {
        CaptureSelectedEntity(false);
    }

    private void CaptureSelectedEntity(bool openEntityAlarmWindow)
    {
        var entity = m_inspectorsManager.GetFirstActiveEntityOrNull();
        if (entity == null)
        {
            SetStatus(
                UnmaText.Get("auto.6f402f1dba14"));
            return;
        }

        ClearLinkedInstrumentSource();
        RequestEntityInspection(entity.Id.Value, openEntityAlarmWindow);
    }

    private void OpenConditionSource(
        ConditionDefinition condition,
        bool openEntityAlarmWindow)
    {
        if (condition != null &&
            !string.IsNullOrWhiteSpace(condition.InstrumentId) &&
            SelectLinkedInstrumentSource(condition.InstrumentId))
        {
            if (openEntityAlarmWindow)
            {
                OpenRuleEditorWindow();
            }
            return;
        }
        if (condition != null && SystemMetricCatalog.TryParseRulePath(
                condition.MetricPath,
                out _))
        {
            SelectGlobalMetricSource(false);
            if (openEntityAlarmWindow)
            {
                OpenRuleEditorWindow();
            }
            return;
        }
        if (condition != null)
        {
            RequestEntityInspection(
                condition.EntityId,
                openEntityAlarmWindow);
        }
    }

    private void SelectGlobalMetricSource(bool preserveSelection)
    {
        if (!preserveSelection)
        {
            ClearLinkedInstrumentSource();
        }
        var selectedPath = preserveSelection &&
                           m_selectedMetricIndex >= 0 &&
                           m_selectedMetricIndex < m_selectedMetrics.Count
            ? m_selectedMetrics[m_selectedMetricIndex].Path
            : "";
        var referencePath = preserveSelection &&
                            m_selectedReferenceMetricIndex >= 0 &&
                            m_selectedReferenceMetricIndex <
                            m_selectedMetrics.Count
            ? m_selectedMetrics[m_selectedReferenceMetricIndex].Path
            : "";
        var values = m_runtime.GetSystemMetricValues();
        var metrics = m_runtime.GetAvailableSystemMetrics()
            .Select(metric => new MetricDescriptor(
                SystemMetricCatalog.ToRulePath(metric.Id),
                metric.Label,
                values.TryGetValue(metric.Id, out var value) ? value : 0d,
                metric.Unit))
            .ToArray();
        m_selectedEntity = new EntityInspectionSnapshot(
            -1,
            UnmaText.Get(
                "ui.editor.global_variables",
                "GLOBAL VARIABLES"),
            UnmaText.Get("ui.editor.global_scope", "GLOBAL"),
            "",
            "",
            metrics);
        m_selectedMetrics = metrics;
        m_selectedMetricIndex = preserveSelection
            ? FindMetricIndex(selectedPath)
            : 0;
        m_selectedReferenceMetricIndex = preserveSelection
            ? FindMetricIndex(referencePath)
            : 0;
        if (!preserveSelection)
        {
            m_metricPickerOpen = false;
            m_referenceMetricPickerOpen = false;
            SetStatus(UnmaText.Get(
                "ui.editor.status.global_source_selected",
                "Global variables selected as source."));
        }
    }

    private void RequestEntityInspection(
        int entityId,
        bool openEntityAlarmWindow,
        bool preserveCurrentSelection = false)
    {
        if (!preserveCurrentSelection && m_entityAssignmentPending)
        {
            CancelEntityAssignment();
        }
        if (!preserveCurrentSelection)
        {
            ClearLinkedInstrumentSource();
            m_selectedEntity = null;
            m_selectedMetrics = Array.Empty<MetricDescriptor>();
            m_selectedMetricIndex = 0;
            m_selectedReferenceMetricIndex = 0;
        }
        m_pendingInspectionEntityId = entityId;
        m_isAutomaticInspectionRefresh = false;
        m_openEntityAlarmAfterInspection = openEntityAlarmWindow;
        if (openEntityAlarmWindow)
        {
            m_entityAlarmWindowOpen = true;
        }
        m_runtime.RequestEntityInspection(entityId);
        SetStatus(UnmaText.Get("auto.82ed078e6c1f"));
    }

    private void ApplyCompletedInspection(EntityInspectionSnapshot inspection)
    {
        if (m_pendingInspectionEntityId >= 0 &&
            inspection.EntityId != m_pendingInspectionEntityId)
        {
            return;
        }
        m_pendingInspectionEntityId = -1;
        var entityPanelInspection =
            inspection.EntityId == m_openEntityPanelAfterInspectionId;
        var automaticRefresh = m_isAutomaticInspectionRefresh;
        m_isAutomaticInspectionRefresh = false;
        var assignmentInspection = m_entityAssignmentPending &&
                                   inspection.EntityId ==
                                   m_assignmentEntityId;

        if (!string.IsNullOrWhiteSpace(inspection.Error))
        {
            if (entityPanelInspection)
            {
                m_openEntityPanelAfterInspectionId = -1;
            }
            if (assignmentInspection)
            {
                CancelEntityAssignment();
            }
            else
            {
                m_selectedEntity = null;
                m_selectedMetrics = Array.Empty<MetricDescriptor>();
                m_openEntityAlarmAfterInspection = false;
            }
            SetStatus(inspection.Error);
            return;
        }

        if (assignmentInspection)
        {
            if (inspection.Metrics == null ||
                inspection.Metrics.Count == 0)
            {
                CancelEntityAssignment();
                SetStatus(
                    UnmaText.Get("auto.5891d5e7de9b"));
                return;
            }
            m_assignmentEntity = inspection;
            SetStatus(
                inspection.Title +
                UnmaText.Get("auto.5b5d1db02fdb") +
                UnmaText.Get("auto.4717f3c076e3"));
            return;
        }

        var selectedMetricPath = m_selectedMetricIndex >= 0 &&
                                 m_selectedMetricIndex < m_selectedMetrics.Count
            ? m_selectedMetrics[m_selectedMetricIndex].Path
            : "";
        var selectedReferencePath = m_selectedReferenceMetricIndex >= 0 &&
                                    m_selectedReferenceMetricIndex <
                                    m_selectedMetrics.Count
            ? m_selectedMetrics[m_selectedReferenceMetricIndex].Path
            : "";
        m_selectedEntity = inspection;
        m_selectedMetrics = inspection.Metrics ??
                            Array.Empty<MetricDescriptor>();
        m_selectedMetricIndex = automaticRefresh
            ? FindMetricIndex(selectedMetricPath)
            : 0;
        m_selectedReferenceMetricIndex = automaticRefresh
            ? FindMetricIndex(selectedReferencePath)
            : 0;
        m_nextEntityInspectionRefresh =
            Time.realtimeSinceStartup + 1f;
        if (entityPanelInspection)
        {
            m_openEntityPanelAfterInspectionId = -1;
            var entityPanel = m_runtime.GetOrCreateEntityPanel(inspection);
            if (entityPanel == null)
            {
                SetStatus(
                    UnmaText.Get("auto.39b196c55f60") +
                    m_runtime.LastPersistenceError);
                return;
            }
            m_activeEntityPanelId = entityPanel.Id;
            m_isOpen = true;
            SelectMainTab(TabBoard);
            m_boardScroll = Vector2.zero;
            SetStatus(
                UnmaText.Get("auto.8a42487f2b31") + inspection.Title + UnmaText.Get("auto.70834308d14f"));
            return;
        }
        if (automaticRefresh)
        {
            return;
        }
        if (m_openEntityAlarmAfterInspection)
        {
            m_entityAlarmWindowOpen = true;
            m_entityAlarmScroll = Vector2.zero;
        }
        m_openEntityAlarmAfterInspection = false;
        if (m_selectedMetrics.Count == 0)
        {
            SetStatus(UnmaText.Get("auto.5891d5e7de9b"));
        }
        else
        {
            SetStatus(
                m_selectedMetrics.Count +
                UnmaText.Get("auto.8df11b2d3716") +
                inspection.Title +
                UnmaText.Get("auto.d6cb6d182723"));
        }
    }

    private void AddDraftCondition()
    {
        if (m_selectedEntity == null || m_selectedMetrics.Count == 0)
        {
            SetStatus(UnmaText.Get("auto.a852547fe390"));
            return;
        }
        if (!TryParseDouble(m_draftThreshold, out var threshold))
        {
            SetStatus(UnmaText.Get("auto.019d3710d0ee"));
            return;
        }

        var metric = m_selectedMetrics[m_selectedMetricIndex];
        MetricDescriptor referenceMetric = null;
        if (m_draftValueMode == ConditionValueMode.PercentOfReference)
        {
            m_selectedReferenceMetricIndex = Math.Max(
                0,
                Math.Min(
                    m_selectedReferenceMetricIndex,
                    m_selectedMetrics.Count - 1));
            referenceMetric = m_selectedMetrics[m_selectedReferenceMetricIndex];
            if (string.Equals(
                    metric.Path,
                    referenceMetric.Path,
                    StringComparison.Ordinal))
            {
                SetStatus(
                    UnmaText.Get("auto.2bdbbc810cee"));
                return;
            }
        }
        m_draftConditions.Add(new ConditionDefinition
        {
            EntityId = m_selectedEntity.EntityId,
            EntityTitle = m_selectedEntity.Title,
            EntityType = m_selectedEntity.EntityType,
            EntityPrototypeId = m_selectedEntity.PrototypeId,
            MetricPath = metric.Path,
            MetricLabel = metric.Label,
            Comparison = m_draftComparison,
            Threshold = threshold,
            ValueMode = m_draftValueMode,
            ReferenceMetricPath = referenceMetric?.Path ?? "",
            ReferenceMetricLabel = referenceMetric?.Label ?? "",
            ExpectedProductId = metric.Path.StartsWith(
                "$stored.",
                StringComparison.Ordinal)
                ? m_selectedEntity.StoredProductId
                : "",
        });
        EnsureDraftHysteresisText(
            m_draftConditions[m_draftConditions.Count - 1]);
        m_draftConditionThresholdTexts.Add(
            threshold.ToString("R", CultureInfo.CurrentCulture));
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        SetStatus(UnmaText.Get("auto.af3edd1b9f09"));
    }

    private void AddLinkedInstrumentCondition(InstrumentDefinition instrument)
    {
        if (instrument == null ||
            !TryParseDouble(m_draftThreshold, out var threshold))
        {
            SetStatus(UnmaText.Get("auto.019d3710d0ee"));
            return;
        }

        var condition = new ConditionDefinition
        {
            EntityId = instrument.EntityId,
            EntityTitle = instrument.EntityTitle,
            EntityPrototypeId = instrument.EntityPrototypeId,
            MetricPath = instrument.MetricPath,
            MetricLabel = instrument.Title,
            Comparison = m_draftComparison,
            Threshold = threshold,
            InstrumentId = instrument.Id,
            TrendMode = InstrumentTrendMode.None,
            WindowSeconds = 60,
            WindowAmount = 1,
            WindowUnit = GameTimeUnit.Month,
            DeltaThreshold = Math.Max(
                1d,
                (instrument.Maximum - instrument.Minimum) * 0.05d),
        };
        m_draftConditions.Add(condition);
        EnsureDraftHysteresisText(condition);
        m_draftConditionThresholdTexts.Add(
            threshold.ToString("R", CultureInfo.CurrentCulture));
        m_draftTrendWindowTexts[condition] = "1";
        SetStatus(UnmaText.Format(
            "ui.editor.status.linked_value_added",
            "Linked value added: {0}",
            instrument.Title));
    }

    private bool SelectLinkedInstrumentSource(string instrumentId)
    {
        var instrument = m_runtime?.Configuration?.Instruments?.FirstOrDefault(
            item => item != null && string.Equals(
                item.Id,
                instrumentId,
                StringComparison.Ordinal));
        if (instrument == null)
        {
            return false;
        }

        m_linkedInstrumentSourceId = instrument.Id;
        var currentTarget = GetDraftTargetPanel();
        if (PanelTopologyPolicy.IsEntityPanel(currentTarget))
        {
            var promotedId = m_draftLinkedPanelIds.FirstOrDefault(id =>
                m_runtime.Configuration.Panels.Any(panel =>
                    panel != null &&
                    !panel.IsDashboard &&
                    !PanelTopologyPolicy.IsEntityPanel(panel) &&
                    string.Equals(panel.Id, id, StringComparison.Ordinal)));
            m_draftTargetPanelId = promotedId ?? "";
            if (!string.IsNullOrWhiteSpace(promotedId))
            {
                m_draftLinkedPanelIds.Remove(promotedId);
            }
            m_draftPreferredSlotIndex = GetDraftTargetPanel()?.Slots?.Count ?? -1;
        }
        m_draftLinkedPanelIds.RemoveWhere(id =>
            !m_runtime.Configuration.Panels.Any(panel =>
                panel != null &&
                !panel.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(panel) &&
                string.Equals(panel.Id, id, StringComparison.Ordinal)));
        var instruments = GetLinkedInstruments();
        m_selectedLinkedInstrumentIndex = Math.Max(
            0,
            instruments.FindIndex(item => string.Equals(
                item.Id,
                instrument.Id,
                StringComparison.Ordinal)));
        m_linkedInstrumentPickerOpen = false;
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        return true;
    }

    private bool TryGetLinkedInstrumentSource(
        out InstrumentDefinition instrument)
    {
        instrument = m_runtime?.Configuration?.Instruments?.FirstOrDefault(
            item => item != null && string.Equals(
                item.Id,
                m_linkedInstrumentSourceId,
                StringComparison.Ordinal));
        if (instrument != null)
        {
            return true;
        }
        m_linkedInstrumentSourceId = "";
        return false;
    }

    private List<InstrumentDefinition> GetLinkedInstruments()
    {
        if (!TryGetLinkedInstrumentSource(out var source))
        {
            return new List<InstrumentDefinition>();
        }
        return m_runtime.Configuration.Instruments
            .Where(item => item != null && string.Equals(
                item.PanelId,
                source.PanelId,
                StringComparison.Ordinal))
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
    }

    private void ClearLinkedInstrumentSource()
    {
        m_linkedInstrumentSourceId = "";
        m_selectedLinkedInstrumentIndex = 0;
        m_linkedInstrumentPickerOpen = false;
    }

    private bool SaveDraftRule(IReadOnlyList<SoundOption> sounds)
    {
        var panel = GetDraftTargetPanel();
        var validationMessage = GetRuleDraftValidationMessage();
        if (!string.IsNullOrEmpty(validationMessage))
        {
            var advancedValidationError =
                HasAdvancedAlarmValidationError();
            m_ruleAdvancedOpen |= advancedValidationError;
            m_entityAlarmScroll.y = advancedValidationError
                ? 100000f
                : 0f;
            SetStatus(validationMessage, StatusSeverity.Error, true);
            return false;
        }

        if (!TryGetTimingTicks(
                m_draftActivationDelay,
                out var activationDelayTicks) ||
            !TryGetTimingTicks(m_draftResetDelay, out var resetDelayTicks) ||
            !TryGetTimingTicks(
                m_draftMinimumActive,
                out var minimumActiveTicks))
        {
            SetStatus(UnmaText.Get(
                "ui.timing.invalid_duration",
                "Enter non-negative whole timing values within 100 game years."));
            return false;
        }
        if (!TryGetTimingTicks(
                m_draftEscalationAfter,
                out var escalationAfterTicks) &&
            m_draftEscalationEnabled)
        {
            SetStatus(UnmaText.Get(
                "ui.escalation.invalid_after",
                "Enter a non-negative whole escalation time within 100 game years."));
            return false;
        }
        if (!m_draftEscalationEnabled &&
            !TryGetTimingTicks(
                m_draftEscalationAfter,
                out escalationAfterTicks))
        {
            escalationAfterTicks = 0;
        }
        if (m_draftEscalationEnabled && escalationAfterTicks <= 0)
        {
            SetStatus(UnmaText.Get(
                "ui.escalation.after_required",
                "Escalation requires a game-time delay greater than zero."));
            return false;
        }
        if (m_draftEscalationEnabled &&
            (m_draftSeverity >= AlarmSeverity.Emergency ||
             m_draftEscalationSeverity <= m_draftSeverity))
        {
            SetStatus(UnmaText.Get(
                "ui.escalation.invalid_severity",
                "Escalation severity must be strictly higher than the base severity."));
            return false;
        }

        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            var condition = m_draftConditions[index];
            if (index >= m_draftConditionThresholdTexts.Count ||
                !TryParseDouble(
                    m_draftConditionThresholdTexts[index],
                    out var threshold))
            {
                SetStatus(
                    UnmaText.Get("auto.cb85d7309ac1") + (index + 1) +
                    UnmaText.Get("auto.ddb8c3cdbc29"));
                return false;
            }
            EnsureDraftHysteresisText(condition);
            if (!TryParseDouble(
                    m_draftHysteresisTexts[condition],
                    out var hysteresis) ||
                hysteresis < 0d)
            {
                SetStatus(UnmaText.Format(
                    "ui.timing.invalid_hysteresis",
                    "Condition {0}: Enter a non-negative hysteresis value.",
                    index + 1));
                return false;
            }
            condition.Hysteresis = hysteresis;
            if (!string.IsNullOrWhiteSpace(condition.InstrumentId))
            {
                if (!m_runtime.Configuration.Instruments.Any(instrument =>
                        instrument != null && string.Equals(
                            instrument.Id,
                            condition.InstrumentId,
                            StringComparison.Ordinal)))
                {
                    SetStatus(UnmaText.Format(
                        "ui.instrument.status.condition_missing",
                        "Condition {0}: The associated instrument no longer exists.",
                        index + 1));
                    return false;
                }
                if (UsesComparisonThreshold(condition.TrendMode))
                {
                    condition.Threshold = threshold;
                }
                else
                {
                    condition.DeltaThreshold = threshold;
                }
                if (condition.TrendMode != InstrumentTrendMode.None)
                {
                    if (!UsesComparisonThreshold(condition.TrendMode) &&
                        threshold < 0d ||
                        !m_draftTrendWindowTexts.TryGetValue(
                            condition,
                            out var windowText) ||
                        !int.TryParse(
                            windowText,
                            NumberStyles.Integer,
                            CultureInfo.CurrentCulture,
                            out var windowAmount) ||
                        windowAmount < 1)
                    {
                        SetStatus(UnmaText.Format(
                            "ui.instrument.status.invalid_time_condition",
                            "Time condition {0}: Enter a valid amount and game-time range.",
                            index + 1));
                        return false;
                    }
                    condition.WindowAmount =
                        GameTimeWindowPolicy.ClampAmount(
                            windowAmount,
                            condition.WindowUnit);
                }
                continue;
            }
            condition.Threshold = threshold;
            if (condition.ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.IsNullOrWhiteSpace(
                    condition.ReferenceMetricPath))
            {
                SetStatus(
                    UnmaText.Get("auto.21ca7079c12b") + (index + 1) +
                    UnmaText.Get("auto.115b04808134"));
                return false;
            }
            if (condition.ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.Equals(
                    condition.MetricPath,
                    condition.ReferenceMetricPath,
                    StringComparison.Ordinal))
            {
                SetStatus(
                    UnmaText.Get("auto.53c26ac33af4") + (index + 1) +
                    UnmaText.Get("auto.be2c20cf5599"));
                return false;
            }
        }

        var selectedSoundId = sounds.Count > 0
            ? sounds[Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1))].Id
            : "auto";
        var isEditing = !string.IsNullOrWhiteSpace(m_editingRuleId);
        var existingRule = !isEditing
            ? null
            : m_runtime.Configuration.Rules.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    m_editingRuleId,
                    StringComparison.Ordinal));
        if (isEditing && existingRule == null)
        {
            SetStatus(
                UnmaText.Get("auto.575fe354a165") +
                UnmaText.Get("auto.e60fb9e527e6"));
            return false;
        }
        var soundId = isEditing && !m_draftSoundChanged
            ? m_originalDraftSoundId
            : selectedSoundId;
        var rule = new AlarmRuleDefinition
        {
            Id = existingRule?.Id ?? Guid.NewGuid().ToString("N"),
            PanelId = panel.Id,
            Name = m_draftRuleName.Trim(),
            Severity = m_draftSeverity,
            Logic = m_draftLogic,
            ActiveColor = NormalizeColor(m_draftColor),
            SoundId = soundId,
            Enabled = m_draftEnabled,
            AutoAcknowledgeOnClear = m_draftAutoAcknowledgeOnClear,
            ActivationDelayTicks = activationDelayTicks,
            ResetDelayTicks = resetDelayTicks,
            MinimumActiveTicks = minimumActiveTicks,
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = m_draftEscalationEnabled,
                AfterTicks = escalationAfterTicks,
                Severity = m_draftEscalationSeverity,
                SoundId = m_draftEscalationSoundId ?? "",
                OperatorAction = m_draftEscalationOperatorAction,
            },
            Conditions = m_draftConditions.Select(CloneCondition).ToList(),
            LinkedPanelIds = m_draftLinkedPanelIds.ToList(),
        };
        var saved = isEditing
            ? m_runtime.UpdateRule(rule)
            : m_runtime.AddRule(rule, m_draftPreferredSlotIndex);
        if (!saved)
        {
            SetStatus(
                UnmaText.Get("auto.5df942eb6687") +
                m_runtime.LastPersistenceError,
                StatusSeverity.Error,
                true);
            return false;
        }
        var wasEditing = existingRule != null;
        var savedPanelId = panel.Id;
        ResetDraftRule();
        m_draftTargetPanelId = savedPanelId;
        SetStatus(
            wasEditing
                ? UnmaText.Get("auto.961c0245ef89")
                : UnmaText.Get("auto.fb19aab1dadd"),
            StatusSeverity.Success);
        return true;
    }

    private void BeginEditingRule(
        AlarmRuleDefinition rule,
        IReadOnlyList<SoundOption> sounds)
    {
        m_draftPreferredSlotIndex = -1;
        m_editingRuleId = rule.Id;
        m_draftTargetPanelId = rule.PanelId;
        m_draftRuleName = rule.Name;
        m_draftEnabled = rule.Enabled;
        m_draftSeverity = rule.Severity;
        m_draftLogic = rule.Logic;
        m_draftColor = rule.ActiveColor;
        m_draftSoundIndex = FindSoundIndex(sounds, rule.SoundId);
        m_originalDraftSoundId = rule.SoundId;
        m_draftSoundChanged = false;
        m_draftAutoAcknowledgeOnClear = rule.AutoAcknowledgeOnClear;
        m_ruleAdvancedOpen = false;
        LoadTimingDraft(m_draftActivationDelay, rule.ActivationDelayTicks);
        LoadTimingDraft(m_draftResetDelay, rule.ResetDelayTicks);
        LoadTimingDraft(m_draftMinimumActive, rule.MinimumActiveTicks);
        var escalation = rule.Escalation ?? new AlarmEscalationDefinition();
        m_draftEscalationEnabled = escalation.Enabled;
        LoadTimingDraft(m_draftEscalationAfter, escalation.AfterTicks);
        m_draftEscalationSeverity = escalation.Severity;
        m_draftEscalationSoundId = escalation.SoundId ?? "";
        m_draftEscalationOperatorAction = escalation.OperatorAction;
        EnsureDraftEscalationTarget();
        m_draftLinkedPanelIds.Clear();
        foreach (var panelId in rule.LinkedPanelIds ?? new List<string>())
        {
            m_draftLinkedPanelIds.Add(panelId);
        }
        m_draftConditions.Clear();
        m_draftConditionThresholdTexts.Clear();
        m_draftTrendWindowTexts.Clear();
        m_draftHysteresisTexts.Clear();
        foreach (var sourceCondition in rule.Conditions)
        {
            var condition = CloneCondition(sourceCondition);
            m_draftConditions.Add(condition);
            m_draftConditionThresholdTexts.Add(
                (UsesComparisonThreshold(condition.TrendMode)
                    ? condition.Threshold
                    : condition.DeltaThreshold).ToString(
                    "R",
                    CultureInfo.CurrentCulture));
            m_draftTrendWindowTexts[condition] =
                condition.WindowAmount.ToString(
                    CultureInfo.CurrentCulture);
            EnsureDraftHysteresisText(condition);
        }
        var linkedCondition = m_draftConditions.FirstOrDefault(condition =>
            !string.IsNullOrWhiteSpace(condition.InstrumentId));
        if (linkedCondition != null)
        {
            SelectLinkedInstrumentSource(linkedCondition.InstrumentId);
        }
        else
        {
            ClearLinkedInstrumentSource();
        }
        m_editorScroll = Vector2.zero;
        SetStatus(UnmaText.Get("auto.bc7894226481"));
    }

    private void ResetDraftRule()
    {
        m_draftConflictMessage = "";
        m_draftConflictMessageUntil = 0f;
        m_editorClosePromptOpen = false;
        m_draftPreferredSlotIndex = -1;
        m_editingRuleId = "";
        m_draftConditions.Clear();
        m_draftConditionThresholdTexts.Clear();
        m_draftTrendWindowTexts.Clear();
        m_draftHysteresisTexts.Clear();
        m_draftRuleName = UnmaText.Get("auto.fe04a9d0e58c");
        m_draftEnabled = true;
        m_draftSeverity = AlarmSeverity.Warning;
        m_draftLogic = AlarmLogic.All;
        m_draftColor = "#F0C541";
        m_draftSoundIndex = 0;
        m_originalDraftSoundId = "auto";
        m_draftSoundChanged = false;
        m_draftAutoAcknowledgeOnClear = false;
        m_ruleAdvancedOpen = false;
        LoadTimingDraft(m_draftActivationDelay, 0);
        LoadTimingDraft(m_draftResetDelay, 0);
        LoadTimingDraft(m_draftMinimumActive, 0);
        m_draftEscalationEnabled = false;
        LoadTimingDraft(m_draftEscalationAfter, 0);
        m_draftEscalationSeverity = AlarmSeverity.Critical;
        m_draftEscalationSoundId = "";
        m_draftEscalationOperatorAction = AlarmOperatorAction.None;
        m_draftLinkedPanelIds.Clear();
        m_draftValueMode = ConditionValueMode.Absolute;
        m_draftComparison = ComparisonOperator.Less;
        m_draftThreshold = "0";
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        m_conditionReferencePickerIndex = -1;
        ClearLinkedInstrumentSource();
        var targetPanel = CurrentPanel != null && !CurrentPanel.IsDashboard
            ? CurrentPanel
            : m_runtime.Configuration.Panels.FirstOrDefault(panel =>
                !panel.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(panel));
        m_draftTargetPanelId = targetPanel?.Id ?? "";
    }

    private void OpenPanelCreationEditor()
    {
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetDraftConflictStatus(
                UnmaText.Get("auto.48d5f7bcd7c1"));
            return;
        }
        m_editorWindowMode = EditorWindowMode.PanelCreation;
        m_editorClosePromptOpen = false;
        m_entityAlarmWindowOpen = true;
        m_entityAlarmScroll = Vector2.zero;
        m_newPanelName = UnmaText.Get("auto.3f5c86818d70");
    }

    private void OpenAlarmAreasEditor()
    {
        if (m_entityAlarmWindowOpen &&
            m_editorWindowMode == EditorWindowMode.AlarmAreas)
        {
            m_clearGuiFocusPending = true;
            return;
        }
        if (m_entityAlarmWindowOpen &&
            m_editorWindowMode != EditorWindowMode.AlarmAreas)
        {
            SetStatus(UnmaText.Get(
                "ui.area.editor_conflict",
                "Close the current editor before managing areas."));
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetDraftConflictStatus(UnmaText.Get("auto.48d5f7bcd7c1"));
            return;
        }
        ReloadAlarmAreaDraft();
        m_editorWindowMode = EditorWindowMode.AlarmAreas;
        m_editorClosePromptOpen = false;
        m_entityAlarmWindowOpen = true;
        m_entityAlarmScroll = Vector2.zero;
    }

    private void OpenPanelSettingsEditor(PanelDefinition panel)
    {
        if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return;
        }
        if (m_entityAlarmWindowOpen &&
            m_editorWindowMode == EditorWindowMode.PanelSettings &&
            string.Equals(
                m_panelSettingsPanelId,
                panel.Id,
                StringComparison.Ordinal))
        {
            m_clearGuiFocusPending = true;
            return;
        }
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetDraftConflictStatus(
                UnmaText.Get("auto.48d5f7bcd7c1"));
            return;
        }
        m_panelSettingsPanelId = panel.Id;
        m_panelSettingsName = panel.Name ?? "";
        m_panelSettingsColumns = panel.Columns;
        m_panelSettingsIncludeVanilla = panel.IncludeVanilla;
        m_panelSettingsIncludeSystem = panel.IncludeSystem;
        m_panelSettingsFilter = panel.NotificationFilter ?? "";
        m_panelSettingsAreaId = panel.AreaId ?? "";
        m_panelSettingsAreaScroll = Vector2.zero;
        m_editorWindowMode = EditorWindowMode.PanelSettings;
        m_editorClosePromptOpen = false;
        m_entityAlarmWindowOpen = true;
        m_entityAlarmScroll = Vector2.zero;
    }

    private void OpenNewRuleEditor(PanelDefinition panel)
    {
        if (panel == null || panel.IsDashboard)
        {
            SetStatus(UnmaText.Get("auto.da029f65d8db"));
            return;
        }
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetStatus(
                UnmaText.Get("auto.243d4fdd7115") +
                UnmaText.Get("auto.eec1a0f95ac1"));
            return;
        }

        ResetDraftRule();
        m_draftTargetPanelId = panel.Id;
        m_draftPreferredSlotIndex = panel.Slots?.Count ?? 0;
        OpenRuleEditorWindow();

        if (PanelTopologyPolicy.IsEntityPanel(panel) &&
            (m_selectedEntity == null ||
             m_selectedEntity.EntityId != panel.OwnerEntityId))
        {
            RequestEntityInspection(panel.OwnerEntityId, true);
        }
    }

    private void OpenRuleEditorWindow()
    {
        if (BlockEditorSwitchFromConfigurationDraft())
        {
            return;
        }
        m_editorWindowMode = EditorWindowMode.Rule;
        m_editorClosePromptOpen = false;
        m_entityAlarmWindowOpen = true;
        m_openEntityAlarmAfterInspection = false;
        m_entityAlarmScroll = Vector2.zero;
    }

    private bool BlockEditorSwitchFromConfigurationDraft()
    {
        if (!m_entityAlarmWindowOpen)
        {
            return false;
        }
        if (m_editorWindowMode == EditorWindowMode.AlarmAreas)
        {
            var dirty = HasUnsavedAlarmAreas();
            SetStatus(UnmaText.Get(
                dirty
                    ? "ui.area.unsaved_editor_conflict"
                    : "ui.area.close_editor_first",
                dirty
                    ? "Save or discard the area draft before opening another editor."
                    : "Close the area editor before opening another editor."));
            return true;
        }
        if (m_editorWindowMode != EditorWindowMode.PanelSettings)
        {
            return false;
        }
        var panel = m_runtime.Configuration.Panels.FirstOrDefault(candidate =>
            candidate != null && string.Equals(
                candidate.Id,
                m_panelSettingsPanelId,
                StringComparison.Ordinal));
        if (!HasUnsavedPanelSettings(panel))
        {
            return false;
        }
        SetStatus(UnmaText.Get(
            "ui.area.unsaved_editor_conflict",
            "Save or discard the panel settings before opening another editor."));
        return true;
    }

    private bool AddPanel()
    {
        var panel = new PanelDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(m_newPanelName)
                ? UnmaText.Get("auto.3f5c86818d70")
                : m_newPanelName.Trim(),
            Columns = 3,
            IncludeVanilla = false,
            IncludeSystem = false,
            IsDashboard = false,
            AreaId = GetCurrentConcreteAlarmAreaId(),
        };
        if (!m_runtime.AddPanel(panel))
        {
            SetStatus(
                UnmaText.Get("auto.27f10f6dc69e") +
                m_runtime.LastPersistenceError);
            return false;
        }
        m_activeEntityPanelId = "";
        m_currentPanelIndex = GlobalPanels.FindIndex(candidate =>
            string.Equals(candidate.Id, panel.Id, StringComparison.Ordinal));
        m_currentPanelIndex = Math.Max(0, m_currentPanelIndex);
        if (!HasDraftRuleWork())
        {
            m_draftTargetPanelId = panel.Id;
        }
        m_newPanelName = UnmaText.Get("auto.3f5c86818d70");
        SetStatus(UnmaText.Get("auto.f63589d2dc6f"));
        return true;
    }

    private void RemoveCurrentPanel()
    {
        if (m_runtime.Configuration.Panels.Count <= 1 ||
            CurrentPanel == null)
        {
            return;
        }
        if (CurrentPanel.IsDashboard)
        {
            SetStatus(
                UnmaText.Get("auto.dec8763d2a8a") +
                UnmaText.Get("auto.6ab723fab24d"));
            return;
        }

        var panelId = CurrentPanel.Id;
        var editingRuleWasInPanel =
            !string.IsNullOrWhiteSpace(m_editingRuleId) &&
            m_runtime.Configuration.Rules.Any(rule =>
                string.Equals(
                    rule.Id,
                    m_editingRuleId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    rule.PanelId,
                    panelId,
                    StringComparison.Ordinal));
        if (!string.Equals(
                m_pendingPanelDeleteId,
                panelId,
                StringComparison.Ordinal) ||
            Time.realtimeSinceStartup > m_pendingPanelDeleteUntil)
        {
            var affectedRules = m_runtime.Configuration.Rules.Count(rule =>
                string.Equals(
                    rule.PanelId,
                    panelId,
                    StringComparison.Ordinal));
            m_pendingPanelDeleteId = panelId;
            m_pendingPanelDeleteUntil = Time.realtimeSinceStartup + 6f;
            SetStatus(
                UnmaText.Get("auto.e7b6d1d30034") + affectedRules +
                UnmaText.Get("auto.85843058797a"));
            return;
        }
        if (!m_runtime.RemovePanel(panelId))
        {
            SetStatus(
                UnmaText.Get("auto.0fb0c7def7d9") +
                m_runtime.LastPersistenceError);
            return;
        }
        m_currentPanelIndex = Math.Max(
            0,
            Math.Min(
                m_currentPanelIndex,
                m_runtime.Configuration.Panels.Count - 1));
        m_pendingPanelDeleteId = "";
        m_pendingPanelDeleteUntil = 0f;
        if (editingRuleWasInPanel)
        {
            ResetDraftRule();
        }
        else if (string.Equals(
                m_draftTargetPanelId,
                panelId,
                StringComparison.Ordinal))
        {
            m_draftTargetPanelId = CurrentPanel?.Id ?? "";
            m_draftPreferredSlotIndex = -1;
        }
        CloseDetachedPanelsForPanel(panelId);
        SetStatus(UnmaText.Get("auto.d57565ce0bc8"));
    }

    private void DetachPanel(string panelId)
    {
        DetachPanel(panelId, persistOpenState: true);
    }

    private void DetachPanel(string panelId, bool persistOpenState)
    {
        var existing = m_detachedPanels.FirstOrDefault(item =>
            item.IsOpen && string.Equals(
                item.PanelId,
                panelId,
                StringComparison.Ordinal));
        if (existing != null)
        {
            var existingPanel = m_runtime.Configuration.Panels.FirstOrDefault(
                panel => panel.Id == panelId);
            if (existingPanel != null)
            {
                existing.NativeShell?.Open(
                    GetDetachedPanelTitle(existingPanel));
            }
            existing.NativeShell?.BringToFront();
            return;
        }

        var config = m_runtime.Configuration;
        config.DetachedPanelLayouts ??= new List<DetachedPanelWindowLayout>();
        var storedLayout = config.DetachedPanelLayouts.FirstOrDefault(layout =>
            layout != null && string.Equals(
                layout.PanelId,
                panelId,
                StringComparison.Ordinal));
        var offset = m_detachedPanels.Count * 28f;
        var initialRect = storedLayout == null
            ? new Rect(40f + offset, 60f + offset, 620f, 460f)
            : new Rect(
                storedLayout.X,
                storedLayout.Y,
                storedLayout.Width,
                storedLayout.Height);
        var detached = new DetachedPanel
        {
            PanelId = panelId,
            Rect = initialRect,
            LastPersistedRect = initialRect,
            LastPersistedOpen = storedLayout?.IsOpen == true,
        };
        var panel = config.Panels.FirstOrDefault(
            item => item.Id == panelId);
        if (m_uiRoot != null && panel != null)
        {
            try
            {
                detached.NativeShell = new UnmaNativeDetachedPanelShell(
                    m_uiRoot,
                    detached.Rect.width,
                    detached.Rect.height,
                    detached.Rect.x,
                    detached.Rect.y,
                    GetDetachedPanelTitle(panel),
                    () => HandleNativeDetachedPanelClosed(detached),
                    (width, height) =>
                    {
                        detached.Rect.width = width;
                        detached.Rect.height = height;
                        MarkDetachedPanelLayoutDirty(detached, immediate: true);
                    },
                    HandleNativeSurfaceActivated);
            }
            catch (Exception exception)
            {
                detached.NativeShell?.Dispose();
                detached.NativeShell = null;
                detached.IsOpen = false;
                Log.Warning(
                    "UNMA: native detached panel could not be created; " +
                    "panel disabled. " + exception.GetType().Name +
                    ": " + exception.Message);
            }
        }
        if (detached.NativeShell == null)
        {
            return;
        }
        m_detachedPanels.Add(detached);
        if (persistOpenState || !detached.LastPersistedOpen)
        {
            MarkDetachedPanelLayoutDirty(detached, immediate: true);
            PersistPendingWindowLayouts(force: true);
        }
    }

    private void RestoreDetachedPanels(UnmaConfiguration config)
    {
        foreach (var layout in (config.DetachedPanelLayouts ??
                     new List<DetachedPanelWindowLayout>())
                 .Where(layout => layout?.IsOpen == true)
                 .ToArray())
        {
            DetachPanel(layout.PanelId, persistOpenState: false);
        }
    }

    private void HandleNativeDetachedPanelClosed(DetachedPanel detached)
    {
        if (detached == null)
        {
            return;
        }
        CaptureDetachedPanelLayout(detached);
        detached.IsOpen = false;
        MarkDetachedPanelLayoutDirty(detached, immediate: true);
        PersistPendingWindowLayouts(force: true);
    }

    private void CloseDetachedPanelsForPanel(string panelId)
    {
        for (var index = m_detachedPanels.Count - 1; index >= 0; index--)
        {
            var detached = m_detachedPanels[index];
            if (!string.Equals(
                    detached.PanelId,
                    panelId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            detached.NativeShell?.Dispose();
            detached.NativeShell = null;
            m_detachedPanels.RemoveAt(index);
        }
        var layouts = m_runtime.Configuration.DetachedPanelLayouts;
        if (layouts != null && layouts.RemoveAll(layout =>
                layout != null && string.Equals(
                    layout.PanelId,
                    panelId,
                    StringComparison.Ordinal)) > 0)
        {
            m_runtime.SaveConfiguration();
        }
    }

    private void TestSound(string soundId, AlarmSeverity severity)
    {
        if (string.Equals(
                soundId,
                "none",
                StringComparison.OrdinalIgnoreCase))
        {
            StopTestSound();
            SetStatus(UnmaText.Get("auto.0fe1119242e6"));
            return;
        }

        m_testAlarm = new AlarmView
        {
            IsActive = true,
            IsAcknowledged = false,
            Severity = severity,
            SoundId = soundId,
        };
        m_testAlarmUntil = Time.realtimeSinceStartup + 8f;
        SetStatus(UnmaText.Get("auto.e176fed55042"));
    }

    private void StopTestSound()
    {
        m_testAlarm = null;
        m_testAlarmUntil = 0f;
        m_audio.StopAlarm();
    }

    private PanelDefinition GetDraftTargetPanel()
    {
        return m_runtime.Configuration.Panels.FirstOrDefault(panel =>
            !panel.IsDashboard &&
            string.Equals(
                panel.Id,
                m_draftTargetPanelId,
                StringComparison.Ordinal));
    }

    private string FormatMetricValue(MetricDescriptor metric)
    {
        if (metric == null)
        {
            return "—";
        }
        var value = metric.CurrentValue.ToString(
            "0.###",
            CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(metric.Unit)
            ? value
            : value + " " + metric.Unit;
    }

    private MetricDescriptor FindSelectedMetric(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        return m_selectedMetrics.FirstOrDefault(metric => string.Equals(
            metric.Path,
            path,
            StringComparison.Ordinal));
    }

    private int FindMetricIndex(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }
        for (var index = 0; index < m_selectedMetrics.Count; index++)
        {
            if (string.Equals(
                    m_selectedMetrics[index].Path,
                    path,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return 0;
    }

    private void SelectSuggestedReferenceMetric(MetricDescriptor metric)
    {
        if (metric == null || m_selectedMetrics.Count == 0)
        {
            return;
        }

        var suggestedPath = string.IsNullOrWhiteSpace(
            metric.SuggestedPercentReferencePath)
            ? SuggestedReferencePath(metric.Path)
            : metric.SuggestedPercentReferencePath;
        var index = -1;
        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            for (var candidateIndex = 0;
                 candidateIndex < m_selectedMetrics.Count;
                 candidateIndex++)
            {
                if (string.Equals(
                        m_selectedMetrics[candidateIndex].Path,
                        suggestedPath,
                        StringComparison.Ordinal))
                {
                    index = candidateIndex;
                    break;
                }
            }
        }

        if (index < 0)
        {
            index = m_selectedMetrics
                .Select((candidate, candidateIndex) => new
                {
                    Candidate = candidate,
                    Index = candidateIndex,
                })
                .Where(item =>
                    item.Index != m_selectedMetricIndex &&
                    item.Candidate.Label.IndexOf(
                        "kapaz",
                        StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Select(item => item.Index)
                .DefaultIfEmpty(0)
                .First();
        }
        m_selectedReferenceMetricIndex = index;
    }

    private static string SuggestedReferencePath(string metricPath)
    {
        if (string.Equals(
                metricPath,
                "$stored.quantity",
                StringComparison.Ordinal))
        {
            return "$stored.capacity";
        }
        const string inputProductPrefix = "$input.product:";
        if (metricPath.StartsWith(
                inputProductPrefix,
                StringComparison.Ordinal))
        {
            return "$input.capacity:" +
                   metricPath.Substring(inputProductPrefix.Length);
        }
        if (string.Equals(
                metricPath,
                "$transport.quantity",
                StringComparison.Ordinal) ||
            metricPath.StartsWith(
                "$transport.product:",
                StringComparison.Ordinal))
        {
            return "$transport.capacity";
        }
        if (string.Equals(
                metricPath,
                "$cargo.quantity",
                StringComparison.Ordinal) ||
            metricPath.StartsWith(
                "$cargo.product:",
                StringComparison.Ordinal))
        {
            return "$cargo.capacity";
        }
        return "";
    }

    private string ConditionActualText(ConditionDefinition condition)
    {
        if (m_selectedEntity == null ||
            m_selectedEntity.EntityId != condition.EntityId)
        {
            return UnmaText.Get("auto.a33569399406");
        }

        var actualMetric = FindSelectedMetric(condition.MetricPath);
        if (actualMetric == null)
        {
            return UnmaText.Get("auto.16fff883f67a");
        }
        if (condition.ValueMode == ConditionValueMode.Absolute)
        {
            return FormatMetricValue(actualMetric);
        }

        var referenceMetric = FindSelectedMetric(
            condition.ReferenceMetricPath);
        if (referenceMetric == null ||
            !AlarmEvaluation.TryCalculateComparable(
                actualMetric.CurrentValue,
                condition.ValueMode,
                referenceMetric.CurrentValue,
                out var comparable))
        {
            return UnmaText.Get("auto.34f75a7a7720");
        }
        return comparable.ToString(
                   "0.###",
                   CultureInfo.CurrentCulture) + " %";
    }

    private void SaveConfiguration(string successMessage)
    {
        if (m_runtime.SaveConfiguration())
        {
            SetStatus(successMessage, StatusSeverity.Success);
        }
        else
        {
            SetStatus(
                UnmaText.Get("auto.5df942eb6687") +
                m_runtime.LastPersistenceError,
                StatusSeverity.Error,
                true);
        }
    }


    private void DrawStatusMessage()
    {
        if (!string.IsNullOrWhiteSpace(m_statusMessage) &&
            (m_statusPersistent ||
             Time.realtimeSinceStartup < m_statusMessageUntil))
        {
            var style = m_statusSeverity switch
            {
                StatusSeverity.Success => m_statusSuccessStyle,
                StatusSeverity.Warning => m_statusWarningStyle,
                StatusSeverity.Error => m_statusErrorStyle,
                _ => m_statusInfoStyle,
            };
            NativeGUILayout.BeginHorizontal();
            NativeGUILayout.Label(
                StatusPrefix(m_statusSeverity) + m_statusMessage,
                style,
                NativeGUILayout.MinHeight(34f));
            var dismissLabel = UnmaText.Get(
                "ui.status.dismiss",
                "Dismiss message");
            if (m_statusPersistent && NativeGUILayout.Button(
                    new GUIContent("×", dismissLabel),
                    m_buttonStyle,
                    new NativeControlMetadata(
                        "status-dismiss",
                        dismissLabel),
                    NativeGUILayout.Width(38f),
                    NativeGUILayout.Height(34f)))
            {
                m_statusMessage = "";
                m_statusPersistent = false;
            }
            NativeGUILayout.EndHorizontal();
        }
    }

    private static string StatusPrefix(StatusSeverity severity)
    {
        return severity switch
        {
            StatusSeverity.Success => "✓ ",
            StatusSeverity.Warning => "! ",
            StatusSeverity.Error => "× ",
            _ => "i ",
        };
    }

    private void DrawDraftConflictBanner()
    {
        if (!string.IsNullOrWhiteSpace(m_draftConflictMessage) &&
            Time.realtimeSinceStartup < m_draftConflictMessageUntil)
        {
            NativeGUILayout.Label(
                m_draftConflictMessage,
                m_warningBannerStyle,
                NativeGUILayout.MinHeight(54f));
        }
    }

    private void SetDraftConflictStatus(string message)
    {
        m_draftConflictMessage = message;
        m_draftConflictMessageUntil = float.PositiveInfinity;
        SetStatus(message);
    }

    private void DeleteEditedRule(bool confirmed)
    {
        var ruleId = m_editingRuleId;
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }
        if (!confirmed)
        {
            m_pendingRuleDeleteId = ruleId;
            m_pendingRuleDeleteUntil = Time.realtimeSinceStartup + 6f;
            SetStatus(UnmaText.Get(
                "ui.editor.status.confirm_delete_alarm",
                "Press again to delete the alarm."));
            return;
        }
        if (!m_runtime.RemoveRule(ruleId))
        {
            SetStatus(
                UnmaText.Get("auto.c1f0ffc84e81") +
                m_runtime.LastPersistenceError,
                StatusSeverity.Error,
                true);
            return;
        }
        m_pendingRuleDeleteId = "";
        m_pendingRuleDeleteUntil = 0f;
        ResetDraftRule();
        CloseEditorWindow();
        SetStatus(
            UnmaText.Get("auto.61bea0138542"),
            StatusSeverity.Success);
    }

    private void SetStatus(
        string message,
        StatusSeverity severity = StatusSeverity.Info,
        bool persistent = false)
    {
        m_statusMessage = message;
        m_statusSeverity = severity;
        m_statusPersistent = persistent;
        m_statusMessageUntil = persistent
            ? float.PositiveInfinity
            : Time.realtimeSinceStartup + 8f;
    }

    private void CycleMetric(int direction)
    {
        m_selectedMetricIndex = Wrap(
            m_selectedMetricIndex + direction,
            m_selectedMetrics.Count);
    }


    private void CaptureNativeWindowLayouts()
    {
        CaptureMainWindowLayout();
        CaptureEditorWindowLayout();
        foreach (var detached in m_detachedPanels)
        {
            CaptureDetachedPanelLayout(detached);
        }
    }

    private void CaptureMainWindowLayout()
    {
        if (m_historianPreviousWindowSizeValid ||
            m_nativeWindowShell?.TryGetCurrentPosition(out var position) != true)
        {
            return;
        }
        if (Mathf.Approximately(m_windowRect.x, position.x) &&
            Mathf.Approximately(m_windowRect.y, position.y))
        {
            return;
        }
        m_windowRect.x = position.x;
        m_windowRect.y = position.y;
        m_windowRectPersistAt = Time.realtimeSinceStartup +
                                WindowLayoutPersistenceDelaySeconds;
    }

    private void CaptureEditorWindowLayout()
    {
        if (m_nativeEditorShell?.TryGetCurrentPosition(out var position) != true)
        {
            return;
        }
        if (Mathf.Approximately(m_entityAlarmWindowRect.x, position.x) &&
            Mathf.Approximately(m_entityAlarmWindowRect.y, position.y))
        {
            return;
        }
        m_entityAlarmWindowRect.x = position.x;
        m_entityAlarmWindowRect.y = position.y;
        m_editorWindowRectPersistAt = Time.realtimeSinceStartup +
                                      WindowLayoutPersistenceDelaySeconds;
    }

    private void CaptureDetachedPanelLayout(DetachedPanel detached)
    {
        if (detached?.NativeShell?.TryGetCurrentPosition(out var position) !=
            true)
        {
            return;
        }
        if (Mathf.Approximately(detached.Rect.x, position.x) &&
            Mathf.Approximately(detached.Rect.y, position.y))
        {
            return;
        }
        detached.Rect.x = position.x;
        detached.Rect.y = position.y;
        MarkDetachedPanelLayoutDirty(detached, immediate: false);
    }

    private void MarkDetachedPanelLayoutDirty(
        DetachedPanel detached,
        bool immediate)
    {
        if (detached == null)
        {
            return;
        }
        detached.PersistAt = immediate
            ? Time.realtimeSinceStartup
            : Time.realtimeSinceStartup +
              WindowLayoutPersistenceDelaySeconds;
    }

    private void PersistPendingWindowLayouts(bool force)
    {
        if (m_runtime?.Configuration == null)
        {
            return;
        }
        var now = Time.realtimeSinceStartup;
        var mainChanged = !m_historianPreviousWindowSizeValid &&
                          !RectsApproximatelyEqual(
                              m_windowRect,
                              m_lastPersistedWindowRect);
        var editorChanged = !RectsApproximatelyEqual(
            m_entityAlarmWindowRect,
            m_lastPersistedEditorWindowRect);
        var changedDetached = m_detachedPanels.Where(detached =>
            detached != null &&
            (!RectsApproximatelyEqual(
                 detached.Rect,
                 detached.LastPersistedRect) ||
             detached.IsOpen != detached.LastPersistedOpen)).ToArray();
        var due = force && (mainChanged || editorChanged ||
                            changedDetached.Length > 0) ||
                  mainChanged && m_windowRectPersistAt >= 0f &&
                  now >= m_windowRectPersistAt ||
                  editorChanged && m_editorWindowRectPersistAt >= 0f &&
                  now >= m_editorWindowRectPersistAt ||
                  changedDetached.Any(detached =>
                      detached.PersistAt >= 0f && now >= detached.PersistAt);
        if (!due)
        {
            return;
        }

        var config = m_runtime.Configuration;
        var previousMain = new Rect(
            config.WindowX,
            config.WindowY,
            config.WindowWidth,
            config.WindowHeight);
        var previousEditor = new Rect(
            config.EditorWindowX,
            config.EditorWindowY,
            config.EditorWindowWidth,
            config.EditorWindowHeight);
        var previousDetached = (config.DetachedPanelLayouts ??
                new List<DetachedPanelWindowLayout>())
            .Select(CloneDetachedPanelLayout)
            .ToList();
        if (mainChanged)
        {
            config.WindowX = m_windowRect.x;
            config.WindowY = m_windowRect.y;
            config.WindowWidth = m_windowRect.width;
            config.WindowHeight = m_windowRect.height;
        }
        if (editorChanged)
        {
            config.EditorWindowX = m_entityAlarmWindowRect.x;
            config.EditorWindowY = m_entityAlarmWindowRect.y;
            config.EditorWindowWidth = m_entityAlarmWindowRect.width;
            config.EditorWindowHeight = m_entityAlarmWindowRect.height;
        }
        foreach (var detached in changedDetached)
        {
            StoreDetachedPanelLayout(config, detached);
        }
        if (m_runtime.SaveConfiguration())
        {
            if (mainChanged)
            {
                m_lastPersistedWindowRect = m_windowRect;
                m_windowRectPersistAt = -1f;
            }
            if (editorChanged)
            {
                m_lastPersistedEditorWindowRect = m_entityAlarmWindowRect;
                m_editorWindowRectPersistAt = -1f;
            }
            foreach (var detached in changedDetached)
            {
                detached.LastPersistedRect = detached.Rect;
                detached.LastPersistedOpen = detached.IsOpen;
                detached.PersistAt = -1f;
            }
            return;
        }

        config.WindowX = previousMain.x;
        config.WindowY = previousMain.y;
        config.WindowWidth = previousMain.width;
        config.WindowHeight = previousMain.height;
        config.EditorWindowX = previousEditor.x;
        config.EditorWindowY = previousEditor.y;
        config.EditorWindowWidth = previousEditor.width;
        config.EditorWindowHeight = previousEditor.height;
        config.DetachedPanelLayouts = previousDetached;
    }

    private static void StoreDetachedPanelLayout(
        UnmaConfiguration config,
        DetachedPanel detached)
    {
        config.DetachedPanelLayouts ??= new List<DetachedPanelWindowLayout>();
        var stored = config.DetachedPanelLayouts.FirstOrDefault(layout =>
            layout != null && string.Equals(
                layout.PanelId,
                detached.PanelId,
                StringComparison.Ordinal));
        if (stored == null)
        {
            stored = new DetachedPanelWindowLayout
            {
                PanelId = detached.PanelId,
            };
            config.DetachedPanelLayouts.Add(stored);
        }
        stored.X = detached.Rect.x;
        stored.Y = detached.Rect.y;
        stored.Width = detached.Rect.width;
        stored.Height = detached.Rect.height;
        stored.IsOpen = detached.IsOpen;
    }

    private static DetachedPanelWindowLayout CloneDetachedPanelLayout(
        DetachedPanelWindowLayout source)
    {
        return new DetachedPanelWindowLayout
        {
            PanelId = source?.PanelId ?? "",
            X = source?.X ?? 40f,
            Y = source?.Y ?? 60f,
            Width = source?.Width ?? 620f,
            Height = source?.Height ?? 460f,
            IsOpen = source?.IsOpen == true,
        };
    }

    private void ApplyWindowLayoutFromConfiguration()
    {
        var config = m_runtime.Configuration;
        m_windowRect = new Rect(
            config.WindowX,
            config.WindowY,
            Mathf.Max(700f, config.WindowWidth),
            Mathf.Max(520f, config.WindowHeight));
        m_entityAlarmWindowRect = new Rect(
            config.EditorWindowX,
            config.EditorWindowY,
            Mathf.Max(700f, config.EditorWindowWidth),
            Mathf.Max(520f, config.EditorWindowHeight));
        m_lastPersistedWindowRect = m_windowRect;
        m_lastPersistedEditorWindowRect = m_entityAlarmWindowRect;
        m_windowRectPersistAt = -1f;
        m_editorWindowRectPersistAt = -1f;
        m_nativeWindowShell?.ApplyLayout(
            m_windowRect.position,
            m_windowRect.size);
        m_nativeEditorShell?.ApplyLayout(
            m_entityAlarmWindowRect.position,
            m_entityAlarmWindowRect.size);
        m_nativeLauncher?.SetPosition(
            config.LauncherX < 0f ? 8f : config.LauncherX,
            config.LauncherY < 0f ? 160f : config.LauncherY);
        foreach (var detached in m_detachedPanels)
        {
            detached.NativeShell?.Dispose();
            detached.NativeShell = null;
        }
        m_detachedPanels.Clear();
        RestoreDetachedPanels(config);
    }

    private static bool RectsApproximatelyEqual(Rect left, Rect right)
    {
        return Mathf.Approximately(left.x, right.x) &&
               Mathf.Approximately(left.y, right.y) &&
               Mathf.Approximately(left.width, right.width) &&
               Mathf.Approximately(left.height, right.height);
    }

    private void EnsureStyles()
    {
        if (m_stylesReady)
        {
            return;
        }
        m_stylesReady = true;

        m_panelStyle = new GUIStyle()
        {
            stretchWidth = true,
            padding = new RectOffset(8, 8, 7, 7),
            margin = new RectOffset(3, 3, 3, 3),
            normal =
            {
                textColor = CoiUiPalette.Text,
                background = SolidTexture(
                    "panel",
                    CoiUiPalette.SurfaceDark),
            },
        };
        SetBackgroundForAllStates(
            m_panelStyle,
            m_panelStyle.normal.background);
        m_headerStyle = new GUIStyle()
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = CoiUiPalette.TextBright },
        };
        m_sectionStyle = new GUIStyle()
        {
            stretchWidth = true,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 5, 5),
            normal =
            {
                textColor = Color.white,
                background = SolidTexture(
                    "section",
                    CoiUiPalette.SurfaceRaised),
            },
        };
        m_labelStyle = new GUIStyle()
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = CoiUiPalette.TextBright },
        };
        m_smallLabelStyle = new GUIStyle(m_labelStyle)
        {
            fontSize = 11,
            normal = { textColor = CoiUiPalette.Text },
        };
        m_tileTitleStyle = new GUIStyle()
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.black },
        };
        m_tileDetailStyle = new GUIStyle()
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.black },
        };
        m_tileTitleLightStyle = new GUIStyle(m_tileTitleStyle)
        {
            normal = { textColor = Color.white },
        };
        m_tileDetailLightStyle = new GUIStyle(m_tileDetailStyle)
        {
            normal = { textColor = Color.white },
        };
        m_assignmentActionStyle = new GUIStyle(m_tileDetailStyle)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        m_buttonStyle = MakeButtonStyle(
            "button",
            CoiUiPalette.Control,
            CoiUiPalette.BorderLight,
            CoiUiPalette.TextBright);
        m_primaryButtonStyle = MakeButtonStyle(
            "primary",
            CoiUiPalette.Border,
            CoiUiPalette.Yellow,
            CoiUiPalette.Yellow);
        m_dangerButtonStyle = MakeButtonStyle(
            "danger",
            CoiUiPalette.ScaleRgb(CoiUiPalette.Orange, 0.62f),
            CoiUiPalette.Orange,
            CoiUiPalette.TextBright);
        m_warningBannerStyle = new GUIStyle(m_sectionStyle)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal =
            {
                textColor = Color.white,
                background = SolidTexture(
                    "draft-warning",
                    CoiUiPalette.ScaleRgb(CoiUiPalette.Orange, 0.72f)),
            },
        };
        m_statusInfoStyle = MakeStatusStyle(
            "status-info",
            CoiUiPalette.SurfaceRaised,
            CoiUiPalette.BorderLight);
        m_statusSuccessStyle = MakeStatusStyle(
            "status-success",
            new Color(0.08f, 0.32f, 0.18f, 1f),
            new Color(0.25f, 0.82f, 0.42f, 1f));
        m_statusWarningStyle = MakeStatusStyle(
            "status-warning",
            new Color(0.40f, 0.25f, 0.04f, 1f),
            CoiUiPalette.Yellow);
        m_statusErrorStyle = MakeStatusStyle(
            "status-error",
            new Color(0.43f, 0.06f, 0.06f, 1f),
            CoiUiPalette.Orange);
        m_textFieldStyle = new GUIStyle()
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(9, 9, 5, 5),
            border = new RectOffset(3, 3, 3, 3),
            normal =
            {
                textColor = Color.white,
                background = CoiButtonTexture(
                    "field",
                    CoiUiPalette.InputBackground,
                    CoiUiPalette.InputBorder),
            },
            focused =
            {
                textColor = Color.white,
                background = CoiButtonTexture(
                    "field-focus",
                    CoiUiPalette.InputBackground,
                    CoiUiPalette.Green),
            },
        };
        m_textFieldStyle.hover.textColor = Color.white;
        m_textFieldStyle.hover.background = m_textFieldStyle.normal.background;
        m_textFieldStyle.active.textColor = Color.white;
        m_textFieldStyle.active.background = m_textFieldStyle.normal.background;
        m_textFieldStyle.onNormal.textColor = Color.white;
        m_textFieldStyle.onNormal.background = m_textFieldStyle.normal.background;
        m_textFieldStyle.onHover.textColor = Color.white;
        m_textFieldStyle.onHover.background = m_textFieldStyle.normal.background;
        m_textFieldStyle.onActive.textColor = Color.white;
        m_textFieldStyle.onActive.background = m_textFieldStyle.normal.background;
        m_textFieldStyle.onFocused.textColor = Color.white;
        m_textFieldStyle.onFocused.background = m_textFieldStyle.focused.background;
        m_historyHeaderStyle = new GUIStyle(m_labelStyle)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };
        m_historyTextStyle = new GUIStyle(m_labelStyle)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = CoiUiPalette.TextBright },
        };
        m_historyStateStyle = new GUIStyle(m_historyTextStyle)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
        };
        m_historyAlertTextStyle = new GUIStyle(m_historyTextStyle)
        {
            normal = { textColor = Color.white },
        };
        m_historyAlertStateStyle = new GUIStyle(m_historyStateStyle)
        {
            normal = { textColor = Color.white },
        };
    }

    private GUIStyle MakeStatusStyle(
        string key,
        Color fill,
        Color accent)
    {
        var style = new GUIStyle(m_smallLabelStyle)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            padding = new RectOffset(10, 10, 7, 7),
            normal =
            {
                textColor = Color.white,
                background = CoiButtonTexture(key, fill, accent),
            },
        };
        SetBackgroundForAllStates(style, style.normal.background);
        return style;
    }

    private GUIStyle MakeButtonStyle(
        string key,
        Color normal,
        Color accent,
        Color textColor)
    {
        var hover = CoiUiPalette.ScaleRgb(normal, 1.14f);
        var active = CoiUiPalette.ScaleRgb(normal, 0.82f);
        var style = new GUIStyle()
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 4, 4),
            border = new RectOffset(3, 3, 3, 3),
            normal =
            {
                textColor = textColor,
                background = CoiButtonTexture(key, normal, accent),
            },
            hover =
            {
                textColor = textColor,
                background = CoiButtonTexture(
                    key + "-hover",
                    hover,
                    CoiUiPalette.ScaleRgb(accent, 1.10f)),
            },
            active =
            {
                textColor = textColor,
                background = CoiButtonTexture(
                    key + "-active",
                    active,
                    accent,
                    true),
            },
        };
        style.focused.textColor = textColor;
        style.focused.background = style.hover.background;
        style.onNormal.textColor = textColor;
        style.onNormal.background = style.normal.background;
        style.onHover.textColor = textColor;
        style.onHover.background = style.hover.background;
        style.onActive.textColor = textColor;
        style.onActive.background = style.active.background;
        style.onFocused.textColor = textColor;
        style.onFocused.background = style.hover.background;
        return style;
    }

    private Texture2D CoiButtonTexture(
        string key,
        Color fill,
        Color accent,
        bool pressed = false)
    {
        const int width = 24;
        const int height = 16;
        var cacheKey = "coi-button-" + key;
        if (m_colorTextures.TryGetValue(cacheKey, out var texture))
        {
            return texture;
        }

        texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "UNMA " + cacheKey,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var vertical = y / (height - 1f);
            var shade = pressed
                ? Mathf.Lerp(0.76f, 1.02f, vertical)
                : Mathf.Lerp(1.12f, 0.78f, vertical);
            for (var x = 0; x < width; x++)
            {
                var edge = x == 0 || x == width - 1 ||
                           y == 0 || y == height - 1;
                var insetEdge = x == 1 || x == width - 2 ||
                                y == 1 || y == height - 2;
                var color = edge
                    ? CoiUiPalette.Window
                    : insetEdge
                        ? accent
                        : CoiUiPalette.ScaleRgb(fill, shade);
                pixels[y * width + x] = color;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        m_colorTextures[cacheKey] = texture;
        return texture;
    }

    private static void SetBackgroundForAllStates(
        GUIStyle style,
        Texture2D background)
    {
        style.normal.background = background;
        style.hover.background = background;
        style.active.background = background;
        style.focused.background = background;
        style.onNormal.background = background;
        style.onHover.background = background;
        style.onActive.background = background;
        style.onFocused.background = background;
    }

    private Texture2D SolidTexture(string key, Color color)
    {
        if (m_colorTextures.TryGetValue(key, out var texture))
        {
            return texture;
        }
        texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = UnmaText.Get("auto.9efeab6faae0") + key,
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        m_colorTextures[key] = texture;
        return texture;
    }

    private PanelDefinition CurrentPanel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(m_activeEntityPanelId))
            {
                var entityPanel = m_runtime.Configuration.Panels
                    .FirstOrDefault(panel => string.Equals(
                        panel.Id,
                        m_activeEntityPanelId,
                        StringComparison.Ordinal));
                if (PanelTopologyPolicy.IsEntityPanel(entityPanel))
                {
                    return entityPanel;
                }
                m_activeEntityPanelId = "";
            }

            var panels = GlobalPanels;
            if (panels.Count == 0)
            {
                return null;
            }
            m_currentPanelIndex = Math.Max(
                0,
                Math.Min(
                    m_currentPanelIndex,
                    panels.Count - 1));
            return panels[m_currentPanelIndex];
        }
    }

    private InstrumentPanelDefinition CurrentInstrumentPanel
    {
        get
        {
            var panels = m_runtime.Configuration.InstrumentPanels;
            if (panels == null || panels.Count == 0)
            {
                return null;
            }
            m_currentInstrumentPanelIndex = Math.Max(
                0,
                Math.Min(m_currentInstrumentPanelIndex, panels.Count - 1));
            return panels[m_currentInstrumentPanelIndex];
        }
    }

    private List<PanelDefinition> GlobalPanels =>
        m_runtime.Configuration.Panels
            .Where(panel => !PanelTopologyPolicy.IsEntityPanel(panel))
            .ToList();

    private static ConditionDefinition CloneCondition(
        ConditionDefinition source)
    {
        return new ConditionDefinition
        {
            EntityId = source.EntityId,
            EntityTitle = source.EntityTitle,
            EntityType = source.EntityType,
            MetricPath = source.MetricPath,
            MetricLabel = source.MetricLabel,
            Comparison = source.Comparison,
            Threshold = source.Threshold,
            Hysteresis = source.Hysteresis,
            ExpectedProductId = source.ExpectedProductId,
            EntityPrototypeId = source.EntityPrototypeId,
            ValueMode = source.ValueMode,
            ReferenceMetricPath = source.ReferenceMetricPath,
            ReferenceMetricLabel = source.ReferenceMetricLabel,
            InstrumentId = source.InstrumentId,
            TrendMode = source.TrendMode,
            WindowSeconds = source.WindowSeconds,
            DeltaThreshold = source.DeltaThreshold,
            WindowAmount = source.WindowAmount,
            WindowUnit = source.WindowUnit,
        };
    }

    private static T NextEnum<T>(T value) where T : struct, Enum
    {
        var values = (T[])Enum.GetValues(typeof(T));
        var index = Array.IndexOf(values, value);
        return values[(index + 1) % values.Length];
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0)
        {
            return 0;
        }
        var result = value % count;
        return result < 0 ? result + count : result;
    }

    private static int FindSoundIndex(
        IReadOnlyList<SoundOption> sounds,
        string soundId)
    {
        for (var index = 0; index < sounds.Count; index++)
        {
            if (string.Equals(
                    sounds[index].Id,
                    soundId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return 0;
    }

    private static bool TryParseDouble(string text, out double value)
    {
        var parsed = double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out value);
        if (!parsed)
        {
            parsed = double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
        return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string SeverityLabel(AlarmSeverity severity)
    {
        return severity switch
        {
            AlarmSeverity.Emergency => UnmaText.Get(
                "ui.severity.emergency",
                "EMERGENCY"),
            AlarmSeverity.Critical => UnmaText.Get(
                "ui.severity.critical",
                "CRITICAL"),
            AlarmSeverity.Warning => UnmaText.Get(
                "ui.severity.warning",
                "WARNING"),
            _ => UnmaText.Get(
                "ui.severity.notice",
                "NOTICE"),
        };
    }

    private static string DefaultColorFor(AlarmSeverity severity)
    {
        return severity switch
        {
            AlarmSeverity.Emergency => "#E51B23",
            AlarmSeverity.Critical => "#F05A32",
            AlarmSeverity.Warning => "#F0C541",
            _ => "#83C5BE",
        };
    }

    private static string NormalizeColor(string color)
    {
        return ColorUtility.TryParseHtmlString(color, out _)
            ? color
            : "#F0C541";
    }

    private static string NormalizeSystemColor(string color)
    {
        return string.IsNullOrWhiteSpace(color) ||
               string.Equals(
                   color,
                   "auto",
                   StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : NormalizeColor(color);
    }

    private static string ShortTypeName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return UnmaText.Get("auto.a8a6d6ec304d");
        }
        var separator = fullName.LastIndexOf('.');
        return separator >= 0 && separator + 1 < fullName.Length
            ? fullName.Substring(separator + 1)
            : fullName;
    }

    private static bool IsGameplayActive()
    {
        try
        {
            var menuDepth = s_menuDepthField == null
                ? 0
                : (int)s_menuDepthField.GetValue(null);
            var isLoading = s_loadingField != null &&
                            (bool)s_loadingField.GetValue(null);
            return menuDepth <= 0 && !isLoading;
        }
        catch (Exception exception)
        {
            if (!s_gameplayStateFailureLogged)
            {
                s_gameplayStateFailureLogged = true;
                Log.Warning(UnmaText.Format(
                    "log.menu_state_unreadable",
                    "UNMA: Menu state could not be read; annunciator remains " +
                    "available: {0}",
                    exception.Message));
            }
            return true;
        }
    }

    private static Color ParseColor(string value, Color fallback)
    {
        return ColorUtility.TryParseHtmlString(value, out var result)
            ? result
            : fallback;
    }

    private float UiScale => Mathf.Clamp(
        (m_runtime?.Configuration.UiScalePercent ?? 100) / 100f,
        0.75f,
        2f);

    private bool IsPointerOverAnyUnmaSurface()
    {
        if (!m_gameplayWasActive || m_isUiSuppressedByMenu)
        {
            return false;
        }

        var physicalMouse = Input.mousePosition;
        var pointerTopLeft = new Vector2(
            physicalMouse.x,
            Screen.height - physicalMouse.y);
        var pointerOverMain = m_isOpen &&
            m_nativeWindowShell?.ContainsPointer(pointerTopLeft) == true;
        var pointerOverEditor = m_entityAlarmWindowOpen &&
            m_nativeEditorShell?.ContainsPointer(pointerTopLeft) == true;
        if (pointerOverMain ||
            pointerOverEditor ||
            m_nativeLauncher?.ContainsPointer(pointerTopLeft) == true)
        {
            return true;
        }
        return m_detachedPanels.Any(panel =>
            panel.IsOpen &&
            panel.NativeShell?.ContainsPointer(pointerTopLeft) == true);
    }


    private void OnDestroy()
    {
        DisposeUi();
        m_runtime?.SetGameplayActive(false);
        foreach (var texture in m_colorTextures.Values)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
        m_colorTextures.Clear();
    }
}
