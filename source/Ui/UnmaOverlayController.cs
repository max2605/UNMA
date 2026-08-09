using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi.Core.Entities;
using Mafi;
using Mafi.Unity;
using Mafi.Unity.Audio;
using Mafi.Unity.Camera;
using Mafi.Unity.Ui;
using UnityEngine;
using UNMA.Audio;
using UNMA.Domain;
using UNMA.Localization;
using UNMA.Runtime;

namespace UNMA.Ui;

public sealed class UnmaOverlayController : MonoBehaviour
{
    private sealed class DetachedPanel
    {
        public int WindowId;
        public string PanelId = "";
        public Rect Rect;
        public Vector2 Scroll;
        public Vector2? PendingSize;
        public bool IsOpen = true;
    }

    private sealed class PanelViewCacheEntry
    {
        public int Frame = -1;
        public IReadOnlyList<AlarmView> Views = Array.Empty<AlarmView>();
    }

    private const int MainWindowId = 0x554E4D41;
    private const int EntityAlarmWindowId = 0x4D4E5541;
    private const int MainResizeControlHint = 0x554E5253;
    private const int EditorResizeControlHint = 0x554E4552;
    private const int TabBoard = 0;
    private const int TabHistory = 1;
    private const int TabSystem = 2;
    private const int TabSounds = 3;
    private const int TabOptions = 4;
    private const float MainResizeHandleSize = 30f;
    private const float MainResizeHandleInset = 4f;
    private const float MainWindowContentBottomInset =
        MainResizeHandleSize + MainResizeHandleInset + 4f;
    private const float TileHeight = 112f;
    private const float HistoryRowHeight = 40f;

    private enum EditorWindowMode
    {
        Rule,
        PanelCreation,
        PanelSettings,
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
    private readonly Dictionary<string, PanelViewCacheEntry> m_panelViewCache =
        new(StringComparer.Ordinal);
    private readonly List<Rect> m_inputShieldRects = new();

    private UnmaRuntime m_runtime;
    private InspectorsManager m_inspectorsManager;
    private CameraController m_cameraController;
    private UnmaInputBlocker m_inputBlocker;
    private UnmaPointerRaycastShield m_pointerRaycastShield;
    private UnmaAudioController m_audio;
    private InspectorAlarmButtonBridge m_inspectorAlarmButtons;
    private Rect m_windowRect;
    private Rect m_lastPersistedWindowRect;
    private Rect m_launcherRect;
    private Rect m_entityAlarmWindowRect = new(180f, 110f, 1080f, 720f);
    private Rect m_lastPersistedEditorWindowRect;
    private Vector2 m_boardScroll;
    private Vector2 m_panelTabsScroll;
    private Vector2 m_historyScroll;
    private Vector2 m_editorScroll;
    private Vector2 m_entityAlarmScroll;
    private Vector2 m_metricPickerScroll;
    private Vector2 m_referenceMetricPickerScroll;
    private Vector2 m_soundOverrideScroll;
    private Vector2 m_systemAlarmScroll;
    private IReadOnlyList<PanelSlotDefinition> m_panelSlotCandidates =
        Array.Empty<PanelSlotDefinition>();
    private float m_nextPanelSlotCandidateRefresh;
    private bool m_isOpen;
    private bool m_entityAlarmWindowOpen;
    private EditorWindowMode m_editorWindowMode;
    private string m_panelSettingsPanelId = "";
    private string m_panelSettingsName = "";
    private int m_panelSettingsColumns = 3;
    private bool m_panelSettingsIncludeVanilla;
    private bool m_panelSettingsIncludeSystem;
    private string m_panelSettingsFilter = "";
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
    private int m_nextDetachedWindowId = MainWindowId + 1;
    private bool m_gameplayWasActive;
    private bool m_isUiSuppressedByMenu;
    private bool m_isResizing;
    private int m_resizeControlId;
    private bool m_isDraggingLauncher;
    private Vector2 m_resizeStartMouse;
    private Vector2 m_resizeStartSize;
    private Vector2 m_launcherDragOffset;
    private Vector2? m_pendingMainWindowSize;
    private bool m_isEditorResizing;
    private int m_editorResizeControlId;
    private Vector2 m_editorResizeStartMouse;
    private Vector2 m_editorResizeStartSize;
    private Vector2? m_pendingEditorWindowSize;
    private readonly HashSet<string> m_draftLinkedPanelIds =
        new(StringComparer.Ordinal);

    private EntityInspectionSnapshot m_selectedEntity;
    private IReadOnlyList<MetricDescriptor> m_selectedMetrics =
        Array.Empty<MetricDescriptor>();
    private int m_selectedMetricIndex;
    private int m_selectedReferenceMetricIndex;
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
    private string m_draftThreshold = "0";
    private string m_draftRuleName = UnmaText.Get("auto.fe04a9d0e58c");
    private string m_draftColor = "#F0C541";
    private int m_draftSoundIndex;
    private string m_originalDraftSoundId = "auto";
    private bool m_draftSoundChanged;
    private bool m_draftAutoAcknowledgeOnClear;
    private string m_editingRuleId = "";
    private string m_draftTargetPanelId = "";
    private string m_lastAlarmTileClickId = "";
    private float m_lastAlarmTileClickAt;
    private string m_newPanelName = UnmaText.Get("auto.3f5c86818d70");
    private string m_panelSlotFilter = "";
    private string m_soundOverrideFilter = "";
    private SystemAlarmDefinition m_systemAlarmDraft;
    private readonly Dictionary<string, string> m_systemThresholdTexts =
        new(StringComparer.Ordinal);
    private string m_pendingSystemResetId = "";
    private float m_pendingSystemResetUntil;
    private string m_pendingPanelDeleteId = "";
    private float m_pendingPanelDeleteUntil;
    private float m_pendingHistoryDeleteUntil;
    private string m_statusMessage = "";
    private float m_statusMessageUntil;
    private AlarmView m_testAlarm;
    private float m_testAlarmUntil;
    private long m_historyCacheRevision = -1;
    private IReadOnlyList<AlarmHistoryDefinition> m_historyCache =
        Array.Empty<AlarmHistoryDefinition>();

