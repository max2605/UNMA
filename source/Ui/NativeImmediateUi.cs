using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiTextField = UnityEngine.UIElements.TextField;
using UiToggle = UnityEngine.UIElements.Toggle;

namespace UNMA.Ui;

/// <summary>
/// Hosts an immediate-style draw pass in a runtime UI Toolkit hierarchy.
/// Elements are reconciled by their position in the draw tree and are reused
/// between passes, preserving focus, scroll positions, and pointer capture.
/// </summary>
internal sealed class NativeUiSurface : IDisposable
{
    private readonly VisualElement m_contentRoot;
    private readonly VisualElement m_focusSink;
    private readonly NativeRenderContext m_context;
    private bool m_disposed;
    private bool m_rendering;
    private float m_scale = 1f;

    public NativeUiSurface(string name = "UNMA.NativeUiSurface")
    {
        RootElement = new VisualElement
        {
            name = name,
            pickingMode = PickingMode.Position,
            focusable = false,
        };
        RootElement.style.position = Position.Relative;
        RootElement.style.flexGrow = 1f;
        RootElement.style.flexShrink = 1f;
        RootElement.style.overflow = Overflow.Hidden;

        m_contentRoot = new VisualElement
        {
            name = name + ".Content",
            pickingMode = PickingMode.Position,
        };
        m_contentRoot.style.position = Position.Absolute;
        m_contentRoot.style.left = 0f;
        m_contentRoot.style.top = 0f;
        m_contentRoot.style.flexDirection = FlexDirection.Column;
        m_contentRoot.style.alignItems = Align.Stretch;
        m_contentRoot.style.overflow = Overflow.Hidden;
        RootElement.Add(m_contentRoot);

        // Moving focus here releases TextField keyboard capture without
        // moving focus into another game window.
        m_focusSink = new VisualElement
        {
            name = name + ".FocusSink",
            pickingMode = PickingMode.Ignore,
            focusable = true,
            tabIndex = -1,
        };
        m_focusSink.style.position = Position.Absolute;
        m_focusSink.style.left = -2f;
        m_focusSink.style.top = -2f;
        m_focusSink.style.width = 1f;
        m_focusSink.style.height = 1f;
        RootElement.Add(m_focusSink);

        m_context = new NativeRenderContext(this, m_contentRoot);
        RootElement.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
        ApplyScaleLayout();
    }

    public VisualElement RootElement { get; }

    public float Scale => m_scale;

