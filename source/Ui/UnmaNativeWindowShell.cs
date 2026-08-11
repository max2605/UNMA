using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using UNMA.Localization;
using NativeButton = Mafi.Unity.UiToolkit.Library.Button;
using NativeColumn = Mafi.Unity.UiToolkit.Library.Column;
using ResizeLabel = UnityEngine.UIElements.Label;

namespace UNMA.Ui;

/// <summary>
/// Provides a native Captain of Industry window whose complete body lives in
/// the same runtime UI Toolkit hierarchy as the frame, so COI owns one
/// coherent clipping, input and stacking surface for frame and content.
/// </summary>
internal sealed class UnmaNativeWindowShell : IDisposable
{
    private sealed class UnmaWindow : Window
    {
        private readonly ResizeLabel m_resizeHandle;
        private Action<Vector2> m_resizeDelta;
        private Action m_resizeCompleted;
        private readonly Action<bool> m_onActivated;
        private readonly Func<Vector2, bool> m_isBodyPoint;
        private Vector2 m_resizeStart;
        private int m_resizePointerId = -1;

        public UnmaWindow(
            LocStrFormatted title,
            Action<bool> onActivated,
            Func<Vector2, bool> isBodyPoint)
            : base(title, false)
        {
            m_onActivated = onActivated;
            m_isBodyPoint = isBodyPoint;
            Frame.RootElement.RegisterCallback<PointerDownEvent>(
                HandleFramePointerDown,
                TrickleDown.TrickleDown);
            m_resizeHandle = new ResizeLabel("◢")
            {
                name = "UNMA.NativeResizeHandle",
                tooltip = UnmaText.Get(
                    "native.resize",
                    "FENSTERGRÖSSE ZIEHEN"),
                pickingMode = PickingMode.Position,
            };
            m_resizeHandle.style.position = Position.Absolute;
            m_resizeHandle.style.right = 8f;
            m_resizeHandle.style.bottom = 8f;
            m_resizeHandle.style.width = 28f;
            m_resizeHandle.style.height = 28f;
            m_resizeHandle.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_resizeHandle.style.fontSize = 16f;
            m_resizeHandle.style.color = new StyleColor(
                new Color32(220, 226, 232, 255));
            m_resizeHandle.style.backgroundColor =
                new StyleColor(new Color32(62, 65, 72, 245));
            m_resizeHandle.style.borderTopWidth = 1f;
            m_resizeHandle.style.borderRightWidth = 1f;
            m_resizeHandle.style.borderBottomWidth = 1f;
            m_resizeHandle.style.borderLeftWidth = 1f;
            var border = new Color32(118, 119, 121, 255);
            m_resizeHandle.style.borderTopColor = new StyleColor(border);
            m_resizeHandle.style.borderRightColor = new StyleColor(border);
            m_resizeHandle.style.borderBottomColor = new StyleColor(border);
            m_resizeHandle.style.borderLeftColor = new StyleColor(border);
            m_resizeHandle.RegisterCallback<PointerDownEvent>(
                HandleResizePointerDown);
            m_resizeHandle.RegisterCallback<PointerMoveEvent>(
                HandleResizePointerMove);
            m_resizeHandle.RegisterCallback<PointerUpEvent>(
                HandleResizePointerUp);
            m_resizeHandle.RegisterCallback<PointerCaptureOutEvent>(
                HandleResizeCaptureOut);
            Frame.RootElement.Add(m_resizeHandle);
        }

        public bool ContainsInteractivePoint(Vector2 panelPoint)
        {
            // Window.WorldBound belongs to COI's full-screen mask. Only the
            // visible frame must own pointer input; otherwise an open UNMA
            // window would block building selection and camera movement on
            // the entire map.
            return Frame.WorldBound.Contains(panelPoint);
        }

        public Vector2 FrameWorldPosition =>
            Frame.WorldBound.position;

        public void RestoreFrameWorldPosition(Vector2 worldPosition)
        {
            var currentWorldPosition = Frame.WorldBound.position;
            var currentTranslation =
                Frame.RootElement.resolvedStyle.translate;
            this.TranslateFrame(
                new Px(
                    currentTranslation.x +
                    worldPosition.x -
                    currentWorldPosition.x),
                new Px(
                    currentTranslation.y +
                    worldPosition.y -
                    currentWorldPosition.y));
        }

        public void ScheduleFrameWorldPositionRestore(
            Vector2 worldPosition)
        {
            Frame.RootElement.schedule
                .Execute(() => RestoreFrameWorldPosition(worldPosition))
                .StartingIn(16);
        }

