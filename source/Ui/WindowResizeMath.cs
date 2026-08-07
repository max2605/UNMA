using System;

namespace UNMA.Ui;

internal static class WindowResizeMath
{
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
}
