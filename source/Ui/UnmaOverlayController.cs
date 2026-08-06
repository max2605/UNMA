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
    private const float TileHeight = 112f;

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
    private readonly Dictionary<string, PanelViewCacheEntry> m_panelViewCache =
        new(StringComparer.Ordinal);

    private UnmaRuntime m_runtime;
    private InspectorsManager m_inspectorsManager;
    private UnmaAudioController m_audio;
    private Rect m_windowRect;
    private Rect m_launcherRect;
    private Vector2 m_boardScroll;
    private Vector2 m_editorScroll;
    private Vector2 m_soundOverrideScroll;
    private bool m_isOpen;
    private bool m_stylesReady;
    private int m_tab;
    private int m_currentPanelIndex;
    private int m_nextDetachedWindowId = MainWindowId + 1;
    private bool m_gameplayWasActive;
    private bool m_isUiSuppressedByMenu;
    private bool m_isResizing;
    private bool m_isDraggingLauncher;
    private Vector2 m_resizeStartMouse;
    private Vector2 m_resizeStartSize;
    private Vector2 m_launcherDragOffset;
    private Vector2? m_pendingMainWindowSize;

    private EntityInspectionSnapshot m_selectedEntity;
    private IReadOnlyList<MetricDescriptor> m_selectedMetrics =
        Array.Empty<MetricDescriptor>();
    private int m_selectedMetricIndex;
    private ComparisonOperator m_draftComparison =
        ComparisonOperator.Less;
    private AlarmSeverity m_draftSeverity = AlarmSeverity.Warning;
    private AlarmLogic m_draftLogic = AlarmLogic.All;
    private string m_draftThreshold = "0";
    private string m_draftRuleName = "NEUE MELDUNG";
    private string m_draftColor = "#F0C541";
    private int m_draftSoundIndex;
    private string m_newPanelName = "NEUES PANEL";
    private string m_soundOverrideFilter = "";
    private string m_statusMessage = "";
    private float m_statusMessageUntil;
    private AlarmView m_testAlarm;
    private float m_testAlarmUntil;

    private GUIStyle m_windowStyle;
    private GUIStyle m_headerStyle;
    private GUIStyle m_sectionStyle;
    private GUIStyle m_labelStyle;
    private GUIStyle m_smallLabelStyle;
    private GUIStyle m_tileTitleStyle;
    private GUIStyle m_tileDetailStyle;
    private GUIStyle m_buttonStyle;
    private GUIStyle m_primaryButtonStyle;
    private GUIStyle m_dangerButtonStyle;
    private GUIStyle m_textFieldStyle;

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
        m_isOpen = runtime.Settings.ShowOnGameStart;
        var config = runtime.Configuration;
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

        if (m_runtime.TryTakeCompletedInspection(out var inspection))
        {
            ApplyCompletedInspection(inspection);
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
            return;
        }

        EnsureStyles();
        DrawLauncher();

        if (m_isOpen)
        {
            var nextWindowRect = GUI.Window(
                MainWindowId,
                ClampToScreen(m_windowRect),
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
        DrawWindowHeader("UNMA · UNIVERSELLE NACHRICHTEN-MELDEANLAGE");

        GUILayout.BeginArea(new Rect(
            12f,
            42f,
            m_windowRect.width - 24f,
            m_windowRect.height - 56f));

        GUILayout.BeginHorizontal();
        DrawTabButton(0, "MELDETAFEL");
        DrawTabButton(1, "EDITOR");
        DrawTabButton(2, "MELDUNGSTÖNE");
        DrawTabButton(3, "OPTIONEN / INFO");
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
                DrawEditor();
                break;
            case 2:
                DrawSoundOverrides();
                break;
            case 3:
                DrawOptions();
                break;
            default:
                DrawBoard();
                break;
        }

        GUILayout.EndArea();
        HandleResize();
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

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "AKTIV " + m_runtime.ActiveCount +
            "   ·   UNQUITTIERT " + m_runtime.UnacknowledgedCount,
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
            SetStatus("Alle aktuell anstehenden Meldungen quittiert.");
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
        var alarms = GetPanelViews(panel);
        m_boardScroll = GUILayout.BeginScrollView(m_boardScroll);
        DrawAlarmGrid(
            alarms,
            panel.Columns,
            m_windowRect.width - 54f,
            m_boardScroll.y,
            Math.Max(220f, m_windowRect.height - 190f));
        GUILayout.EndScrollView();
    }

    private void DrawEditor()
    {
        var panel = CurrentPanel;
        m_editorScroll = GUILayout.BeginScrollView(m_editorScroll);

        GUILayout.Label("PANEL", m_sectionStyle);
        if (panel != null)
        {
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
            if (GUILayout.Button("−", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Columns = Math.Max(1, panel.Columns - 1);
            }
            if (GUILayout.Button("+", m_buttonStyle, GUILayout.Width(34f)))
            {
                panel.Columns = Math.Min(8, panel.Columns + 1);
            }
            panel.IncludeVanilla = GUILayout.Toggle(
                panel.IncludeVanilla,
                " Vanilla",
                GUILayout.Width(100f));
            panel.IncludeSystem = GUILayout.Toggle(
                panel.IncludeSystem,
                " System",
                GUILayout.Width(100f));
            if (GUILayout.Button(
                    "PANEL SPEICHERN",
                    m_primaryButtonStyle,
                    GUILayout.Width(155f)))
            {
                SaveConfiguration("Panel gespeichert.");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Filter",
                m_labelStyle,
                GUILayout.Width(90f));
            panel.NotificationFilter = GUILayout.TextField(
                panel.NotificationFilter ?? "",
                240,
                m_textFieldStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Kommagetrennte Begriffe filtern Vanilla- und Systemmeldungen. Eigene Regeln sind fest dem Panel zugeordnet.",
                m_smallLabelStyle);
        }

        GUILayout.BeginHorizontal();
        m_newPanelName = GUILayout.TextField(
            m_newPanelName,
            40,
            m_textFieldStyle,
            GUILayout.Width(260f));
        if (GUILayout.Button(
                "+ PANEL",
                m_buttonStyle,
                GUILayout.Width(110f)))
        {
            AddPanel();
        }
        GUI.enabled = m_runtime.Configuration.Panels.Count > 1;
        if (GUILayout.Button(
                "AKTUELLES PANEL LÖSCHEN",
                m_dangerButtonStyle,
                GUILayout.Width(220f)))
        {
            RemoveCurrentPanel();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("NEUE MELDUNG / SAMMELMELDUNG", m_sectionStyle);
        GUILayout.Label(
            "1. Entität im Spiel anklicken und Inspector geöffnet lassen. 2. Auswahl übernehmen. 3. Messwert und Schwelle wählen. Für eine Sammelmeldung weitere Entitäten nacheinander hinzufügen.",
            m_smallLabelStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(
                "AKTUELLE SPIEL-AUSWAHL ÜBERNEHMEN",
                m_primaryButtonStyle,
                GUILayout.Width(305f),
                GUILayout.Height(30f)))
        {
            CaptureSelectedEntity();
        }
        GUILayout.Label(
            m_selectedEntity == null
                ? "Keine Entität übernommen"
                : m_selectedEntity.Title +
                  " · " + ShortTypeName(m_selectedEntity.EntityType) +
                  " · ID " + m_selectedEntity.EntityId,
            m_labelStyle);
        GUILayout.EndHorizontal();

        if (m_selectedEntity != null && m_selectedMetrics.Count > 0)
        {
            m_selectedMetricIndex = Math.Max(
                0,
                Math.Min(m_selectedMetricIndex, m_selectedMetrics.Count - 1));
            var metric = m_selectedMetrics[m_selectedMetricIndex];

            GUILayout.BeginHorizontal();
            GUILayout.Label("Messwert", m_labelStyle, GUILayout.Width(90f));
            if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(34f)))
            {
                CycleMetric(-1);
            }
            GUILayout.Label(
                metric.Label + "   [aktuell " +
                metric.CurrentValue.ToString(
                    "0.###",
                    CultureInfo.CurrentCulture) + "]",
                m_labelStyle,
                GUILayout.Width(360f));
            if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(34f)))
            {
                CycleMetric(1);
            }
            if (GUILayout.Button(
                    UnmaRuntime.OperatorText(m_draftComparison),
                    m_buttonStyle,
                    GUILayout.Width(48f)))
            {
                m_draftComparison = NextEnum(m_draftComparison);
            }
            m_draftThreshold = GUILayout.TextField(
                m_draftThreshold,
                24,
                m_textFieldStyle,
                GUILayout.Width(105f));
            if (GUILayout.Button(
                    "+ BEDINGUNG",
                    m_primaryButtonStyle,
                    GUILayout.Width(145f)))
            {
                AddDraftCondition();
            }
            GUILayout.EndHorizontal();
        }

        if (m_draftConditions.Count > 0)
        {
            GUILayout.Space(4f);
            for (var index = 0; index < m_draftConditions.Count; index++)
            {
                var condition = m_draftConditions[index];
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    (index + 1) + ". " + condition.EntityTitle + " · " +
                    condition.MetricLabel + " " +
                    UnmaRuntime.OperatorText(condition.Comparison) + " " +
                    condition.Threshold.ToString(
                        "0.###",
                        CultureInfo.CurrentCulture),
                    m_smallLabelStyle);
                if (GUILayout.Button(
                        "ENTFERNEN",
                        m_dangerButtonStyle,
                        GUILayout.Width(105f)))
                {
                    m_draftConditions.RemoveAt(index);
                    index--;
                }
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Verknüpfung", m_labelStyle, GUILayout.Width(90f));
        if (GUILayout.Button(
                m_draftLogic == AlarmLogic.All
                    ? "UND · alle Bedingungen"
                    : "ODER · mindestens eine",
                m_buttonStyle,
                GUILayout.Width(210f)))
        {
            m_draftLogic = m_draftLogic == AlarmLogic.All
                ? AlarmLogic.Any
                : AlarmLogic.All;
        }
        GUILayout.Label("Stufe", m_labelStyle, GUILayout.Width(52f));
        if (GUILayout.Button(
                SeverityLabel(m_draftSeverity),
                m_buttonStyle,
                GUILayout.Width(125f)))
        {
            m_draftSeverity = NextEnum(m_draftSeverity);
            m_draftColor = DefaultColorFor(m_draftSeverity);
        }
        GUILayout.Label("Farbe", m_labelStyle, GUILayout.Width(50f));
        m_draftColor = GUILayout.TextField(
            m_draftColor,
            9,
            m_textFieldStyle,
            GUILayout.Width(92f));
        GUILayout.EndHorizontal();

        var sounds = m_audio.GetSoundOptions();
        if (sounds.Count > 0)
        {
            m_draftSoundIndex = Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ton", m_labelStyle, GUILayout.Width(90f));
            if (GUILayout.Button("◀", m_buttonStyle, GUILayout.Width(34f)))
            {
                m_draftSoundIndex = Wrap(
                    m_draftSoundIndex - 1,
                    sounds.Count);
            }
            GUILayout.Label(
                sounds[m_draftSoundIndex].Label,
                m_labelStyle,
                GUILayout.Width(320f));
            if (GUILayout.Button("▶", m_buttonStyle, GUILayout.Width(34f)))
            {
                m_draftSoundIndex = Wrap(
                    m_draftSoundIndex + 1,
                    sounds.Count);
            }
            if (GUILayout.Button("TON TESTEN", m_buttonStyle, GUILayout.Width(125f)))
            {
                TestSound(sounds[m_draftSoundIndex].Id, m_draftSeverity);
            }
            if (GUILayout.Button("TON STOP", m_buttonStyle, GUILayout.Width(105f)))
            {
                StopTestSound();
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Meldetext", m_labelStyle, GUILayout.Width(90f));
        m_draftRuleName = GUILayout.TextField(
            m_draftRuleName,
            80,
            m_textFieldStyle);
        GUI.enabled = m_draftConditions.Count > 0 && CurrentPanel != null;
        if (GUILayout.Button(
                "MELDUNG SPEICHERN",
                m_primaryButtonStyle,
                GUILayout.Width(190f),
                GUILayout.Height(30f)))
        {
            SaveDraftRule(sounds);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("DEFINIERTE MELDUNGEN", m_sectionStyle);
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
                (rule.Logic == AlarmLogic.All ? "UND" : "ODER"),
                m_labelStyle);
            if (GUILayout.Button(
                    "LÖSCHEN",
                    m_dangerButtonStyle,
                    GUILayout.Width(90f)))
            {
                if (m_runtime.RemoveRule(rule.Id))
                {
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

        DrawStatusMessage();
        GUILayout.EndScrollView();
    }

    private void DrawSoundOverrides()
    {
        GUILayout.Label("TÖNE FÜR VANILLA- UND SYSTEMMELDUNGEN", m_sectionStyle);
        GUILayout.Label(
            "Eigene Regeln wählen ihren Ton im Editor. Hier kann jede bereits bekannte Vanilla- oder Systemmeldung separat auf Automatik, lautlos, einen Oszillator oder eine eigene WAV-/OGG-Datei gelegt werden.",
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
                "Noch keine passende Meldung bekannt. Vanilla-Meldungen erscheinen hier, sobald das Spiel sie einmal erzeugt hat.",
                m_labelStyle);
        }

        foreach (var candidate in candidates)
        {
            var configured = m_runtime.GetConfiguredSound(
                candidate.OverrideId);
            var soundIndex = FindSoundIndex(sounds, configured);

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
            "Mitgeliefert: Klingel, Industriehorn, E51-artige Auf-/Ab-Sirene sowie Sinus-, Rechteck-, Sägezahn-, Dreieck- und Impulston. Alle werden mathematisch erzeugt und enthalten keine Samples Dritter.",
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
            "Gesundheit, Nahrung und Arbeiter eskalieren automatisch: Warnung → Klingel, kritisch → Horn, Notfall → Sirene. Schwellen, Lautstärke, Startzustand und Prüfintervall stehen in den normalen Mod-Einstellungen.",
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label("ZWEITER BILDSCHIRM", m_sectionStyle);
        GUILayout.Label(
            "Mehrere abgekoppelte Meldetafeln sind unterstützt, bleiben aber innerhalb des Captain-of-Industry-Fensters. Ein echtes zweites Betriebssystemfenster stellt die Mod-API nicht bereit; ein externer Companion-Prozess wäre ein separates, experimentelles Phase-2-Projekt.",
            m_labelStyle);

        GUILayout.Space(10f);
        GUILayout.Label("ZUSTANDSMODELL", m_sectionStyle);
        GUILayout.Label(
            "NORMAL: hellgrau, schwarze Schrift. KOMMT: Aktivfarbe blinkt und der Ton wiederholt sich. MASTER QUIT: Aktivfarbe bleibt stehen, Ton endet. Nach Rückkehr zu NORMAL löst eine neue Aktivierungsflanke erneut aus.",
            m_labelStyle);
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

        DrawWindowHeader("UNMA · " + panel.Name);
        GUILayout.BeginArea(new Rect(
            10f,
            40f,
            detached.Rect.width - 20f,
            detached.Rect.height - 50f));
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            "AKTIV " + m_runtime.ActiveCount +
            " · UNQUITTIERT " + m_runtime.UnacknowledgedCount,
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
            GetPanelViews(panel),
            Math.Max(1, Math.Min(panel.Columns, 5)),
            detached.Rect.width - 38f,
            detached.Scroll.y,
            Math.Max(180f, detached.Rect.height - 100f));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.DragWindow(new Rect(0f, 0f, detached.Rect.width - 38f, 36f));
    }

    private void DrawAlarmGrid(
        IReadOnlyList<AlarmView> alarms,
        int columns,
        float availableWidth,
        float scrollY,
        float viewportHeight)
    {
        columns = Math.Max(1, Math.Min(8, columns));
        if (alarms.Count == 0)
        {
            GUILayout.Space(20f);
            GUILayout.Label(
                "Keine Meldeschlitze in diesem Panel.",
                m_labelStyle);
            return;
        }

        var tileWidth = Math.Max(140f, (availableWidth -
            (columns - 1) * 6f) / columns);
        var rowHeight = TileHeight + 12f;
        var rowCount = (alarms.Count + columns - 1) / columns;
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
            for (var column = 0; column < columns; column++)
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
                }
                else
                {
                    DrawEmptyTile(rect);
                }
                if (column < columns - 1)
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

    private void DrawAlarmTile(Rect rect, AlarmView alarm)
    {
        var background = new Color(0.83f, 0.84f, 0.82f, 1f);
        if (alarm.IsActive)
        {
            var active = ParseColor(alarm.ActiveColor, Color.yellow);
            var blinkOn = alarm.IsAcknowledged ||
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

        var badge = alarm.IsActive
            ? alarm.IsAcknowledged ? "STEHT" : "KOMMT"
            : alarm.IsMissingSource ? "QUELLE FEHLT" : "NORMAL";
        if (alarm.IsActive && alarm.IsMissingSource)
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

    private void DrawPanelRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void CaptureSelectedEntity()
    {
        var entity = m_inspectorsManager.GetFirstActiveEntityOrNull();
        if (entity == null)
        {
            SetStatus(
                "Keine gültige Entität gefunden. Erst Gebäude, Fahrzeug oder Transport anklicken.");
            return;
        }

        m_selectedEntity = null;
        m_selectedMetrics = Array.Empty<MetricDescriptor>();
        m_selectedMetricIndex = 0;
        m_runtime.RequestEntityInspection(entity.Id.Value);
        SetStatus("Spielauswahl wird sicher im Simulations-Takt gelesen …");
    }

    private void ApplyCompletedInspection(EntityInspectionSnapshot inspection)
    {
        if (!string.IsNullOrWhiteSpace(inspection.Error))
        {
            m_selectedEntity = null;
            m_selectedMetrics = Array.Empty<MetricDescriptor>();
            SetStatus(inspection.Error);
            return;
        }

        m_selectedEntity = inspection;
        m_selectedMetrics = inspection.Metrics;
        m_selectedMetricIndex = 0;
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
            ExpectedProductId = metric.Path.StartsWith(
                "$stored.",
                StringComparison.Ordinal)
                ? m_selectedEntity.StoredProductId
                : "",
        });
        SetStatus("Bedingung zur Sammelmeldung hinzugefügt.");
    }

    private void SaveDraftRule(IReadOnlyList<SoundOption> sounds)
    {
        var panel = CurrentPanel;
        if (panel == null || m_draftConditions.Count == 0)
        {
            return;
        }

        var soundId = sounds.Count > 0
            ? sounds[Math.Max(
                0,
                Math.Min(m_draftSoundIndex, sounds.Count - 1))].Id
            : "auto";
        var rule = new AlarmRuleDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            PanelId = panel.Id,
            Name = string.IsNullOrWhiteSpace(m_draftRuleName)
                ? "MELDUNG"
                : m_draftRuleName.Trim(),
            Severity = m_draftSeverity,
            Logic = m_draftLogic,
            ActiveColor = NormalizeColor(m_draftColor),
            SoundId = soundId,
            Enabled = true,
            Conditions = m_draftConditions.Select(CloneCondition).ToList(),
        };
        if (!m_runtime.AddRule(rule))
        {
            SetStatus(
                "Speichern fehlgeschlagen: " +
                m_runtime.LastPersistenceError);
            return;
        }
        m_draftConditions.Clear();
        m_draftRuleName = "NEUE MELDUNG";
        SetStatus("Meldung gespeichert; Überwachung startet im nächsten Takt.");
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
        };
        if (!m_runtime.AddPanel(panel))
        {
            SetStatus(
                "Panel konnte nicht gespeichert werden: " +
                m_runtime.LastPersistenceError);
            return;
        }
        m_currentPanelIndex = m_runtime.Configuration.Panels.Count - 1;
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

        var panelId = CurrentPanel.Id;
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
        if (GUILayout.Button(
                label,
                m_tab == tab ? m_primaryButtonStyle : m_buttonStyle,
                GUILayout.Width(165f),
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

    private void HandleResize()
    {
        var handle = new Rect(
            m_windowRect.width - 24f,
            m_windowRect.height - 24f,
            20f,
            20f);
        GUI.Label(handle, "◢", m_labelStyle);
        var currentEvent = UnityEngine.Event.current;
        if (currentEvent.type == EventType.MouseDown &&
            handle.Contains(currentEvent.mousePosition))
        {
            m_isResizing = true;
            m_resizeStartMouse = GUIUtility.GUIToScreenPoint(
                currentEvent.mousePosition);
            m_resizeStartSize = new Vector2(
                m_windowRect.width,
                m_windowRect.height);
            currentEvent.Use();
        }
        else if (m_isResizing && currentEvent.type == EventType.MouseDrag)
        {
            var current = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            var delta = current - m_resizeStartMouse;
            m_pendingMainWindowSize = new Vector2(
                Mathf.Clamp(
                    m_resizeStartSize.x + delta.x,
                    700f,
                    Math.Max(700f, Screen.width - 12f)),
                Mathf.Clamp(
                    m_resizeStartSize.y + delta.y,
                    520f,
                    Math.Max(520f, Screen.height - 12f)));
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseUp)
        {
            m_isResizing = false;
        }
    }

    private void PersistWindowRectOnMouseUp()
    {
        if (UnityEngine.Event.current.type != EventType.MouseUp)
        {
            return;
        }
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

        m_windowStyle = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(8, 8, 8, 8),
            normal =
            {
                background = SolidTexture(
                    "window",
                    new Color(0.075f, 0.085f, 0.085f, 0.98f)),
            },
        };
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
        return double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out value) ||
               double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value);
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
        rect.x = Mathf.Clamp(rect.x, 0f, Math.Max(0f, Screen.width - 80f));
        rect.y = Mathf.Clamp(rect.y, 0f, Math.Max(0f, Screen.height - 44f));
        return rect;
    }

    private void OnDestroy()
    {
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