    public bool HasTextInputFocus
    {
        get
        {
            var focused = RootElement.panel?.focusController?.focusedElement
                as VisualElement;
            if (focused == null || !IsWithinRoot(focused))
            {
                return false;
            }

            for (var current = focused;
                 current != null && current != RootElement;
                 current = current.parent)
            {
                if (current is UiTextField)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Render(Action draw, float scale = 1f)
    {
        if (m_disposed || draw == null)
        {
            return;
        }

        if (m_rendering)
        {
            throw new InvalidOperationException(
                "A NativeUiSurface cannot recursively render itself.");
        }

        m_scale = IsFinitePositive(scale) ? scale : 1f;
        ApplyScaleLayout();
        m_rendering = true;
        var passStarted = false;
        try
        {
            m_context.BeginPass();
            passStarted = true;
            using var scope = NativeUiRuntime.Enter(m_context);
            draw();
        }
        finally
        {
            try
            {
                if (passStarted)
                {
                    m_context.CompletePass();
                }
            }
            finally
            {
                m_rendering = false;
            }
        }
    }

    public void ClearFocus()
    {
        if (m_disposed)
        {
            return;
        }

        var panel = RootElement.panel;
        if (panel == null)
        {
            return;
        }

        var focused = panel.focusController?.focusedElement;
        if (focused is VisualElement element && IsWithinRoot(element))
        {
            focused.Blur();
            m_focusSink.Focus();
        }
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        RootElement.UnregisterCallback<GeometryChangedEvent>(
            HandleGeometryChanged);
        m_context.Dispose();
        RootElement.RemoveFromHierarchy();
    }

    private void HandleGeometryChanged(GeometryChangedEvent _)
    {
        ApplyScaleLayout();
    }

    private void ApplyScaleLayout()
    {
        var scale = IsFinitePositive(m_scale) ? m_scale : 1f;
        m_contentRoot.style.transformOrigin = new TransformOrigin(
            new Length(0f, LengthUnit.Pixel),
            new Length(0f, LengthUnit.Pixel));
        m_contentRoot.style.scale = new Scale(
            new Vector3(scale, scale, 1f));

        var physicalWidth = RootElement.contentRect.width;
        var physicalHeight = RootElement.contentRect.height;
        if (!IsFinitePositive(physicalWidth))
        {
            physicalWidth = RootElement.resolvedStyle.width;
        }
        if (!IsFinitePositive(physicalHeight))
        {
            physicalHeight = RootElement.resolvedStyle.height;
        }

        if (IsFinitePositive(physicalWidth))
        {
            m_contentRoot.style.width = physicalWidth / scale;
        }
        else
        {
            m_contentRoot.style.width = Length.Percent(100f);
        }

        if (IsFinitePositive(physicalHeight))
        {
            m_contentRoot.style.height = physicalHeight / scale;
        }
        else
        {
            m_contentRoot.style.height = Length.Percent(100f);
        }
    }

    private bool IsWithinRoot(VisualElement element)
    {
        for (var current = element; current != null; current = current.parent)
        {
            if (current == RootElement)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFinitePositive(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal enum NativeGUILayoutOptionKind
{
    Width,
    Height,
    MinWidth,
    MaxWidth,
    MinHeight,
    MaxHeight,
    ExpandWidth,
    ExpandHeight,
}

internal readonly struct NativeGUILayoutOption
{
    public NativeGUILayoutOption(
        NativeGUILayoutOptionKind kind,
        float value)
    {
        Kind = kind;
        Value = value;
    }

    public NativeGUILayoutOptionKind Kind { get; }
    public float Value { get; }
}

internal static class NativeGUILayout
{
    public static NativeGUILayoutOption Width(float width) =>
        new(NativeGUILayoutOptionKind.Width, width);

    public static NativeGUILayoutOption Height(float height) =>
        new(NativeGUILayoutOptionKind.Height, height);

    public static NativeGUILayoutOption MinWidth(float width) =>
        new(NativeGUILayoutOptionKind.MinWidth, width);

    public static NativeGUILayoutOption MaxWidth(float width) =>
        new(NativeGUILayoutOptionKind.MaxWidth, width);

    public static NativeGUILayoutOption MinHeight(float height) =>
        new(NativeGUILayoutOptionKind.MinHeight, height);

    public static NativeGUILayoutOption MaxHeight(float height) =>
        new(NativeGUILayoutOptionKind.MaxHeight, height);

    public static NativeGUILayoutOption ExpandWidth(bool expand) =>
        new(NativeGUILayoutOptionKind.ExpandWidth, expand ? 1f : 0f);

    public static NativeGUILayoutOption ExpandHeight(bool expand) =>
        new(NativeGUILayoutOptionKind.ExpandHeight, expand ? 1f : 0f);

    public static void BeginHorizontal(
        params NativeGUILayoutOption[] options) =>
        BeginHorizontalCore(null, null, options);

    public static void BeginHorizontal(
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        BeginHorizontalCore(null, style, options);

    public static void BeginHorizontal(
        string reconciliationKey,
        params NativeGUILayoutOption[] options) =>
        BeginHorizontalCore(reconciliationKey, null, options);

    private static void BeginHorizontalCore(
        string reconciliationKey,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(
            NativeNodeKind.Group,
            reconciliationKey: reconciliationKey);
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        node.IsHorizontal = true;
        node.Element.style.flexDirection = FlexDirection.Row;
        node.Element.style.alignItems = Align.Stretch;
        context.PushGroup(node);
    }

    public static void EndHorizontal()
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        context.PopGroup();
    }

    public static void BeginVertical(
        params NativeGUILayoutOption[] options) =>
        BeginVerticalCore(null, null, options);

    public static void BeginVertical(
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        BeginVerticalCore(null, style, options);

    public static void BeginVertical(
        string reconciliationKey,
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        BeginVerticalCore(reconciliationKey, style, options);

    private static void BeginVerticalCore(
        string reconciliationKey,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(
            NativeNodeKind.Group,
            reconciliationKey: reconciliationKey);
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        node.IsHorizontal = false;
        node.Element.style.flexDirection = FlexDirection.Column;
        node.Element.style.alignItems = Align.Stretch;
        context.PushGroup(node);
    }

    public static void EndVertical()
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        context.PopGroup();
    }

    public static Vector2 BeginScrollView(
        Vector2 scrollPosition,
        params NativeGUILayoutOption[] options) =>
        BeginScrollViewCore(
            scrollPosition,
            false,
            false,
            null,
            options);

    public static Vector2 BeginScrollView(
        Vector2 scrollPosition,
        GUIStyle background,
        params NativeGUILayoutOption[] options) =>
        BeginScrollViewCore(
            scrollPosition,
            false,
            false,
            background,
            options);

    public static Vector2 BeginScrollView(
        Vector2 scrollPosition,
        bool alwaysShowHorizontal,
        bool alwaysShowVertical,
        params NativeGUILayoutOption[] options) =>
        BeginScrollViewCore(
            scrollPosition,
            alwaysShowHorizontal,
            alwaysShowVertical,
            null,
            options);

    public static Vector2 BeginScrollView(
        Vector2 scrollPosition,
        bool alwaysShowHorizontal,
        bool alwaysShowVertical,
        GUIStyle horizontalScrollbar,
        GUIStyle verticalScrollbar,
        params NativeGUILayoutOption[] options) =>
        BeginScrollViewCore(
            scrollPosition,
            alwaysShowHorizontal,
            alwaysShowVertical,
            null,
            options);

    public static void EndScrollView()
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        context.PopGroup();
    }

    public static void BeginArea(Rect screenRect) =>
        BeginArea(screenRect, null);

    public static void BeginArea(Rect screenRect, GUIStyle style)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Group);
        NativeStyleMapper.Apply(
            node,
            style,
            Array.Empty<NativeGUILayoutOption>(),
            context.Enabled,
            context.Color);
        NativeStyleMapper.ApplyAbsoluteRect(node.Element, screenRect);
        node.IsHorizontal = false;
        node.Element.style.flexDirection = FlexDirection.Column;
        node.Element.style.alignItems = Align.Stretch;
        node.Element.style.overflow = Overflow.Hidden;
        context.PushGroup(node);
    }

    public static void EndArea()
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        context.PopGroup();
    }

    public static void Label(
        string text,
        params NativeGUILayoutOption[] options) =>
        Label(text, null, options);

    public static void Label(
        string text,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Label);
        var label = (UiLabel)node.Element;
        label.text = text ?? string.Empty;
        label.enableRichText = style?.richText ?? false;
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
    }

    public static void Label(
        GUIContent content,
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        Label(content?.text, style, options);

    public static bool Button(
        string text,
        params NativeGUILayoutOption[] options) =>
        Button(text, null, options);

    public static bool Button(
        string text,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Button);
        var button = (UiButton)node.Element;
        button.text = text ?? string.Empty;
        button.enableRichText = style?.richText ?? false;
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        return node.ConsumeClick();
    }

    public static bool Button(
        GUIContent content,
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        Button(content?.text, style, options);

    public static string TextField(
        string text,
        params NativeGUILayoutOption[] options) =>
        TextField(text, -1, null, options);

    public static string TextField(
        string text,
        GUIStyle style,
        params NativeGUILayoutOption[] options) =>
        TextField(text, -1, style, options);

    public static string TextField(
        string text,
        int maxLength,
        params NativeGUILayoutOption[] options) =>
        TextField(text, maxLength, null, options);

    public static string TextField(
        string text,
        int maxLength,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.TextField);
        var field = (UiTextField)node.Element;
        field.maxLength = maxLength > 0 ? maxLength : -1;
        var result = node.ConsumeText(text ?? string.Empty);
        node.SuppressValueEvent = true;
        field.SetValueWithoutNotify(result);
        node.SuppressValueEvent = false;
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        return result;
    }

    public static bool Toggle(
        bool value,
        string text,
        params NativeGUILayoutOption[] options) =>
        Toggle(value, text, null, options);

    public static bool Toggle(
        bool value,
        string text,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Toggle);
        var toggle = (UiToggle)node.Element;
        toggle.text = text ?? string.Empty;
        var result = node.ConsumeToggle(value);
        node.SuppressValueEvent = true;
        toggle.SetValueWithoutNotify(result);
        node.SuppressValueEvent = false;
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        return result;
    }

    public static void Space(float pixels)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Spacer);
        NativeStyleMapper.Reset(node.Element);
        node.Element.pickingMode = PickingMode.Ignore;
        node.Element.style.flexShrink = 0f;
        if (context.CurrentGroup.IsHorizontal)
        {
            node.Element.style.width = Mathf.Max(0f, pixels);
            node.Element.style.height = 1f;
        }
        else
        {
            node.Element.style.width = 1f;
            node.Element.style.height = Mathf.Max(0f, pixels);
        }
    }

