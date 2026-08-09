using System;
using System.Globalization;
using MultiLangLib;
using Mafi;

namespace UNMA.Localization;

/// <summary>
/// Small, failure-tolerant boundary around MultiLangLib. UNMA deliberately does
/// not configure MultiLangLib: language selection and cache ownership belong to
/// the MultiLangLib mod itself.
/// </summary>
public static class UnmaText
{
    public const string ModId = "UNMA";

    public static void Initialize(string modRoot)
    {
        Lang.RegisterMod(ModId, modRoot);
    }

    public static bool TryRegisterProvider(
        string localizationNamespace,
        string providerRoot,
        out string error)
    {
        try
        {
            Lang.RegisterMod(localizationNamespace, providerRoot);
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Log.Warning(
                $"UNMA: MultiLangLib-Provider '{localizationNamespace}' " +
                $"konnte nicht registriert werden: {exception.Message}");
            return false;
        }
    }

    public static bool IsValidNamespace(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > 128 ||
            !IsAsciiLetterOrDigit(candidate[0]))
        {
            return false;
        }
        for (var index = 1; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (!IsAsciiLetterOrDigit(character) && character != '_' &&
                character != '-')
            {
                return false;
            }
        }
        return true;
    }

    public static string Get(string textId, string fallback)
    {
        return Resolve("multilanglib." + ModId + "." + textId, fallback);
    }

    public static string Resolve(string canonicalKey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
        {
            return fallback ?? "";
        }

        try
        {
            return Lang.TryGet(canonicalKey, out var translated)
                ? translated
                : fallback ?? canonicalKey;
        }
        catch (Exception exception)
        {
            Log.Warning(
                $"UNMA: Übersetzung '{canonicalKey}' konnte nicht " +
                $"gelesen werden: {exception.Message}");
            return fallback ?? canonicalKey;
        }
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

    public static void Reload()
    {
        Lang.Reload();
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character >= 'a' && character <= 'z' ||
               character >= 'A' && character <= 'Z' ||
               character >= '0' && character <= '9';
    }
}
