using System;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using NativeLabel = UnityEngine.UIElements.Label;

namespace UNMA.Ui;

/// <summary>
/// Compact launcher hosted entirely in Captain of Industry's UI Toolkit tree.
/// The dedicated handle moves the launcher without interfering with the
/// button's click interaction.
/// </summary>
internal sealed class UnmaNativeLauncher : IDisposable
{
    private const float LauncherWidth = 132f;
    private const float LauncherHeight = 34f;
    private const float ButtonWidth = 102f;
    private const float HandleWidth = 26f;
    private const float HandleGap = 4f;
    private const float ViewportMargin = 8f;

    private readonly UiRoot m_uiRoot;
    private readonly Action m_onOpen;
    private readonly Action<float, float> m_onPositionChanged;
    private readonly UiComponent m_component;
    private readonly ButtonText m_openButton;
    private readonly VisualElement m_rootElement;
    private readonly NativeLabel m_dragHandle;

    private VisualElement m_viewportElement;
    private int m_dragPointerId = -1;
    private Vector2 m_dragStartPointer;
    private Vector2 m_dragStartPosition;
    private bool m_dragPositionChanged;
    private float m_x = float.NaN;
    private float m_y = float.NaN;
    private int m_count = -1;
    private bool m_visible = true;
    private bool m_disposed;

    public UnmaNativeLauncher(
        UiRoot uiRoot,
        float initialX,
        float initialY,
        Action onOpen,
        Action<float, float> onPositionChanged)
    {
        m_uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        m_onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        m_onPositionChanged = onPositionChanged ??
                              throw new ArgumentNullException(
                                  nameof(onPositionChanged));

        m_rootElement = new VisualElement
        {
            name = "UNMA.NativeLauncher",
            pickingMode = PickingMode.Position,
        };
        ConfigureRoot(m_rootElement);

        m_openButton = new ButtonText(
            new LocStrFormatted("UNMA"),
            HandleOpen);
        ConfigureOpenButton(m_openButton.RootElement);

        m_dragHandle = new NativeLabel("\u2195")
        {
            name = "UNMA.NativeLauncherDragHandle",
            tooltip = "Move UNMA launcher",
            pickingMode = PickingMode.Position,
        };
        ConfigureDragHandle(m_dragHandle);

        m_component = new UiComponent(m_rootElement);
        m_component.Add(m_openButton);
        m_rootElement.Add(m_dragHandle);

        m_dragHandle.RegisterCallback<PointerDownEvent>(
            HandleDragPointerDown);
        m_dragHandle.RegisterCallback<PointerMoveEvent>(
            HandleDragPointerMove);
        m_dragHandle.RegisterCallback<PointerUpEvent>(
            HandleDragPointerUp);
        m_dragHandle.RegisterCallback<PointerCaptureOutEvent>(
            HandleDragCaptureOut);
        m_rootElement.RegisterCallback<AttachToPanelEvent>(
            HandleAttachedToPanel);
        m_rootElement.RegisterCallback<DetachFromPanelEvent>(
            HandleDetachedFromPanel);

        var initialPosition = ClampToViewport(initialX, initialY);
        ApplyPosition(initialPosition.x, initialPosition.y, false);
        SetCount(0);

        m_uiRoot.AddComponent(m_component, UiLayer.GENERAL);
        SetViewportElement(m_rootElement.parent);
        ClampCurrentPosition(false);
    }

    public void SetVisible(bool visible)
    {
        if (m_disposed)
        {
            return;
        }

        m_visible = visible;
        if (!visible)
        {
            CancelDrag(true);
        }
        else
        {
            ClampCurrentPosition(true);
        }
        m_component.SetVisible(visible);
    }

    public bool ContainsPointer(Vector2 screenPointTopLeft)
    {
        if (m_disposed || !m_visible || m_rootElement.panel == null)
        {
            return false;
        }

        var panelPoint = RuntimePanelUtils.ScreenToPanel(
            m_rootElement.panel,
            screenPointTopLeft);
        return m_rootElement.worldBound.Contains(panelPoint);
    }