    private GUIStyle m_windowStyle;
    private GUIStyle m_headerStyle;
    private GUIStyle m_sectionStyle;
    private GUIStyle m_labelStyle;
    private GUIStyle m_smallLabelStyle;
    private GUIStyle m_tileTitleStyle;
    private GUIStyle m_tileDetailStyle;
    private GUIStyle m_assignmentActionStyle;
    private GUIStyle m_buttonStyle;
    private GUIStyle m_primaryButtonStyle;
    private GUIStyle m_dangerButtonStyle;
    private GUIStyle m_resizeHandleStyle;
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
            audio);
        return overlay;
    }

    public void Configure(
        UnmaRuntime runtime,
        InspectorsManager inspectorsManager,
        CameraController cameraController,
        IUnityInputMgr inputManager,
        UnmaAudioController audio)
    {
        m_runtime = runtime;
        m_inspectorsManager = inspectorsManager;
        m_cameraController = cameraController;
        m_audio = audio;
        m_inputBlocker = new UnmaInputBlocker(
            inputManager,
            IsPointerOverAnyUnmaSurface);
        m_pointerRaycastShield = new UnmaPointerRaycastShield(transform);
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
        m_launcherRect = new Rect(
            config.LauncherX < 0f
                ? 8f
                : config.LauncherX,
            config.LauncherY < 0f
                ? 160f
                : config.LauncherY,
            116f,
            34f);
    }

    public void ApplySettings(UnmaSettings settings)
    {
        m_runtime.ApplySettings(settings);
    }

    private void Update()
    {
        if (!m_gameplayWasActive)
        {
            if (!IsGameplayActive())
            {
                m_runtime.SetGameplayActive(false);
                m_audio.StopAlarm();
                UpdatePointerRaycastShield(false);
                return;
            }
            m_gameplayWasActive = true;
            m_runtime.SetGameplayActive(true);
            m_audio.StopAlarm();
        }

        m_isUiSuppressedByMenu = !IsGameplayActive();
        var pointerOverUnma = !m_isUiSuppressedByMenu &&
                              IsPointerOverAnyUnmaSurface();
        m_inputBlocker?.SetBlockingEnabled(!m_isUiSuppressedByMenu);
        m_inputBlocker?.SetPointerState(pointerOverUnma);
        UpdatePointerRaycastShield(!m_isUiSuppressedByMenu);
        if (pointerOverUnma && m_cameraController != null)
        {
            m_cameraController.DisableZoomNextFrame = true;
        }

        if (!m_isUiSuppressedByMenu)
        {
            m_inspectorAlarmButtons?.Update();
            m_inputBlocker?.EnsureActive();
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
            m_entityAlarmWindowOpen = false;
            SetStatus(
                UnmaText.Get("auto.ced08b6f8b50") +
                UnmaText.Get("auto.e24c442816b5"));
        }

        var alarmEditorVisible = m_entityAlarmWindowOpen &&
                                 m_editorWindowMode == EditorWindowMode.Rule;
        if (alarmEditorVisible &&
            !m_entityAssignmentPending &&
            m_selectedEntity != null &&
            m_pendingInspectionEntityId < 0 &&
            Time.realtimeSinceStartup >= m_nextEntityInspectionRefresh)
        {
            m_pendingInspectionEntityId = m_selectedEntity.EntityId;
            m_isAutomaticInspectionRefresh = true;
            m_runtime.RequestEntityInspection(m_selectedEntity.EntityId);
            m_nextEntityInspectionRefresh =
                Time.realtimeSinceStartup + 1f;
        }

        if (!m_isUiSuppressedByMenu && Input.GetKeyDown(KeyCode.F8))
        {
            m_isOpen = !m_isOpen;
        }

        var audible = m_testAlarm != null &&
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
    }

    private void OnGUI()
    {
        if (!m_gameplayWasActive || m_isUiSuppressedByMenu)
        {
            CancelResizeCapture();
            CancelEditorResizeCapture();
            m_inputBlocker?.SetKeyboardCaptured(false);
            return;
        }

        EnsureStyles();
        var previousMatrix = GUI.matrix;
        try
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(
                UiScale,
                UiScale,
                1f)) * previousMatrix;
            DrawGuiSurfaces();
            UpdateKeyboardInputCapture();
            ConsumePointerEventOverUi();
        }
        finally
        {
            GUI.matrix = previousMatrix;
        }
    }

    private void DrawGuiSurfaces()
    {
        DrawLauncher();

        if (m_isOpen)
        {
            // Keep the dimensions used inside DrawMainWindow identical to the
            // rectangle passed to GUI.Window, including after a resolution
            // change or a previously saved oversized window.
            m_windowRect = ClampToScreen(m_windowRect);
            var nextWindowRect = GUI.Window(
                MainWindowId,
                m_windowRect,
                DrawMainWindow,
                GUIContent.none,
                m_windowStyle);
            if (m_pendingMainWindowSize.HasValue)
            {
                nextWindowRect.width = m_pendingMainWindowSize.Value.x;
                nextWindowRect.height = m_pendingMainWindowSize.Value.y;
                m_pendingMainWindowSize = null;
            }
            m_windowRect = ClampToScreen(nextWindowRect);
            if (UnityEngine.Event.current.rawType == EventType.MouseUp)
            {
                PersistWindowRect();
            }
        }
        else
        {
            CancelResizeCapture();
        }

        if (m_entityAlarmWindowOpen)
        {
            var nextEditorRect = GUI.Window(
                EntityAlarmWindowId,
                ClampToScreen(m_entityAlarmWindowRect),
                DrawEntityAlarmWindow,
                GUIContent.none,
                m_windowStyle);
            if (m_pendingEditorWindowSize.HasValue)
            {
                nextEditorRect.width = m_pendingEditorWindowSize.Value.x;
                nextEditorRect.height = m_pendingEditorWindowSize.Value.y;
                m_pendingEditorWindowSize = null;
            }
            m_entityAlarmWindowRect = ClampToScreen(nextEditorRect);
            if (UnityEngine.Event.current.rawType == EventType.MouseUp)
            {
                PersistEditorWindowRect();
            }
        }
        else
        {
            CancelEditorResizeCapture();
        }

        for (var index = m_detachedPanels.Count - 1; index >= 0; index--)
        {
            var detached = m_detachedPanels[index];
            if (!detached.IsOpen)
            {
                m_detachedPanels.RemoveAt(index);
                continue;
            }

            var captured = detached;
            var nextDetachedRect = GUI.Window(
                captured.WindowId,
                ClampToScreen(captured.Rect),
                _ => DrawDetachedWindow(captured),
                GUIContent.none,
                m_windowStyle);
            if (captured.PendingSize.HasValue)
            {
                nextDetachedRect.width = captured.PendingSize.Value.x;
                nextDetachedRect.height = captured.PendingSize.Value.y;
                captured.PendingSize = null;
            }
            captured.Rect = ClampToScreen(nextDetachedRect);
        }

        // Keep the EventSystem representation in sync with window moves and
        // resizes performed during this IMGUI pass. It will be available to
        // the camera's early input pass on the following frame.
        UpdatePointerRaycastShield(true);
    }

    private void DrawLauncher()
    {
        if (m_isOpen)
        {
            return;
        }

        m_launcherRect.x = Mathf.Clamp(
            m_launcherRect.x,
            4f,
            Math.Max(4f, LogicalScreenWidth - m_launcherRect.width - 4f));
        m_launcherRect.y = Mathf.Clamp(
            m_launcherRect.y,
            72f,
            Math.Max(72f, LogicalScreenHeight - m_launcherRect.height - 4f));
        DrawPanelRect(m_launcherRect, new Color(0.08f, 0.09f, 0.09f, 0.96f));

        var buttonRect = new Rect(
            m_launcherRect.x + 3f,
            m_launcherRect.y + 3f,
            88f,
            28f);
        if (GUI.Button(
                buttonRect,
                m_runtime.UnacknowledgedCount > 0
                    ? UnmaText.Get("auto.bda0aafdab42") + m_runtime.UnacknowledgedCount
                    : UnmaText.Get("auto.6da300ca5a04"),
                m_runtime.UnacknowledgedCount > 0
                    ? m_dangerButtonStyle
                    : m_buttonStyle))
        {
            m_isOpen = !m_isOpen;
        }

        var dragRect = new Rect(
            m_launcherRect.x + 93f,
            m_launcherRect.y + 3f,
            20f,
            28f);
        GUI.Label(dragRect, "↕", m_headerStyle);
        var currentEvent = UnityEngine.Event.current;
        if (currentEvent.type == EventType.MouseDown &&
            dragRect.Contains(currentEvent.mousePosition))
        {
            m_isDraggingLauncher = true;
            m_launcherDragOffset = currentEvent.mousePosition -
                                   m_launcherRect.position;
            currentEvent.Use();
        }
        else if (m_isDraggingLauncher &&
                 currentEvent.type == EventType.MouseDrag)
        {
            m_launcherRect.position = currentEvent.mousePosition -
                                      m_launcherDragOffset;
            currentEvent.Use();
        }
        else if (m_isDraggingLauncher &&
                 currentEvent.type == EventType.MouseUp)
        {
            m_isDraggingLauncher = false;
            var config = m_runtime.Configuration;
            config.LauncherX = m_launcherRect.x;
            config.LauncherY = m_launcherRect.y;
            m_runtime.SaveConfiguration();
            currentEvent.Use();
        }
    }

    private void DrawMainWindow(int _)
    {
        HandleResizeInput();
        DrawWindowHeader(UnmaText.Get(
            "window.title",
            UnmaText.Get("auto.dfab1e8598ee")),
            m_windowRect.width);

        GUILayout.BeginArea(new Rect(
            12f,
            42f,
            m_windowRect.width - 24f,
            m_windowRect.height - 42f - MainWindowContentBottomInset));

        GUILayout.BeginHorizontal();
        DrawTabButton(TabBoard, UnmaText.Get("tab.board", "MELDETAFEL"));
        DrawTabButton(TabHistory, UnmaText.Get("tab.history", "VERLAUF"));
        DrawTabButton(TabSystem, UnmaText.Get("tab.system", "SYSTEM"));
        DrawTabButton(
            TabSounds,
            UnmaText.Get(
                "tab.notification_options",
                "NOTIFICATION OPTIONS"));
        DrawTabButton(TabOptions, UnmaText.Get("tab.options", "OPTIONEN"));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("—", m_buttonStyle, GUILayout.Width(36f)))
        {
            m_isOpen = false;
            GUI.FocusControl(null);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
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
            default:
                DrawBoard();
                break;
        }

        GUILayout.EndArea();
        DrawResizeHandle();
        GUI.DragWindow(new Rect(0f, 0f, m_windowRect.width - 44f, 38f));
    }

    private void DrawBoard()
    {
        var panel = CurrentPanel;
        if (panel == null)
        {
            GUILayout.Label(UnmaText.Get("auto.660051723bb3"), m_labelStyle);
            return;
        }

        GUILayout.BeginHorizontal();
        if (PanelTopologyPolicy.IsEntityPanel(panel))
        {
            if (GUILayout.Button(
                    UnmaText.Get("auto.c76615f2e3a1"),
                    m_buttonStyle,
                    GUILayout.Width(170f),
                    GUILayout.Height(30f)))
            {
                m_activeEntityPanelId = "";
                m_boardScroll = Vector2.zero;
            }
            GUILayout.Label(
                UnmaText.Get("auto.5a88ed325cbb") + panel.Name,
                m_primaryButtonStyle,
                GUILayout.Height(30f));
        }
        else
        {
            var globalPanels = GlobalPanels;
            m_panelTabsScroll = GUILayout.BeginScrollView(
                m_panelTabsScroll,
                false,
                false,
                GUILayout.Height(52f),
                GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            for (var index = 0; index < globalPanels.Count; index++)
            {
                var candidate = globalPanels[index];
                var tabWidth = Mathf.Clamp(
                    m_buttonStyle.CalcSize(
                        new GUIContent(candidate.Name)).x + 24f,
                    110f,
                    230f);
                if (GUILayout.Button(
                        candidate.Name,
                        index == m_currentPanelIndex
                            ? m_primaryButtonStyle
                            : m_buttonStyle,
                        GUILayout.Width(tabWidth),
                        GUILayout.Height(30f)))
                {
                    m_currentPanelIndex = index;
                    m_boardScroll = Vector2.zero;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            if (GUILayout.Button(
                    UnmaText.Get("auto.6f4982ecd932"),
                    m_buttonStyle,
                    GUILayout.Width(88f),
                    GUILayout.Height(30f)))
            {
                OpenPanelCreationEditor();
            }
            if (GUILayout.Button(
                    "⚙",
                    m_buttonStyle,
                    GUILayout.Width(42f),
                    GUILayout.Height(30f)))
            {
                OpenPanelSettingsEditor(panel);
            }
        }
        GUILayout.EndHorizontal();

        DrawEntityAssignmentBanner(panel);
        var alarms = GetPanelViews(panel);
        var activeCount = panel.IsDashboard
            ? alarms.Count
            : m_runtime.ActiveCount;
        var unacknowledgedCount = panel.IsDashboard
            ? alarms.Count(alarm => !alarm.IsAcknowledged)
            : m_runtime.UnacknowledgedCount;
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.397544fe1d24") + activeCount +
            UnmaText.Get("auto.ac9ef4c5783a") + unacknowledgedCount,
            m_sectionStyle,
            GUILayout.Height(34f));
        if (GUILayout.Button(
                UnmaText.Get("auto.e47523e046af"),
                m_dangerButtonStyle,
                GUILayout.Width(245f),
                GUILayout.Height(34f)))
        {
            m_runtime.AcknowledgeAll();
            m_audio.StopAlarm();
            SetStatus(
                UnmaText.Get("auto.dc2bb45a2f14"));
        }
        if (GUILayout.Button(
                UnmaText.Get("auto.c70a06d3a782"),
                m_buttonStyle,
                GUILayout.Width(180f),
                GUILayout.Height(34f)))
        {
            DetachPanel(panel.Id);
        }
        if (!panel.IsDashboard && GUILayout.Button(
                UnmaText.Get("auto.1cc8d34d4b3e"),
                m_primaryButtonStyle,
                GUILayout.Width(175f),
                GUILayout.Height(34f)))
        {
            OpenNewRuleEditor(panel);
        }
        GUILayout.EndHorizontal();

        DrawStatusMessage();
        if (!m_entityAssignmentPending)
        {
            GUILayout.Label(
                UnmaText.Get("auto.22344e5e1ac7"),
                m_smallLabelStyle);
        }
        m_boardScroll = GUILayout.BeginScrollView(m_boardScroll);
        DrawAlarmGrid(
            alarms,
            panel.Columns,
            m_windowRect.width - 54f,
            m_boardScroll.y,
            Math.Max(220f, m_windowRect.height - 190f),
            panel.IsDashboard ? null : panel,
            panel,
            m_entityAssignmentPending && !panel.IsDashboard,
            panel.IsDashboard
                ? UnmaText.Get("auto.f895fe84e658")
                : UnmaText.Get("auto.e8bad0a4452b"),
            !panel.IsDashboard);
        GUILayout.EndScrollView();
    }

    private void DrawEntityAssignmentBanner(PanelDefinition panel)
    {
        if (!m_entityAssignmentPending)
        {
            return;
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        var entityText = m_assignmentEntity == null
            ? UnmaText.Get("auto.2623e678be24") + m_assignmentEntityId + UnmaText.Get("auto.76e7b0bbc88e")
            : UnmaText.Get("auto.9eb6dbd0927f") +
              m_assignmentEntity.Title.ToUpperInvariant() +
              UnmaText.Get("auto.9da04860d6fc") + m_assignmentEntity.EntityId;
        GUILayout.Label(
            entityText,
            m_sectionStyle,
            GUILayout.Height(34f));
        if (GUILayout.Button(
                UnmaText.Get("auto.71418af14024"),
                m_buttonStyle,
                GUILayout.Width(190f),
                GUILayout.Height(34f)))
        {
            CancelEntityAssignment();
            SetStatus(UnmaText.Get("auto.a0b453e90074"));
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(
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
        var entries = GetHistoryEntries();

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.2cf87f46efd8") + entries.Count + UnmaText.Get("auto.79c82a039536"),
            m_sectionStyle,
            GUILayout.Height(34f));
        var confirmingDelete =
            Time.realtimeSinceStartup < m_pendingHistoryDeleteUntil;
        if (GUILayout.Button(
                confirmingDelete
                    ? UnmaText.Get("auto.beb568ff57a3")
                    : UnmaText.Get("auto.3ecf169c4abf"),
                confirmingDelete
                    ? m_dangerButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(230f),
                GUILayout.Height(34f)))
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
        GUILayout.EndHorizontal();

        GUILayout.Label(
            UnmaText.Get("auto.546f06f29ca0"),
            m_smallLabelStyle);
        DrawStatusMessage();
        DrawHistoryHeader();

        var historyViewportHeight =
            Math.Max(180f, m_windowRect.height - 210f);
        m_historyScroll.y = Mathf.Min(
            m_historyScroll.y,
            Math.Max(
                0f,
                entries.Count * (HistoryRowHeight + 4f) -
                historyViewportHeight));
        m_historyScroll = GUILayout.BeginScrollView(
            m_historyScroll,
            GUILayout.ExpandHeight(true));
        if (entries.Count == 0)
        {
            GUILayout.Space(16f);
            GUILayout.Label(
                UnmaText.Get("auto.d63794d49841"),
                m_labelStyle);
        }
        else
        {
            DrawHistoryRows(
                entries,
                m_historyScroll.y,
                historyViewportHeight);
        }
        GUILayout.EndScrollView();
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

    private void DrawHistoryHeader()
    {
        var rect = GUILayoutUtility.GetRect(
            0f,
            30f,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(30f));
        DrawPanelRect(rect, new Color(0.16f, 0.18f, 0.18f, 1f));
        var actionWidth = 98f;
        var stateWidth = 92f;
        GUI.Label(
            new Rect(
                rect.x + 10f,
                rect.y,
                rect.width - actionWidth - stateWidth - 20f,
                rect.height),
            "MELDETEXT",
            m_historyHeaderStyle);
        GUI.Label(
            new Rect(
                rect.xMax - actionWidth - stateWidth,
                rect.y,
                stateWidth,
                rect.height),
            "ZUSTAND",
            m_historyHeaderStyle);
        GUI.Label(
            new Rect(
                rect.xMax - actionWidth,
                rect.y,
                actionWidth,
                rect.height),
            "AKTION",
            m_historyHeaderStyle);
    }

    private void DrawHistoryRows(
        IReadOnlyList<AlarmHistoryDefinition> entries,
        float scrollY,
        float viewportHeight)
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
            GUILayout.Space(firstVisible * rowStep);
        }

        var blinkOn =
            Mathf.FloorToInt(Time.realtimeSinceStartup * 2.2f) % 2 == 0;
        for (var index = firstVisible; index < lastVisible; index++)
        {
            var rect = GUILayoutUtility.GetRect(
                0f,
                HistoryRowHeight,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(HistoryRowHeight));
            DrawHistoryRow(rect, entries[index], blinkOn);
            GUILayout.Space(4f);
        }

        if (lastVisible < entries.Count)
        {
            GUILayout.Space((entries.Count - lastVisible) * rowStep);
        }
    }

    private void DrawHistoryRow(
        Rect rect,
        AlarmHistoryDefinition entry,
        bool blinkOn)
    {
        var background = Color.white;
        var textStyle = m_historyTextStyle;
        if (!entry.IsGone && !entry.IsAcknowledged)
        {
            background = blinkOn
                ? new Color(0.82f, 0.04f, 0.04f, 1f)
                : new Color(0.18f, 0.03f, 0.03f, 1f);
            textStyle = m_historyAlertTextStyle;
        }
        else if (entry.IsGone && !entry.IsAcknowledged)
        {
            background = blinkOn
                ? Color.white
                : new Color(0.66f, 0.67f, 0.64f, 1f);
        }

        DrawPanelRect(rect, Color.black);
        var inner = new Rect(
            rect.x + 2f,
            rect.y + 2f,
            rect.width - 4f,
            rect.height - 4f);
        DrawPanelRect(inner, background);

        var actionWidth = 96f;
        var stateWidth = 90f;
        GUI.Label(
            new Rect(
                inner.x + 9f,
                inner.y,
                inner.width - actionWidth - stateWidth - 14f,
                inner.height),
            string.IsNullOrWhiteSpace(entry.Message)
                ? entry.AlarmKey
                : entry.Message,
            textStyle);
        GUI.Label(
            new Rect(
                inner.xMax - actionWidth - stateWidth,
                inner.y,
                stateWidth,
                inner.height),
            entry.StateCode,
            entry.StateCode == "K"
                ? m_historyAlertStateStyle
                : m_historyStateStyle);

        if (entry.CanDelete && GUI.Button(
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

    private void DrawEditor()
    {
        m_editorScroll = GUILayout.BeginScrollView(m_editorScroll);
        DrawStatusMessage();
        DrawPanelManagement();

        GUILayout.Space(12f);
        GUILayout.Label(
            string.IsNullOrWhiteSpace(m_editingRuleId)
                ? UnmaText.Get("auto.3fc83596b4ef")
                : UnmaText.Get("auto.f8226d218f15"),
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.30893e3ab657") +
            UnmaText.Get("auto.a4af228f3574") +
            UnmaText.Get("auto.9053cc535627") +
            UnmaText.Get("auto.6232fc63f818"),
            m_smallLabelStyle);
        DrawAlarmRuleEditor(false);

        GUILayout.Space(12f);
        DrawDefinedRules();
        GUILayout.EndScrollView();
    }

    private void DrawPanelManagement()
    {
        if (Time.realtimeSinceStartup > m_pendingPanelDeleteUntil)
        {
            m_pendingPanelDeleteId = "";
        }
        GUILayout.Label(UnmaText.Get("auto.251e714a80a6"), m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.8db078b96ea7"),
            m_smallLabelStyle);

        var panels = m_runtime.Configuration.Panels;
        if (panels.Count > 0)
        {
            m_currentPanelIndex = Math.Max(
                0,
                Math.Min(m_currentPanelIndex, panels.Count - 1));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", m_buttonStyle, GUILayout.Width(38f)))
            {
                m_currentPanelIndex = Wrap(m_currentPanelIndex - 1, panels.Count);
            }
            GUILayout.Label(
                panels[m_currentPanelIndex].Name +
                "   (" + (m_currentPanelIndex + 1) + "/" + panels.Count + ")",
                m_headerStyle,
                GUILayout.Height(30f));
            if (GUILayout.Button(">", m_buttonStyle, GUILayout.Width(38f)))
            {
                m_currentPanelIndex = Wrap(m_currentPanelIndex + 1, panels.Count);
            }
            GUILayout.EndHorizontal();
        }

        var panel = CurrentPanel;
        if (panel != null)
        {
            GUILayout.Space(6f);
            GUILayout.Label(UnmaText.Get("auto.d03a4752df6c"), m_sectionStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", m_labelStyle, GUILayout.Width(90f));
            panel.Name = GUILayout.TextField(
                panel.Name,
                40,
                m_textFieldStyle,
                GUILayout.Width(260f));
            GUILayout.Label(
                UnmaText.Get("auto.7f6972b99a3e") + panel.Columns,
                m_labelStyle,
                GUILayout.Width(90f));
            if (GUILayout.Button("-", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Columns = Math.Max(1, panel.Columns - 1);
            }
            if (GUILayout.Button("+", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Columns = Math.Min(8, panel.Columns + 1);
            }
            if (!panel.IsDashboard)
            {
                panel.IncludeVanilla = GUILayout.Toggle(
                    panel.IncludeVanilla,
                    UnmaText.Get("auto.ef309fc5dd19"),
                    GUILayout.Width(100f));
                panel.IncludeSystem = GUILayout.Toggle(
                    panel.IncludeSystem,
                    UnmaText.Get("auto.025c249edeb5"),
                    GUILayout.Width(100f));
            }
            else
            {
                GUILayout.Label(
                    UnmaText.Get("auto.6e1d936caf5d"),
                    m_smallLabelStyle,
                    GUILayout.Width(205f));
            }
            if (GUILayout.Button(
                    UnmaText.Get("auto.d4efd9369153"),
                    m_primaryButtonStyle,
                    GUILayout.Width(190f)))
            {
                SaveConfiguration(UnmaText.Get("auto.4bd5b213cd77"));
            }
            GUILayout.EndHorizontal();

            if (panel.IsDashboard)
            {
                GUILayout.Label(
                    UnmaText.Get("auto.e0e998aea68a") +
                    UnmaText.Get("auto.fee217fd8b0d") +
                    UnmaText.Get("auto.df66ce36493c") +
                    UnmaText.Get("ui.dashboard.not_deletable"),
                    m_smallLabelStyle);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "Auto-Filter",
                    m_labelStyle,
                    GUILayout.Width(90f));
                panel.NotificationFilter = GUILayout.TextField(
                    panel.NotificationFilter ?? "",
                    240,
                    m_textFieldStyle);
                GUI.enabled = panels.Count > 1;
                var pendingDelete = string.Equals(
                    m_pendingPanelDeleteId,
                    panel.Id,
                    StringComparison.Ordinal);
                var affectedRules = m_runtime.Configuration.Rules.Count(rule =>
                    string.Equals(
                        rule.PanelId,
                        panel.Id,
                        StringComparison.Ordinal));
                if (GUILayout.Button(
                        pendingDelete
                            ? UnmaText.Get("auto.2f4d2d64f711") + affectedRules + UnmaText.Get("auto.29b8add2ed8c")
                            : UnmaText.Get("auto.48a2c61d595d"),
                        m_dangerButtonStyle,
                        GUILayout.Width(220f)))
                {
                    RemoveCurrentPanel();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                DrawPanelSlots(panel);
            }
        }

        GUILayout.Space(6f);
        GUILayout.Label(UnmaText.Get("auto.ba2a4502c2e0"), m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.770ddae89d54"),
            m_labelStyle,
            GUILayout.Width(205f));
        m_newPanelName = GUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            GUILayout.Width(300f));
        if (GUILayout.Button(
                UnmaText.Get("auto.1aedbc19e04e"),
                m_primaryButtonStyle,
                GUILayout.Width(190f)))
        {
            AddPanel();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawPanelSlots(PanelDefinition panel)
    {
        panel.Slots ??= new List<PanelSlotDefinition>();
        GUILayout.Space(10f);
        GUILayout.Label(
            UnmaText.Get("auto.47b5a4a498c8") + panel.Slots.Count,
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.882f8bc83052"),
            m_smallLabelStyle);

        for (var index = 0; index < panel.Slots.Count; index++)
        {
            var slot = panel.Slots[index];
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                (index + 1).ToString("00", CultureInfo.InvariantCulture),
                m_smallLabelStyle,
                GUILayout.Width(28f));
            GUILayout.Label(
                (slot.DisplayName ?? "MELDUNG") + "   ·   " +
                SlotSourceLabel(slot.Source),
                m_labelStyle);
            GUI.enabled = index > 0;
            if (GUILayout.Button("↑", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Slots.RemoveAt(index);
                panel.Slots.Insert(index - 1, slot);
                SaveConfiguration(UnmaText.Get("auto.e4e962c7b82e"));
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                return;
            }
            GUI.enabled = index < panel.Slots.Count - 1;
            if (GUILayout.Button("↓", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Slots.RemoveAt(index);
                panel.Slots.Insert(index + 1, slot);
                SaveConfiguration(UnmaText.Get("auto.f0dec1316ddd"));
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                return;
            }
            var isCustom = string.Equals(
                slot.Source,
                "custom",
                StringComparison.Ordinal);
            GUI.enabled = !isCustom;
            if (GUILayout.Button(
                    isCustom ? UnmaText.Get("auto.063bd868b890") : "ENTFERNEN",
                    m_buttonStyle,
                    GUILayout.Width(105f)))
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
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                return;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.02a7427b4413"),
            m_labelStyle,
            GUILayout.Width(205f));
        m_panelSlotFilter = GUILayout.TextField(
            m_panelSlotFilter,
            80,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

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
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                (slot.DisplayName ?? "MELDUNG") + "   ·   " +
                SlotSourceLabel(slot.Source),
                m_smallLabelStyle);
            if (GUILayout.Button(
                    UnmaText.Get("auto.15a322e13c45"),
                    m_primaryButtonStyle,
                    GUILayout.Width(105f)))
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
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
        }
        if (available.Length == 0)
        {
            GUILayout.Label(
                UnmaText.Get("auto.f7502479c781"),
                m_smallLabelStyle);
        }
    }

    private static string SlotSourceLabel(string source)
    {
        return source switch
        {
            "vanilla" => "VANILLA",
            "system" => "SYSTEM",
            "custom" => UnmaText.Get("auto.5aa074c71bd3"),
            _ => "MELDUNG",
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
        GUILayout.Label(UnmaText.Get("auto.1d7281b62bea"), m_sectionStyle);
        var sounds = m_audio.GetSoundOptions();
        var panelId = CurrentPanel?.Id;
        foreach (var rule in m_runtime.Configuration.Rules
                     .Where(rule => rule.PanelId == panelId)
                     .ToArray())
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    rule.Enabled ? "AN" : "AUS",
                    rule.Enabled ? m_primaryButtonStyle : m_buttonStyle,
                    GUILayout.Width(52f)))
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
            GUILayout.Label(
                rule.Name + " · " + SeverityLabel(rule.Severity) +
                " · " + rule.Conditions.Count + UnmaText.Get("auto.05534195bbe5") +
                (rule.Logic == AlarmLogic.All ? "UND" : "ODER") + " · " +
                (rule.AutoAcknowledgeOnClear
                    ? UnmaText.Get("auto.367f30137868")
                    : UnmaText.Get("auto.c9097d398192")),
                m_labelStyle);
            if (GUILayout.Button(
                    "BEARBEITEN",
                    m_buttonStyle,
                    GUILayout.Width(105f)))
            {
                BeginEditingRule(rule, sounds);
                var firstCondition = rule.Conditions.FirstOrDefault();
                if (firstCondition == null)
                {
                    m_entityAlarmWindowOpen = true;
                }
                else
                {
                    RequestEntityInspection(
                        firstCondition.EntityId,
                        true);
                }
            }
            if (GUILayout.Button(
                    UnmaText.Get("auto.9cf94f11833b"),
                    m_dangerButtonStyle,
                    GUILayout.Width(90f)))
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
            GUILayout.EndHorizontal();
        }
    }

    private void DrawEntityAlarmWindow(int _)
    {
        HandleEditorResizeInput();
        var targetPanel = GetDraftTargetPanel();
        var title = m_editorWindowMode switch
        {
            EditorWindowMode.PanelCreation =>
                UnmaText.Get("auto.5e9e7c9addd9"),
            EditorWindowMode.PanelSettings =>
                UnmaText.Get("auto.0e8b76140a09"),
            _ => UnmaText.Get("auto.b9ccafdfaef7") +
                 (targetPanel == null ? "" : " · " + targetPanel.Name),
        };
        DrawWindowHeader(title, m_entityAlarmWindowRect.width);

        if (GUI.Button(
                new Rect(m_entityAlarmWindowRect.width - 52f, 8f, 40f, 28f),
                "X",
                m_buttonStyle))
        {
            m_entityAlarmWindowOpen = false;
            m_openEntityAlarmAfterInspection = false;
            CancelEditorResizeCapture();
            GUI.FocusControl(null);
        }

        GUILayout.BeginArea(new Rect(
            12f,
            42f,
            m_entityAlarmWindowRect.width - 24f,
            m_entityAlarmWindowRect.height - 42f -
            MainWindowContentBottomInset));
        DrawStatusMessage();
        m_entityAlarmScroll = GUILayout.BeginScrollView(m_entityAlarmScroll);
        if (m_editorWindowMode == EditorWindowMode.PanelCreation)
        {
            DrawNewPanelWindowContent();
        }
        else if (m_editorWindowMode == EditorWindowMode.PanelSettings)
        {
            DrawPanelSettingsWindowContent();
        }
        else
        {
            DrawAlarmRuleEditor(true);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        DrawEditorResizeHandle();
        GUI.DragWindow(new Rect(
            0f,
            0f,
            m_entityAlarmWindowRect.width - 58f,
            38f));
    }

    private void DrawNewPanelWindowContent()
    {
        GUILayout.Label(UnmaText.Get("auto.ba2a4502c2e0"), m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.05a309b6f1bd") +
            UnmaText.Get("auto.61fdafb643aa"),
            m_smallLabelStyle);
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", m_labelStyle, GUILayout.Width(120f));
        m_newPanelName = GUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            GUILayout.Width(360f));
        if (GUILayout.Button(
                UnmaText.Get("auto.ea4da1cee467"),
                m_primaryButtonStyle,
                GUILayout.Width(180f),
                GUILayout.Height(32f)))
        {
            if (AddPanel())
            {
                m_entityAlarmWindowOpen = false;
            }
        }
        GUILayout.EndHorizontal();
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
            GUILayout.Label(
                UnmaText.Get("auto.0e35fa3ee857"),
                m_labelStyle);
            return;
        }

        GUILayout.Label(UnmaText.Get("auto.63a4d85953f8"), m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", m_labelStyle, GUILayout.Width(90f));
        m_panelSettingsName = GUILayout.TextField(
            m_panelSettingsName,
            40,
            m_textFieldStyle,
            GUILayout.Width(300f));
        GUILayout.Label(
            UnmaText.Get("auto.7f6972b99a3e") + m_panelSettingsColumns,
            m_labelStyle,
            GUILayout.Width(90f));
        if (GUILayout.Button("−", m_buttonStyle, GUILayout.Width(36f)))
        {
            m_panelSettingsColumns = Math.Max(
                1,
                m_panelSettingsColumns - 1);
        }
        if (GUILayout.Button("+", m_buttonStyle, GUILayout.Width(36f)))
        {
            m_panelSettingsColumns = Math.Min(
                8,
                m_panelSettingsColumns + 1);
        }
        if (GUILayout.Button(
                "SPEICHERN",
                m_primaryButtonStyle,
                GUILayout.Width(150f)))
        {
            SavePanelSettings(panel);
        }
        GUILayout.EndHorizontal();

        if (panel.IsDashboard)
        {
            GUILayout.Label(
                UnmaText.Get("auto.2eb2c75b7d87") +
                UnmaText.Get("auto.a1af7061ed28"),
                m_smallLabelStyle);
            return;
        }

        GUILayout.BeginHorizontal();
        m_panelSettingsIncludeVanilla = GUILayout.Toggle(
            m_panelSettingsIncludeVanilla,
            UnmaText.Get("auto.d696777f43cd"),
            GUILayout.Width(170f));
        m_panelSettingsIncludeSystem = GUILayout.Toggle(
            m_panelSettingsIncludeSystem,
            UnmaText.Get("auto.e71a0cea7772"),
            GUILayout.Width(170f));
        GUILayout.Label("Auto-Filter", m_labelStyle, GUILayout.Width(90f));
        m_panelSettingsFilter = GUILayout.TextField(
            m_panelSettingsFilter,
            240,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

        GUILayout.Label(
            UnmaText.Get("auto.fe1185445958"),
            m_smallLabelStyle);
        DrawPanelSlots(panel);

        GUILayout.Space(12f);
        var confirmingDelete = string.Equals(
                                   m_pendingPanelDeleteId,
                                   panel.Id,
                                   StringComparison.Ordinal) &&
                               Time.realtimeSinceStartup <=
                               m_pendingPanelDeleteUntil;
        if (GUILayout.Button(
                confirmingDelete
                    ? UnmaText.Get("auto.df65358a4dae")
                    : UnmaText.Get("auto.74d628988b87"),
                confirmingDelete ? m_dangerButtonStyle : m_buttonStyle,
                GUILayout.Width(220f)))
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
                m_detachedPanels.RemoveAll(item => item.PanelId == panel.Id);
                m_entityAlarmWindowOpen = false;
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

    private void SavePanelSettings(PanelDefinition panel)
    {
        if (m_runtime.UpdatePanelSettings(
                panel.Id,
                m_panelSettingsName,
                m_panelSettingsColumns,
                m_panelSettingsIncludeVanilla,
                m_panelSettingsIncludeSystem,
                m_panelSettingsFilter))
        {
            m_panelSettingsName = panel.Name;
            m_panelSettingsColumns = panel.Columns;
            m_panelSettingsIncludeVanilla = panel.IncludeVanilla;
            m_panelSettingsIncludeSystem = panel.IncludeSystem;
            m_panelSettingsFilter = panel.NotificationFilter ?? "";
            SetStatus(UnmaText.Get("auto.4bd5b213cd77"));
            return;
        }

        SetStatus(
            UnmaText.Get("auto.27f10f6dc69e") +
            m_runtime.LastPersistenceError);
    }

    private void DrawAlarmRuleEditor(bool inEntityWindow)
    {
        DrawTargetPanelSelector(inEntityWindow);
        GUILayout.Space(6f);
        DrawEntitySourceSelector(inEntityWindow);
        if (m_selectedEntity != null && m_selectedMetrics.Count > 0)
        {
            GUILayout.Space(6f);
            DrawNewConditionForm();
        }

        GUILayout.Space(8f);
        DrawConditionTable();
        GUILayout.Space(8f);
        DrawAlarmProperties();
    }

    private void DrawTargetPanelSelector(bool allowCreate)
    {
        GUILayout.Label("ZIEL-MELDETAFEL", m_sectionStyle);
        var panel = GetDraftTargetPanel();
        if (panel == null)
        {
            GUILayout.Label(
                UnmaText.Get("auto.ebe65b2ddfb6") +
                UnmaText.Get("auto.193650f56055"),
                m_labelStyle);
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            PanelTopologyPolicy.IsEntityPanel(panel)
                ? UnmaText.Get("auto.ef933adc4bdb")
                : UnmaText.Get("auto.3ed702323b47"),
            m_labelStyle,
            GUILayout.Width(160f));
        GUILayout.Label(
            panel.Name,
            m_headerStyle,
            GUILayout.Height(30f));
        GUILayout.EndHorizontal();

        if (PanelTopologyPolicy.IsEntityPanel(panel))
        {
            DrawGlobalPanelLinks();
        }
    }

    private void DrawGlobalPanelLinks()
    {
        GUILayout.Space(6f);
        GUILayout.Label(
            UnmaText.Get("auto.c350c4d6b1d5"),
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.7237b12624f3") +
            UnmaText.Get("auto.e4505264649b"),
            m_smallLabelStyle);

        var globalTargets = GlobalPanels
            .Where(panel => !panel.IsDashboard)
            .ToArray();
        if (globalTargets.Length == 0)
        {
            GUILayout.Label(
                UnmaText.Get("auto.637c3fbb4c15"),
                m_smallLabelStyle);
            return;
        }

        foreach (var globalPanel in globalTargets)
        {
            GUILayout.BeginHorizontal();
            var linked = m_draftLinkedPanelIds.Contains(globalPanel.Id);
            if (GUILayout.Button(
                    linked
                        ? "✓ " + globalPanel.Name
                        : "+ " + globalPanel.Name,
                    linked ? m_primaryButtonStyle : m_buttonStyle,
                    GUILayout.Width(420f),
                    GUILayout.Height(30f)))
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
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    private void DrawCreateTargetPanelRow(bool slotPositionLocked)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.96cad36109c7"),
            m_labelStyle,
            GUILayout.Width(205f));
        var guiWasEnabled = GUI.enabled;
        GUI.enabled = guiWasEnabled && !slotPositionLocked;
        m_newPanelName = GUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            GUILayout.Width(310f));
        if (GUILayout.Button(
                UnmaText.Get("auto.af812ec572bb"),
                m_buttonStyle,
                GUILayout.Width(205f)))
        {
            AddPanel();
        }
        GUI.enabled = guiWasEnabled;
        GUILayout.Label(
            slotPositionLocked
                ? UnmaText.Get("auto.da45fd0a048f")
                : UnmaText.Get("auto.83f9628c70ab"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawEntitySourceSelector(bool inEntityWindow)
    {
        GUILayout.Label("QUELLOBJEKT", m_sectionStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
                UnmaText.Get("auto.7edb47ed7ea9"),
                m_primaryButtonStyle,
                GUILayout.Width(315f),
                GUILayout.Height(30f)))
        {
            CaptureSelectedEntity(inEntityWindow);
        }
        GUILayout.Label(
            m_selectedEntity == null
                ? UnmaText.Get("auto.51f6d86aa271") +
                  UnmaText.Get("auto.3ebeb0f6f700") +
                  UnmaText.Get("ui.entity.take_selection")
                : m_selectedEntity.Title + " · " +
                  ShortTypeName(m_selectedEntity.EntityType) +
                  UnmaText.Get("auto.9da04860d6fc") + m_selectedEntity.EntityId +
                  " · " + m_selectedMetrics.Count + UnmaText.Get("auto.c8b47a039c3f"),
            m_labelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawNewConditionForm()
    {
        m_selectedMetricIndex = Math.Max(
            0,
            Math.Min(m_selectedMetricIndex, m_selectedMetrics.Count - 1));
        var metric = m_selectedMetrics[m_selectedMetricIndex];

        GUILayout.Label(UnmaText.Get("auto.d7ee9125f8f1"), m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label(UnmaText.Get("auto.6bb4e33de37c"), m_labelStyle, GUILayout.Width(150f));
        if (GUILayout.Button(
                metric.Label + UnmaText.Get("auto.e824707b8b2d") + FormatMetricValue(metric) + "]",
                m_metricPickerOpen ? m_primaryButtonStyle : m_buttonStyle,
                GUILayout.Height(30f)))
        {
            m_metricPickerOpen = !m_metricPickerOpen;
            m_referenceMetricPickerOpen = false;
        }
        GUILayout.EndHorizontal();

        if (m_metricPickerOpen)
        {
            DrawMetricPicker(false);
            metric = m_selectedMetrics[m_selectedMetricIndex];
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Berechnung", m_labelStyle, GUILayout.Width(150f));
        if (GUILayout.Button(
                "ABSOLUT",
                m_draftValueMode == ConditionValueMode.Absolute
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(125f)))
        {
            m_draftValueMode = ConditionValueMode.Absolute;
            m_referenceMetricPickerOpen = false;
        }
        if (GUILayout.Button(
                metric.Path.StartsWith(
                    "$input.product:",
                    StringComparison.Ordinal)
                    ? UnmaText.Get("auto.b3ada244026c")
                    : UnmaText.Get("auto.9424124c3537"),
                m_draftValueMode == ConditionValueMode.PercentOfReference
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(125f)))
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
            if (GUILayout.Button(
                    UnmaText.Get("auto.cbe287253675") + reference.Label +
                    " [" + FormatMetricValue(reference) + "]",
                    m_referenceMetricPickerOpen
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    GUILayout.Height(30f)))
            {
                m_referenceMetricPickerOpen = !m_referenceMetricPickerOpen;
                m_metricPickerOpen = false;
            }
        }
        GUILayout.EndHorizontal();

        if (metric.Path.StartsWith(
                "$input.product:",
                StringComparison.Ordinal))
        {
            GUILayout.Label(
                UnmaText.Get("auto.104537c3e0ed") +
                UnmaText.Get("auto.0c0050a05708"),
                m_smallLabelStyle);
        }

        if (m_referenceMetricPickerOpen &&
            m_draftValueMode == ConditionValueMode.PercentOfReference)
        {
            DrawMetricPicker(true);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Steuerzeichen", m_labelStyle, GUILayout.Width(150f));
        DrawComparisonSelector(ref m_draftComparison);
        GUILayout.Space(12f);
        GUILayout.Label(
            m_draftValueMode == ConditionValueMode.PercentOfReference
                ? UnmaText.Get("auto.23a9b1f4773d")
                : "Soll-Wert",
            m_labelStyle,
            GUILayout.Width(105f));
        m_draftThreshold = GUILayout.TextField(
            m_draftThreshold,
            24,
            m_textFieldStyle,
            GUILayout.Width(105f));
        if (GUILayout.Button(
                UnmaText.Get("auto.3cb2b0054d58"),
                m_primaryButtonStyle,
                GUILayout.Width(190f),
                GUILayout.Height(30f)))
        {
            AddDraftCondition();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawMetricPicker(bool referencePicker)
    {
        var filter = referencePicker
            ? m_referenceMetricPickerFilter
            : m_metricPickerFilter;
        GUILayout.BeginHorizontal();
        GUILayout.Space(150f);
        GUILayout.Label("Suchen", m_smallLabelStyle, GUILayout.Width(60f));
        filter = GUILayout.TextField(
            filter,
            60,
            m_textFieldStyle,
            GUILayout.Width(280f));
        GUILayout.Label(
            UnmaText.Get("auto.84d283754bde"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();
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
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(170f));
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
                GUILayout.Label(
                    UnmaText.Get("auto.7a9d07fa642b"),
                    m_smallLabelStyle);
                break;
            }

            var selected = referencePicker
                ? index == m_selectedReferenceMetricIndex
                : index == m_selectedMetricIndex;
            if (GUILayout.Button(
                    candidate.Label + UnmaText.Get("auto.fe59854f2cdf") +
                    FormatMetricValue(candidate),
                    selected ? m_primaryButtonStyle : m_buttonStyle,
                    GUILayout.Height(27f)))
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
        GUILayout.EndScrollView();
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
            if (GUILayout.Button(
                    UnmaRuntime.OperatorText(candidate),
                    comparison == candidate
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    GUILayout.Width(42f),
                    GUILayout.Height(28f)))
            {
                comparison = candidate;
            }
        }
    }

    private void DrawConditionTable()
    {
        GUILayout.Label(UnmaText.Get("auto.6dc84400fbd4"), m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("IST-WERT", m_smallLabelStyle, GUILayout.Width(135f));
        GUILayout.Label("KENNUNG", m_smallLabelStyle, GUILayout.Width(330f));
        GUILayout.Label("STEUERZEICHEN", m_smallLabelStyle, GUILayout.Width(265f));
        GUILayout.Label("SOLL-WERT", m_smallLabelStyle, GUILayout.Width(115f));
        GUILayout.Label("BEDINGUNG", m_smallLabelStyle, GUILayout.Width(90f));
        GUILayout.EndHorizontal();

        if (m_draftConditions.Count == 0)
        {
            GUILayout.Label(
                UnmaText.Get("auto.71931e3b5361"),
                m_smallLabelStyle);
            return;
        }

        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            var condition = m_draftConditions[index];
            while (m_draftConditionThresholdTexts.Count <= index)
            {
                m_draftConditionThresholdTexts.Add(
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture));
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                ConditionActualText(condition),
                m_labelStyle,
                GUILayout.Width(135f),
                GUILayout.Height(42f));
            GUILayout.BeginVertical(GUILayout.Width(330f));
            GUILayout.Label(
                condition.EntityTitle + " #" + condition.EntityId +
                " · " + condition.MetricLabel,
                m_labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    condition.ValueMode == ConditionValueMode.Absolute
                        ? "ABSOLUT"
                        : UnmaText.Get("auto.9424124c3537"),
                    m_buttonStyle,
                    GUILayout.Width(85f)))
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
                if (GUILayout.Button(
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
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            var comparison = condition.Comparison;
            GUILayout.BeginHorizontal(GUILayout.Width(265f));
            DrawComparisonSelector(ref comparison);
            GUILayout.EndHorizontal();
            condition.Comparison = comparison;

            m_draftConditionThresholdTexts[index] = GUILayout.TextField(
                m_draftConditionThresholdTexts[index],
                24,
                m_textFieldStyle,
                GUILayout.Width(105f),
                GUILayout.Height(30f));
            GUILayout.Label(
                index == 0
                    ? "START"
                    : m_draftLogic == AlarmLogic.All ? "UND" : "ODER",
                m_headerStyle,
                GUILayout.Width(70f),
                GUILayout.Height(30f));
            if (GUILayout.Button(
                    "X",
                    m_dangerButtonStyle,
                    GUILayout.Width(38f),
                    GUILayout.Height(30f)))
            {
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
            GUILayout.EndHorizontal();

            if (index >= 0 && m_conditionReferencePickerIndex == index)
            {
                DrawConditionReferencePicker(condition);
            }
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UnmaText.Get("auto.956d69c9e3ca"), m_labelStyle, GUILayout.Width(210f));
        if (GUILayout.Button(
                UnmaText.Get("auto.76efbe95b3a4"),
                m_draftLogic == AlarmLogic.All
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(290f)))
        {
            m_draftLogic = AlarmLogic.All;
        }
        if (GUILayout.Button(
                UnmaText.Get("auto.556080cfb23f"),
                m_draftLogic == AlarmLogic.Any
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(300f)))
        {
            m_draftLogic = AlarmLogic.Any;
        }
        GUILayout.Label(
            UnmaText.Get("auto.9a99ea646292"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawConditionReferencePicker(ConditionDefinition condition)
    {
        if (m_selectedEntity == null ||
            m_selectedEntity.EntityId != condition.EntityId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(135f);
            GUILayout.Label(
                UnmaText.Get("auto.af0f45a59557"),
                m_smallLabelStyle);
            if (GUILayout.Button(
                    UnmaText.Get("auto.c29601081242"),
                    m_buttonStyle,
                    GUILayout.Width(190f)))
            {
                RequestEntityInspection(condition.EntityId, false);
            }
            GUILayout.EndHorizontal();
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Space(135f);
        GUILayout.Label(UnmaText.Get("auto.bb45057d02f0"), m_smallLabelStyle, GUILayout.Width(90f));
        m_referenceMetricPickerFilter = GUILayout.TextField(
            m_referenceMetricPickerFilter,
            60,
            m_textFieldStyle,
            GUILayout.Width(280f));
        GUILayout.Label(
            UnmaText.Get("auto.d47099108ed4"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();

        m_referenceMetricPickerScroll = GUILayout.BeginScrollView(
            m_referenceMetricPickerScroll,
            GUILayout.Height(170f));
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
            if (GUILayout.Button(
                    UnmaText.Get("auto.64762227fbd5") + metric.Label + UnmaText.Get("auto.f583d8b1f88d") +
                    FormatMetricValue(metric),
                    string.Equals(
                        condition.ReferenceMetricPath,
                        metric.Path,
                        StringComparison.Ordinal)
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    GUILayout.Height(26f)))
            {
                condition.ReferenceMetricPath = metric.Path;
                condition.ReferenceMetricLabel = metric.Label;
                m_conditionReferencePickerIndex = -1;
            }
        }
        GUILayout.EndScrollView();
    }

    private void DrawAlarmProperties()
    {
        GUILayout.Label("MELDUNG", m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Meldetext", m_labelStyle, GUILayout.Width(105f));
        m_draftRuleName = GUILayout.TextField(
            m_draftRuleName,
            80,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Stufe", m_labelStyle, GUILayout.Width(105f));
        foreach (AlarmSeverity severity in Enum.GetValues(typeof(AlarmSeverity)))
        {
            if (GUILayout.Button(
                    SeverityLabel(severity),
                    m_draftSeverity == severity
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    GUILayout.Width(125f)))
            {
                m_draftSeverity = severity;
                m_draftColor = DefaultColorFor(severity);
            }
        }
        GUILayout.Label("Aktivfarbe", m_labelStyle, GUILayout.Width(85f));
        m_draftColor = GUILayout.TextField(
            m_draftColor,
            9,
            m_textFieldStyle,
            GUILayout.Width(95f));
        GUILayout.EndHorizontal();

        var sounds = m_audio.GetSoundOptions();
        if (sounds.Count > 0)
        {
            m_draftSoundIndex = Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ton", m_labelStyle, GUILayout.Width(105f));
            if (GUILayout.Button("<", m_buttonStyle, GUILayout.Width(38f)))
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
            GUILayout.Label(
                originalSoundMissing
                    ? UnmaText.Get("auto.40bffd508dbf") + m_originalDraftSoundId
                    : sounds[m_draftSoundIndex].Label,
                m_labelStyle,
                GUILayout.Width(310f));
            if (GUILayout.Button(">", m_buttonStyle, GUILayout.Width(38f)))
            {
                m_draftSoundIndex = Wrap(m_draftSoundIndex + 1, sounds.Count);
                m_draftSoundChanged = true;
            }
            GUI.enabled = !originalSoundMissing;
            if (GUILayout.Button(
                    UnmaText.Get("auto.775da082f4c5"),
                    m_buttonStyle,
                    GUILayout.Width(125f)))
            {
                TestSound(sounds[m_draftSoundIndex].Id, m_draftSeverity);
            }
            GUI.enabled = true;
            if (GUILayout.Button(
                    UnmaText.Get("auto.ae84ac2ff8ca"),
                    m_buttonStyle,
                    GUILayout.Width(105f)))
            {
                StopTestSound();
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Space(105f);
        m_draftAutoAcknowledgeOnClear = GUILayout.Toggle(
            m_draftAutoAcknowledgeOnClear,
            UnmaText.Get("auto.19a7e6f7335e"),
            GUILayout.Width(340f));
        GUILayout.Label(
            UnmaText.Get("auto.f8daf4186ab9"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = m_draftConditions.Count > 0 &&
                      GetDraftTargetPanel() != null;
        if (GUILayout.Button(
                string.IsNullOrWhiteSpace(m_editingRuleId)
                    ? UnmaText.Get("auto.3a86ba973853")
                    : UnmaText.Get("auto.d4efd9369153"),
                m_primaryButtonStyle,
                GUILayout.Width(220f),
                GUILayout.Height(34f)))
        {
            SaveDraftRule(sounds);
        }
        GUI.enabled = true;
        if (GUILayout.Button(
                UnmaText.Get("auto.bc47a8f97988"),
                m_buttonStyle,
                GUILayout.Width(155f),
                GUILayout.Height(34f)))
        {
            ResetDraftRule();
            SetStatus(UnmaText.Get("auto.8df90cb55cac"));
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSystemAlarms()
    {
        if (Time.realtimeSinceStartup > m_pendingSystemResetUntil)
        {
            m_pendingSystemResetId = "";
        }
        GUILayout.Label(UnmaText.Get("auto.2d1f579a5d01"), m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.2092938a7b0b"),
            m_smallLabelStyle);

        m_systemAlarmScroll = GUILayout.BeginScrollView(m_systemAlarmScroll);
        if (m_systemAlarmDraft == null)
        {
            foreach (var alarm in m_runtime.GetSystemAlarmDefinitions())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    alarm.DisplayName + " · " +
                    alarm.Stages.Count(stage => stage.Enabled) +
                    UnmaText.Get("auto.da08863fac44") +
                    (alarm.AutoAcknowledgeOnClear
                        ? UnmaText.Get("auto.367f30137868")
                        : UnmaText.Get("auto.c9097d398192")),
                    m_labelStyle);
                if (GUILayout.Button(
                        alarm.Enabled ? "AN" : "AUS",
                        alarm.Enabled
                            ? m_primaryButtonStyle
                            : m_buttonStyle,
                        GUILayout.Width(55f)))
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
                if (GUILayout.Button(
                        "BEARBEITEN",
                        m_buttonStyle,
                        GUILayout.Width(115f)))
                {
                    BeginEditingSystemAlarm(alarm);
                }
                if (GUILayout.Button(
                        string.Equals(
                            m_pendingSystemResetId,
                            alarm.Id,
                            StringComparison.Ordinal)
                            ? UnmaText.Get("auto.91d331fc1397")
                            : "WERKSVORGABE",
                        string.Equals(
                            m_pendingSystemResetId,
                            alarm.Id,
                            StringComparison.Ordinal)
                            ? m_dangerButtonStyle
                            : m_buttonStyle,
                        GUILayout.Width(125f)))
                {
                    if (!string.Equals(
                            m_pendingSystemResetId,
                            alarm.Id,
                            StringComparison.Ordinal))
                    {
                        m_pendingSystemResetId = alarm.Id;
                        m_pendingSystemResetUntil =
                            Time.realtimeSinceStartup + 5f;
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
                GUILayout.EndHorizontal();
            }
        }
        else
        {
            DrawSystemAlarmDraft();
        }
        DrawStatusMessage();
        GUILayout.EndScrollView();
    }

    private void DrawSystemAlarmDraft()
    {
        var draft = m_systemAlarmDraft;
        var sounds = m_audio.GetSoundOptions();
        var metrics = SystemMetricCatalog.All;
        var currentValues = m_runtime.GetSystemMetricValues();

        GUILayout.BeginHorizontal();
        draft.Enabled = GUILayout.Toggle(
            draft.Enabled,
            UnmaText.Get("auto.9bb40c22f772"),
            GUILayout.Width(170f));
        GUILayout.Label("Name", m_labelStyle, GUILayout.Width(45f));
        draft.DisplayName = GUILayout.TextField(
            draft.DisplayName ?? "",
            60,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        draft.AutoAcknowledgeOnClear = GUILayout.Toggle(
            draft.AutoAcknowledgeOnClear,
            UnmaText.Get("auto.19a7e6f7335e"),
            GUILayout.Width(340f));
        GUILayout.Label(
            UnmaText.Get("auto.e330dc16dd70"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();

        foreach (var stage in draft.Stages
                     .OrderBy(stage => stage.Priority)
                     .ToArray())
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            stage.Enabled = GUILayout.Toggle(
                stage.Enabled,
                UnmaText.Get("auto.6477bc93951f"),
                GUILayout.Width(105f));
            GUILayout.Label("Text", m_labelStyle, GUILayout.Width(38f));
            stage.Message = GUILayout.TextField(
                stage.Message ?? "",
                100,
                m_textFieldStyle);
            if (GUILayout.Button(
                    SeverityLabel(stage.Severity),
                    m_buttonStyle,
                    GUILayout.Width(105f)))
            {
                stage.Severity = NextEnum(stage.Severity);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    stage.Logic == AlarmLogic.All
                        ? UnmaText.Get("auto.77bbd577fc42")
                        : UnmaText.Get("auto.7c378839a7f0"),
                    m_buttonStyle,
                    GUILayout.Width(115f)))
            {
                stage.Logic = stage.Logic == AlarmLogic.All
                    ? AlarmLogic.Any
                    : AlarmLogic.All;
            }
            GUILayout.Label("Farbe", m_labelStyle, GUILayout.Width(48f));
            stage.ActiveColor = GUILayout.TextField(
                stage.ActiveColor ?? "auto",
                9,
                m_textFieldStyle,
                GUILayout.Width(92f));

            if (sounds.Count > 0)
            {
                var soundIndex = FindSoundIndex(sounds, stage.SoundId);
                var soundAvailable = sounds.Any(sound => string.Equals(
                    sound.Id,
                    stage.SoundId,
                    StringComparison.OrdinalIgnoreCase));
                if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(30f)))
                {
                    soundIndex = Wrap(soundIndex - 1, sounds.Count);
                    stage.SoundId = sounds[soundIndex].Id;
                }
                GUILayout.Label(
                    soundAvailable
                        ? sounds[soundIndex].Label
                        : UnmaText.Get("auto.40bffd508dbf") + stage.SoundId,
                    m_smallLabelStyle,
                    GUILayout.Width(190f));
                if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(30f)))
                {
                    soundIndex = Wrap(soundIndex + 1, sounds.Count);
                    stage.SoundId = sounds[soundIndex].Id;
                }
                if (GUILayout.Button(
                        "TEST",
                        m_buttonStyle,
                        GUILayout.Width(55f)))
                {
                    TestSound(stage.SoundId, stage.Severity);
                }
            }
            GUILayout.EndHorizontal();

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
                var thresholdKey = SystemThresholdKey(stage.Id, index);
                if (!m_systemThresholdTexts.TryGetValue(
                        thresholdKey,
                        out var thresholdText))
                {
                    thresholdText = condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture);
                    m_systemThresholdTexts[thresholdKey] = thresholdText;
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(30f)))
                {
                    metricIndex = metricIndex < 0
                        ? metrics.Count - 1
                        : Wrap(metricIndex - 1, metrics.Count);
                    condition.MetricId = metrics[metricIndex].Id;
                    metric = metrics[metricIndex];
                }
                GUILayout.Label(
                    metric.Label + " · " + metric.Unit +
                    (currentValues.TryGetValue(metric.Id, out var current)
                        ? UnmaText.Get("auto.aa3d8483c2cc") + current.ToString(
                            "0.##",
                            CultureInfo.CurrentCulture) + "]"
                        : ""),
                    m_smallLabelStyle,
                    GUILayout.Width(260f));
                if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(30f)))
                {
                    metricIndex = metricIndex < 0
                        ? 0
                        : Wrap(metricIndex + 1, metrics.Count);
                    condition.MetricId = metrics[metricIndex].Id;
                }
                if (GUILayout.Button(
                        UnmaRuntime.OperatorText(condition.Comparison),
                        m_buttonStyle,
                        GUILayout.Width(45f)))
                {
                    condition.Comparison = NextEnum(condition.Comparison);
                }
                thresholdText = GUILayout.TextField(
                    thresholdText,
                    24,
                    m_textFieldStyle,
                    GUILayout.Width(90f));
                m_systemThresholdTexts[thresholdKey] = thresholdText;
                if (GUILayout.Button(
                        "ENTFERNEN",
                        m_dangerButtonStyle,
                        GUILayout.Width(95f)))
                {
                    ApplyValidSystemThresholdTexts();
                    stage.Conditions.RemoveAt(index);
                    RebuildSystemThresholdTexts();
                    index--;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button(
                    UnmaText.Get("auto.d6c391b41588"),
                    m_buttonStyle,
                    GUILayout.Width(135f)))
            {
                ApplyValidSystemThresholdTexts();
                stage.Conditions.Add(new SystemConditionDefinition
                {
                    MetricId = metrics[0].Id,
                    Comparison = ComparisonOperator.Less,
                    Threshold = 0d,
                });
                RebuildSystemThresholdTexts();
            }
            GUILayout.EndVertical();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
                UnmaText.Get("auto.2cf14a67c208"),
                m_primaryButtonStyle,
                GUILayout.Width(235f),
                GUILayout.Height(30f)))
        {
            SaveSystemAlarmDraft();
        }
        if (GUILayout.Button(
                "ABBRECHEN",
                m_buttonStyle,
                GUILayout.Width(115f),
                GUILayout.Height(30f)))
        {
            m_systemAlarmDraft = null;
            m_systemThresholdTexts.Clear();
            SetStatus(UnmaText.Get("auto.6b89012b7c85"));
        }
        GUILayout.EndHorizontal();
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
        foreach (var stage in m_systemAlarmDraft.Stages)
        {
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var key = SystemThresholdKey(stage.Id, index);
                if (!m_systemThresholdTexts.TryGetValue(key, out var text) ||
                    !TryParseDouble(text, out var threshold))
                {
                    SetStatus(
                        UnmaText.Get("auto.85b8b6dcd53e") +
                        stage.Message + "'.");
                    return;
                }
                stage.Conditions[index].Threshold = threshold;
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
        m_systemThresholdTexts.Clear();
        SetStatus(UnmaText.Get("auto.a62e7b126c0b"));
    }

    private void RebuildSystemThresholdTexts()
    {
        m_systemThresholdTexts.Clear();
        if (m_systemAlarmDraft == null)
        {
            return;
        }
        foreach (var stage in m_systemAlarmDraft.Stages)
        {
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                m_systemThresholdTexts[SystemThresholdKey(stage.Id, index)] =
                    stage.Conditions[index].Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture);
            }
        }
    }

    private void ApplyValidSystemThresholdTexts()
    {
        if (m_systemAlarmDraft == null)
        {
            return;
        }
        foreach (var stage in m_systemAlarmDraft.Stages)
        {
            for (var index = 0; index < stage.Conditions.Count; index++)
            {
                var key = SystemThresholdKey(stage.Id, index);
                if (m_systemThresholdTexts.TryGetValue(key, out var text) &&
                    TryParseDouble(text, out var threshold))
                {
                    stage.Conditions[index].Threshold = threshold;
                }
            }
        }
    }

    private static string SystemThresholdKey(string stageId, int index)
    {
        return (stageId ?? "") + "|" + index;
    }

    private void DrawSoundOverrides()
    {
        GUILayout.Label(
            UnmaText.Get(
                "sounds.override.title",
                UnmaText.Get("auto.8d7c9716a814")),
            m_sectionStyle);
        GUILayout.Label(
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

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get(
                "sounds.override.filter_label",
                UnmaText.Get("auto.8567d6ad7823")),
            m_labelStyle,
            GUILayout.Width(155f));
        m_soundOverrideFilter = GUILayout.TextField(
            m_soundOverrideFilter,
            100,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

        var sounds = m_audio.GetSoundOptions();
        var candidates = m_runtime.GetSoundOverrideCandidates()
            .Where(MatchesSoundOverrideFilter)
            .ToArray();

        m_soundOverrideScroll = GUILayout.BeginScrollView(
            m_soundOverrideScroll);
        if (candidates.Length == 0)
        {
            GUILayout.Label(
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

            GUILayout.Label(
                candidate.Name + "\n" + candidate.Detail,
                m_labelStyle,
                GUILayout.MinHeight(42f));

            if (isVanilla)
            {
                DrawVanillaBehaviorControls(candidate);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                UnmaText.Get("sounds.override.sound_label", "Ton"),
                m_smallLabelStyle,
                GUILayout.Width(70f));
            if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex - 1, sounds.Count)]);
            }
            GUILayout.Label(
                sounds[soundIndex].Label,
                m_smallLabelStyle,
                GUILayout.MinWidth(90f),
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex + 1, sounds.Count)]);
            }
            GUILayout.EndHorizontal();

            var updatedAutoAcknowledgeOnClear = GUILayout.Toggle(
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
            GUILayout.Space(8f);
        }
        GUILayout.EndScrollView();
        DrawStatusMessage();
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
        else if (candidate.EntityId < 0)
        {
            DrawVanillaBehaviorRow(
                candidate,
                VanillaNotificationScope.NotificationType,
                UnmaText.Get(
                    "sounds.override.scope_notification",
                    "DIESER MELDUNGSTYP"));
        }
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
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            scopeLabel,
            m_smallLabelStyle,
            GUILayout.MinWidth(190f),
            GUILayout.ExpandWidth(true));
        if (GUILayout.Button(
                VanillaBehaviorLabel(behavior),
                behavior == VanillaNotificationBehavior.Hidden ||
                behavior == VanillaNotificationBehavior.Ignored
                    ? m_dangerButtonStyle
                    : behavior == VanillaNotificationBehavior.Silent
                        ? m_buttonStyle
                        : m_primaryButtonStyle,
                GUILayout.Width(245f),
                GUILayout.Height(30f)))
        {
            SaveVanillaNotificationBehavior(
                candidate,
                scope,
                NextVanillaBehavior(behavior));
        }
        GUILayout.EndHorizontal();
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
        GUILayout.Label("ANZEIGE", m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.05e9f359f2e3"),
            m_labelStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "UI-Skalierung",
            m_labelStyle,
            GUILayout.Width(120f));
        var scaleChanged = false;
        if (GUILayout.Button("−", m_buttonStyle, GUILayout.Width(38f)))
        {
            m_runtime.Configuration.UiScalePercent = Math.Max(
                75,
                m_runtime.Configuration.UiScalePercent - 25);
            scaleChanged = true;
        }
        GUILayout.Label(
            m_runtime.Configuration.UiScalePercent + " %",
            m_headerStyle,
            GUILayout.Width(90f));
        if (GUILayout.Button("+", m_buttonStyle, GUILayout.Width(38f)))
        {
            m_runtime.Configuration.UiScalePercent = Math.Min(
                200,
                m_runtime.Configuration.UiScalePercent + 25);
            scaleChanged = true;
        }
        if (GUILayout.Button(
                "100 %",
                m_buttonStyle,
                GUILayout.Width(80f)))
        {
            m_runtime.Configuration.UiScalePercent = 100;
            scaleChanged = true;
        }
        GUILayout.Label(
            UnmaText.Get("auto.df85f85313da"),
            m_smallLabelStyle);
        GUILayout.EndHorizontal();
        if (scaleChanged)
        {
            CancelResizeCapture();
            CancelEditorResizeCapture();
            SaveConfiguration(
                UnmaText.Get("auto.9f37ceb925ab") +
                m_runtime.Configuration.UiScalePercent + " %.");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Warnfarbe", m_labelStyle, GUILayout.Width(95f));
        m_runtime.Configuration.WarningColor = GUILayout.TextField(
            m_runtime.Configuration.WarningColor,
            9,
            m_textFieldStyle,
            GUILayout.Width(100f));
        GUILayout.Label("Kritisch", m_labelStyle, GUILayout.Width(72f));
        m_runtime.Configuration.CriticalColor = GUILayout.TextField(
            m_runtime.Configuration.CriticalColor,
            9,
            m_textFieldStyle,
            GUILayout.Width(100f));
        GUILayout.Label("Notfall", m_labelStyle, GUILayout.Width(68f));
        m_runtime.Configuration.EmergencyColor = GUILayout.TextField(
            m_runtime.Configuration.EmergencyColor,
            9,
            m_textFieldStyle,
            GUILayout.Width(100f));
        if (GUILayout.Button(
                UnmaText.Get("auto.373d6df29cf1"),
                m_primaryButtonStyle,
                GUILayout.Width(175f)))
        {
            SaveConfiguration(UnmaText.Get("auto.f7bb0c5b2c6c"));
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        GUILayout.Label("AUDIO", m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.f98a9c516625"),
            m_labelStyle);
        GUILayout.Label(
            UnmaText.Get("auto.665123745b97") +
            m_audio.SoundsDirectory,
            m_smallLabelStyle);
        GUILayout.Label(
            UnmaText.Get("auto.b4f0fa6a9f20"),
            m_smallLabelStyle);
        if (GUILayout.Button(
                UnmaText.Get("auto.3ac4c11a94ac"),
                m_buttonStyle,
                GUILayout.Width(220f)))
        {
            m_audio.RefreshSoundOptions();
            SetStatus(UnmaText.Get("auto.48d4265633fa"));
        }

        GUILayout.Space(10f);
        GUILayout.Label("SYSTEMALARME", m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.5ca97b0efd51"),
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label(UnmaText.Get("auto.461c23ce7edb"), m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.a183668aa2b3"),
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label("ZUSTANDSMODELL", m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get("auto.fdea5764a7c1"),
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label(
            UnmaText.Get("options.integration.title", "FREMDMOD-API"),
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get(
                "options.integration.description",
                UnmaText.Get("auto.a67711e569a9") +
                UnmaText.Get("auto.ae53894897ea")),
            m_labelStyle);
        var integration = m_runtime.GetExternalIntegrationStatus();
        GUILayout.Label(
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
            m_smallLabelStyle);
        if (GUILayout.Button(
                UnmaText.Get(
                    "options.integration.reload",
                    UnmaText.Get("auto.6a0576853198")),
                m_buttonStyle,
                GUILayout.Width(260f)))
        {
            var clean = m_runtime.ReloadExternalDefinitions();
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
            GUILayout.Label(
                diagnostic.ProviderId + " · " + diagnostic.Code + " · " +
                diagnostic.Message,
                m_smallLabelStyle);
        }
        DrawStatusMessage();
    }

    private void DrawDetachedWindow(DetachedPanel detached)
    {
        var panel = m_runtime.Configuration.Panels.FirstOrDefault(
            item => item.Id == detached.PanelId);
        if (panel == null)
        {
            detached.IsOpen = false;
            return;
        }

        var alarms = GetPanelViews(panel);
        var activeCount = panel.IsDashboard
            ? alarms.Count
            : m_runtime.ActiveCount;
        var unacknowledgedCount = panel.IsDashboard
            ? alarms.Count(alarm => !alarm.IsAcknowledged)
            : m_runtime.UnacknowledgedCount;
        DrawWindowHeader(UnmaText.Get("auto.528ebd6136c2") + panel.Name, detached.Rect.width);
        GUILayout.BeginArea(new Rect(
            10f,
            40f,
            detached.Rect.width - 20f,
            detached.Rect.height - 50f));
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            UnmaText.Get("auto.397544fe1d24") + activeCount +
            UnmaText.Get("auto.ddc0834bf463") + unacknowledgedCount,
            m_smallLabelStyle);
        if (GUILayout.Button(
                UnmaText.Get("auto.77be2ec4ae31"),
                m_dangerButtonStyle,
                GUILayout.Width(130f)))
        {
            m_runtime.AcknowledgeAll();
            m_audio.StopAlarm();
        }
        if (!panel.IsDashboard && GUILayout.Button(
                UnmaText.Get("auto.d5302ca93907"),
                m_primaryButtonStyle,
                GUILayout.Width(120f)))
        {
            OpenNewRuleEditor(panel);
        }
        if (GUILayout.Button("−", m_buttonStyle, GUILayout.Width(30f)))
        {
            detached.PendingSize = new Vector2(
                Mathf.Max(360f, detached.Rect.width - 120f),
                Mathf.Max(300f, detached.Rect.height - 80f));
        }
        if (GUILayout.Button("+", m_buttonStyle, GUILayout.Width(30f)))
        {
            detached.PendingSize = new Vector2(
                Mathf.Min(
                    LogicalScreenWidth - 20f,
                    detached.Rect.width + 120f),
                Mathf.Min(
                    LogicalScreenHeight - 20f,
                    detached.Rect.height + 80f));
        }
        if (GUILayout.Button("X", m_dangerButtonStyle, GUILayout.Width(30f)))
        {
            detached.IsOpen = false;
        }
        GUILayout.EndHorizontal();

        detached.Scroll = GUILayout.BeginScrollView(detached.Scroll);
        DrawAlarmGrid(
            alarms,
            Math.Max(1, Math.Min(panel.Columns, 5)),
            detached.Rect.width - 38f,
            detached.Scroll.y,
            Math.Max(180f, detached.Rect.height - 100f),
            null,
            panel,
            false,
            panel.IsDashboard
                ? UnmaText.Get("auto.f895fe84e658")
                : UnmaText.Get("auto.e8bad0a4452b"),
            !panel.IsDashboard);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.DragWindow(new Rect(0f, 0f, detached.Rect.width - 38f, 36f));
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
            GUILayout.Space(20f);
            GUILayout.Label(
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
            GUILayout.Space(firstVisibleRow * rowHeight);
        }

        for (var row = firstVisibleRow; row < lastVisibleRow; row++)
        {
            var rowStart = row * columns;
            GUILayout.BeginHorizontal();
            var columnsInRow = drawEmptyCells
                ? columns
                : Math.Min(columns, itemCount - rowStart);
            for (var column = 0; column < columnsInRow; column++)
            {
                var index = rowStart + column;
                var rect = GUILayoutUtility.GetRect(
                    tileWidth,
                    TileHeight,
                    GUILayout.Width(tileWidth),
                    GUILayout.Height(TileHeight));
                if (index < alarms.Count)
                {
                    DrawAlarmTile(rect, alarms[index], displayPanel);
                    if (assignmentPending && interactionPanel != null)
                    {
                        DrawExistingAssignmentTarget(
                            rect,
                            interactionPanel,
                            alarms[index]);
                    }
                    else if (!m_entityAssignmentPending)
                    {
                        var hasEntityVanillaControls =
                            IsEntityVanillaTile(
                                displayPanel,
                                alarms[index]);
                        var tileClickRect =
                            TryGetNavigationEntityId(
                                alarms[index],
                                out _)
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
                        if (GUI.Button(
                                tileClickRect,
                                GUIContent.none,
                                GUIStyle.none))
                        {
                            HandleAlarmTileClick(alarms[index]);
                        }
                        if (!hasEntityVanillaControls)
                        {
                            DrawAlarmNavigationButton(rect, alarms[index]);
                        }
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
                    GUILayout.Space(6f);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }
        if (lastVisibleRow < rowCount)
        {
            GUILayout.Space((rowCount - lastVisibleRow) * rowHeight);
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
                SetStatus(
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
            var panelIndex = GlobalPanels.FindIndex(panel => string.Equals(
                panel.Id,
                rule.PanelId,
                StringComparison.Ordinal));
            if (panelIndex >= 0)
            {
                m_currentPanelIndex = panelIndex;
            }
        }
        m_isOpen = true;
        m_tab = TabBoard;
        OpenRuleEditorWindow();

        var firstCondition = rule.Conditions.FirstOrDefault();
        if (alreadyEditing)
        {
            SetStatus(UnmaText.Get("auto.7f308a23243a"));
        }
        else if (firstCondition != null)
        {
            RequestEntityInspection(firstCondition.EntityId, true);
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
        PanelDefinition displayPanel)
    {
        var background = new Color(0.83f, 0.84f, 0.82f, 1f);
        if (alarm.IsActive || alarm.IsGoneUnacknowledged)
        {
            var active = ParseColor(alarm.ActiveColor, Color.yellow);
            var blinkOn = !alarm.RequiresAcknowledgement ||
                          Mathf.FloorToInt(Time.realtimeSinceStartup * 2.2f) %
                          2 == 0;
            background = blinkOn
                ? active
                : new Color(0.20f, 0.20f, 0.19f, 1f);
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

        var badge = alarm.IsGoneUnacknowledged
            ? UnmaText.Get("auto.3f6e1a7c5590")
            : alarm.IsActive
                ? alarm.IsAcknowledged ? "STEHT" : "KOMMT"
                : alarm.IsMissingSource ? UnmaText.Get("auto.6a49896902cb") : "NORMAL";
        if ((alarm.IsActive || alarm.IsGoneUnacknowledged) &&
            alarm.IsMissingSource)
        {
            badge += UnmaText.Get("auto.70ab47b6f195");
        }
        GUI.Label(
            new Rect(inner.x + 7f, inner.y + 5f, inner.width - 14f, 18f),
            badge + " · " + SeverityLabel(alarm.Severity),
            m_tileDetailStyle);
        GUI.Label(
            new Rect(inner.x + 7f, inner.y + 24f, inner.width - 14f, 48f),
            (alarm.Name ?? "MELDUNG").ToUpperInvariant(),
            m_tileTitleStyle);
        GUI.Label(
            new Rect(
                inner.x + 7f,
                inner.y + 72f,
                inner.width - 14f,
                IsEntityVanillaTile(displayPanel, alarm) ? 13f : 25f),
            alarm.Detail ?? "",
            m_tileDetailStyle);
        DrawEntityVanillaBehaviorButtons(inner, alarm, displayPanel);
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
        if (GUI.Button(
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
            GUI.Button(
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

    private void DrawAlarmNavigationButton(Rect tileRect, AlarmView alarm)
    {
        if (!TryGetNavigationEntityId(alarm, out var entityId))
        {
            return;
        }

        var buttonRect = new Rect(
            tileRect.xMax - 31f,
            tileRect.yMax - 31f,
            27f,
            27f);
        if (GUI.Button(
                buttonRect,
                "↗",
                m_primaryButtonStyle))
        {
            NavigateToEntity(entityId);
        }
    }

    private bool TryGetNavigationEntityId(
        AlarmView alarm,
        out int entityId)
    {
        entityId = -1;
        if (alarm == null)
        {
            return false;
        }

        if (PanelSlotProjection.TryGetCustomRuleId(alarm, out var ruleId))
        {
            var rule = m_runtime.Configuration.Rules.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
            if (rule != null)
            {
                var ownerPanel = m_runtime.Configuration.Panels
                    .FirstOrDefault(panel => string.Equals(
                        panel.Id,
                        rule.PanelId,
                        StringComparison.Ordinal));
                if (PanelTopologyPolicy.IsEntityPanel(ownerPanel))
                {
                    entityId = ownerPanel.OwnerEntityId;
                    return entityId > 0;
                }

                entityId = rule.Conditions.FirstOrDefault()?.EntityId ?? -1;
                return entityId > 0;
            }
        }

        var slotId = alarm.SlotId ?? "";
        var marker = slotId.LastIndexOf(
            ":entity:",
            StringComparison.Ordinal);
        return marker >= 0 &&
               int.TryParse(
                   slotId.Substring(marker + 8),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out entityId) &&
               entityId > 0;
    }

    private void NavigateToEntity(int entityId)
    {
        if (!m_runtime.TryGetLiveEntity(entityId, out var entity))
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
            new Color(0.83f, 0.84f, 0.82f, 1f));
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
                ? new Color(0.08f, 0.39f, 0.41f, 1f)
                : new Color(0.28f, 0.29f, 0.28f, 1f));
        GUI.Label(
            actionRect,
            canLink
                ? UnmaText.Get("auto.fe5cfa5cedb5")
                : UnmaText.Get("auto.dcc40b537b28"),
            m_assignmentActionStyle);
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
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
        DrawPanelRect(inner, new Color(0.78f, 0.86f, 0.84f, 1f));
        GUI.Label(
            new Rect(inner.x + 7f, inner.y + 17f, inner.width - 14f, 52f),
            UnmaText.Get("auto.1cc8d34d4b3e"),
            m_tileTitleStyle);
        GUI.Label(
            new Rect(inner.x + 7f, inner.y + 73f, inner.width - 14f, 25f),
            m_assignmentEntity == null
                ? UnmaText.Get("auto.7c06a5edce22")
                : UnmaText.Get("auto.36a818f7f3f3") +
                  m_assignmentEntity.Title.ToUpperInvariant(),
            m_tileDetailStyle);
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
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
                SetStatus(
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
            SetStatus(
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
               m_draftLogic != AlarmLogic.All ||
               !string.Equals(
                   m_draftColor?.Trim(),
                   "#F0C541",
                   StringComparison.OrdinalIgnoreCase) ||
               m_draftSoundIndex != 0 ||
               m_draftSoundChanged ||
               m_draftAutoAcknowledgeOnClear ||
               m_draftValueMode != ConditionValueMode.Absolute ||
               m_draftComparison != ComparisonOperator.Less ||
               !string.Equals(
                   m_draftThreshold?.Trim(),
                   "0",
                   StringComparison.Ordinal);
    }

    private void DrawPanelRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
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
        m_tab = TabBoard;
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

        RequestEntityInspection(entity.Id.Value, openEntityAlarmWindow);
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
            m_tab = TabBoard;
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
        m_draftConditionThresholdTexts.Add(
            threshold.ToString("0.###", CultureInfo.CurrentCulture));
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        SetStatus(UnmaText.Get("auto.af3edd1b9f09"));
    }

    private void SaveDraftRule(IReadOnlyList<SoundOption> sounds)
    {
        var panel = GetDraftTargetPanel();
        if (panel == null || m_draftConditions.Count == 0)
        {
            return;
        }

        for (var index = 0; index < m_draftConditions.Count; index++)
        {
            if (index >= m_draftConditionThresholdTexts.Count ||
                !TryParseDouble(
                    m_draftConditionThresholdTexts[index],
                    out var threshold))
            {
                SetStatus(
                    UnmaText.Get("auto.cb85d7309ac1") + (index + 1) +
                    UnmaText.Get("auto.ddb8c3cdbc29"));
                return;
            }
            m_draftConditions[index].Threshold = threshold;
            if (m_draftConditions[index].ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.IsNullOrWhiteSpace(
                    m_draftConditions[index].ReferenceMetricPath))
            {
                SetStatus(
                    UnmaText.Get("auto.21ca7079c12b") + (index + 1) +
                    UnmaText.Get("auto.115b04808134"));
                return;
            }
            if (m_draftConditions[index].ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.Equals(
                    m_draftConditions[index].MetricPath,
                    m_draftConditions[index].ReferenceMetricPath,
                    StringComparison.Ordinal))
            {
                SetStatus(
                    UnmaText.Get("auto.53c26ac33af4") + (index + 1) +
                    UnmaText.Get("auto.be2c20cf5599"));
                return;
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
            return;
        }
        var soundId = isEditing && !m_draftSoundChanged
            ? m_originalDraftSoundId
            : selectedSoundId;
        var rule = new AlarmRuleDefinition
        {
            Id = existingRule?.Id ?? Guid.NewGuid().ToString("N"),
            PanelId = panel.Id,
            Name = string.IsNullOrWhiteSpace(m_draftRuleName)
                ? "MELDUNG"
                : m_draftRuleName.Trim(),
            Severity = m_draftSeverity,
            Logic = m_draftLogic,
            ActiveColor = NormalizeColor(m_draftColor),
            SoundId = soundId,
            Enabled = existingRule?.Enabled ?? true,
            AutoAcknowledgeOnClear = m_draftAutoAcknowledgeOnClear,
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
                m_runtime.LastPersistenceError);
            return;
        }
        var wasEditing = existingRule != null;
        var savedPanelId = panel.Id;
        ResetDraftRule();
        m_draftTargetPanelId = savedPanelId;
        SetStatus(
            wasEditing
                ? UnmaText.Get("auto.961c0245ef89")
                : UnmaText.Get("auto.fb19aab1dadd"));
    }

    private void BeginEditingRule(
        AlarmRuleDefinition rule,
        IReadOnlyList<SoundOption> sounds)
    {
        m_draftPreferredSlotIndex = -1;
        m_editingRuleId = rule.Id;
        m_draftTargetPanelId = rule.PanelId;
        m_draftRuleName = rule.Name;
        m_draftSeverity = rule.Severity;
        m_draftLogic = rule.Logic;
        m_draftColor = rule.ActiveColor;
        m_draftSoundIndex = FindSoundIndex(sounds, rule.SoundId);
        m_originalDraftSoundId = rule.SoundId;
        m_draftSoundChanged = false;
        m_draftAutoAcknowledgeOnClear = rule.AutoAcknowledgeOnClear;
        m_draftLinkedPanelIds.Clear();
        foreach (var panelId in rule.LinkedPanelIds ?? new List<string>())
        {
            m_draftLinkedPanelIds.Add(panelId);
        }
        m_draftConditions.Clear();
        m_draftConditionThresholdTexts.Clear();
        m_draftConditions.AddRange(
            rule.Conditions.Select(CloneCondition));
        m_draftConditionThresholdTexts.AddRange(
            rule.Conditions.Select(condition =>
                condition.Threshold.ToString(
                    "0.###",
                    CultureInfo.CurrentCulture)));
        m_editorScroll = Vector2.zero;
        SetStatus(UnmaText.Get("auto.bc7894226481"));
    }

    private void ResetDraftRule()
    {
        m_draftPreferredSlotIndex = -1;
        m_editingRuleId = "";
        m_draftConditions.Clear();
        m_draftConditionThresholdTexts.Clear();
        m_draftRuleName = UnmaText.Get("auto.fe04a9d0e58c");
        m_draftSeverity = AlarmSeverity.Warning;
        m_draftLogic = AlarmLogic.All;
        m_draftColor = "#F0C541";
        m_draftSoundIndex = 0;
        m_originalDraftSoundId = "auto";
        m_draftSoundChanged = false;
        m_draftAutoAcknowledgeOnClear = false;
        m_draftLinkedPanelIds.Clear();
        m_draftValueMode = ConditionValueMode.Absolute;
        m_draftComparison = ComparisonOperator.Less;
        m_draftThreshold = "0";
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        m_conditionReferencePickerIndex = -1;
        var targetPanel = CurrentPanel != null && !CurrentPanel.IsDashboard
            ? CurrentPanel
            : m_runtime.Configuration.Panels.FirstOrDefault(panel =>
                !panel.IsDashboard &&
                !PanelTopologyPolicy.IsEntityPanel(panel));
        m_draftTargetPanelId = targetPanel?.Id ?? "";
    }

    private void OpenPanelCreationEditor()
    {
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetStatus(
                UnmaText.Get("auto.48d5f7bcd7c1"));
            return;
        }
        m_editorWindowMode = EditorWindowMode.PanelCreation;
        m_entityAlarmWindowOpen = true;
        m_entityAlarmScroll = Vector2.zero;
        m_newPanelName = UnmaText.Get("auto.3f5c86818d70");
    }

    private void OpenPanelSettingsEditor(PanelDefinition panel)
    {
        if (panel == null || PanelTopologyPolicy.IsEntityPanel(panel))
        {
            return;
        }
        if (HasDraftRuleWork())
        {
            OpenRuleEditorWindow();
            SetStatus(
                UnmaText.Get("auto.48d5f7bcd7c1"));
            return;
        }
        m_panelSettingsPanelId = panel.Id;
        m_panelSettingsName = panel.Name ?? "";
        m_panelSettingsColumns = panel.Columns;
        m_panelSettingsIncludeVanilla = panel.IncludeVanilla;
        m_panelSettingsIncludeSystem = panel.IncludeSystem;
        m_panelSettingsFilter = panel.NotificationFilter ?? "";
        m_editorWindowMode = EditorWindowMode.PanelSettings;
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
        m_editorWindowMode = EditorWindowMode.Rule;
        m_entityAlarmWindowOpen = true;
        m_openEntityAlarmAfterInspection = false;
        m_entityAlarmScroll = Vector2.zero;
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
        m_detachedPanels.RemoveAll(item => item.PanelId == panelId);
        SetStatus(UnmaText.Get("auto.d57565ce0bc8"));
    }

    private void DetachPanel(string panelId)
    {
        var offset = m_detachedPanels.Count * 28f;
        m_detachedPanels.Add(new DetachedPanel
        {
            WindowId = m_nextDetachedWindowId++,
            PanelId = panelId,
            Rect = new Rect(
                40f + offset,
                60f + offset,
                620f,
                460f),
        });
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
            SetStatus(successMessage);
        }
        else
        {
            SetStatus(
                UnmaText.Get("auto.5df942eb6687") +
                m_runtime.LastPersistenceError);
        }
    }

    private void DrawTabButton(int tab, string label)
    {
        var width = Mathf.Clamp(
            (m_windowRect.width - 105f) / 5f,
            88f,
            190f);
        if (GUILayout.Button(
                label,
                m_tab == tab ? m_primaryButtonStyle : m_buttonStyle,
                GUILayout.Width(width),
                GUILayout.Height(30f)))
        {
            m_tab = tab;
            GUI.FocusControl(null);
        }
    }

    private void DrawWindowHeader(string title, float windowWidth)
    {
        GUI.Label(
            new Rect(12f, 8f, Math.Max(120f, windowWidth - 76f), 28f),
            title,
            m_headerStyle);
    }

    private void DrawStatusMessage()
    {
        if (!string.IsNullOrWhiteSpace(m_statusMessage) &&
            Time.realtimeSinceStartup < m_statusMessageUntil)
        {
            GUILayout.Label(m_statusMessage, m_smallLabelStyle);
        }
    }

    private void SetStatus(string message)
    {
        m_statusMessage = message;
        m_statusMessageUntil = Time.realtimeSinceStartup + 6f;
    }

    private void CycleMetric(int direction)
    {
        m_selectedMetricIndex = Wrap(
            m_selectedMetricIndex + direction,
            m_selectedMetrics.Count);
    }

    private Rect GetResizeHandleRect()
    {
        return new Rect(
            WindowResizeMath.GetHandleOrigin(
                m_windowRect.width,
                MainResizeHandleSize,
                MainResizeHandleInset),
            WindowResizeMath.GetHandleOrigin(
                m_windowRect.height,
                MainResizeHandleSize,
                MainResizeHandleInset),
            MainResizeHandleSize,
            MainResizeHandleSize);
    }

    private void HandleResizeInput()
    {
        var currentEvent = UnityEngine.Event.current;
        var controlId = GUIUtility.GetControlID(
            MainResizeControlHint,
            FocusType.Passive);
        m_resizeControlId = controlId;
        var eventType = currentEvent.GetTypeForControl(controlId);

        if (m_isResizing && GUIUtility.hotControl != controlId)
        {
            // Never leave resizing armed after Unity has released or replaced
            // the captured pointer. Otherwise a later drag in the window body
            // can accidentally continue the old resize operation.
            m_isResizing = false;
        }

        if (m_isResizing &&
            GUIUtility.hotControl == controlId &&
            eventType == EventType.Repaint &&
            !Input.GetMouseButton(0))
        {
            // A mouse-up outside the game window is not guaranteed to arrive
            // as an IMGUI event. Recover the capture as soon as Unity reports
            // that the physical button is no longer held.
            GUIUtility.hotControl = 0;
            m_resizeControlId = 0;
            m_isResizing = false;
            PersistWindowRect();
        }

        if (eventType == EventType.MouseDown &&
            currentEvent.button == 0 &&
            WindowResizeMath.IsInsideHandle(
                m_windowRect.width,
                m_windowRect.height,
                currentEvent.mousePosition.x,
                currentEvent.mousePosition.y,
                MainResizeHandleSize,
                MainResizeHandleInset))
        {
            GUIUtility.hotControl = controlId;
            m_isResizing = true;
            m_resizeStartMouse = GUIUtility.GUIToScreenPoint(
                currentEvent.mousePosition);
            m_resizeStartSize = new Vector2(
                m_windowRect.width,
                m_windowRect.height);
            currentEvent.Use();
        }
        else if (eventType == EventType.MouseDrag &&
                 m_isResizing &&
                 GUIUtility.hotControl == controlId)
        {
            var current = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            var delta = (current - m_resizeStartMouse) / UiScale;
            m_pendingMainWindowSize = new Vector2(
                WindowResizeMath.ResizeExtent(
                    m_resizeStartSize.x,
                    delta.x,
                    700f,
                    Math.Max(700f, LogicalScreenWidth - 12f)),
                WindowResizeMath.ResizeExtent(
                    m_resizeStartSize.y,
                    delta.y,
                    520f,
                    Math.Max(520f, LogicalScreenHeight - 12f)));
            currentEvent.Use();
        }
        else if (eventType == EventType.MouseUp &&
                 GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            m_resizeControlId = 0;
            m_isResizing = false;
            PersistWindowRect();
            currentEvent.Use();
        }
    }

    private void DrawResizeHandle()
    {
        GUI.Label(GetResizeHandleRect(), "◢", m_resizeHandleStyle);
    }

    private void CancelResizeCapture()
    {
        if (!m_isResizing)
        {
            return;
        }
        m_isResizing = false;
        m_pendingMainWindowSize = null;
        if (GUIUtility.hotControl == m_resizeControlId)
        {
            GUIUtility.hotControl = 0;
        }
        m_resizeControlId = 0;
    }

    private void PersistWindowRect()
    {
        if (RectsApproximatelyEqual(
                m_windowRect,
                m_lastPersistedWindowRect))
        {
            return;
        }

        var config = m_runtime.Configuration;
        var previousX = config.WindowX;
        var previousY = config.WindowY;
        var previousWidth = config.WindowWidth;
        var previousHeight = config.WindowHeight;
        config.WindowX = m_windowRect.x;
        config.WindowY = m_windowRect.y;
        config.WindowWidth = m_windowRect.width;
        config.WindowHeight = m_windowRect.height;
        if (m_runtime.SaveConfiguration())
        {
            m_lastPersistedWindowRect = m_windowRect;
            return;
        }

        config.WindowX = previousX;
        config.WindowY = previousY;
        config.WindowWidth = previousWidth;
        config.WindowHeight = previousHeight;
    }

    private Rect GetEditorResizeHandleRect()
    {
        return new Rect(
            WindowResizeMath.GetHandleOrigin(
                m_entityAlarmWindowRect.width,
                MainResizeHandleSize,
                MainResizeHandleInset),
            WindowResizeMath.GetHandleOrigin(
                m_entityAlarmWindowRect.height,
                MainResizeHandleSize,
                MainResizeHandleInset),
            MainResizeHandleSize,
            MainResizeHandleSize);
    }

    private void HandleEditorResizeInput()
    {
        var currentEvent = UnityEngine.Event.current;
        var controlId = GUIUtility.GetControlID(
            EditorResizeControlHint,
            FocusType.Passive);
        m_editorResizeControlId = controlId;
        var eventType = currentEvent.GetTypeForControl(controlId);

        if (m_isEditorResizing && GUIUtility.hotControl != controlId)
        {
            m_isEditorResizing = false;
        }
        if (m_isEditorResizing &&
            GUIUtility.hotControl == controlId &&
            eventType == EventType.Repaint &&
            !Input.GetMouseButton(0))
        {
            GUIUtility.hotControl = 0;
            m_editorResizeControlId = 0;
            m_isEditorResizing = false;
            PersistEditorWindowRect();
        }

        if (eventType == EventType.MouseDown &&
            currentEvent.button == 0 &&
            WindowResizeMath.IsInsideHandle(
                m_entityAlarmWindowRect.width,
                m_entityAlarmWindowRect.height,
                currentEvent.mousePosition.x,
                currentEvent.mousePosition.y,
                MainResizeHandleSize,
                MainResizeHandleInset))
        {
            GUIUtility.hotControl = controlId;
            m_isEditorResizing = true;
            m_editorResizeStartMouse = GUIUtility.GUIToScreenPoint(
                currentEvent.mousePosition);
            m_editorResizeStartSize = new Vector2(
                m_entityAlarmWindowRect.width,
                m_entityAlarmWindowRect.height);
            currentEvent.Use();
        }
        else if (eventType == EventType.MouseDrag &&
                 m_isEditorResizing &&
                 GUIUtility.hotControl == controlId)
        {
            var current = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            var delta = (current - m_editorResizeStartMouse) / UiScale;
            m_pendingEditorWindowSize = new Vector2(
                WindowResizeMath.ResizeExtent(
                    m_editorResizeStartSize.x,
                    delta.x,
                    700f,
                    Math.Max(700f, LogicalScreenWidth - 12f)),
                WindowResizeMath.ResizeExtent(
                    m_editorResizeStartSize.y,
                    delta.y,
                    520f,
                    Math.Max(520f, LogicalScreenHeight - 12f)));
            currentEvent.Use();
        }
        else if (eventType == EventType.MouseUp &&
                 GUIUtility.hotControl == controlId)
        {
            GUIUtility.hotControl = 0;
            m_editorResizeControlId = 0;
            m_isEditorResizing = false;
            PersistEditorWindowRect();
            currentEvent.Use();
        }
    }

    private void DrawEditorResizeHandle()
    {
        GUI.Label(GetEditorResizeHandleRect(), "◢", m_resizeHandleStyle);
    }

    private void CancelEditorResizeCapture()
    {
        if (!m_isEditorResizing)
        {
            return;
        }
        m_isEditorResizing = false;
        m_pendingEditorWindowSize = null;
        if (GUIUtility.hotControl == m_editorResizeControlId)
        {
            GUIUtility.hotControl = 0;
        }
        m_editorResizeControlId = 0;
    }

    private void PersistEditorWindowRect()
    {
        if (RectsApproximatelyEqual(
                m_entityAlarmWindowRect,
                m_lastPersistedEditorWindowRect))
        {
            return;
        }

        var config = m_runtime.Configuration;
        var previousX = config.EditorWindowX;
        var previousY = config.EditorWindowY;
        var previousWidth = config.EditorWindowWidth;
        var previousHeight = config.EditorWindowHeight;
        config.EditorWindowX = m_entityAlarmWindowRect.x;
        config.EditorWindowY = m_entityAlarmWindowRect.y;
        config.EditorWindowWidth = m_entityAlarmWindowRect.width;
        config.EditorWindowHeight = m_entityAlarmWindowRect.height;
        if (m_runtime.SaveConfiguration())
        {
            m_lastPersistedEditorWindowRect = m_entityAlarmWindowRect;
            return;
        }

        config.EditorWindowX = previousX;
        config.EditorWindowY = previousY;
        config.EditorWindowWidth = previousWidth;
        config.EditorWindowHeight = previousHeight;
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

        var windowBackground = SolidTexture(
            "window",
            new Color(0.075f, 0.085f, 0.085f, 0.98f));
        m_windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(8, 8, 8, 8),
        };
        SetBackgroundForAllStates(m_windowStyle, windowBackground);
        m_headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.90f, 0.91f, 0.88f) },
        };
        m_sectionStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 10, 5, 5),
            normal =
            {
                textColor = Color.white,
                background = SolidTexture(
                    "section",
                    new Color(0.16f, 0.18f, 0.18f, 1f)),
            },
        };
        m_labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.91f, 0.92f, 0.89f) },
        };
        m_smallLabelStyle = new GUIStyle(m_labelStyle)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.70f, 0.73f, 0.70f) },
        };
        m_tileTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.black },
        };
        m_tileDetailStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 9,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.black },
        };
        m_assignmentActionStyle = new GUIStyle(m_tileDetailStyle)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        m_buttonStyle = MakeButtonStyle(
            "button",
            new Color(0.21f, 0.23f, 0.23f),
            new Color(0.30f, 0.33f, 0.32f));
        m_primaryButtonStyle = MakeButtonStyle(
            "primary",
            new Color(0.10f, 0.35f, 0.36f),
            new Color(0.13f, 0.48f, 0.48f));
        m_dangerButtonStyle = MakeButtonStyle(
            "danger",
            new Color(0.55f, 0.09f, 0.08f),
            new Color(0.75f, 0.13f, 0.10f));
        m_resizeHandleStyle = new GUIStyle(m_buttonStyle)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 0),
        };
        m_textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 13,
            normal =
            {
                textColor = Color.black,
                background = SolidTexture(
                    "field",
                    new Color(0.84f, 0.85f, 0.82f)),
            },
            focused =
            {
                textColor = Color.black,
                background = SolidTexture(
                    "field-focus",
                    new Color(0.96f, 0.88f, 0.55f)),
            },
        };
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
            normal = { textColor = Color.black },
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

    private GUIStyle MakeButtonStyle(
        string key,
        Color normal,
        Color hover)
    {
        return new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 4, 4),
            normal =
            {
                textColor = Color.white,
                background = SolidTexture(key, normal),
            },
            hover =
            {
                textColor = Color.white,
                background = SolidTexture(key + "-hover", hover),
            },
            active =
            {
                textColor = Color.white,
                background = SolidTexture(key + "-active", hover * 0.85f),
            },
        };
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
            ExpectedProductId = source.ExpectedProductId,
            EntityPrototypeId = source.EntityPrototypeId,
            ValueMode = source.ValueMode,
            ReferenceMetricPath = source.ReferenceMetricPath,
            ReferenceMetricLabel = source.ReferenceMetricLabel,
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
            AlarmSeverity.Emergency => "NOTFALL",
            AlarmSeverity.Critical => "KRITISCH",
            AlarmSeverity.Warning => "WARNUNG",
            _ => "HINWEIS",
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

    private float LogicalScreenWidth => Screen.width / UiScale;

    private float LogicalScreenHeight => Screen.height / UiScale;

    private bool IsPointerOverAnyUnmaSurface()
    {
        if (!m_gameplayWasActive || m_isUiSuppressedByMenu)
        {
            return false;
        }

        var physicalMouse = Input.mousePosition;
        var logicalMouse = new Vector2(
            physicalMouse.x / UiScale,
            (Screen.height - physicalMouse.y) / UiScale);
        if (m_isOpen && m_windowRect.Contains(logicalMouse) ||
            m_entityAlarmWindowOpen &&
            m_entityAlarmWindowRect.Contains(logicalMouse) ||
            !m_isOpen && m_launcherRect.Contains(logicalMouse))
        {
            return true;
        }
        return m_detachedPanels.Any(panel =>
            panel.IsOpen && panel.Rect.Contains(logicalMouse));
    }

    private void UpdatePointerRaycastShield(bool enabled)
    {
        if (m_pointerRaycastShield == null)
        {
            return;
        }

        m_inputShieldRects.Clear();
        if (enabled)
        {
            if (m_isOpen)
            {
                m_inputShieldRects.Add(m_windowRect);
            }
            else
            {
                m_inputShieldRects.Add(m_launcherRect);
            }

            if (m_entityAlarmWindowOpen)
            {
                m_inputShieldRects.Add(m_entityAlarmWindowRect);
            }

            foreach (var detached in m_detachedPanels)
            {
                if (detached.IsOpen)
                {
                    m_inputShieldRects.Add(detached.Rect);
                }
            }
        }

        m_pointerRaycastShield.UpdateSurfaces(
            m_inputShieldRects,
            UiScale,
            enabled);
    }

    private void UpdateKeyboardInputCapture()
    {
        var currentEvent = UnityEngine.Event.current;
        if (currentEvent != null &&
            currentEvent.rawType == EventType.MouseDown &&
            !IsPointerOverAnyUnmaSurface())
        {
            GUI.FocusControl(null);
        }

        // Only full UNMA windows contain text fields. A non-zero IMGUI
        // keyboard control while one of them is visible therefore represents
        // an active text edit; buttons and resize handles use hotControl.
        var textInputFocused =
            (m_isOpen || m_entityAlarmWindowOpen) &&
            GUIUtility.keyboardControl != 0;
        m_inputBlocker?.SetKeyboardCaptured(textInputFocused);
    }

    private void ConsumePointerEventOverUi()
    {
        var currentEvent = UnityEngine.Event.current;
        if (!IsPointerOverAnyUnmaSurface())
        {
            return;
        }
        switch (currentEvent.type)
        {
            case EventType.MouseDown:
            case EventType.MouseUp:
            case EventType.MouseDrag:
            case EventType.MouseMove:
            case EventType.ScrollWheel:
            case EventType.ContextClick:
            case EventType.DragUpdated:
            case EventType.DragPerform:
                currentEvent.Use();
                break;
        }
    }

    private Rect ClampToScreen(Rect rect)
    {
        rect.width = Mathf.Min(
            rect.width,
            Math.Max(320f, LogicalScreenWidth - 8f));
        rect.height = Mathf.Min(
            rect.height,
            Math.Max(260f, LogicalScreenHeight - 8f));
        rect.x = Mathf.Clamp(
            rect.x,
            0f,
            Math.Max(0f, LogicalScreenWidth - rect.width));
        rect.y = Mathf.Clamp(
            rect.y,
            0f,
            Math.Max(0f, LogicalScreenHeight - rect.height));
        return rect;
    }

    private void OnDestroy()
    {
        m_inspectorAlarmButtons?.Dispose();
        m_inputBlocker?.Dispose();
        m_pointerRaycastShield?.Dispose();
        m_runtime?.SetGameplayActive(false);
        if (m_audio != null)
        {
            m_audio.StopAlarm();
        }
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
