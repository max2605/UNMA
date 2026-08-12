using UnityEngine;

namespace UNMA.Ui;

internal static class CoiUiPalette
{
    // Captain of Industry UI neutrals (2026 palette).
    public static readonly Color Window = Rgb(0x19, 0x1A, 0x1C);
    public static readonly Color SurfaceDark = Rgb(0x2A, 0x2C, 0x33);
    public static readonly Color Surface = Rgb(0x2B, 0x2D, 0x32);
    public static readonly Color SurfaceRaised = Rgb(0x30, 0x33, 0x38);
    public static readonly Color Control = Rgb(0x34, 0x37, 0x3C);
    public static readonly Color Border = Rgb(0x3E, 0x41, 0x48);
    public static readonly Color BorderSoft = Rgb(0x3D, 0x3F, 0x40);
    public static readonly Color BorderLight = Rgb(0x76, 0x77, 0x79);

    public static readonly Color InputBackground = Rgb(0x10, 0x35, 0x22);
    public static readonly Color InputBorder = Rgb(0x42, 0x73, 0x4C);

    public static readonly Color TextMuted = Rgb(0xA0, 0xA0, 0xA0);
    public static readonly Color Text = Rgb(0xC6, 0xC6, 0xC6);
    public static readonly Color Symbol = Rgb(0xDC, 0xE2, 0xE8);
    public static readonly Color TextBright = Rgb(0xF1, 0xF1, 0xF8);

    public static readonly Color Yellow = Rgb(0xE5, 0xCA, 0x5F);
    public static readonly Color Blue = Rgb(0x6E, 0xB9, 0xD7);
    public static readonly Color Green = Rgb(0x6E, 0xB6, 0x60);
    public static readonly Color Orange = Rgb(0xF9, 0x88, 0x41);
    public static readonly Color Purple = Rgb(0x9D, 0x41, 0xF9);

    public static Color WithAlpha(Color color, float alpha) =>
        new(color.r, color.g, color.b, alpha);

    public static Color ScaleRgb(Color color, float factor) =>
        new(color.r * factor, color.g * factor, color.b * factor, color.a);

    private static Color Rgb(byte red, byte green, byte blue) =>
        new(red / 255f, green / 255f, blue / 255f, 1f);
}