        public void ConfigureResize(
            Action<Vector2> resizeDelta,
            Action resizeCompleted)
        {
            m_resizeDelta = resizeDelta;
            m_resizeCompleted = resizeCompleted;
            m_resizeHandle.BringToFront();
        }

        private void HandleResizePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || m_resizePointerId >= 0)
            {
                return;
            }

            m_resizePointerId = evt.pointerId;
            m_resizeStart = (Vector2)evt.position;
            m_resizeHandle.CapturePointer(evt.pointerId);
            evt.StopImmediatePropagation();
        }

        private void HandleFramePointerDown(PointerDownEvent evt)
        {
            RootElement.BringToFront();
            var panelPoint = (Vector2)evt.position;
            var isNativeBodyPoint =
                m_isBodyPoint?.Invoke(panelPoint) == true &&
                !m_resizeHandle.worldBound.Contains(panelPoint);
            m_onActivated?.Invoke(isNativeBodyPoint);
        }

        private void HandleResizePointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != m_resizePointerId ||
                !m_resizeHandle.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            m_resizeDelta?.Invoke((Vector2)evt.position - m_resizeStart);
            evt.StopImmediatePropagation();
        }

        private void HandleResizePointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != m_resizePointerId)
            {
                return;
            }

            if (m_resizeHandle.HasPointerCapture(evt.pointerId))
            {
                m_resizeHandle.ReleasePointer(evt.pointerId);
            }
            CompleteResize();
            evt.StopImmediatePropagation();
        }

        private void HandleResizeCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == m_resizePointerId)
            {
                CompleteResize();
            }
        }

        private void CompleteResize()
        {
            if (m_resizePointerId < 0)
            {
                return;
            }

            m_resizePointerId = -1;
            m_resizeCompleted?.Invoke();
        }
    }

    private const float MinimumWindowWidth = 700f;
    private const float MinimumWindowHeight = 520f;
    private const float HorizontalBodyInset = 44f;
    private const float VerticalChromeInset = 138f;
    private const float MinimumBodyWidth =
        MinimumWindowWidth - HorizontalBodyInset;
    private const float MinimumBodyHeight =
        MinimumWindowHeight - VerticalChromeInset;

    private readonly UiRoot m_uiRoot;
    private readonly Func<int> m_selectedTab;
    private readonly Action<int> m_selectTab;
    private readonly Action m_onMinimized;
    private readonly Action<float, float> m_onResized;
    private readonly UnmaWindow m_window;
    private readonly NativeUiSurface m_bodySurface;
    private readonly VisualElement m_bodyElement;
    private readonly VisualElement m_rootElement;
    private readonly VisualElement m_navigationElement;
    private readonly List<(ButtonText Button, float BaseWidth)>
        m_navigationButtons = new();

    private bool m_disposed;
    private bool m_suppressed;
    private float m_windowWidth;
    private float m_windowHeight;
    private float m_contentScale = 1f;
    private float m_resizeStartWidth;
    private float m_resizeStartHeight;
    private bool m_temporarilyFullscreen;
    private Vector2 m_preFullscreenFrameWorldPosition;
    private bool m_preFullscreenFrameWorldPositionValid;

    public UnmaNativeWindowShell(
        UiRoot uiRoot,
        float requestedWidth,
        float requestedHeight,
        Func<int> selectedTab,
        Action<int> selectTab,
        Action onMinimized,
        Action<float, float> onResized,
        Action<bool> onActivated)
    {
        m_uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        m_selectedTab = selectedTab ??
                        throw new ArgumentNullException(nameof(selectedTab));
        m_selectTab = selectTab ??
                      throw new ArgumentNullException(nameof(selectTab));
        m_onMinimized = onMinimized ??
                        throw new ArgumentNullException(nameof(onMinimized));
        m_onResized = onResized ??
                      throw new ArgumentNullException(nameof(onResized));

        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        var logicalWidth = Screen.width / rootScale;
        var logicalHeight = Screen.height / rootScale;
        var viewportWidth = Mathf.Max(
            HorizontalBodyInset + 1f,
            Mathf.Min(logicalWidth - 32f, logicalWidth * 0.96f));
        var viewportHeight = Mathf.Max(
            VerticalChromeInset + 1f,
            Mathf.Min(logicalHeight - 48f, logicalHeight * 0.96f));
        var windowWidth = ClampRequestedDimension(
            requestedWidth,
            Mathf.Min(GetMinimumWindowWidth(), viewportWidth),
            viewportWidth);
        var windowHeight = ClampRequestedDimension(
            requestedHeight,
            Mathf.Min(GetMinimumWindowHeight(), viewportHeight),
            viewportHeight);
        m_windowWidth = windowWidth;
        m_windowHeight = windowHeight;
        var bodyWidth = windowWidth - HorizontalBodyInset;
        var bodyHeight = windowHeight - VerticalChromeInset;

        m_bodySurface = new NativeUiSurface("UNMA.NativeBody");
        m_bodyElement = m_bodySurface.RootElement;
        m_bodyElement.style.width = bodyWidth;
        m_bodyElement.style.height = bodyHeight;
        m_bodyElement.style.flexGrow = 1f;
        m_bodyElement.style.flexShrink = 1f;
        m_bodyElement.style.overflow = Overflow.Hidden;

        var body = new UiComponent(m_bodyElement)
            .Width(new Px(bodyWidth))
            .Height(new Px(bodyHeight))
            .MinWidth(new Px(Mathf.Min(
                MinimumBodyWidth * m_contentScale,
                bodyWidth)))
            .MinHeight(new Px(Mathf.Min(
                MinimumBodyHeight * m_contentScale,
                bodyHeight)))
            .FlexGrow(1f)
            .OverflowHidden();

        var navigation = BuildNavigation();
        var root = new NativeColumn(8)
            .Width(new Px(windowWidth - 28f))
            .Height(new Px(windowHeight - 82f))
            .FlexGrow(1f);
        root.Add(navigation);
        root.Add(body);
        m_rootElement = root.RootElement;
        m_navigationElement = navigation.RootElement;

        m_window = new UnmaWindow(
            new LocStrFormatted(UnmaText.Get(
                "window.title",
                "UNMA · UNIVERSAL ALARM ANNUNCIATOR")),
            onActivated,
            panelPoint => m_bodyElement.worldBound.Contains(panelPoint));
        m_window
            .WindowSize(new Px(windowWidth), new Px(windowHeight))
            .MakeMovableAndEnablePositionSaving();
        m_window.EnablePinning();
        m_window.AddBodySingle(root);
        m_window.OnCloseStart += HandleWindowClose;
        m_window.ConfigureResize(HandleResizeDelta, HandleResizeCompleted);
    }

    public bool IsOpen => !m_disposed && m_window.IsOpen;

    public bool IsBodyKeyboardCaptured =>
        !m_disposed &&
        !m_suppressed &&
        m_window.IsOpen &&
        m_bodySurface.HasTextInputFocus;

    public void ClearBodyFocus()
    {
        if (!m_disposed)
        {
            m_bodySurface.ClearFocus();
        }
    }

    /// <summary>
    /// Current logical window dimensions. Callers can retain this value before
    /// entering a temporary fullscreen recorder view and restore it later.
    /// </summary>
    public Vector2 CurrentSize => new(m_windowWidth, m_windowHeight);

    /// <summary>
    /// Keeps the same usable logical body at every UNMA content scale. The
    /// physical minimum is still capped by the current COI viewport.
    /// </summary>
    public void SetContentScale(float scale)
    {
        if (m_disposed)
        {
            return;
        }

        var normalized = NormalizeContentScale(scale);
        if (Mathf.Approximately(m_contentScale, normalized))
        {
            return;
        }

        m_contentScale = normalized;
        ApplyWindowSize(m_windowWidth, m_windowHeight);
    }

    /// <summary>
    /// Applies a transient size without invoking the persistence callback.
    /// The regular resize handle remains the only path that stores a size.
    /// </summary>
    public void SetTemporarySize(Vector2 size)
    {
        if (m_disposed)
        {
            return;
        }

        if (m_temporarilyFullscreen)
        {
            m_window.Fullscreen(false);
            m_temporarilyFullscreen = false;
        }
        ApplyWindowSize(size.x, size.y);
        if (m_preFullscreenFrameWorldPositionValid)
        {
            m_window.ScheduleFrameWorldPositionRestore(
                m_preFullscreenFrameWorldPosition);
            m_preFullscreenFrameWorldPositionValid = false;
        }
    }

    /// <summary>
    /// Expands the shell to the maximum size allowed by the current viewport
    /// without persisting the result. Returns the previous size for restoration
    /// through <see cref="SetTemporarySize(Vector2)"/>.
    /// </summary>
    public Vector2 MaximizeTemporarily()
    {
        var previousSize = CurrentSize;
        if (!m_disposed)
        {
            m_preFullscreenFrameWorldPosition =
                m_window.FrameWorldPosition;
            m_preFullscreenFrameWorldPositionValid = true;
            m_temporarilyFullscreen = true;
            m_window.Fullscreen();
            ApplyFullscreenContentSize();
        }
        return previousSize;
    }

    public void Open()
    {
        RefreshViewportConstraints();
        if (!m_disposed && !m_window.IsOpen)
        {
            m_window.Open(m_uiRoot);
            m_window.SetVisible(!m_suppressed);
        }
    }

    public void Close()
    {
        if (!m_disposed && m_window.IsOpen)
        {
            m_window.Close();
        }
    }

    public void RenderBody(Action drawBody, float scale)
    {
        if (!m_disposed && !m_suppressed && m_window.IsOpen)
        {
            SetContentScale(scale);
            m_bodySurface.Render(drawBody, scale);
        }
    }

    public bool ContainsPointer(Vector2 screenPointTopLeft)
    {
        if (!IsOpen || m_suppressed)
        {
            return false;
        }

        var panel = m_window.RootElement.panel;
        if (panel == null)
        {
            return false;
        }

        var panelPoint = RuntimePanelUtils.ScreenToPanel(
            panel,
            screenPointTopLeft);
        return m_window.ContainsInteractivePoint(panelPoint);
    }

    public void SetSuppressed(bool suppressed)
    {
        if (m_disposed || m_suppressed == suppressed)
        {
            return;
        }

        m_suppressed = suppressed;
        if (m_window.IsOpen)
        {
            m_window.SetVisible(!suppressed);
        }
    }

    public void RefreshViewportConstraints()
    {
        if (!m_disposed)
        {
            ApplyWindowSize(m_windowWidth, m_windowHeight);
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        m_bodySurface.Dispose();
        m_window.OnCloseStart -= HandleWindowClose;
        m_window.RemoveFromHierarchy();
    }

    private Row BuildNavigation()
    {
        var row = new Row(6)
            .Width(new Px(Mathf.Max(
                1f,
                m_windowWidth - HorizontalBodyInset)))
            .NoShrink();

        row.Add(CreateTabButton(
            0,
            UnmaText.Get("tab.board", "MELDETAFEL"),
            112));
        row.Add(CreateTabButton(
            5,
            UnmaText.Get("tab.instruments", "MESSPULT"),
            112));
        row.Add(CreateTabButton(
            1,
            UnmaText.Get("tab.history", "VERLAUF"),
            102));
        row.Add(CreateTabButton(
            2,
            UnmaText.Get("tab.system", "SYSTEM"),
            102));
        row.Add(CreateTabButton(
            3,
            UnmaText.Get(
                "tab.notification_options",
                "NOTIFICATION OPTIONS"),
            188));
        row.Add(CreateTabButton(
            4,
            UnmaText.Get("tab.options", "OPTIONEN"),
            108));

        var minimize = new ButtonText(
                NativeButton.General,
                new LocStrFormatted(UnmaText.Get(
                    "native.minimize",
                    "MINIMIEREN")),
                m_onMinimized)
            .NoShrink();
        m_navigationButtons.Add((minimize, 118f));
        UpdateNavigationButtonWidths(
            m_windowWidth - HorizontalBodyInset);
        row.Add(minimize);
        return row;
    }

    private ButtonText CreateTabButton(
        int tab,
        string label,
        int minimumWidth)
    {
        var button = new ButtonText(
                NativeButton.General,
                new LocStrFormatted(label),
                () => m_selectTab(tab))
            .Toggleable()
            .ObserveSelected(() => m_selectedTab() == tab)
            .FlexGrow(1f);
        m_navigationButtons.Add((button, minimumWidth));
        return button;
    }

    private void HandleWindowClose(Window _)
    {
        if (!m_disposed)
        {
            m_onMinimized();
        }
    }

    private void HandleResizeDelta(Vector2 delta)
    {
        if (m_disposed || m_temporarilyFullscreen)
        {
            return;
        }

        if (Mathf.Approximately(m_resizeStartWidth, 0f) ||
            Mathf.Approximately(m_resizeStartHeight, 0f))
        {
            m_resizeStartWidth = m_windowWidth;
            m_resizeStartHeight = m_windowHeight;
        }

        ApplyWindowSize(
            m_resizeStartWidth + delta.x,
            m_resizeStartHeight + delta.y);
    }

    private void HandleResizeCompleted()
    {
        if (m_disposed)
        {
            return;
        }

        m_resizeStartWidth = 0f;
        m_resizeStartHeight = 0f;
        m_onResized(m_windowWidth, m_windowHeight);
    }

    private void ApplyWindowSize(float requestedWidth, float requestedHeight)
    {
        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        var logicalWidth = Screen.width / rootScale;
        var logicalHeight = Screen.height / rootScale;
        var viewportWidth = Mathf.Max(
            HorizontalBodyInset + 1f,
            Mathf.Min(logicalWidth - 32f, logicalWidth * 0.96f));
        var viewportHeight = Mathf.Max(
            VerticalChromeInset + 1f,
            Mathf.Min(logicalHeight - 48f, logicalHeight * 0.96f));
        var nextWidth = ClampRequestedDimension(
            requestedWidth,
            Mathf.Min(GetMinimumWindowWidth(), viewportWidth),
            viewportWidth);
        var nextHeight = ClampRequestedDimension(
            requestedHeight,
            Mathf.Min(GetMinimumWindowHeight(), viewportHeight),
            viewportHeight);
        if (Mathf.Approximately(m_windowWidth, nextWidth) &&
            Mathf.Approximately(m_windowHeight, nextHeight))
        {
            return;
        }
        m_windowWidth = nextWidth;
        m_windowHeight = nextHeight;

        var bodyWidth = m_windowWidth - HorizontalBodyInset;
        var bodyHeight = m_windowHeight - VerticalChromeInset;
        m_window.WindowSize(
            new Px(m_windowWidth),
            new Px(m_windowHeight));
        m_rootElement.style.width = m_windowWidth - 28f;
        m_rootElement.style.height = m_windowHeight - 82f;
        m_navigationElement.style.width = bodyWidth;
        m_navigationElement.style.minWidth = Mathf.Max(1f, bodyWidth);
        m_bodyElement.style.width = bodyWidth;
        m_bodyElement.style.height = bodyHeight;
        m_bodyElement.style.minWidth = Mathf.Min(
            MinimumBodyWidth * m_contentScale,
            bodyWidth);
        m_bodyElement.style.minHeight = Mathf.Min(
            MinimumBodyHeight * m_contentScale,
            bodyHeight);
        UpdateNavigationButtonWidths(bodyWidth);
    }

    private void ApplyFullscreenContentSize()
    {
        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        m_windowWidth = Mathf.Max(
            HorizontalBodyInset + 1f,
            Screen.width / rootScale);
        m_windowHeight = Mathf.Max(
            VerticalChromeInset + 1f,
            Screen.height / rootScale);

        var bodyWidth = m_windowWidth - HorizontalBodyInset;
        var bodyHeight = m_windowHeight - VerticalChromeInset;
        m_rootElement.style.width = m_windowWidth - 28f;
        m_rootElement.style.height = m_windowHeight - 82f;
        m_navigationElement.style.width = bodyWidth;
        m_navigationElement.style.minWidth = Mathf.Max(1f, bodyWidth);
        m_bodyElement.style.width = bodyWidth;
        m_bodyElement.style.height = bodyHeight;
        m_bodyElement.style.minWidth = Mathf.Min(
            MinimumBodyWidth * m_contentScale,
            bodyWidth);
        m_bodyElement.style.minHeight = Mathf.Min(
            MinimumBodyHeight * m_contentScale,
            bodyHeight);
        UpdateNavigationButtonWidths(bodyWidth);
    }

    private float GetMinimumWindowWidth()
    {
        return HorizontalBodyInset + MinimumBodyWidth * m_contentScale;
    }

    private float GetMinimumWindowHeight()
    {
        return VerticalChromeInset + MinimumBodyHeight * m_contentScale;
    }

    private static float NormalizeContentScale(float scale)
    {
        return Mathf.Clamp(IsFinitePositive(scale) ? scale : 1f, 0.75f, 2f);
    }

    private void UpdateNavigationButtonWidths(float availableWidth)
    {
        var scale = Mathf.Clamp(
            (availableWidth - 36f) / 842f,
            0.25f,
            1f);
        foreach (var entry in m_navigationButtons)
        {
            entry.Button.MinWidth(new Px(Mathf.Max(
                1f,
                entry.BaseWidth * scale)));
        }
    }

    private static float ClampRequestedDimension(
        float requested,
        float minimum,
        float maximum)
    {
        return Mathf.Clamp(
            IsFinitePositive(requested) ? requested : minimum,
            minimum,
            maximum);
    }

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value) &&
               value > 0f;
    }
}