    public static void FlexibleSpace()
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.Spacer);
        NativeStyleMapper.Reset(node.Element);
        node.Element.pickingMode = PickingMode.Ignore;
        node.Element.style.flexGrow = 1f;
        node.Element.style.flexShrink = 1f;
        node.Element.style.minWidth = 0f;
        node.Element.style.minHeight = 0f;
    }

    private static Vector2 BeginScrollViewCore(
        Vector2 scrollPosition,
        bool alwaysShowHorizontal,
        bool alwaysShowVertical,
        GUIStyle background,
        NativeGUILayoutOption[] options)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(NativeNodeKind.ScrollView);
        var scroll = (ScrollView)node.Element;
        scroll.horizontalScrollerVisibility = alwaysShowHorizontal
            ? ScrollerVisibility.AlwaysVisible
            : ScrollerVisibility.Auto;
        scroll.verticalScrollerVisibility = alwaysShowVertical
            ? ScrollerVisibility.AlwaysVisible
            : ScrollerVisibility.Auto;
        NativeStyleMapper.Apply(
            node,
            background,
            options,
            context.Enabled,
            context.Color);
        node.IsHorizontal = false;
        if (!NativeLayoutOptions.HasVerticalExtent(options))
        {
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
        }
        scroll.contentContainer.style.flexDirection = FlexDirection.Column;
        scroll.contentContainer.style.alignItems = Align.Stretch;
        var result = node.ReconcileScroll(scrollPosition);
        context.PushGroup(node);
        return result;
    }
}