    public void SetCount(int count)
    {
        if (m_disposed)
        {
            return;
        }

        var normalizedCount = Math.Max(0, count);
        if (m_count == normalizedCount)
        {
            return;
        }

        m_count = normalizedCount;
        var text = normalizedCount > 0
            ? "UNMA +" + normalizedCount
            : "UNMA";
        ((IComponentWithText)m_openButton).SetValue(
            new LocStrFormatted(text));
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        CancelDrag(false);
        SetViewportElement(null);
        m_dragHandle.UnregisterCallback<PointerDownEvent>(
            HandleDragPointerDown);
        m_dragHandle.UnregisterCallback<PointerMoveEvent>(
            HandleDragPointerMove);
        m_dragHandle.UnregisterCallback<PointerUpEvent>(
            HandleDragPointerUp);
        m_dragHandle.UnregisterCallback<PointerCaptureOutEvent>(
            HandleDragCaptureOut);
        m_rootElement.UnregisterCallback<AttachToPanelEvent>(
            HandleAttachedToPanel);
        m_rootElement.UnregisterCallback<DetachFromPanelEvent>(
            HandleDetachedFromPanel);
        m_component.RemoveFromHierarchy();
    }

    private static void ConfigureRoot(VisualElement root)
    {
        root.style.position = Position.Absolute;
        root.style.width = LauncherWidth;
        root.style.minWidth = LauncherWidth;
        root.style.maxWidth = LauncherWidth;
        root.style.height = LauncherHeight;
        root.style.minHeight = LauncherHeight;
        root.style.maxHeight = LauncherHeight;
        root.style.flexDirection = FlexDirection.Row;
        root.style.alignItems = UnityEngine.UIElements.Align.Center;
        root.style.flexShrink = 0f;
        root.style.overflow = Overflow.Visible;
    }

    private static void ConfigureOpenButton(VisualElement button)
    {
        button.style.width = ButtonWidth;
        button.style.minWidth = ButtonWidth;
        button.style.maxWidth = ButtonWidth;
        button.style.height = LauncherHeight;
        button.style.minHeight = LauncherHeight;
        button.style.maxHeight = LauncherHeight;
        button.style.flexShrink = 0f;
    }

    private static void ConfigureDragHandle(NativeLabel handle)
    {
        handle.style.width = HandleWidth;
        handle.style.minWidth = HandleWidth;
        handle.style.maxWidth = HandleWidth;
        handle.style.height = LauncherHeight;
        handle.style.minHeight = LauncherHeight;
        handle.style.maxHeight = LauncherHeight;
        handle.style.marginLeft = HandleGap;
        handle.style.flexShrink = 0f;
        handle.style.unityTextAlign = TextAnchor.MiddleCenter;
        handle.style.fontSize = 17f;
        handle.style.color = new StyleColor(CoiUiPalette.Symbol);
        handle.style.backgroundColor =
            new StyleColor(CoiUiPalette.Control);
        handle.style.borderTopWidth = 1f;
        handle.style.borderRightWidth = 1f;
        handle.style.borderBottomWidth = 1f;
        handle.style.borderLeftWidth = 1f;
        handle.style.borderTopColor =
            new StyleColor(CoiUiPalette.BorderLight);
        handle.style.borderRightColor =
            new StyleColor(CoiUiPalette.BorderLight);
        handle.style.borderBottomColor =
            new StyleColor(CoiUiPalette.BorderLight);
        handle.style.borderLeftColor =
            new StyleColor(CoiUiPalette.BorderLight);
    }

    private void HandleOpen()
    {
        if (!m_disposed)
        {
            m_onOpen();
        }
    }

    private void HandleDragPointerDown(PointerDownEvent evt)
    {
        if (m_disposed || evt.button != 0 || m_dragPointerId >= 0)
        {
            return;
        }

        m_dragPointerId = evt.pointerId;
        m_dragStartPointer = (Vector2)evt.position;
        m_dragStartPosition = new Vector2(m_x, m_y);
        m_dragPositionChanged = false;
        m_dragHandle.CapturePointer(evt.pointerId);
        evt.StopImmediatePropagation();
    }

    private void HandleDragPointerMove(PointerMoveEvent evt)
    {
        if (m_disposed ||
            evt.pointerId != m_dragPointerId ||
            !m_dragHandle.HasPointerCapture(evt.pointerId))
        {
            return;
        }

        var pointer = (Vector2)evt.position;
        var next = ClampToViewport(
            m_dragStartPosition.x + pointer.x - m_dragStartPointer.x,
            m_dragStartPosition.y + pointer.y - m_dragStartPointer.y);
        m_dragPositionChanged |= ApplyPosition(next.x, next.y, false);
        evt.StopImmediatePropagation();
    }

