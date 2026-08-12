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
/// Hosts one detached UNMA panel in an independently movable native Captain
/// of Industry runtime UI Toolkit window, keeping content and chrome in one
/// clipping, input and stacking layer.
/// Position persistence intentionally remains with the owning panel instance
/// so multiple detached windows never share offsets.
/// </summary>
internal sealed class UnmaNativeDetachedPanelShell : IDisposable
{
    private sealed class DetachedWindow : NativeWindow
    {
        private readonly ResizeLabel m_resizeHandle;
        private Action<Vector2> m_resizeDelta;
        private Action m_resizeCompleted;
        private readonly Action<bool> m_onActivated;
        private readonly Func<Vector2, bool> m_isBodyPoint;
        private Vector2 m_resizeStart;
        private int m_resizePointerId = -1;

        public DetachedWindow(
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
                name = "UNMA.NativeDetachedResizeHandle",
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
            // NativeWindow.WorldBound is COI's full-screen input mask. The
            // protected frame is the exact visible and interactive window.
            return Frame.WorldBound.Contains(panelPoint);
        }

        public Vector2 FrameWorldPosition => Frame.WorldBound.position;

        public void RestoreFrameWorldPosition(Vector2 worldPosition)
        {
            var currentWorldPosition = Frame.WorldBound.position;
            var frameShadowElement = Frame.RootElement.parent;
            if (frameShadowElement == null)
            {
                return;
            }
            var currentTranslation =
                frameShadowElement.resolvedStyle.translate;
            this.TranslateFrame(
                new Px(
                    currentTranslation.x +
                    worldPosition.x - currentWorldPosition.x),
                new Px(
                    currentTranslation.y +
                    worldPosition.y - currentWorldPosition.y));
        }

        public void ScheduleFrameWorldPositionRestore(
            Vector2 worldPosition,
            Func<bool> shouldRestore,
            Action onRestored)
        {
            Frame.RootElement.schedule
                .Execute(() =>
                {
                    if (shouldRestore?.Invoke() != true)
                    {
                        return;
                    }
                    RestoreFrameWorldPosition(worldPosition);
                    onRestored?.Invoke();
                })
                .StartingIn(16);
        }

        public void ScheduleFrameWorldPositionRead(
            Func<bool> shouldRead,
            Action<Vector2> onRead)
        {
            Frame.RootElement.schedule
                .Execute(() =>
                {
                    if (shouldRead?.Invoke() != true)
                    {
                        return;
                    }
                    onRead?.Invoke(FrameWorldPosition);
                })
                .StartingIn(16);
        }

        public void BringFrameToFront()
        {
            RootElement.BringToFront();
        }