internal static class NativeGUILayoutUtility
{
    public static Rect GetRect(
        float width,
        float height,
        params NativeGUILayoutOption[] options)
    {
        var allOptions = NativeLayoutOptions.Prepend(
            options,
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.Width,
                width),
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.Height,
                height));
        return CreateCanvas(width, height, allOptions);
    }

    public static Rect GetRect(
        string reconciliationKey,
        float width,
        float height,
        params NativeGUILayoutOption[] options)
    {
        var allOptions = NativeLayoutOptions.Prepend(
            options,
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.Width,
                width),
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.Height,
                height));
        return CreateCanvas(
            width,
            height,
            allOptions,
            reconciliationKey: reconciliationKey);
    }

    public static Rect GetRect(
        float minWidth,
        float maxWidth,
        float minHeight,
        float maxHeight,
        params NativeGUILayoutOption[] options)
    {
        var allOptions = NativeLayoutOptions.Prepend(
            options,
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.MinWidth,
                minWidth),
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.MaxWidth,
                maxWidth),
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.MinHeight,
                minHeight),
            new NativeGUILayoutOption(
                NativeGUILayoutOptionKind.MaxHeight,
                maxHeight));
        return CreateCanvas(maxWidth, maxHeight, allOptions);
    }

    public static Rect GetRect(
        GUIContent content,
        GUIStyle style,
        params NativeGUILayoutOption[] options)
    {
        var size = style?.CalcSize(content ?? GUIContent.none) ??
                   Vector2.zero;
        return CreateCanvas(size.x, size.y, options, style);
    }

    public static Rect GetRect(
        GUIContent content,
        GUIStyle style,
        float maxWidth,
        params NativeGUILayoutOption[] options)
    {
        var height = style?.CalcHeight(
            content ?? GUIContent.none,
            maxWidth) ?? 0f;
        return CreateCanvas(maxWidth, height, options, style);
    }

    private static Rect CreateCanvas(
        float preferredWidth,
        float preferredHeight,
        NativeGUILayoutOption[] options,
        GUIStyle style = null,
        string reconciliationKey = null)
    {
        var context = NativeUiRuntime.RequireCurrent();
        context.InvalidateAbsoluteCanvas();
        var node = context.Acquire(
            NativeNodeKind.Canvas,
            reconciliationKey: reconciliationKey);
        NativeStyleMapper.Apply(
            node,
            style,
            options,
            context.Enabled,
            context.Color);
        var canvas = (NativeAbsoluteCanvas)node.Element;
        var width = NativeLayoutOptions.ResolveWidth(
            canvas,
            preferredWidth,
            options);
        var height = NativeLayoutOptions.ResolveHeight(
            canvas,
            preferredHeight,
            options);
        if (!IsFinitePositive(width))
        {
            width = ResolveParentExtent(
                context.CurrentGroup.Element,
                true);
        }
        if (!IsFinitePositive(height))
        {
            height = ResolveParentExtent(
                context.CurrentGroup.Element,
                false);
        }
        canvas.style.overflow = Overflow.Hidden;
        canvas.SetLogicalSize(width, height);
        context.SetAbsoluteCanvas(node);
        return new Rect(0f, 0f, width, height);
    }

    private static float ResolveParentExtent(
        VisualElement parent,
        bool horizontal)
    {
        var extent = horizontal
            ? parent.contentRect.width
            : parent.contentRect.height;
        if (!IsFinitePositive(extent))
        {
            extent = horizontal
                ? parent.resolvedStyle.width
                : parent.resolvedStyle.height;
        }
        return IsFinitePositive(extent) ? extent : 1f;
    }

    private static bool IsFinitePositive(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class NativeGUI
{
    [ThreadStatic]
    private static bool s_fallbackEnabled;
    [ThreadStatic]
    private static bool s_fallbackEnabledInitialized;
    [ThreadStatic]
    private static Color s_fallbackColor;
    [ThreadStatic]
    private static bool s_fallbackColorInitialized;

    public static bool enabled
    {
        get
        {
            var context = NativeUiRuntime.Current;
            if (context != null)
            {
                return context.Enabled;
            }

            if (!s_fallbackEnabledInitialized)
            {
                s_fallbackEnabled = true;
                s_fallbackEnabledInitialized = true;
            }
            return s_fallbackEnabled;
        }
        set
        {
            var context = NativeUiRuntime.Current;
            if (context != null)
            {
                context.Enabled = value;
                return;
            }

            s_fallbackEnabled = value;
            s_fallbackEnabledInitialized = true;
        }
    }

    public static Color color
    {
        get
        {
            var context = NativeUiRuntime.Current;
            if (context != null)
            {
                return context.Color;
            }

            if (!s_fallbackColorInitialized)
            {
                s_fallbackColor = Color.white;
                s_fallbackColorInitialized = true;
            }
            return s_fallbackColor;
        }
        set
        {
            var context = NativeUiRuntime.Current;
            if (context != null)
            {
                context.Color = value;
                return;
            }

            s_fallbackColor = value;
            s_fallbackColorInitialized = true;
        }
    }

    public static void Label(Rect position, string text) =>
        Label(position, text, null);

    public static void Label(
        Rect position,
        string text,
        GUIStyle style)
    {
        var context = NativeUiRuntime.RequireCurrent();
        var canvasNode = context.RequireAbsoluteCanvas();
        var node = context.Acquire(
            NativeNodeKind.Label,
            canvasNode);
        var label = (UiLabel)node.Element;
        label.text = text ?? string.Empty;
        label.enableRichText = style?.richText ?? false;
        NativeStyleMapper.Apply(
            node,
            style,
            Array.Empty<NativeGUILayoutOption>(),
            context.Enabled,
            context.Color);
        NativeStyleMapper.ApplyAbsoluteRect(node.Element, position);
    }

    public static void Label(
        Rect position,
        GUIContent content,
        GUIStyle style) =>
        Label(position, content?.text, style);

    public static bool Button(Rect position, string text) =>
        Button(position, text, null);

    public static bool Button(
        Rect position,
        string text,
        GUIStyle style)
    {
        var context = NativeUiRuntime.RequireCurrent();
        var canvasNode = context.RequireAbsoluteCanvas();
        var node = context.Acquire(
            NativeNodeKind.Button,
            canvasNode);
        var button = (UiButton)node.Element;
        button.text = text ?? string.Empty;
        button.enableRichText = style?.richText ?? false;
        NativeStyleMapper.Apply(
            node,
            style,
            Array.Empty<NativeGUILayoutOption>(),
            context.Enabled,
            context.Color);
        NativeStyleMapper.ApplyAbsoluteRect(node.Element, position);
        return node.ConsumeClick();
    }

    public static bool Button(
        Rect position,
        GUIContent content,
        GUIStyle style) =>
        Button(position, content?.text, style);

    public static void DrawTexture(Rect position, Texture image)
    {
        if (image != Texture2D.whiteTexture)
        {
            throw new NotSupportedException(
                "NativeGUI.DrawTexture supports Texture2D.whiteTexture only.");
        }

        var context = NativeUiRuntime.RequireCurrent();
        var canvas = (NativeAbsoluteCanvas)context
            .RequireAbsoluteCanvas()
            .Element;
        canvas.AddFill(position, context.Color);
    }

    public static void FocusControl(string name)
    {
        var context = NativeUiRuntime.Current;
        if (context == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            context.Surface.ClearFocus();
            return;
        }

        context.Surface.RootElement.Q<VisualElement>(name)?.Focus();
    }
}

internal enum NativeNodeKind
{
    Root,
    Group,
    ScrollView,
    Label,
    Button,
    TextField,
    Toggle,
    Spacer,
    Canvas,
}

internal sealed class NativeRenderNode
{
    private bool m_hasPendingText;
    private string m_pendingText;
    private bool m_hasPendingToggle;
    private bool m_pendingToggle;
    private bool m_scrollInitialized;
    private Vector2 m_lastReturnedScroll;

    public NativeRenderNode(
        NativeNodeKind kind,
        VisualElement element)
    {
        Kind = kind;
        Element = element;
        PresentationElement = element;
    }

    public NativeNodeKind Kind { get; }
    public VisualElement Element { get; }
    public VisualElement PresentationElement { get; set; }
    public List<NativeRenderNode> Children { get; } = new();
    public int UsedChildren { get; set; }
    public int VisitedGeneration { get; set; }
    public NativeRenderNode Parent { get; set; }
    public string ReconciliationKey { get; set; }
    public bool IsHorizontal { get; set; }
    public int PendingClicks { get; set; }
    public bool SuppressValueEvent { get; set; }
    public GUIStyle GuiStyle { get; set; }
    public Color Tint { get; set; } = Color.white;
    public bool IsEnabled { get; set; } = true;
    public bool IsHovered { get; set; }
    public bool IsPressed { get; set; }
    public bool IsFocused { get; set; }

    public VisualElement ChildHost =>
        Element is ScrollView scroll
            ? scroll.contentContainer
            : Element is NativeAbsoluteCanvas canvas
                ? canvas.InteractiveLayer
                : Element;

    public bool ConsumeClick()
    {
        if (!IsEnabled)
        {
            PendingClicks = 0;
            return false;
        }
        if (PendingClicks <= 0)
        {
            return false;
        }

        PendingClicks--;
        return true;
    }

    public void QueueText(string value)
    {
        if (SuppressValueEvent)
        {
            return;
        }

        m_pendingText = value ?? string.Empty;
        m_hasPendingText = true;
    }

    public string ConsumeText(string fallback)
    {
        if (!m_hasPendingText)
        {
            return fallback;
        }

        m_hasPendingText = false;
        return m_pendingText ?? string.Empty;
    }

    public void QueueToggle(bool value)
    {
        if (SuppressValueEvent)
        {
            return;
        }

        m_pendingToggle = value;
        m_hasPendingToggle = true;
    }

    public bool ConsumeToggle(bool fallback)
    {
        if (!m_hasPendingToggle)
        {
            return fallback;
        }

        m_hasPendingToggle = false;
        return m_pendingToggle;
    }

    public Vector2 ReconcileScroll(Vector2 requested)
    {
        var scroll = (ScrollView)Element;
        if (!m_scrollInitialized)
        {
            m_scrollInitialized = true;
            m_lastReturnedScroll = requested;
            scroll.scrollOffset = requested;
            return requested;
        }

        // A caller change wins over the retained visual state. Otherwise the
        // live ScrollView value is the user event buffered for this pass.
        if ((requested - m_lastReturnedScroll).sqrMagnitude > 0.01f)
        {
            scroll.scrollOffset = requested;
            m_lastReturnedScroll = requested;
            return requested;
        }

        m_lastReturnedScroll = scroll.scrollOffset;
        return m_lastReturnedScroll;
    }
}

internal sealed class NativeRenderContext : IDisposable
{
    private readonly NativeRenderNode m_rootNode;
    private readonly List<NativeRenderNode> m_groupStack = new();
    private readonly List<NativeRenderNode> m_visitedNodes = new();
    private int m_generation;
    private NativeRenderNode m_absoluteCanvas;

    public NativeRenderContext(
        NativeUiSurface surface,
        VisualElement contentRoot)
    {
        Surface = surface;
        m_rootNode = new NativeRenderNode(
            NativeNodeKind.Root,
            contentRoot);
        m_rootNode.IsHorizontal = false;
    }

    public NativeUiSurface Surface { get; }
    public bool Enabled { get; set; } = true;
    public Color Color { get; set; } = Color.white;
    public NativeRenderNode CurrentGroup =>
        m_groupStack[m_groupStack.Count - 1];

    public void BeginPass()
    {
        m_generation++;
        Enabled = true;
        Color = Color.white;
        m_groupStack.Clear();
        m_visitedNodes.Clear();
        Visit(m_rootNode);
        m_groupStack.Add(m_rootNode);
        m_absoluteCanvas = null;
    }

    public void CompletePass()
    {
        for (var i = m_visitedNodes.Count - 1; i >= 0; i--)
        {
            var node = m_visitedNodes[i];
            TrimUnusedChildren(node);
            if (node.Element is NativeAbsoluteCanvas canvas)
            {
                canvas.CompletePass();
            }
        }

        m_groupStack.Clear();
        m_visitedNodes.Clear();
        m_absoluteCanvas = null;
    }

    public NativeRenderNode Acquire(
        NativeNodeKind kind,
        NativeRenderNode parent = null,
        string reconciliationKey = null)
    {
        parent ??= CurrentGroup;
        Visit(parent);
        var index = parent.UsedChildren++;
        NativeRenderNode node;
        if (index < parent.Children.Count &&
            parent.Children[index].Kind == kind &&
            string.Equals(
                parent.Children[index].ReconciliationKey,
                reconciliationKey,
                StringComparison.Ordinal))
        {
            node = parent.Children[index];
        }
        else
        {
            if (index < parent.Children.Count)
            {
                RemoveNode(parent.Children[index]);
                parent.Children.RemoveAt(index);
            }

            node = CreateNode(kind);
            parent.Children.Insert(index, node);
        }

        node.Parent = parent;
        node.ReconciliationKey = reconciliationKey;
        AttachAt(parent.ChildHost, node.Element, index);
        Visit(node);
        return node;
    }

    public void PushGroup(NativeRenderNode node)
    {
        m_groupStack.Add(node);
    }

    public void PopGroup()
    {
        if (m_groupStack.Count <= 1)
        {
            throw new InvalidOperationException(
                "NativeGUILayout group stack underflow.");
        }

        m_groupStack.RemoveAt(m_groupStack.Count - 1);
    }

    public void SetAbsoluteCanvas(NativeRenderNode node)
    {
        m_absoluteCanvas = node;
    }

    public void InvalidateAbsoluteCanvas()
    {
        m_absoluteCanvas = null;
    }

    public NativeRenderNode RequireAbsoluteCanvas()
    {
        if (m_absoluteCanvas != null)
        {
            return m_absoluteCanvas;
        }

        var node = Acquire(NativeNodeKind.Canvas);
        NativeStyleMapper.Reset(node.Element);
        node.Element.style.position = Position.Absolute;
        node.Element.style.left = 0f;
        node.Element.style.top = 0f;
        node.Element.style.width = Length.Percent(100f);
        node.Element.style.height = Length.Percent(100f);
        node.Element.style.overflow = Overflow.Hidden;
        ((NativeAbsoluteCanvas)node.Element).SetLogicalSize(
            CurrentGroup.Element.contentRect.width,
            CurrentGroup.Element.contentRect.height);
        m_absoluteCanvas = node;
        return node;
    }

    public void Dispose()
    {
        foreach (var child in m_rootNode.Children)
        {
            RemoveNode(child);
        }
        m_rootNode.Children.Clear();
        m_groupStack.Clear();
        m_visitedNodes.Clear();
    }

    private void Visit(NativeRenderNode node)
    {
        if (node.VisitedGeneration == m_generation)
        {
            return;
        }

        node.VisitedGeneration = m_generation;
        node.UsedChildren = 0;
        m_visitedNodes.Add(node);
        if (node.Element is NativeAbsoluteCanvas canvas)
        {
            canvas.BeginPass();
        }
    }

    private NativeRenderNode CreateNode(NativeNodeKind kind)
    {
        NativeRenderNode node;
        switch (kind)
        {
            case NativeNodeKind.Group:
                node = new NativeRenderNode(kind, new VisualElement());
                break;
            case NativeNodeKind.ScrollView:
                node = new NativeRenderNode(
                    kind,
                    new ScrollView(ScrollViewMode.VerticalAndHorizontal));
                break;
            case NativeNodeKind.Label:
                node = new NativeRenderNode(kind, new UiLabel
                {
                    pickingMode = PickingMode.Ignore,
                });
                break;
            case NativeNodeKind.Button:
            {
                var button = new UiButton();
                node = new NativeRenderNode(kind, button);
                button.clicked += () => node.PendingClicks++;
                RegisterPointerStateCallbacks(node);
                RegisterFocusStateCallbacks(node);
                break;
            }
            case NativeNodeKind.TextField:
            {
                var field = new UiTextField();
                node = new NativeRenderNode(kind, field);
                node.PresentationElement = field.Q<VisualElement>(
                    className: UiTextField.inputUssClassName) ?? field;
                field.RegisterValueChangedCallback(
                    evt => node.QueueText(evt.newValue));
                RegisterFocusStateCallbacks(node);
                break;
            }
            case NativeNodeKind.Toggle:
            {
                var toggle = new UiToggle();
                node = new NativeRenderNode(kind, toggle);
                toggle.RegisterValueChangedCallback(
                    evt => node.QueueToggle(evt.newValue));
                RegisterFocusStateCallbacks(node);
                break;
            }
            case NativeNodeKind.Spacer:
                node = new NativeRenderNode(kind, new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                });
                break;
            case NativeNodeKind.Canvas:
                node = new NativeRenderNode(
                    kind,
                    new NativeAbsoluteCanvas());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return node;
    }

    private static void RegisterPointerStateCallbacks(
        NativeRenderNode node)
    {
        node.Element.RegisterCallback<PointerEnterEvent>(_ =>
        {
            node.IsHovered = true;
            NativeStyleMapper.RefreshVisualState(node);
        });
        node.Element.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            node.IsHovered = false;
            node.IsPressed = false;
            NativeStyleMapper.RefreshVisualState(node);
        });
        node.Element.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == 0)
            {
                node.IsPressed = true;
                NativeStyleMapper.RefreshVisualState(node);
            }
        });
        node.Element.RegisterCallback<PointerUpEvent>(_ =>
        {
            node.IsPressed = false;
            NativeStyleMapper.RefreshVisualState(node);
        });
    }

    private static void RegisterFocusStateCallbacks(
        NativeRenderNode node)
    {
        node.Element.RegisterCallback<FocusInEvent>(_ =>
        {
            node.IsFocused = true;
            NativeStyleMapper.RefreshVisualState(node);
        });
        node.Element.RegisterCallback<FocusOutEvent>(_ =>
        {
            node.IsFocused = false;
            NativeStyleMapper.RefreshVisualState(node);
        });
    }

    private static void AttachAt(
        VisualElement parent,
        VisualElement child,
        int index)
    {
        if (child.parent == parent && parent.IndexOf(child) == index)
        {
            return;
        }

        child.RemoveFromHierarchy();
        parent.Insert(Mathf.Clamp(index, 0, parent.childCount), child);
    }

    private static void TrimUnusedChildren(NativeRenderNode parent)
    {
        for (var i = parent.Children.Count - 1;
             i >= parent.UsedChildren;
             i--)
        {
            RemoveNode(parent.Children[i]);
            parent.Children.RemoveAt(i);
        }
    }

    private static void RemoveNode(NativeRenderNode node)
    {
        foreach (var child in node.Children)
        {
            RemoveNode(child);
        }
        node.Children.Clear();
        node.Element.RemoveFromHierarchy();
    }
}

