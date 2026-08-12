using System;

namespace UNMA.Ui;

internal static class WindowResizeMath
{
    public static float NormalizePreferredExtent(
        float requested,
        float fallback)
    {
        return IsFinitePositive(requested)
            ? requested
            : Math.Max(1f, fallback);
    }

    public static float ResolveEffectiveExtent(
        float preferred,
        float minimum,
        float maximum)
    {
        maximum = Math.Max(1f, maximum);
        minimum = Math.Max(1f, Math.Min(minimum, maximum));
        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                NormalizePreferredExtent(preferred, minimum)));
    }

    public static float NormalizePreferredCoordinate(
        float requested,
        float fallback)
    {
        return IsFinite(requested)
            ? requested
            : IsFinite(fallback) ? fallback : 0f;
    }

    public static float ResolveEffectiveCoordinate(
        float preferred,
        float windowExtent,
        float viewportExtent)
    {
        var normalizedPreferred = NormalizePreferredCoordinate(
            preferred,
            0f);
        var maximum = Math.Max(
            0f,
            NormalizePreferredExtent(viewportExtent, 1f) -
            NormalizePreferredExtent(windowExtent, 1f));
        return Math.Max(0f, Math.Min(maximum, normalizedPreferred));
    }

    public static float GetHandleOrigin(
        float windowExtent,
        float handleSize,
        float inset)
    {
        return windowExtent - handleSize - inset;
    }

    public static bool IsInsideHandle(
        float windowWidth,
        float windowHeight,
        float mouseX,
        float mouseY,
        float handleSize,
        float inset)
    {
        var left = GetHandleOrigin(windowWidth, handleSize, inset);
        var top = GetHandleOrigin(windowHeight, handleSize, inset);
        return mouseX >= left &&
               mouseX < left + handleSize &&
               mouseY >= top &&
               mouseY < top + handleSize;
    }

    public static float ResizeExtent(
        float startExtent,
        float delta,
        float minimum,
        float maximum)
    {
        maximum = Math.Max(minimum, maximum);
        return Math.Max(
            minimum,
            Math.Min(maximum, startExtent + delta));
    }

    private static bool IsFinitePositive(float value)
    {
        return IsFinite(value) && value > 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