        public WindowDragger CreateWindowDragger()
        {
            var frameShadowElement = Frame.RootElement.parent ??
                throw new InvalidOperationException(
                    "Native detached frame shadow is not attached.");
            return new WindowDragger(
                this,
                new UiComponent(frameShadowElement),
                TitleBar);
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

    // The detached action row needs a wider logical body than the tile grid.
    // Keeping this minimum honest avoids hiding ACK/NEXT/NEW ALARM when the
    // user raises UNMA's content scale.
    private const float MinimumWindowWidth = 560f;
    private const float MinimumWindowHeight = 300f;
    private const float MaximumViewportShare = 0.70f;
    private const float HorizontalBodyInset = 44f;
    private const float VerticalBodyInset = 82f;
    private const float HorizontalRootInset = 28f;
    private const float VerticalRootInset = 82f;
    private const float MinimumBodyWidth =
        MinimumWindowWidth - HorizontalBodyInset;
    private const float MinimumBodyHeight =
        MinimumWindowHeight - VerticalBodyInset;
    private const float ViewportHorizontalMargin = 32f;
    private const float ViewportVerticalMargin = 48f;
    private static string DefaultTitle => UnmaText.Get(
        "native.detached.default_title",
        "UNMA - DETACHED PANEL");

    private readonly UiRoot m_uiRoot;
    private readonly Action<float, float> m_onResized;
    private readonly DetachedWindow m_window;
    private readonly WindowDragger m_windowDragger;
    private readonly NativeUiSurface m_bodySurface;
    private readonly VisualElement m_bodyElement;
    private readonly VisualElement m_rootElement;

    private bool m_disposed;
    private bool m_suppressed;
    private float m_preferredWindowWidth;
    private float m_preferredWindowHeight;
    private float m_windowWidth;
    private float m_windowHeight;
    private float m_contentScale = 1f;
    private int m_viewportScreenWidth;
    private int m_viewportScreenHeight;
    private float m_viewportRootScale;
    private bool m_viewportSignatureReady;
    private float m_resizeStartWidth;
    private float m_resizeStartHeight;
    private float m_resizeStartPreferredWidth;
    private float m_resizeStartPreferredHeight;
    private string m_currentTitle;
    private Vector2 m_preferredFrameWorldPosition;
    private Vector2 m_effectiveFrameWorldPosition;
    private int m_positionRestoreVersion;

    public UnmaNativeDetachedPanelShell(
        UiRoot uiRoot,
        float requestedWidth,
        float requestedHeight,
        float initialX,
        float initialY,
        string initialTitle,
        Action onClose,
        Action<float, float> onResized,
        Action<bool> onActivated)
    {
        m_uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        if (onClose == null)
        {
            throw new ArgumentNullException(nameof(onClose));
        }
        m_onResized = onResized ??
                      throw new ArgumentNullException(nameof(onResized));

        var viewportSize = GetMaximumViewportSize();
        m_preferredWindowWidth =
            WindowResizeMath.NormalizePreferredExtent(
                requestedWidth,
                MinimumWindowWidth);
        m_preferredWindowHeight =
            WindowResizeMath.NormalizePreferredExtent(
                requestedHeight,
                MinimumWindowHeight);
        m_windowWidth = WindowResizeMath.ResolveEffectiveExtent(
            m_preferredWindowWidth,
            Mathf.Min(GetMinimumWindowWidth(), viewportSize.x),
            viewportSize.x);
        m_windowHeight = WindowResizeMath.ResolveEffectiveExtent(
            m_preferredWindowHeight,
            Mathf.Min(GetMinimumWindowHeight(), viewportSize.y),
            viewportSize.y);

        var bodyWidth = GetBodyWidth(m_windowWidth);
        var bodyHeight = GetBodyHeight(m_windowHeight);
        m_bodySurface = new NativeUiSurface("UNMA.NativeDetachedBody");
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
                MinimumBodyWidth * m_contentScale,
                bodyWidth)))
            .MinHeight(new Px(Mathf.Min(
                MinimumBodyHeight * m_contentScale,
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
        m_window = new DetachedWindow(
            ToTitle(m_currentTitle),
            onClose,
            onActivated,
            panelPoint => m_bodyElement.worldBound.Contains(panelPoint));
        m_preferredFrameWorldPosition = NormalizePreferredPosition(
            new Vector2(initialX, initialY));
        m_effectiveFrameWorldPosition = ClampPosition(
            m_preferredFrameWorldPosition);
        m_window
            .WindowSize(
                new Px(m_windowWidth),
                new Px(m_windowHeight))
            .TranslateFrame(
                new Px(m_effectiveFrameWorldPosition.x),
                new Px(m_effectiveFrameWorldPosition.y));
        m_windowDragger = m_window.CreateWindowDragger();
        m_windowDragger.OnMoved += HandleWindowMoved;
        m_window.EnablePinning();
        m_window.AddBodySingle(root);
        m_window.ConfigureResize(HandleResizeDelta, HandleResizeCompleted);
    }

    public bool IsOpen => !m_disposed && m_window.IsOpen;

    public bool IsBodyKeyboardCaptured =>
        !m_disposed &&
        !m_suppressed &&
        m_window.IsOpen &&
        m_bodySurface.HasKeyboardFocus;

    public Vector2 CurrentSize => new(m_windowWidth, m_windowHeight);

    /// <summary>
    /// User-selected size before transient UI-scale and viewport constraints.
    /// This is the size that may be persisted by the controller.
    /// </summary>
    public Vector2 PreferredSize =>
        new(m_preferredWindowWidth, m_preferredWindowHeight);

    public bool TryGetCurrentPosition(out Vector2 position)
    {
        position = m_preferredFrameWorldPosition;
        return !m_disposed && m_window.IsOpen;
    }

    public void BringToFront()
    {
        if (!m_disposed && m_window.IsOpen)
        {
            m_window.BringFrameToFront();
        }
    }

    /// <summary>
    /// Preserves the detached panel's usable logical body across UNMA UI
    /// scales, capped by its normal viewport share.
    /// </summary>
    public void SetContentScale(float scale)
    {
        if (m_disposed)
        {
            return;
        }

        var normalized = NormalizeContentScale(scale);
        var contentScaleChanged =
            !Mathf.Approximately(m_contentScale, normalized);
        var viewportChanged = UpdateViewportSignature();
        if (!contentScaleChanged && !viewportChanged)
        {
            return;
        }

        m_contentScale = normalized;
        ApplyPreferredWindowSize();
    }

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
            RestorePosition(m_preferredFrameWorldPosition);
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
            SetContentScale(scale);
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
        if (m_disposed || !UpdateViewportSignature())
        {
            return;
        }

        ApplyPreferredWindowSize();
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
        m_windowDragger.OnMoved -= HandleWindowMoved;
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
        m_windowDragger.Disable();
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
            m_resizeStartPreferredWidth = m_preferredWindowWidth;
            m_resizeStartPreferredHeight = m_preferredWindowHeight;
        }

        ApplyWindowSize(
            m_resizeStartWidth + delta.x,
            m_resizeStartHeight + delta.y);
        m_preferredWindowWidth = Mathf.Approximately(
            m_windowWidth,
            m_resizeStartWidth)
            ? m_resizeStartPreferredWidth
            : m_windowWidth;
        m_preferredWindowHeight = Mathf.Approximately(
            m_windowHeight,
            m_resizeStartHeight)
            ? m_resizeStartPreferredHeight
            : m_windowHeight;
        RestoreEffectivePositionFromPreferred(force: true);
    }