internal static class NativeUiRuntime
{
    [ThreadStatic]
    private static NativeRenderContext s_current;

    public static NativeRenderContext Current => s_current;

    public static NativeRenderContext RequireCurrent() =>
        s_current ?? throw new InvalidOperationException(
            "Native immediate UI calls must run inside NativeUiSurface.Render.");

    public static IDisposable Enter(NativeRenderContext context)
    {
        var previous = s_current;
        s_current = context;
        return new Scope(() => s_current = previous);
    }

    private sealed class Scope : IDisposable
    {
        private Action m_onDispose;

        public Scope(Action onDispose)
        {
            m_onDispose = onDispose;
        }

        public void Dispose()
        {
            var action = m_onDispose;
            m_onDispose = null;
            action?.Invoke();
        }
    }
}

internal static class NativeStyleMapper
{
    public static void Apply(
        NativeRenderNode node,
        GUIStyle guiStyle,
        NativeGUILayoutOption[] options,
        bool enabled,
        Color tint)
    {
        var element = node.Element;
        Reset(element);
        if (node.PresentationElement != element)
        {
            ResetPresentation(node.PresentationElement);
        }
        node.GuiStyle = guiStyle;
        node.Tint = tint;
        node.IsEnabled = enabled;

        if (guiStyle != null)
        {
            ApplyGuiStyle(node, guiStyle, tint);
        }

        NativeLayoutOptions.Apply(node, options);
        element.SetEnabled(enabled);
        element.style.opacity = enabled ? 1f : 0.55f;
        RefreshVisualState(node);
    }

