using System;

namespace UNMA.Ui;

/// <summary>
/// Pure, dependency-free UI decisions. Keeping these rules outside Unity makes
/// contrast and editor readiness deterministic and regression-testable.
/// </summary>
public static class AlarmUiErgonomics
{
    public const double MinimumNormalTextContrast = 4.5d;

    public static bool IsValidHtmlColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length != 7 || candidate[0] != '#')
        {
            return false;
        }
        for (var index = 1; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (!(character >= '0' && character <= '9') &&
                !(character >= 'a' && character <= 'f') &&
                !(character >= 'A' && character <= 'F'))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldUseLightText(
        double red,
        double green,
        double blue)
    {
        var background = RelativeLuminance(red, green, blue);
        return ContrastRatio(1d, background) >=
               ContrastRatio(background, 0d);
    }

    public static double BestTextContrast(
        double red,
        double green,
        double blue)
    {
        var background = RelativeLuminance(red, green, blue);
        return Math.Max(
            ContrastRatio(1d, background),
            ContrastRatio(background, 0d));
    }

    public static bool CanSaveRule(
        string title,
        bool hasTargetPanel,
        int conditionCount,
        bool colorIsValid,
        bool timingIsValid)
    {
        return !string.IsNullOrWhiteSpace(title) &&
               hasTargetPanel &&
               conditionCount > 0 &&
               colorIsValid &&
               timingIsValid;
    }

    private static double RelativeLuminance(
        double red,
        double green,
        double blue)
    {
        return 0.2126d * Linearize(red) +
               0.7152d * Linearize(green) +
               0.0722d * Linearize(blue);
    }

    private static double Linearize(double channel)
    {
        channel = Math.Max(0d, Math.Min(1d, channel));
        return channel <= 0.04045d
            ? channel / 12.92d
            : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
    }

    private static double ContrastRatio(double lighter, double darker)
    {
        if (lighter < darker)
        {
            var swap = lighter;
            lighter = darker;
            darker = swap;
        }
        return (lighter + 0.05d) / (darker + 0.05d);
    }
}