    private void HandleResizeCompleted()
    {
        if (m_disposed)
        {
            return;
        }

        m_resizeStartWidth = 0f;
        m_resizeStartHeight = 0f;
        m_resizeStartPreferredWidth = 0f;
        m_resizeStartPreferredHeight = 0f;
        m_onResized(
            m_preferredWindowWidth,
            m_preferredWindowHeight);
    }

    private void HandleWindowMoved(Vector2 _)
    {
        if (m_disposed || m_window.IsPinned || !m_window.IsOpen)
        {
            return;
        }

        // WorldBound is finalized after the dragger's PointerUpEvent. Delay
        // persistence so a stale bound cannot restore the pre-drag position.
        var moveVersion = ++m_positionRestoreVersion;
        m_window.ScheduleFrameWorldPositionRead(
            () => !m_disposed &&
                  moveVersion == m_positionRestoreVersion &&
                  m_window.IsOpen &&
                  !m_window.IsPinned,
            current => CompleteWindowMove(moveVersion, current));
    }

    private void CompleteWindowMove(int moveVersion, Vector2 current)
    {
        if (m_disposed || moveVersion != m_positionRestoreVersion ||
            !IsFinite(current.x) || !IsFinite(current.y))
        {
            return;
        }

        m_preferredFrameWorldPosition =
            NormalizePreferredPosition(current);
        var effectivePosition = ClampPosition(m_preferredFrameWorldPosition);
        m_effectiveFrameWorldPosition = effectivePosition;
        if (!PositionsApproximately(current, effectivePosition))
        {
            RestoreEffectivePositionFromPreferred(force: true);
        }
    }

    private void ApplyPreferredWindowSize()
    {
        ApplyWindowSize(
            m_preferredWindowWidth,
            m_preferredWindowHeight);
        RestoreEffectivePositionFromPreferred(force: true);
    }

    private void ApplyWindowSize(float requestedWidth, float requestedHeight)
    {
        var viewportSize = GetMaximumViewportSize();
        var nextWidth = WindowResizeMath.ResolveEffectiveExtent(
            requestedWidth,
            Mathf.Min(GetMinimumWindowWidth(), viewportSize.x),
            viewportSize.x);
        var nextHeight = WindowResizeMath.ResolveEffectiveExtent(
            requestedHeight,
            Mathf.Min(GetMinimumWindowHeight(), viewportSize.y),
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
            MinimumBodyWidth * m_contentScale,
            bodyWidth);
        m_bodyElement.style.minHeight = Mathf.Min(
            MinimumBodyHeight * m_contentScale,
            bodyHeight);
    }