    public static void Reset(VisualElement element)
    {
        element.style.position = Position.Relative;
        element.style.left = StyleKeyword.Null;
        element.style.right = StyleKeyword.Null;
        element.style.top = StyleKeyword.Null;
        element.style.bottom = StyleKeyword.Null;
        element.style.width = StyleKeyword.Null;
        element.style.height = StyleKeyword.Null;
        element.style.minWidth = StyleKeyword.Null;
        element.style.maxWidth = StyleKeyword.Null;
        element.style.minHeight = StyleKeyword.Null;
        element.style.maxHeight = StyleKeyword.Null;
        element.style.flexGrow = 0f;
        element.style.flexShrink = 1f;
        element.style.alignSelf = StyleKeyword.Null;
        element.style.marginLeft = StyleKeyword.Null;
        element.style.marginRight = StyleKeyword.Null;
        element.style.marginTop = StyleKeyword.Null;
        element.style.marginBottom = StyleKeyword.Null;
        element.style.paddingLeft = StyleKeyword.Null;
        element.style.paddingRight = StyleKeyword.Null;
        element.style.paddingTop = StyleKeyword.Null;
        element.style.paddingBottom = StyleKeyword.Null;
        element.style.borderLeftWidth = StyleKeyword.Null;
        element.style.borderRightWidth = StyleKeyword.Null;
        element.style.borderTopWidth = StyleKeyword.Null;
        element.style.borderBottomWidth = StyleKeyword.Null;
        element.style.backgroundImage = StyleKeyword.Null;
        element.style.backgroundColor = StyleKeyword.Null;
        element.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        element.style.color = StyleKeyword.Null;
        element.style.fontSize = StyleKeyword.Null;
        element.style.unityFont = StyleKeyword.Null;
        element.style.unityFontStyleAndWeight = StyleKeyword.Null;
        element.style.unityTextAlign = StyleKeyword.Null;
        element.style.whiteSpace = StyleKeyword.Null;
        element.style.overflow = StyleKeyword.Null;
        element.style.unitySliceLeft = StyleKeyword.Null;
        element.style.unitySliceRight = StyleKeyword.Null;
        element.style.unitySliceTop = StyleKeyword.Null;
        element.style.unitySliceBottom = StyleKeyword.Null;
    }

