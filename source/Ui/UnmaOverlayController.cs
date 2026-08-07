using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Unity;
using Mafi.Unity.Audio;
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
    private const float MainResizeHandleSize = 30f;
    private const float MainResizeHandleInset = 4f;
    private const float MainWindowContentBottomInset =
        MainResizeHandleSize + MainResizeHandleInset + 4f;
    private const float TileHeight = 112f;
    private const float HistoryRowHeight = 40f;

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

    private UnmaRuntime m_runtime;
    private InspectorsManager m_inspectorsManager;
    private UnmaAudioController m_audio;
    private InspectorAlarmButtonBridge m_inspectorAlarmButtons;
    private Rect m_windowRect;
    private Rect m_launcherRect;
    private Rect m_entityAlarmWindowRect = new(180f, 110f, 1080f, 720f);
    private Vector2 m_boardScroll;
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
    private string m_draftRuleName = "NEUE MELDUNG";
    private string m_draftColor = "#F0C541";
    private int m_draftSoundIndex;
    private string m_originalDraftSoundId = "auto";
    private bool m_draftSoundChanged;
    private bool m_draftAutoAcknowledgeOnClear;
    private string m_editingRuleId = "";
    private string m_draftTargetPanelId = "";
    private string m_lastAlarmTileClickId = "";
    private float m_lastAlarmTileClickAt;
    private string m_newPanelName = "NEUES PANEL";
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
        AudioDb audioDb,
        string modRoot)
    {
        var gameObject = new GameObject("UNMA Overlay");
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
        var overlay = gameObject.AddComponent<UnmaOverlayController>();
        var audio = gameObject.AddComponent<UnmaAudioController>();
        audio.Configure(modRoot, audioDb);
        overlay.Configure(runtime, inspectorsManager, audio);
        return overlay;
    }

    public void Configure(
        UnmaRuntime runtime,
        InspectorsManager inspectorsManager,
        UnmaAudioController audio)
    {
        m_runtime = runtime;
        m_inspectorsManager = inspectorsManager;
        m_audio = audio;
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
                return;
            }
            m_gameplayWasActive = true;
            m_runtime.SetGameplayActive(true);
            m_audio.StopAlarm();
        }

        m_isUiSuppressedByMenu = !IsGameplayActive();

        if (!m_isUiSuppressedByMenu)
        {
            m_inspectorAlarmButtons?.Update();
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
                "Die bearbeitete Meldung wurde entfernt, weil ihre " +
                "überwachte Entity nicht mehr existiert.");
        }

        var alarmEditorVisible = m_entityAlarmWindowOpen ||
                                 m_isOpen && m_tab == 2;
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
            return;
        }

        EnsureStyles();
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
        }
        else
        {
            CancelResizeCapture();
        }

        if (m_entityAlarmWindowOpen)
        {
            m_entityAlarmWindowRect = ClampToScreen(GUI.Window(
                EntityAlarmWindowId,
                ClampToScreen(m_entityAlarmWindowRect),
                DrawEntityAlarmWindow,
                GUIContent.none,
                m_windowStyle));
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
            Math.Max(4f, Screen.width - m_launcherRect.width - 4f));
        m_launcherRect.y = Mathf.Clamp(
            m_launcherRect.y,
            72f,
            Math.Max(72f, Screen.height - m_launcherRect.height - 4f));
        DrawPanelRect(m_launcherRect, new Color(0.08f, 0.09f, 0.09f, 0.96f));

        var buttonRect = new Rect(
            m_launcherRect.x + 3f,
            m_launcherRect.y + 3f,
            88f,
            28f);
        if (GUI.Button(
                buttonRect,
                m_runtime.UnacknowledgedCount > 0
                    ? "UNMA  !" + m_runtime.UnacknowledgedCount
                    : "UNMA  F8",
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
            "UNMA · UNIVERSELLE NACHRICHTEN-MELDEANLAGE"));

        GUILayout.BeginArea(new Rect(
            12f,
            42f,
            m_windowRect.width - 24f,
            m_windowRect.height - 42f - MainWindowContentBottomInset));

        GUILayout.BeginHorizontal();
        DrawTabButton(0, UnmaText.Get("tab.board", "MELDETAFEL"));
        DrawTabButton(1, UnmaText.Get("tab.history", "VERLAUF"));
        DrawTabButton(2, UnmaText.Get("tab.editor", "EDITOR"));
        DrawTabButton(3, UnmaText.Get("tab.system", "SYSTEM"));
        DrawTabButton(4, UnmaText.Get("tab.sounds", "TÖNE"));
        DrawTabButton(5, UnmaText.Get("tab.options", "OPTIONEN"));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("—", m_buttonStyle, GUILayout.Width(36f)))
        {
            m_isOpen = false;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        switch (m_tab)
        {
            case 1:
                DrawHistory();
                break;
            case 2:
                DrawEditor();
                break;
            case 3:
                DrawSystemAlarms();
                break;
            case 4:
                DrawSoundOverrides();
                break;
            case 5:
                DrawOptions();
                break;
            default:
                DrawBoard();
                break;
        }

        GUILayout.EndArea();
        DrawResizeHandle();
        GUI.DragWindow(new Rect(0f, 0f, m_windowRect.width - 44f, 38f));
        PersistWindowRectOnMouseUp();
    }

    private void DrawBoard()
    {
        var panel = CurrentPanel;
        if (panel == null)
        {
            GUILayout.Label("Kein Panel vorhanden.", m_labelStyle);
            return;
        }

        GUILayout.BeginHorizontal();
        for (var index = 0;
             index < m_runtime.Configuration.Panels.Count;
             index++)
        {
            var candidate = m_runtime.Configuration.Panels[index];
            if (GUILayout.Button(
                    candidate.Name,
                    index == m_currentPanelIndex
                        ? m_primaryButtonStyle
                        : m_buttonStyle,
                    GUILayout.Height(30f)))
            {
                m_currentPanelIndex = index;
                m_boardScroll = Vector2.zero;
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
            "AKTIVE EREIGNISSE " + activeCount +
            "   ·   UNQUITTIERT " + unacknowledgedCount,
            m_sectionStyle,
            GUILayout.Height(34f));
        if (GUILayout.Button(
                "MASTER QUIT · QUITTIEREN",
                m_dangerButtonStyle,
                GUILayout.Width(245f),
                GUILayout.Height(34f)))
        {
            m_runtime.AcknowledgeAll();
            m_audio.StopAlarm();
            SetStatus(
                "Alle kommenden und gegangenen Meldungen quittiert.");
        }
        if (GUILayout.Button(
                "PANEL ABKOPPELN",
                m_buttonStyle,
                GUILayout.Width(180f),
                GUILayout.Height(34f)))
        {
            DetachPanel(panel.Id);
        }
        GUILayout.EndHorizontal();

        DrawStatusMessage();
        if (!m_entityAssignmentPending)
        {
            GUILayout.Label(
                "Eigene Meldung doppelklicken = direkt im Editor öffnen.",
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
            m_entityAssignmentPending && !panel.IsDashboard,
            panel.IsDashboard
                ? "Keine aktiven Meldungen."
                : "Keine Meldeschlitze in diesem Panel.",
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
            ? "OBJEKT #" + m_assignmentEntityId + " WIRD GELADEN"
            : "OBJEKT HINZUGEFÜGT: " +
              m_assignmentEntity.Title.ToUpperInvariant() +
              " · ID " + m_assignmentEntity.EntityId;
        GUILayout.Label(
            entityText,
            m_sectionStyle,
            GUILayout.Height(34f));
        if (GUILayout.Button(
                "ZUWEISUNG ABBRECHEN",
                m_buttonStyle,
                GUILayout.Width(190f),
                GUILayout.Height(34f)))
        {
            CancelEntityAssignment();
            SetStatus("Objektzuweisung abgebrochen.");
        }
        GUILayout.EndHorizontal();
        GUILayout.Label(
            m_assignmentEntity == null
                ? "Nach dem Laden kann ein Ziel gewählt werden."
                : panel.IsDashboard
                    ? "HOME zeigt nur aktive Meldungen. Für einen festen " +
                      "Meldeschlitz zuerst ein Fachpanel wählen."
                    : "Auf " + panel.Name +
                  ": eigene Meldung anklicken = verknüpfen; " +
                  "+ NEUE MELDUNG anklicken = neuer fester Schlitz.",
            m_smallLabelStyle);
    }

    private void DrawHistory()
    {
        var entries = GetHistoryEntries();

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "VERLAUF   " + entries.Count + " EINTRÄGE",
            m_sectionStyle,
            GUILayout.Height(34f));
        var confirmingDelete =
            Time.realtimeSinceStartup < m_pendingHistoryDeleteUntil;
        if (GUILayout.Button(
                confirmingDelete
                    ? "NOCHMAL: ALLE KGQ LÖSCHEN"
                    : "ALLE KGQ LÖSCHEN",
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
                    "Zum Löschen aller KGQ-Einträge erneut drücken.");
            }
            else if (m_runtime.DeleteCompletedAlarmHistory(
                         out var deletedCount))
            {
                m_pendingHistoryDeleteUntil = 0f;
                m_historyScroll = Vector2.zero;
                SetStatus(
                    deletedCount + " KGQ-Einträge gelöscht.");
            }
            else
            {
                SetStatus(
                    "Löschen fehlgeschlagen: " +
                    m_runtime.LastPersistenceError);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label(
            "K = KOMMEN   |   G = GEGANGEN   |   Q = QUITTIERT",
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
                "Noch keine Meldungen im Verlauf.",
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
                "LÖSCHEN",
                m_buttonStyle))
        {
            if (m_runtime.DeleteAlarmHistoryEntry(entry.Sequence))
            {
                SetStatus("KGQ-Eintrag gelöscht.");
            }
            else
            {
                SetStatus(
                    "Löschen fehlgeschlagen: " +
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
                ? "NEUE MELDUNG / SAMMELMELDUNG"
                : "MELDUNG NACHTRÄGLICH BEARBEITEN",
            m_sectionStyle);
        GUILayout.Label(
            "Am einfachsten: UNMA-Glocke im Inspector drücken, dann auf " +
            "der Meldetafel eine eigene Meldung zum Verknüpfen oder das " +
            "freie + Karree wählen. Hier kann weiterhin die aktuelle " +
            "Spielauswahl übernommen werden.",
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
        GUILayout.Label("PANEL AUSWÄHLEN", m_sectionStyle);
        GUILayout.Label(
            "Ein Panel ist eine Meldetafel. Beim Speichern einer Meldung wird ausdrücklich gewählt, auf welcher Tafel sie erscheint.",
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
            GUILayout.Label("AKTUELLES PANEL BEARBEITEN", m_sectionStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", m_labelStyle, GUILayout.Width(90f));
            panel.Name = GUILayout.TextField(
                panel.Name,
                40,
                m_textFieldStyle,
                GUILayout.Width(260f));
            GUILayout.Label(
                "Spalten " + panel.Columns,
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
                    " Vanilla auto",
                    GUILayout.Width(100f));
                panel.IncludeSystem = GUILayout.Toggle(
                    panel.IncludeSystem,
                    " System auto",
                    GUILayout.Width(100f));
            }
            else
            {
                GUILayout.Label(
                    "HOME / DASHBOARD",
                    m_smallLabelStyle,
                    GUILayout.Width(205f));
            }
            if (GUILayout.Button(
                    "ÄNDERUNGEN SPEICHERN",
                    m_primaryButtonStyle,
                    GUILayout.Width(190f)))
            {
                SaveConfiguration("Panel gespeichert.");
            }
            GUILayout.EndHorizontal();

            if (panel.IsDashboard)
            {
                GUILayout.Label(
                    "HOME zeigt automatisch nur aktuell anstehende " +
                    "Meldungen: K und KQ. Gegangene, normale und leere " +
                    "Schlitze werden hier nicht angezeigt. HOME ist nicht " +
                    "löschbar.",
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
                            ? "SICHER? " + affectedRules + " MELDUNG(EN)"
                            : "AKTUELLES PANEL LÖSCHEN",
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
        GUILayout.Label("NEUES PANEL ANLEGEN", m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "Name der neuen Meldetafel",
            m_labelStyle,
            GUILayout.Width(205f));
        m_newPanelName = GUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            GUILayout.Width(300f));
        if (GUILayout.Button(
                "MELDETAFEL ANLEGEN",
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
            "FESTE MELDESCHLITZE   " + panel.Slots.Count,
            m_sectionStyle);
        GUILayout.Label(
            "Jede Alarm-ID belegt genau einen dauerhaften Platz. Zustand und Schwere verschieben keinen Schlitz. 'Vanilla auto' und 'System auto' hängen passende Arten einmalig hinten an. ENTFERNEN sperrt den Auto-Schlitz dauerhaft; + SCHLITZ gibt ihn wieder frei.",
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
                SaveConfiguration("Schlitz nach oben verschoben.");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                return;
            }
            GUI.enabled = index < panel.Slots.Count - 1;
            if (GUILayout.Button("↓", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Slots.RemoveAt(index);
                panel.Slots.Insert(index + 1, slot);
                SaveConfiguration("Schlitz nach unten verschoben.");
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
                    isCustom ? "ÜBER REGEL" : "ENTFERNEN",
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
                SaveConfiguration("Fester Meldeschlitz entfernt.");
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
            "Bekannte Meldung hinzufügen",
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
                    "+ SCHLITZ",
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
                SaveConfiguration("Fester Meldeschlitz angehängt.");
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
        }
        if (available.Length == 0)
        {
            GUILayout.Label(
                "Keine weitere bekannte Vanilla- oder Systemmeldung passend zur Suche.",
                m_smallLabelStyle);
        }
    }

    private static string SlotSourceLabel(string source)
    {
        return source switch
        {
            "vanilla" => "VANILLA",
            "system" => "SYSTEM",
            "custom" => "EIGENE REGEL",
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
        GUILayout.Label("DEFINIERTE MELDUNGEN", m_sectionStyle);
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
                    SetStatus("Meldung aktualisiert.");
                }
                else
                {
                    SetStatus(
                        "Speichern fehlgeschlagen: " +
                        m_runtime.LastPersistenceError);
                }
            }
            GUILayout.Label(
                rule.Name + " · " + SeverityLabel(rule.Severity) +
                " · " + rule.Conditions.Count + " Bedingung(en) · " +
                (rule.Logic == AlarmLogic.All ? "UND" : "ODER") + " · " +
                (rule.AutoAcknowledgeOnClear
                    ? "GEHT: AUTOMATISCH"
                    : "GEHT: MASTER QUIT"),
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
                    "LÖSCHEN",
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
                    SetStatus("Meldung gelöscht.");
                }
                else
                {
                    SetStatus(
                        "Löschen fehlgeschlagen: " +
                        m_runtime.LastPersistenceError);
                }
            }
            GUILayout.EndHorizontal();
        }
    }

    private void DrawEntityAlarmWindow(int _)
    {
        var title = m_selectedEntity == null
            ? "UNMA · OBJEKT-ALARM WIRD GELADEN"
            : "UNMA · ALARM FÜR " + m_selectedEntity.Title.ToUpperInvariant() +
              " · OBJEKT #" + m_selectedEntity.EntityId;
        DrawWindowHeader(title);

        if (GUI.Button(
                new Rect(m_entityAlarmWindowRect.width - 52f, 8f, 40f, 28f),
                "X",
                m_buttonStyle))
        {
            m_entityAlarmWindowOpen = false;
            m_openEntityAlarmAfterInspection = false;
        }

        GUILayout.BeginArea(new Rect(
            12f,
            42f,
            m_entityAlarmWindowRect.width - 24f,
            m_entityAlarmWindowRect.height - 56f));
        DrawStatusMessage();
        m_entityAlarmScroll = GUILayout.BeginScrollView(m_entityAlarmScroll);
        DrawAlarmRuleEditor(true);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.DragWindow(new Rect(
            0f,
            0f,
            m_entityAlarmWindowRect.width - 58f,
            38f));
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
        var panels = m_runtime.Configuration.Panels
            .Where(panel => !panel.IsDashboard)
            .ToList();
        GUILayout.Label("ZIEL-MELDETAFEL", m_sectionStyle);
        if (panels.Count == 0)
        {
            GUILayout.Label(
                "Kein Fachpanel vorhanden. Für feste Meldeschlitze jetzt " +
                "eine dauerhafte Meldetafel anlegen.",
                m_labelStyle);
            if (allowCreate)
            {
                DrawCreateTargetPanelRow(false);
            }
            return;
        }

        var targetIndex = panels.FindIndex(panel => string.Equals(
            panel.Id,
            m_draftTargetPanelId,
            StringComparison.Ordinal));
        if (targetIndex < 0)
        {
            targetIndex = CurrentPanel == null
                ? -1
                : panels.FindIndex(panel => string.Equals(
                    panel.Id,
                    CurrentPanel.Id,
                    StringComparison.Ordinal));
            targetIndex = Math.Max(0, targetIndex);
            m_draftTargetPanelId = panels[targetIndex].Id;
            m_draftPreferredSlotIndex = -1;
        }

        var slotPositionLocked = m_draftPreferredSlotIndex >= 0;
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "Hier erscheint die Meldung:",
            m_labelStyle,
            GUILayout.Width(205f));
        var guiWasEnabled = GUI.enabled;
        GUI.enabled = guiWasEnabled && !slotPositionLocked;
        if (GUILayout.Button("<", m_buttonStyle, GUILayout.Width(38f)))
        {
            targetIndex = Wrap(targetIndex - 1, panels.Count);
            m_draftTargetPanelId = panels[targetIndex].Id;
        }
        GUILayout.Label(
            panels[targetIndex].Name,
            m_headerStyle,
            GUILayout.Width(310f),
            GUILayout.Height(30f));
        if (GUILayout.Button(">", m_buttonStyle, GUILayout.Width(38f)))
        {
            targetIndex = Wrap(targetIndex + 1, panels.Count);
            m_draftTargetPanelId = panels[targetIndex].Id;
        }
        GUI.enabled = guiWasEnabled;
        GUILayout.Label(
            slotPositionLocked
                ? "Fester Schlitz auf dieser Tafel gewählt."
                : "Ziel mit < / > wechseln.",
            m_smallLabelStyle);
        GUILayout.EndHorizontal();

        if (allowCreate)
        {
            DrawCreateTargetPanelRow(slotPositionLocked);
        }
    }

    private void DrawCreateTargetPanelRow(bool slotPositionLocked)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "Neue Meldetafel",
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
                "+ MELDETAFEL ANLEGEN",
                m_buttonStyle,
                GUILayout.Width(205f)))
        {
            AddPanel();
        }
        GUI.enabled = guiWasEnabled;
        GUILayout.Label(
            slotPositionLocked
                ? "Für ein anderes Ziel zuerst ENTWURF LEEREN."
                : "Das neue Panel wird sofort als Ziel gewählt.",
            m_smallLabelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawEntitySourceSelector(bool inEntityWindow)
    {
        GUILayout.Label("QUELLOBJEKT", m_sectionStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
                "AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN",
                m_primaryButtonStyle,
                GUILayout.Width(315f),
                GUILayout.Height(30f)))
        {
            CaptureSelectedEntity(inEntityWindow);
        }
        GUILayout.Label(
            m_selectedEntity == null
                ? "Noch kein Objekt geladen. Im Inspector die " +
                  "UNMA-Glocke drücken oder die aktuelle Spielauswahl " +
                  "übernehmen."
                : m_selectedEntity.Title + " · " +
                  ShortTypeName(m_selectedEntity.EntityType) +
                  " · ID " + m_selectedEntity.EntityId +
                  " · " + m_selectedMetrics.Count + " Messwerte",
            m_labelStyle);
        GUILayout.EndHorizontal();
    }

    private void DrawNewConditionForm()
    {
        m_selectedMetricIndex = Math.Max(
            0,
            Math.Min(m_selectedMetricIndex, m_selectedMetrics.Count - 1));
        var metric = m_selectedMetrics[m_selectedMetricIndex];

        GUILayout.Label("NEUE AWL-BEDINGUNG", m_sectionStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Kennung / Ist-Wert", m_labelStyle, GUILayout.Width(150f));
        if (GUILayout.Button(
                metric.Label + "   [aktuell " + FormatMetricValue(metric) + "]",
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
                    ? "% KAPAZITÄT"
                    : "% VON",
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
                    "Bezug: " + reference.Label +
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
                "ABSOLUT prüft die Produktmenge, z. B. Kartoffeln < 400. " +
                "% KAPAZITÄT prüft den Füllstand, z. B. Kartoffeln < 50 %.",
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
                ? "Soll-Wert in %"
                : "Soll-Wert",
            m_labelStyle,
            GUILayout.Width(105f));
        m_draftThreshold = GUILayout.TextField(
            m_draftThreshold,
            24,
            m_textFieldStyle,
            GUILayout.Width(105f));
        if (GUILayout.Button(
                "+ ZEILE HINZUFÜGEN",
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
            "Messwert anklicken; technische Pfade bleiben intern.",
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
                    "Weitere Treffer ausblendet – Suche genauer eingrenzen.",
                    m_smallLabelStyle);
                break;
            }

            var selected = referencePicker
                ? index == m_selectedReferenceMetricIndex
                : index == m_selectedMetricIndex;
            if (GUILayout.Button(
                    candidate.Label + "   · aktuell " +
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
        GUILayout.Label("BEDINGUNGEN DER MELDUNG", m_sectionStyle);
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
                "Noch keine Zeile. Oben Messwert, Berechnung, Steuerzeichen und Soll-Wert auswählen.",
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
                        : "% VON",
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
                            ? "BEZUG WÄHLEN"
                            : "Bezug: " + condition.ReferenceMetricLabel,
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
        GUILayout.Label("Verknüpfung aller Zeilen", m_labelStyle, GUILayout.Width(210f));
        if (GUILayout.Button(
                "UND · alle Bedingungen müssen stimmen",
                m_draftLogic == AlarmLogic.All
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(290f)))
        {
            m_draftLogic = AlarmLogic.All;
        }
        if (GUILayout.Button(
                "ODER · mindestens eine muss stimmen",
                m_draftLogic == AlarmLogic.Any
                    ? m_primaryButtonStyle
                    : m_buttonStyle,
                GUILayout.Width(300f)))
        {
            m_draftLogic = AlarmLogic.Any;
        }
        GUILayout.Label(
            "Gemischte Klammerlogik folgt in einer späteren Ausbaustufe.",
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
                "Zum Ändern des Bezugs muss das Quellobjekt kurz geladen werden.",
                m_smallLabelStyle);
            if (GUILayout.Button(
                    "QUELLOBJEKT LADEN",
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
        GUILayout.Label("Bezug suchen", m_smallLabelStyle, GUILayout.Width(90f));
        m_referenceMetricPickerFilter = GUILayout.TextField(
            m_referenceMetricPickerFilter,
            60,
            m_textFieldStyle,
            GUILayout.Width(280f));
        GUILayout.Label(
            "Der Ist-Wert selbst wird nicht als Bezug angeboten.",
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
                    "BEZUG: " + metric.Label + " · aktuell " +
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
                    ? "DATEI FEHLT · " + m_originalDraftSoundId
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
                    "TON TESTEN",
                    m_buttonStyle,
                    GUILayout.Width(125f)))
            {
                TestSound(sounds[m_draftSoundIndex].Id, m_draftSeverity);
            }
            GUI.enabled = true;
            if (GUILayout.Button(
                    "TON STOP",
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
            "BEIM GEHEN AUTOMATISCH QUITTIEREN",
            GUILayout.Width(340f));
        GUILayout.Label(
            "AUS: KG bleibt bis MASTER QUIT; quittiert wird sonst immer manuell.",
            m_smallLabelStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = m_draftConditions.Count > 0 &&
                      GetDraftTargetPanel() != null;
        if (GUILayout.Button(
                string.IsNullOrWhiteSpace(m_editingRuleId)
                    ? "MELDUNG SPEICHERN"
                    : "ÄNDERUNGEN SPEICHERN",
                m_primaryButtonStyle,
                GUILayout.Width(220f),
                GUILayout.Height(34f)))
        {
            SaveDraftRule(sounds);
        }
        GUI.enabled = true;
        if (GUILayout.Button(
                "ENTWURF LEEREN",
                m_buttonStyle,
                GUILayout.Width(155f),
                GUILayout.Height(34f)))
        {
            ResetDraftRule();
            SetStatus("Entwurf geleert.");
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSystemAlarms()
    {
        if (Time.realtimeSinceStartup > m_pendingSystemResetUntil)
        {
            m_pendingSystemResetId = "";
        }
        GUILayout.Label("EDITIERBARE VORDEFINIERTE MELDUNGEN", m_sectionStyle);
        GUILayout.Label(
            "Jede Systemmeldung und jede Stufe kann auch später geändert werden. Gesundheit 10 ist neutral; NOTFALL ist ab Werk ausschließlich an eine aktive Hunger- oder Gesundheitstodesspirale gebunden.",
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
                    " aktive Stufe(n) · " +
                    (alarm.AutoAcknowledgeOnClear
                        ? "GEHT: AUTOMATISCH"
                        : "GEHT: MASTER QUIT"),
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
                        SetStatus("Systemmeldung aktualisiert.");
                    }
                    else
                    {
                        SetStatus(
                            "Speichern fehlgeschlagen: " +
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
                            ? "SICHER?"
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
                            "Werkvorgabe innerhalb von 5 Sekunden " +
                            "noch einmal bestätigen.");
                    }
                    else
                    {
                        m_pendingSystemResetId = "";
                        if (m_runtime.ResetSystemAlarm(alarm.Id))
                        {
                            SetStatus("Werkvorgabe wiederhergestellt.");
                        }
                        else
                        {
                            SetStatus(
                                "Zurücksetzen fehlgeschlagen: " +
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
            "Gesamtmeldung aktiv",
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
            "BEIM GEHEN AUTOMATISCH QUITTIEREN",
            GUILayout.Width(340f));
        GUILayout.Label(
            "AUS: GEGANGEN · UNQUITTIERT bleibt bis MASTER QUIT.",
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
                "Stufe aktiv",
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
                        ? "UND · alle"
                        : "ODER · eine",
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
                        : "DATEI FEHLT · " + stage.SoundId,
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
                        "UNBEKANNT: " + (condition.MetricId ?? ""),
                        "nicht verfügbar");
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
                        ? " [jetzt " + current.ToString(
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
                    "+ BEDINGUNG",
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
                "SYSTEMMELDUNG SPEICHERN",
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
            SetStatus("Bearbeitung abgebrochen.");
        }
        GUILayout.EndHorizontal();
    }

    private void BeginEditingSystemAlarm(SystemAlarmDefinition alarm)
    {
        m_systemAlarmDraft = alarm;
        RebuildSystemThresholdTexts();
        m_systemAlarmScroll = Vector2.zero;
        SetStatus("Systemmeldung in den Editor geladen.");
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
                        "Ungültige Schwelle in Stufe '" +
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
                "Speichern fehlgeschlagen: " +
                m_runtime.LastPersistenceError);
            return;
        }
        m_systemAlarmDraft = null;
        m_systemThresholdTexts.Clear();
        SetStatus("Systemmeldung dauerhaft gespeichert.");
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
                "VANILLA- UND FREMDMOD-MELDUNGEN: TÖNE / QUITTIERUNG"),
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get(
                "sounds.override.description",
                "Eigene Regeln werden im Editor und vordefinierte " +
                "Meldungen im SYSTEM-Tab eingestellt. Hier erhält jede " +
                "bekannte Vanilla- oder Fremdmod-Meldung separat ihren " +
                "Ton und das Verhalten beim Gehen. Ohne automatische " +
                "Quittierung bleibt GEGANGEN bis MASTER QUIT unquittiert."),
            m_smallLabelStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Filter", m_labelStyle, GUILayout.Width(70f));
        m_soundOverrideFilter = GUILayout.TextField(
            m_soundOverrideFilter,
            100,
            m_textFieldStyle);
        GUILayout.EndHorizontal();

        var sounds = m_audio.GetSoundOptions();
        var candidates = m_runtime.GetSoundOverrideCandidates()
            .Where(candidate =>
                string.IsNullOrWhiteSpace(m_soundOverrideFilter) ||
                (candidate.Name + " " + candidate.Detail + " " +
                 candidate.OverrideId).IndexOf(
                    m_soundOverrideFilter,
                    StringComparison.CurrentCultureIgnoreCase) >= 0)
            .ToArray();

        m_soundOverrideScroll = GUILayout.BeginScrollView(
            m_soundOverrideScroll);
        if (candidates.Length == 0)
        {
            GUILayout.Label(
                UnmaText.Get(
                    "sounds.override.empty",
                    "Noch keine passende Meldung bekannt. Vanilla- und " +
                    "Fremdmod-Meldungen erscheinen hier, sobald UNMA sie " +
                    "einmal ausgewertet hat."),
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

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                candidate.Name + "\n" + candidate.Detail,
                m_labelStyle,
                GUILayout.Width(Mathf.Max(260f, m_windowRect.width - 555f)),
                GUILayout.Height(46f));
            if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex - 1, sounds.Count)]);
            }
            GUILayout.Label(
                sounds[soundIndex].Label,
                m_smallLabelStyle,
                GUILayout.Width(210f));
            if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(34f)))
            {
                SaveSoundOverride(
                    candidate.OverrideId,
                    sounds[Wrap(soundIndex + 1, sounds.Count)]);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(Mathf.Max(260f, m_windowRect.width - 555f));
            var updatedAutoAcknowledgeOnClear = GUILayout.Toggle(
                autoAcknowledgeOnClear,
                "BEIM GEHEN AUTOMATISCH QUITTIEREN",
                GUILayout.Width(340f));
            if (updatedAutoAcknowledgeOnClear != autoAcknowledgeOnClear)
            {
                SaveAutoAcknowledgeOnClear(
                    candidate.OverrideId,
                    updatedAutoAcknowledgeOnClear);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }
        GUILayout.EndScrollView();
        DrawStatusMessage();
    }

    private void SaveSoundOverride(string alarmId, SoundOption sound)
    {
        if (m_runtime.SetConfiguredSound(alarmId, sound.Id))
        {
            SetStatus("Tonzuordnung gespeichert: " + sound.Label);
        }
        else
        {
            SetStatus(
                "Tonzuordnung konnte nicht gespeichert werden: " +
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
                    ? "Beim Gehen wird automatisch quittiert."
                    : "Beim Gehen ist MASTER QUIT erforderlich.");
        }
        else
        {
            SetStatus(
                "Quittierverhalten konnte nicht gespeichert werden: " +
                m_runtime.LastPersistenceError);
        }
    }

    private void DrawOptions()
    {
        GUILayout.Label("ANZEIGE", m_sectionStyle);
        GUILayout.Label(
            "F8 oder der verschiebbare UNMA-Launcher am linken Rand öffnet und schließt die Zentrale. Der Launcher wird bei offener Zentrale ausgeblendet. Panels können mehrfach abgekoppelt, innerhalb des Spielbildes verschoben und in drei Größen dargestellt werden.",
            m_labelStyle);

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
                "FARBEN SPEICHERN",
                m_primaryButtonStyle,
                GUILayout.Width(175f)))
        {
            SaveConfiguration("Farben gespeichert.");
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        GUILayout.Label("AUDIO", m_sectionStyle);
        GUILayout.Label(
            "Mitgeliefert: Klingel, tiefes Industriehorn (3,2 s Ton / 1,2 s Pause), E57-artige Motorsirene (2 s hoch / 2 s runter) sowie Sinus-, Rechteck-, Sägezahn-, Dreieck- und Impulston. Alle werden mathematisch erzeugt und enthalten keine Samples Dritter.",
            m_labelStyle);
        GUILayout.Label(
            "Eigene PCM-WAV- oder Ogg-Vorbis-Dateien: " +
            m_audio.SoundsDirectory,
            m_smallLabelStyle);
        GUILayout.Label(
            "Audioformat und Lizenz sind zwei verschiedene Dinge: Für fremde Dateien bitte CC0, eigene Aufnahmen oder eine andere passende offene Lizenz verwenden.",
            m_smallLabelStyle);
        if (GUILayout.Button(
                "TONDATEIEN NEU EINLESEN",
                m_buttonStyle,
                GUILayout.Width(220f)))
        {
            m_audio.RefreshSoundOptions();
            SetStatus("WAV-/OGG-Tonliste neu eingelesen.");
        }

        GUILayout.Space(10f);
        GUILayout.Label("SYSTEMALARME", m_sectionStyle);
        GUILayout.Label(
            "Gesundheit, Nahrung und Arbeiter werden im SYSTEM-Tab pro Spielstand bearbeitet. Gesundheit 10 ist neutral; die Werkvorgabe nutzt NOTFALL nur für eine aktive Hunger- oder Gesundheitstodesspirale. Warnung → Klingel, kritisch → Horn, Notfall → Sirene.",
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label("ZWEITER BILDSCHIRM", m_sectionStyle);
        GUILayout.Label(
            "Mehrere abgekoppelte Meldetafeln sind unterstützt, bleiben aber innerhalb des Captain-of-Industry-Fensters. Ein echtes zweites Betriebssystemfenster stellt die Mod-API nicht bereit; ein externer Companion-Prozess wäre ein separates, experimentelles Phase-2-Projekt.",
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label("ZUSTANDSMODELL", m_sectionStyle);
        GUILayout.Label(
            "NORMAL: hellgrau, schwarze Schrift. KOMMT: Aktivfarbe blinkt und der Ton wiederholt sich. MASTER QUIT: Aktivfarbe bleibt stehen, Ton endet. GEGANGEN · UNQUITTIERT: Die Ursache ist weg, aber Anzeige und Ton warten auf MASTER QUIT. Mit BEIM GEHEN AUTOMATISCH QUITTIEREN wechselt die Meldung stattdessen direkt zu NORMAL.",
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label(
            UnmaText.Get("options.integration.title", "FREMDMOD-API"),
            m_sectionStyle);
        GUILayout.Label(
            UnmaText.Get(
                "options.integration.description",
                "Aktive Mods können Alarmvorlagen aus UNMA/*.json, " +
                "eigene Messwerte und direkte Alarmzustände bereitstellen."),
            m_labelStyle);
        var integration = m_runtime.GetExternalIntegrationStatus();
        GUILayout.Label(
            UnmaText.Format(
                "options.integration.status",
                "Provider {0} · JSON {1}/{2} Dateien · {3} Alarme · " +
                "API {4} Messwerte / {5} Vorlagen / {6} Zustände · " +
                "Diagnosen {7}",
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
                    "API / JSON / SPRACHE NEU LADEN"),
                m_buttonStyle,
                GUILayout.Width(260f)))
        {
            var clean = m_runtime.ReloadExternalDefinitions();
            SetStatus(clean
                ? UnmaText.Get(
                    "options.integration.reload_ok",
                    "Fremdmod-Definitionen und Sprache neu geladen.")
                : UnmaText.Get(
                    "options.integration.reload_partial",
                    "Gültige Fremdmod-Definitionen geladen; Diagnosen " +
                    "siehe darunter und im Log."));
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
        DrawWindowHeader("UNMA · " + panel.Name);
        GUILayout.BeginArea(new Rect(
            10f,
            40f,
            detached.Rect.width - 20f,
            detached.Rect.height - 50f));
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "AKTIVE EREIGNISSE " + activeCount +
            " · UNQUITTIERT " + unacknowledgedCount,
            m_smallLabelStyle);
        if (GUILayout.Button(
                "MASTER QUIT",
                m_dangerButtonStyle,
                GUILayout.Width(130f)))
        {
            m_runtime.AcknowledgeAll();
            m_audio.StopAlarm();
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
                    Screen.width - 20f,
                    detached.Rect.width + 120f),
                Mathf.Min(
                    Screen.height - 20f,
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
            false,
            panel.IsDashboard
                ? "Keine aktiven Meldungen."
                : "Keine Meldeschlitze in diesem Panel.",
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
                    DrawAlarmTile(rect, alarms[index]);
                    if (assignmentPending && interactionPanel != null)
                    {
                        DrawExistingAssignmentTarget(
                            rect,
                            interactionPanel,
                            alarms[index]);
                    }
                    else if (!m_entityAssignmentPending &&
                             GUI.Button(
                                 rect,
                                 GUIContent.none,
                                 GUIStyle.none))
                    {
                        HandleAlarmTileClick(alarms[index]);
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
            m_entityAlarmWindowOpen = false;
            m_isOpen = true;
            m_tab = 2;
            SetStatus(
                "Die eigene Meldung in diesem Schlitz existiert nicht mehr.");
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
                m_entityAlarmWindowOpen = false;
                m_isOpen = true;
                m_tab = 2;
                SetStatus(
                    "Im Editor liegt bereits eine andere oder ungespeicherte " +
                    "Meldung. Erst speichern oder ENTWURF LEEREN.");
                return true;
            }
            BeginEditingRule(rule, m_audio.GetSoundOptions());
        }

        var panelIndex = m_runtime.Configuration.Panels.FindIndex(panel =>
            string.Equals(panel.Id, rule.PanelId, StringComparison.Ordinal));
        if (panelIndex >= 0)
        {
            m_currentPanelIndex = panelIndex;
        }
        m_entityAlarmWindowOpen = false;
        m_openEntityAlarmAfterInspection = false;
        m_isOpen = true;
        m_tab = 2;

        var firstCondition = rule.Conditions.FirstOrDefault();
        if (alreadyEditing)
        {
            SetStatus("Bereits geöffnete Meldung im Editor fokussiert.");
        }
        else if (firstCondition != null)
        {
            RequestEntityInspection(firstCondition.EntityId, false);
        }
        else
        {
            SetStatus("Meldung per Doppelklick im Editor geöffnet.");
        }
        return true;
    }

    private void DrawAlarmTile(Rect rect, AlarmView alarm)
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
            ? "GEGANGEN · UNQUITTIERT"
            : alarm.IsActive
                ? alarm.IsAcknowledged ? "STEHT" : "KOMMT"
                : alarm.IsMissingSource ? "QUELLE FEHLT" : "NORMAL";
        if ((alarm.IsActive || alarm.IsGoneUnacknowledged) &&
            alarm.IsMissingSource)
        {
            badge += " / QUELLE FEHLT";
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
            new Rect(inner.x + 7f, inner.y + 75f, inner.width - 14f, 25f),
            alarm.Detail ?? "",
            m_tileDetailStyle);
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
                ? "OBJEKT VERKNÜPFEN"
                : "NUR ANZEIGE · NICHT VERKNÜPFBAR",
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
            "+ NEUE MELDUNG",
            m_tileTitleStyle);
        GUI.Label(
            new Rect(inner.x + 7f, inner.y + 73f, inner.width - 14f, 25f),
            m_assignmentEntity == null
                ? "Objekt wird geladen"
                : "FESTER SCHLITZ FÜR " +
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
            SetStatus("Das hinzugefügte Objekt wird noch geladen.");
            return;
        }
        if (!PanelSlotProjection.TryGetCustomRuleId(
                alarm,
                out var ruleId))
        {
            SetStatus(
                "Vanilla- und Systemmeldungen sind feste Anzeigen. " +
                "Für eine Objektbedingung eine eigene Meldung oder " +
                "das freie + Karree wählen.");
            return;
        }

        var rule = m_runtime.Configuration.Rules.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        if (rule == null)
        {
            SetStatus(
                "Die Meldung in diesem Schlitz existiert nicht mehr. " +
                "Bitte das freie + Karree verwenden.");
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
                    "Im Editor ist bereits eine Meldung oder ein " +
                    "ungespeicherter Entwurf geöffnet. Erst speichern " +
                    "oder ENTWURF LEEREN; danach den " +
                    "Meldeschlitz erneut anklicken.");
                return;
            }
            BeginEditingRule(rule, m_audio.GetSoundOptions());
        }

        m_draftPreferredSlotIndex = -1;
        OpenAssignmentEntityEditor(
            m_assignmentEntity.Title + " kann jetzt mit »" +
            rule.Name + "« verknüpft werden. Messwert auswählen und " +
            "Bedingung hinzufügen.");
    }

    private void HandleNewAssignmentTarget(
        PanelDefinition panel,
        int slotIndex)
    {
        if (!IsEntityAssignmentReady())
        {
            SetStatus("Das hinzugefügte Objekt wird noch geladen.");
            return;
        }
        if (HasDraftRuleWork())
        {
            SetStatus(
                "Im Editor ist bereits eine Meldung oder ein " +
                "ungespeicherter Entwurf geöffnet. Erst speichern oder " +
                "ENTWURF LEEREN; danach das freie " +
                "+ Karree erneut anklicken.");
            return;
        }

        ResetDraftRule();
        m_draftTargetPanelId = panel.Id;
        m_draftPreferredSlotIndex = Math.Max(
            0,
            Math.Min(slotIndex, panel.Slots?.Count ?? 0));
        OpenAssignmentEntityEditor(
            "Neue Meldung für " + m_assignmentEntity.Title +
            " im gewählten Meldeschlitz vorbereiten.");
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
            SetStatus("Das hinzugefügte Objekt ist nicht mehr verfügbar.");
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
               m_draftConditions.Count > 0 ||
               !string.Equals(
                   m_draftRuleName?.Trim(),
                   "NEUE MELDUNG",
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
                "Das angeklickte Inspector-Objekt ist nicht mehr verfügbar.");
            return;
        }

        m_entityAssignmentPending = true;
        m_assignmentEntityId = entity.Id.Value;
        m_assignmentEntity = null;
        m_entityAlarmWindowOpen = false;
        m_isOpen = true;
        m_tab = 0;
        RequestEntityInspection(
            entity.Id.Value,
            false,
            preserveCurrentSelection: true);
        SetStatus(
            "Objekt wird hinzugefügt. Danach eigene Meldung oder leeres Karree anklicken.");
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
                "Keine gültige Entität gefunden. Erst Gebäude, Fahrzeug oder Transport anklicken.");
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
        SetStatus("Spielauswahl wird sicher im Simulations-Takt gelesen …");
    }

    private void ApplyCompletedInspection(EntityInspectionSnapshot inspection)
    {
        if (m_pendingInspectionEntityId >= 0 &&
            inspection.EntityId != m_pendingInspectionEntityId)
        {
            return;
        }
        m_pendingInspectionEntityId = -1;
        var automaticRefresh = m_isAutomaticInspectionRefresh;
        m_isAutomaticInspectionRefresh = false;
        var assignmentInspection = m_entityAssignmentPending &&
                                   inspection.EntityId ==
                                   m_assignmentEntityId;

        if (!string.IsNullOrWhiteSpace(inspection.Error))
        {
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
                    "Für diese Entität wurden keine Messwerte gefunden.");
                return;
            }
            m_assignmentEntity = inspection;
            SetStatus(
                inspection.Title +
                " hinzugefügt. Jetzt eigene Meldung oder leeres " +
                "Karree anklicken.");
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
            SetStatus("Für diese Entität wurden keine Messwerte gefunden.");
        }
        else
        {
            SetStatus(
                m_selectedMetrics.Count +
                " Messwerte für " +
                inspection.Title +
                " erkannt.");
        }
    }

    private void AddDraftCondition()
    {
        if (m_selectedEntity == null || m_selectedMetrics.Count == 0)
        {
            SetStatus("Zuerst eine Spiel-Entität übernehmen.");
            return;
        }
        if (!TryParseDouble(m_draftThreshold, out var threshold))
        {
            SetStatus("Schwelle ist keine gültige Zahl.");
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
                    "Ist-Wert und Bezugswert dürfen bei % VON nicht identisch sein.");
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
        SetStatus("Bedingung zur Sammelmeldung hinzugefügt.");
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
                    "Schwelle in Bedingung " + (index + 1) +
                    " ist ungültig.");
                return;
            }
            m_draftConditions[index].Threshold = threshold;
            if (m_draftConditions[index].ValueMode ==
                    ConditionValueMode.PercentOfReference &&
                string.IsNullOrWhiteSpace(
                    m_draftConditions[index].ReferenceMetricPath))
            {
                SetStatus(
                    "Bezugswert in Bedingung " + (index + 1) +
                    " fehlt. Bei % VON bitte einen Bezug wählen.");
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
                    "Ist- und Bezugswert in Bedingung " + (index + 1) +
                    " sind identisch. Bitte einen anderen Bezug wählen.");
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
                "Die bearbeitete Meldung existiert nicht mehr. " +
                "Entwurf wurde nicht als neue Meldung gespeichert.");
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
        };
        var saved = isEditing
            ? m_runtime.UpdateRule(rule)
            : m_runtime.AddRule(rule, m_draftPreferredSlotIndex);
        if (!saved)
        {
            SetStatus(
                "Speichern fehlgeschlagen: " +
                m_runtime.LastPersistenceError);
            return;
        }
        var wasEditing = existingRule != null;
        var savedPanelId = panel.Id;
        ResetDraftRule();
        m_draftTargetPanelId = savedPanelId;
        SetStatus(
            wasEditing
                ? "Meldung aktualisiert; neue Werte gelten im nächsten Takt."
                : "Meldung gespeichert; Überwachung startet im nächsten Takt.");
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
        SetStatus("Meldung in den Editor geladen.");
    }

    private void ResetDraftRule()
    {
        m_draftPreferredSlotIndex = -1;
        m_editingRuleId = "";
        m_draftConditions.Clear();
        m_draftConditionThresholdTexts.Clear();
        m_draftRuleName = "NEUE MELDUNG";
        m_draftSeverity = AlarmSeverity.Warning;
        m_draftLogic = AlarmLogic.All;
        m_draftColor = "#F0C541";
        m_draftSoundIndex = 0;
        m_originalDraftSoundId = "auto";
        m_draftSoundChanged = false;
        m_draftAutoAcknowledgeOnClear = false;
        m_draftValueMode = ConditionValueMode.Absolute;
        m_draftComparison = ComparisonOperator.Less;
        m_draftThreshold = "0";
        m_metricPickerOpen = false;
        m_referenceMetricPickerOpen = false;
        m_conditionReferencePickerIndex = -1;
        var targetPanel = CurrentPanel != null && !CurrentPanel.IsDashboard
            ? CurrentPanel
            : m_runtime.Configuration.Panels.FirstOrDefault(panel =>
                !panel.IsDashboard);
        m_draftTargetPanelId = targetPanel?.Id ?? "";
    }

    private void AddPanel()
    {
        var panel = new PanelDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(m_newPanelName)
                ? "NEUES PANEL"
                : m_newPanelName.Trim(),
            Columns = 3,
            IncludeVanilla = false,
            IncludeSystem = false,
            IsDashboard = false,
        };
        if (!m_runtime.AddPanel(panel))
        {
            SetStatus(
                "Panel konnte nicht gespeichert werden: " +
                m_runtime.LastPersistenceError);
            return;
        }
        m_currentPanelIndex = m_runtime.Configuration.Panels.Count - 1;
        m_draftTargetPanelId = panel.Id;
        m_newPanelName = "NEUES PANEL";
        SetStatus("Panel angelegt.");
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
                "HOME / DASHBOARD ist die aktive Übersicht und kann nicht " +
                "gelöscht werden.");
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
                "Panel löschen entfernt auch " + affectedRules +
                " eigene Meldung(en). Innerhalb von 6 Sekunden erneut bestätigen.");
            return;
        }
        if (!m_runtime.RemovePanel(panelId))
        {
            SetStatus(
                "Panel konnte nicht gelöscht werden: " +
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
        SetStatus("Panel und zugehörige eigene Meldungen gelöscht.");
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
            SetStatus("Kein Ton ausgewählt; laufende Alarme bleiben hörbar.");
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
        SetStatus("Tontest läuft acht Sekunden oder bis TON STOP.");
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
            return "LIVE IM SPIEL";
        }

        var actualMetric = FindSelectedMetric(condition.MetricPath);
        if (actualMetric == null)
        {
            return "MESSWERT FEHLT";
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
            return "BEZUG FEHLT";
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
                "Speichern fehlgeschlagen: " +
                m_runtime.LastPersistenceError);
        }
    }

    private void DrawTabButton(int tab, string label)
    {
        var width = Mathf.Clamp(
            (m_windowRect.width - 105f) / 6f,
            88f,
            150f);
        if (GUILayout.Button(
                label,
                m_tab == tab ? m_primaryButtonStyle : m_buttonStyle,
                GUILayout.Width(width),
                GUILayout.Height(30f)))
        {
            m_tab = tab;
        }
    }

    private void DrawWindowHeader(string title)
    {
        GUI.Label(
            new Rect(12f, 8f, 720f, 28f),
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
            var delta = current - m_resizeStartMouse;
            m_pendingMainWindowSize = new Vector2(
                WindowResizeMath.ResizeExtent(
                    m_resizeStartSize.x,
                    delta.x,
                    700f,
                    Math.Max(700f, Screen.width - 12f)),
                WindowResizeMath.ResizeExtent(
                    m_resizeStartSize.y,
                    delta.y,
                    520f,
                    Math.Max(520f, Screen.height - 12f)));
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

    private void PersistWindowRectOnMouseUp()
    {
        if (UnityEngine.Event.current.type != EventType.MouseUp)
        {
            return;
        }
        PersistWindowRect();
    }

    private void PersistWindowRect()
    {
        var config = m_runtime.Configuration;
        config.WindowX = m_windowRect.x;
        config.WindowY = m_windowRect.y;
        config.WindowWidth = m_windowRect.width;
        config.WindowHeight = m_windowRect.height;
        m_runtime.SaveConfiguration();
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
            name = "UNMA " + key,
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
            if (m_runtime.Configuration.Panels.Count == 0)
            {
                return null;
            }
            m_currentPanelIndex = Math.Max(
                0,
                Math.Min(
                    m_currentPanelIndex,
                    m_runtime.Configuration.Panels.Count - 1));
            return m_runtime.Configuration.Panels[m_currentPanelIndex];
        }
    }

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
            return "Entität";
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
                Log.Warning(
                    $"UNMA: Menüstatus nicht lesbar; Tafel wird zugelassen: {exception.Message}");
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

    private static Rect ClampToScreen(Rect rect)
    {
        rect.width = Mathf.Min(rect.width, Math.Max(320f, Screen.width - 8f));
        rect.height = Mathf.Min(rect.height, Math.Max(260f, Screen.height - 8f));
        rect.x = Mathf.Clamp(
            rect.x,
            0f,
            Math.Max(0f, Screen.width - rect.width));
        rect.y = Mathf.Clamp(
            rect.y,
            0f,
            Math.Max(0f, Screen.height - rect.height));
        return rect;
    }

    private void OnDestroy()
    {
        m_inspectorAlarmButtons?.Dispose();
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