    private void RestorePosition(Vector2 requestedPosition)
    {
        m_preferredFrameWorldPosition =
            NormalizePreferredPosition(requestedPosition);
        RestoreEffectivePositionFromPreferred(force: true);
    }

    private void RestoreEffectivePositionFromPreferred(bool force)
    {
        var effectivePosition = ClampPosition(
            m_preferredFrameWorldPosition);
        m_effectiveFrameWorldPosition = effectivePosition;
        if (!m_window.IsOpen)
        {
            return;
        }

        var current = m_window.FrameWorldPosition;
        if (!force && IsFinite(current.x) && IsFinite(current.y) &&
            PositionsApproximately(current, effectivePosition))
        {
            m_effectiveFrameWorldPosition = current;
            return;
        }

        var restoreVersion = ++m_positionRestoreVersion;
        m_window.RestoreFrameWorldPosition(effectivePosition);
        m_window.ScheduleFrameWorldPositionRestore(
            effectivePosition,
            () => !m_disposed &&
                  restoreVersion == m_positionRestoreVersion &&
                  m_window.IsOpen,
            () => CompleteEffectivePositionRestore(restoreVersion));
    }

    private void CompleteEffectivePositionRestore(int restoreVersion)
    {
        if (m_disposed || restoreVersion != m_positionRestoreVersion)
        {
            return;
        }

        var current = m_window.FrameWorldPosition;
        if (IsFinite(current.x) && IsFinite(current.y))
        {
            m_effectiveFrameWorldPosition = current;
        }
    }

    private Vector2 ClampPosition(Vector2 requestedPosition)
    {
        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        var viewportWidth = Mathf.Max(1f, Screen.width / rootScale);
        var viewportHeight = Mathf.Max(1f, Screen.height / rootScale);
        var normalized = NormalizePreferredPosition(requestedPosition);
        return new Vector2(
            Mathf.Clamp(
                normalized.x,
                0f,
                Mathf.Max(0f, viewportWidth - m_windowWidth)),
            Mathf.Clamp(
                normalized.y,
                0f,
                Mathf.Max(0f, viewportHeight - m_windowHeight)));
    }

    private static Vector2 NormalizePreferredPosition(Vector2 position)
    {
        return new Vector2(
            IsFinite(position.x) ? position.x : 40f,
            IsFinite(position.y) ? position.y : 60f);
    }

    private static bool PositionsApproximately(Vector2 left, Vector2 right)
    {
        return Mathf.Abs(left.x - right.x) <= 0.5f &&
               Mathf.Abs(left.y - right.y) <= 0.5f;
    }

    private float GetMinimumWindowWidth()
    {
        return HorizontalBodyInset + MinimumBodyWidth * m_contentScale;
    }

    private float GetMinimumWindowHeight()
    {
        return VerticalBodyInset + MinimumBodyHeight * m_contentScale;
    }

    private static float NormalizeContentScale(float scale)
    {
        return Mathf.Clamp(IsFinitePositive(scale) ? scale : 1f, 0.75f, 2f);
    }

    private bool UpdateViewportSignature()
    {
        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        var changed = !m_viewportSignatureReady ||
                      m_viewportScreenWidth != Screen.width ||
                      m_viewportScreenHeight != Screen.height ||
                      !Mathf.Approximately(
                          m_viewportRootScale,
                          rootScale);
        m_viewportScreenWidth = Screen.width;
        m_viewportScreenHeight = Screen.height;
        m_viewportRootScale = rootScale;
        m_viewportSignatureReady = true;
        return changed;
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
                    logicalWidth * MaximumViewportShare)),
            Mathf.Max(
                VerticalBodyInset + 1f,
                Mathf.Min(
                    logicalHeight - ViewportVerticalMargin,
                    logicalHeight * MaximumViewportShare)));
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

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinitePositive(float value)
    {
        return IsFinite(value) && value > 0f;
    }
}