    private static void ResetPresentation(VisualElement element)
    {
        element.style.paddingLeft = StyleKeyword.Null;
        element.style.paddingRight = StyleKeyword.Null;
        element.style.paddingTop = StyleKeyword.Null;
        element.style.paddingBottom = StyleKeyword.Null;
        element.style.borderLeftWidth = StyleKeyword.Null;
        element.style.borderRightWidth = StyleKeyword.Null;
        element.style.borderTopWidth = StyleKeyword.Null;
        element.style.borderBottomWidth = StyleKeyword.Null;
        element.style.backgroundImage = StyleKeyword.Null;
        element.style.backgroundColor = StyleKeyword.Null;
        element.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        element.style.color = StyleKeyword.Null;
        element.style.fontSize = StyleKeyword.Null;
        element.style.unityFont = StyleKeyword.Null;
        element.style.unityFontStyleAndWeight = StyleKeyword.Null;
        element.style.unityTextAlign = StyleKeyword.Null;
        element.style.whiteSpace = StyleKeyword.Null;
        element.style.overflow = StyleKeyword.Null;
        element.style.unitySliceLeft = StyleKeyword.Null;
        element.style.unitySliceRight = StyleKeyword.Null;
        element.style.unitySliceTop = StyleKeyword.Null;
        element.style.unitySliceBottom = StyleKeyword.Null;
    }

    public static void ApplyAbsoluteRect(
        VisualElement element,
        Rect rect)
    {
        element.style.position = Position.Absolute;
        element.style.left = rect.x;
        element.style.top = rect.y;
        element.style.width = Mathf.Max(0f, rect.width);
        element.style.height = Mathf.Max(0f, rect.height);
        element.style.flexGrow = 0f;
        element.style.flexShrink = 0f;
    }

    public static void RefreshVisualState(NativeRenderNode node)
    {
        var guiStyle = node.GuiStyle;
        if (guiStyle == null)
        {
            return;
        }

        var state = node.IsPressed
            ? guiStyle.active
            : node.IsHovered
                ? guiStyle.hover
                : node.IsFocused
                    ? guiStyle.focused
                    : guiStyle.normal;
        if (state == null)
        {
            state = guiStyle.normal;
        }

        if (state?.background != null)
        {
            node.PresentationElement.style.backgroundImage =
                new StyleBackground(state.background);
        }
        else if (ReferenceEquals(guiStyle, GUIStyle.none))
        {
            node.PresentationElement.style.backgroundImage = StyleKeyword.None;
            node.PresentationElement.style.backgroundColor = Color.clear;
            node.PresentationElement.style.borderLeftWidth = 0f;
            node.PresentationElement.style.borderRightWidth = 0f;
            node.PresentationElement.style.borderTopWidth = 0f;
            node.PresentationElement.style.borderBottomWidth = 0f;
        }

        if (state != null && state.textColor.a > 0f)
        {
            node.PresentationElement.style.color = Multiply(
                state.textColor,
                node.Tint);
        }
    }

    private static void ApplyGuiStyle(
        NativeRenderNode node,
        GUIStyle guiStyle,
        Color tint)
    {
        var element = node.Element;
        var presentation = node.PresentationElement;
        if (guiStyle.fontSize > 0)
        {
            presentation.style.fontSize = guiStyle.fontSize;
        }
        if (guiStyle.font != null)
        {
            presentation.style.unityFont = guiStyle.font;
        }
        presentation.style.unityFontStyleAndWeight = guiStyle.fontStyle;
        presentation.style.unityTextAlign = guiStyle.alignment;
        presentation.style.whiteSpace = guiStyle.wordWrap
            ? WhiteSpace.Normal
            : WhiteSpace.NoWrap;
        presentation.style.overflow = guiStyle.clipping == TextClipping.Clip
            ? Overflow.Hidden
            : Overflow.Visible;

        ApplyRectOffset(guiStyle.margin, (
            left,
            right,
            top,
            bottom) =>
        {
            element.style.marginLeft = left;
            element.style.marginRight = right;
            element.style.marginTop = top;
            element.style.marginBottom = bottom;
        });
        ApplyRectOffset(guiStyle.padding, (
            left,
            right,
            top,
            bottom) =>
        {
            presentation.style.paddingLeft = left;
            presentation.style.paddingRight = right;
            presentation.style.paddingTop = top;
            presentation.style.paddingBottom = bottom;
        });

        if (guiStyle.fixedWidth > 0f)
        {
            element.style.width = guiStyle.fixedWidth;
        }
        else if (guiStyle.stretchWidth)
        {
            ApplyExpandWidth(node, true);
        }
        if (guiStyle.fixedHeight > 0f)
        {
            element.style.height = guiStyle.fixedHeight;
        }
        else if (guiStyle.stretchHeight)
        {
            ApplyExpandHeight(node, true);
        }

        if (guiStyle.border != null)
        {
            presentation.style.unitySliceLeft = guiStyle.border.left;
            presentation.style.unitySliceRight = guiStyle.border.right;
            presentation.style.unitySliceTop = guiStyle.border.top;
            presentation.style.unitySliceBottom = guiStyle.border.bottom;
        }
        presentation.style.unityBackgroundImageTintColor = tint;
    }

    internal static void ApplyExpandWidth(
        NativeRenderNode node,
        bool expand)
    {
        if (node.Parent?.IsHorizontal == true)
        {
            node.Element.style.flexGrow = expand ? 1f : 0f;
        }
        else
        {
            if (expand)
            {
                node.Element.style.width = StyleKeyword.Auto;
            }
            node.Element.style.alignSelf = expand
                ? Align.Stretch
                : StyleKeyword.Null;
        }
    }

    internal static void ApplyExpandHeight(
        NativeRenderNode node,
        bool expand)
    {
        if (node.Parent?.IsHorizontal == true)
        {
            if (expand)
            {
                node.Element.style.height = StyleKeyword.Auto;
            }
            node.Element.style.alignSelf = expand
                ? Align.Stretch
                : StyleKeyword.Null;
        }
        else
        {
            node.Element.style.flexGrow = expand ? 1f : 0f;
        }
    }

    private static void ApplyRectOffset(
        RectOffset offset,
        Action<float, float, float, float> apply)
    {
        if (offset == null)
        {
            return;
        }
        apply(offset.left, offset.right, offset.top, offset.bottom);
    }

