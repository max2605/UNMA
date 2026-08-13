using System;

namespace UNMA.Ui;

/// <summary>
/// Matches metric picker searches without coupling the filtering rule to the
/// Unity-backed picker UI.
/// </summary>
internal static class MetricPickerFilter
{
    public static bool Matches(
        string label,
        string path,
        string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var searchText = filter.Trim();
        return (label ?? string.Empty).IndexOf(
                   searchText,
                   StringComparison.CurrentCultureIgnoreCase) >= 0 ||
               (path ?? string.Empty).IndexOf(
                   searchText,
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