    private void HandleDragPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != m_dragPointerId)
        {
            return;
        }

        CompleteDrag(evt.pointerId, true, true);
        evt.StopImmediatePropagation();
    }

    private void HandleDragCaptureOut(PointerCaptureOutEvent evt)
    {
        if (evt.pointerId == m_dragPointerId)
        {
            CompleteDrag(evt.pointerId, true, false);
        }
    }

    private void HandleAttachedToPanel(AttachToPanelEvent _)
    {
        if (m_disposed)
        {
            return;
        }

        SetViewportElement(m_rootElement.parent);
        ClampCurrentPosition(false);
    }

    private void HandleDetachedFromPanel(DetachFromPanelEvent _)
    {
        CancelDrag(!m_disposed);
        SetViewportElement(null);
    }

    private void HandleViewportGeometryChanged(GeometryChangedEvent _)
    {
        if (!m_disposed)
        {
            ClampCurrentPosition(true);
        }
    }

    private void SetViewportElement(VisualElement viewport)
    {
        if (ReferenceEquals(m_viewportElement, viewport))
        {
            return;
        }

        if (m_viewportElement != null)
        {
            m_viewportElement.UnregisterCallback<GeometryChangedEvent>(
                HandleViewportGeometryChanged);
        }
        m_viewportElement = viewport;
        if (m_viewportElement != null)
        {
            m_viewportElement.RegisterCallback<GeometryChangedEvent>(
                HandleViewportGeometryChanged);
        }
    }

    private void CancelDrag(bool notifyPositionChanged)
    {
        if (m_dragPointerId < 0)
        {
            return;
        }

        CompleteDrag(
            m_dragPointerId,
            notifyPositionChanged,
            true);
    }

    private void CompleteDrag(
        int pointerId,
        bool notifyPositionChanged,
        bool releasePointer)
    {
        if (pointerId != m_dragPointerId)
        {
            return;
        }

        var positionChanged = m_dragPositionChanged;
        m_dragPointerId = -1;
        m_dragPositionChanged = false;
        if (releasePointer && m_dragHandle.HasPointerCapture(pointerId))
        {
            m_dragHandle.ReleasePointer(pointerId);
        }
        if (notifyPositionChanged && positionChanged && !m_disposed)
        {
            m_onPositionChanged(m_x, m_y);
        }
    }

    private void ClampCurrentPosition(bool notify)
    {
        var next = ClampToViewport(m_x, m_y);
        ApplyPosition(next.x, next.y, notify);
    }

    private Vector2 ClampToViewport(float requestedX, float requestedY)
    {
        var viewport = GetViewportSize();
        return new Vector2(
            ClampCoordinate(requestedX, viewport.x, LauncherWidth),
            ClampCoordinate(requestedY, viewport.y, LauncherHeight));
    }

    private Vector2 GetViewportSize()
    {
        if (m_viewportElement != null)
        {
            var contentRect = m_viewportElement.contentRect;
            if (IsFinitePositive(contentRect.width) &&
                IsFinitePositive(contentRect.height))
            {
                return contentRect.size;
            }
        }

        var rootScale = IsFinitePositive(m_uiRoot.CurrentScale)
            ? m_uiRoot.CurrentScale
            : 1f;
        return new Vector2(
            Mathf.Max(1f, Screen.width / rootScale),
            Mathf.Max(1f, Screen.height / rootScale));
    }

    private static float ClampCoordinate(
        float requested,
        float viewportSize,
        float elementSize)
    {
        var available = Mathf.Max(0f, viewportSize - elementSize);
        var minimum = Mathf.Min(ViewportMargin, available);
        var maximum = Mathf.Max(minimum, available - ViewportMargin);
        var normalized = IsFinite(requested) ? requested : minimum;
        return Mathf.Clamp(normalized, minimum, maximum);
    }

    private bool ApplyPosition(float x, float y, bool notify)
    {
        if (Mathf.Approximately(m_x, x) &&
            Mathf.Approximately(m_y, y))
        {
            return false;
        }

        m_x = x;
        m_y = y;
        m_rootElement.style.left = x;
        m_rootElement.style.top = y;
        if (notify)
        {
            m_onPositionChanged(x, y);
        }
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinitePositive(float value)
    {
        return IsFinite(value) && value > 0f;
    }
}