    private static Color Multiply(Color left, Color right) =>
        new(
            left.r * right.r,
            left.g * right.g,
            left.b * right.b,
            left.a * right.a);
}

internal static class NativeLayoutOptions
{
    public static void Apply(
        NativeRenderNode node,
        NativeGUILayoutOption[] options)
    {
        if (options == null)
        {
            return;
        }

        foreach (var option in options)
        {
            var element = node.Element;
            switch (option.Kind)
            {
                case NativeGUILayoutOptionKind.Width:
                    element.style.width = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.Height:
                    element.style.height = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.MinWidth:
                    element.style.minWidth = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.MaxWidth:
                    element.style.maxWidth = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.MinHeight:
                    element.style.minHeight = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.MaxHeight:
                    element.style.maxHeight = Mathf.Max(0f, option.Value);
                    break;
                case NativeGUILayoutOptionKind.ExpandWidth:
                    NativeStyleMapper.ApplyExpandWidth(
                        node,
                        option.Value > 0f);
                    break;
                case NativeGUILayoutOptionKind.ExpandHeight:
                    NativeStyleMapper.ApplyExpandHeight(
                        node,
                        option.Value > 0f);
                    break;
            }
        }
    }

    public static bool HasVerticalExtent(
        NativeGUILayoutOption[] options)
    {
        if (options == null)
        {
            return false;
        }

        foreach (var option in options)
        {
            if (option.Kind == NativeGUILayoutOptionKind.Height ||
                option.Kind == NativeGUILayoutOptionKind.MinHeight ||
                option.Kind == NativeGUILayoutOptionKind.MaxHeight ||
                option.Kind == NativeGUILayoutOptionKind.ExpandHeight)
            {
                return true;
            }
        }
        return false;
    }

    public static NativeGUILayoutOption[] Prepend(
        NativeGUILayoutOption[] options,
        params NativeGUILayoutOption[] prefix)
    {
        options ??= Array.Empty<NativeGUILayoutOption>();
        var result = new NativeGUILayoutOption[
            prefix.Length + options.Length];
        Array.Copy(prefix, 0, result, 0, prefix.Length);
        Array.Copy(options, 0, result, prefix.Length, options.Length);
        return result;
    }

    public static float ResolveWidth(
        VisualElement element,
        float preferred,
        NativeGUILayoutOption[] options)
    {
        var resolved = element.resolvedStyle.width;
        return IsFinitePositive(resolved)
            ? resolved
            : Mathf.Max(0f, ResolveRequested(
                preferred,
                options,
                NativeGUILayoutOptionKind.Width,
                NativeGUILayoutOptionKind.MaxWidth,
                NativeGUILayoutOptionKind.MinWidth));
    }

    public static float ResolveHeight(
        VisualElement element,
        float preferred,
        NativeGUILayoutOption[] options)
    {
        var resolved = element.resolvedStyle.height;
        return IsFinitePositive(resolved)
            ? resolved
            : Mathf.Max(0f, ResolveRequested(
                preferred,
                options,
                NativeGUILayoutOptionKind.Height,
                NativeGUILayoutOptionKind.MaxHeight,
                NativeGUILayoutOptionKind.MinHeight));
    }

    private static float ResolveRequested(
        float fallback,
        NativeGUILayoutOption[] options,
        NativeGUILayoutOptionKind exact,
        NativeGUILayoutOptionKind maximum,
        NativeGUILayoutOptionKind minimum)
    {
        var result = fallback;
        if (options == null)
        {
            return result;
        }

        foreach (var option in options)
        {
            if (option.Kind == exact || option.Kind == maximum)
            {
                result = option.Value;
            }
            else if (option.Kind == minimum && result < option.Value)
            {
                result = option.Value;
            }
        }
        return result;
    }

    private static bool IsFinitePositive(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class NativeAbsoluteCanvas : VisualElement
{
    private readonly NativeFillBatch m_fillBatch;

    public NativeAbsoluteCanvas()
    {
        pickingMode = PickingMode.Position;
        style.position = Position.Relative;
        style.overflow = Overflow.Hidden;

        m_fillBatch = new NativeFillBatch();
        hierarchy.Add(m_fillBatch);

        InteractiveLayer = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
        };
        InteractiveLayer.style.position = Position.Absolute;
        InteractiveLayer.style.left = 0f;
        InteractiveLayer.style.right = 0f;
        InteractiveLayer.style.top = 0f;
        InteractiveLayer.style.bottom = 0f;
        hierarchy.Add(InteractiveLayer);
    }

    public VisualElement InteractiveLayer { get; }
    public Vector2 LogicalSize { get; private set; }

    public void BeginPass()
    {
        m_fillBatch.ClearCommands();
    }

    public void AddFill(Rect rect, Color color)
    {
        m_fillBatch.Add(rect, color);
    }

    public void SetLogicalSize(float width, float height)
    {
        // The values define the immediate coordinate system. Layout remains
        // governed by the exact/min/max options so flex expansion is not
        // accidentally replaced by a fixed measured size.
        LogicalSize = new Vector2(
            IsFinitePositive(width) ? width : 0f,
            IsFinitePositive(height) ? height : 0f);
    }

    public void CompletePass()
    {
        m_fillBatch.MarkDirtyRepaint();
    }

    private static bool IsFinitePositive(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class NativeFillBatch : VisualElement
{
    private readonly List<FillCommand> m_commands = new();

    public NativeFillBatch()
    {
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = 0f;
        style.right = 0f;
        style.top = 0f;
        style.bottom = 0f;
        generateVisualContent += DrawCommands;
    }

    public void ClearCommands()
    {
        m_commands.Clear();
    }

    public void Add(Rect rect, Color color)
    {
        if (rect.width <= 0f || rect.height <= 0f || color.a <= 0f)
        {
            return;
        }
        m_commands.Add(new FillCommand(rect, color));
    }

    private void DrawCommands(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        foreach (var command in m_commands)
        {
            var rect = command.Rect;
            painter.fillColor = command.Color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);
        }
    }

    private readonly struct FillCommand
    {
        public FillCommand(Rect rect, Color color)
        {
            Rect = rect;
            Color = color;
        }

        public Rect Rect { get; }
        public Color Color { get; }
    }
}
