using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace UNMA.Localization;

/// <summary>
/// Resolves the German production catalog for source files linked into the
/// dependency-free core test executable.
/// </summary>
public static class UnmaText
{
    private static readonly IReadOnlyDictionary<string, string> s_catalog =
        LoadCatalog();

    public static string Get(string textId)
    {
        return s_catalog.TryGetValue(textId, out var text) ? text : textId;
    }

    public static string Get(string textId, string fallback)
    {
        return s_catalog.TryGetValue(textId, out var text)
            ? text
            : fallback ?? textId;
    }

    public static string Format(
        string textId,
        string fallback,
        params object[] arguments)
    {
        var template = Get(textId, fallback);
        try
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                template,
                arguments ?? Array.Empty<object>());
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public static string Format(string textId, params object[] arguments) =>
        Format(textId, textId, arguments);

    private static IReadOnlyDictionary<string, string> LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "lang", "de.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }
}
