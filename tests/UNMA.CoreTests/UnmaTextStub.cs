using System;
using System.Collections.Generic;
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

    private static IReadOnlyDictionary<string, string> LoadCatalog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "lang", "de.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>();
    }
}
