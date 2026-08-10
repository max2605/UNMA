using System;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using UNMA.Localization;
using NativeColumn = Mafi.Unity.UiToolkit.Library.Column;
using NativeWindow = Mafi.Unity.UiToolkit.Library.Window;
using ResizeLabel = UnityEngine.UIElements.Label;

namespace UNMA.Ui;

/// <summary>
/// Hosts the UNMA alarm and panel editor in a native Captain of Industry
/// runtime UI Toolkit window so frame and content share clipping, input and
/// stacking.
/// </summary>
internal sealed class UnmaNativeEditorShell : IDisposable
{
    private sealed class EditorWindow : NativeWindow
    {
        private readonly ResizeLabel m_resizeHandle;
        private Action<Vector2> m_resizeDelta;
        private Action m_resizeCompleted;
        private readonly Action<bool> m_onActivated;
        private readonly Func<Vector2, bool> m_isBodyPoint;
        private Vector2 m_resizeStart;
        private int m_resizePointerId = -1;

        public EditorWindow(
            LocStrFormatted title,
            Action requestClose,
            Action<bool> onActivated,
            Func<Vector2, bool> isBodyPoint)
            : base(title, false)
        {
            m_onActivated = onActivated;
            m_isBodyPoint = isBodyPoint;
            CloseButton.OnClick(requestClose);
            Frame.RootElement.RegisterCallback<PointerDownEvent>(
                HandleFramePointerDown,
                TrickleDown.TrickleDown);
            m_resizeHandle = new ResizeLabel("\u25E2")
            {
                name = "UNMA.NativeEditorResizeHandle",
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
            // Window.WorldBound is COI's full-screen input mask. Frame is the
            // exact visible metal window and therefore the only valid hit
            // target for UNMA's legacy input blocker.
            return Frame.WorldBound.Contains(panelPoint);
        }

        public void ConfigureResize(
            Action<Vector2> resizeDelta,
            Action resizeCompleted)
        {
            m_resizeDelta = resizeDelta;
            m_resizeCompleted = resizeCompleted;
            m_resizeHandle.BringToFront();
        }

        public void DisposeResizeHandle()
        {
            m_resizeDelta = null;
            m_resizeCompleted = null;
            if (m_resizePointerId >= 0 &&
                m_resizeHandle.HasPointerCapture(m_resizePointerId))
            {
                m_resizeHandle.ReleasePointer(m_resizePointerId);
            }
            m_resizePointerId = -1;
            m_resizeHandle.UnregisterCallback<PointerDownEvent>(
                HandleResizePointerDown);
            m_resizeHandle.UnregisterCallback<PointerMoveEvent>(
                HandleResizePointerMove);
            m_resizeHandle.UnregisterCallback<PointerUpEvent>(
                HandleResizePointerUp);
            m_resizeHandle.UnregisterCallback<PointerCaptureOutEvent>(
                HandleResizeCaptureOut);
            m_resizeHandle.RemoveFromHierarchy();
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
    private const float VerticalBodyInset = 82f;
    private const float HorizontalRootInset = 28f;
    private const float VerticalRootInset = 82f;
    private const float ViewportHorizontalMargin = 32f;
    private const float ViewportVerticalMargin = 48f;
    private static string DefaultTitle => UnmaText.Get(
        "native.editor.default_title",
        "UNMA - ALARM EDITOR");

    private readonly UiRoot m_uiRoot;
    private readonly Action m_onClose;
    private readonly Action<float, float> m_onResized;
    private readonly EditorWindow m_window;
    private readonly NativeUiSurface m_bodySurface;
    private readonly VisualElement m_bodyElement;
    private readonly VisualElement m_rootElement;

    private bool m_disposed;
    private bool m_suppressed;
    private float m_windowWidth;
    private float m_windowHeight;
    private float m_resizeStartWidth;
    private float m_resizeStartHeight;
    private string m_currentTitle;

    public UnmaNativeEditorShell(
        UiRoot uiRoot,
        float requestedWidth,
        float requestedHeight,
        string initialTitle,
        Action onClose,
        Action<float, float> onResized,
        Action<bool> onActivated)
    {
        m_uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        m_onClose = onClose ?? throw new ArgumentNullException(nameof(onClose));
        m_onResized = onResized ??
                      throw new ArgumentNullException(nameof(onResized));

        var viewportSize = GetMaximumViewportSize();
        m_windowWidth = ClampRequestedDimension(
            requestedWidth,
            Mathf.Min(MinimumWindowWidth, viewportSize.x),
            viewportSize.x);
        m_windowHeight = ClampRequestedDimension(
            requestedHeight,
            Mathf.Min(MinimumWindowHeight, viewportSize.y),
            viewportSize.y);

        var bodyWidth = GetBodyWidth(m_windowWidth);
        var bodyHeight = GetBodyHeight(m_windowHeight);
        m_bodySurface = new NativeUiSurface("UNMA.NativeEditorBody");
        m_bodyElement = m_bodySurface.RootElement;
        m_bodyElement.style.width = bodyWidth;
        m_bodyElement.style.height = bodyHeight;
        m_bodyElement.style.flexGrow = 1f;
        m_bodyElement.style.flexShrink = 1f;
        m_bodyElement.style.overflow = Overflow.Hidden;
        m_bodyElement.style.backgroundColor =
            new StyleColor(Color.clear);

        var body = new UiComponent(m_bodyElement)
            .Width(new Px(bodyWidth))
            .Height(new Px(bodyHeight))
            .MinWidth(new Px(Mathf.Min(
                MinimumWindowWidth - HorizontalBodyInset,
                bodyWidth)))
            .MinHeight(new Px(Mathf.Min(
                MinimumWindowHeight - VerticalBodyInset,
                bodyHeight)))
            .FlexGrow(1f)
            .OverflowHidden();

        var root = new NativeColumn()
            .Width(new Px(m_windowWidth - HorizontalRootInset))
            .Height(new Px(m_windowHeight - VerticalRootInset))
            .FlexGrow(1f);
        root.Add(body);
        m_rootElement = root.RootElement;

        m_currentTitle = NormalizeTitle(initialTitle);
        m_window = new EditorWindow(
            ToTitle(m_currentTitle),
            m_onClose,
            onActivated,
            panelPoint => m_bodyElement.worldBound.Contains(panelPoint));
        m_window
            .WindowSize(
                new Px(m_windowWidth),
                new Px(m_windowHeight))
            .MakeMovableAndEnablePositionSaving();
        m_window.EnablePinning();
        m_window.AddBodySingle(root);
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

    public void SetTitle(string title)
    {
        var normalized = NormalizeTitle(title);
        if (!m_disposed && !string.Equals(
                m_currentTitle,
                normalized,
                StringComparison.Ordinal))
        {
            m_currentTitle = normalized;
            m_window.Title(ToTitle(normalized));
        }
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

    public void Open(string title)
    {
        SetTitle(title);
        Open();
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
            m_bodySurface.Render(drawBody, scale);
        }
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

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        m_bodySurface.Dispose();
        m_window.DisposeResizeHandle();
        if (m_window.IsOpen)
        {
            m_window.CloseNoFade();
        }
        else
        {
            m_window.RemoveFromHierarchy();
        }
    }

    private void HandleResizeDelta(Vector2 delta)
    {
        if (m_disposed)
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
        var viewportSize = GetMaximumViewportSize();
        var nextWidth = ClampRequestedDimension(
            requestedWidth,
            Mathf.Min(MinimumWindowWidth, viewportSize.x),
            viewportSize.x);
        var nextHeight = ClampRequestedDimension(
            requestedHeight,
            Mathf.Min(MinimumWindowHeight, viewportSize.y),
            viewportSize.y);
        if (Mathf.Approximately(m_windowWidth, nextWidth) &&
            Mathf.Approximately(m_windowHeight, nextHeight))
        {
            return;
        }
        m_windowWidth = nextWidth;
        m_windowHeight = nextHeight;

        var bodyWidth = GetBodyWidth(m_windowWidth);
        var bodyHeight = GetBodyHeight(m_windowHeight);
        m_window.WindowSize(
            new Px(m_windowWidth),
            new Px(m_windowHeight));
        m_rootElement.style.width =
            m_windowWidth - HorizontalRootInset;
        m_rootElement.style.height =
            m_windowHeight - VerticalRootInset;
        m_bodyElement.style.width = bodyWidth;
        m_bodyElement.style.height = bodyHeight;
        m_bodyElement.style.minWidth = Mathf.Min(
            MinimumWindowWidth - HorizontalBodyInset,
            bodyWidth);
        m_bodyElement.style.minHeight = Mathf.Min(
            MinimumWindowHeight - VerticalBodyInset,
            bodyHeight);
    }

    private Vector2 GetMaximumViewportSize()
    {
        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        var logicalWidth = Screen.width / rootScale;
        var logicalHeight = Screen.height / rootScale;
        return new Vector2(
            Mathf.Max(
                HorizontalBodyInset + 1f,
                Mathf.Min(
                    logicalWidth - ViewportHorizontalMargin,
                    logicalWidth * 0.96f)),
            Mathf.Max(
                VerticalBodyInset + 1f,
                Mathf.Min(
                    logicalHeight - ViewportVerticalMargin,
                    logicalHeight * 0.96f)));
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

    private static float GetBodyWidth(float windowWidth)
    {
        return Mathf.Max(1f, windowWidth - HorizontalBodyInset);
    }

    private static float GetBodyHeight(float windowHeight)
    {
        return Mathf.Max(1f, windowHeight - VerticalBodyInset);
    }

    private static LocStrFormatted ToTitle(string title)
    {
        return new LocStrFormatted(NormalizeTitle(title));
    }

    private static string NormalizeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value) &&
               value > 0f;
    }
}
