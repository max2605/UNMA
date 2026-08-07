using System;
using System.Collections.Generic;

namespace UNMA.Api;

internal static class ExternalContractValidator
{
    internal const int MaxConditionsPerAlarm = 32;
    internal const int MaxPrototypeIdsPerAlarm = 128;

    private const int MaxOwnerLength = 128;
    private const int MaxIdLength = 192;
    private const int MaxPrototypeLength = 256;
    private const int MaxKeyLength = 256;
    private const int MaxFallbackLength = 4096;

    internal static bool TryNormalizeMetric(
        string ownerModId,
        ExternalMetricDefinition definition,
        out ExternalMetricSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        if (!TryNormalizeOwner(ownerModId, out var owner, out error))
        {
            return false;
        }

        if (definition == null)
        {
            error = "Metric definition is required.";
            return false;
        }

        if (!TryNormalizeToken(
                definition.Id,
                "metric id",
                MaxIdLength,
                allowWildcard: false,
                out var id,
                out error))
        {
            return false;
        }

        var prototypeId = string.IsNullOrWhiteSpace(definition.PrototypeId)
            ? "*"
            : definition.PrototypeId.Trim();
        if (!TryValidatePrototype(prototypeId, allowWildcard: true, out error))
        {
            return false;
        }

        if (definition.Reader == null)
        {
            error = "Metric reader callback is required.";
            return false;
        }

        if (!TryNormalizeOptional(
                definition.LabelKey,
                "metric label key",
                MaxKeyLength,
                out var labelKey,
                out error) ||
            !TryNormalizeOptional(
                definition.LabelFallback,
                "metric label fallback",
                MaxFallbackLength,
                out var labelFallback,
                out error) ||
            !TryNormalizeOptional(
                definition.Unit,
                "metric unit",
                64,
                out var unit,
                out error) ||
            !TryNormalizeOptional(
                definition.SuggestedReferenceMetric,
                "suggested reference metric",
                MaxIdLength,
                out var suggestedReference,
                out error))
        {
            return false;
        }

        if (!TryValidateAnyLocalizationKey(
                labelKey,
                "metric label key",
                out error))
        {
            return false;
        }

        if (suggestedReference.Length > 0 &&
            ContainsWhitespaceOrControl(suggestedReference))
        {
            error = "Suggested reference metric contains whitespace or " +
                    "control characters.";
            return false;
        }

        snapshot = new ExternalMetricSnapshot(
            owner,
            id,
            prototypeId,
            labelKey,
            labelFallback,
            unit,
            suggestedReference,
            definition.Reader);
        error = "";
        return true;
    }

    internal static bool TryNormalizeTemplate(
        string ownerModId,
        ExternalAlarmTemplateDefinition definition,
        out ExternalAlarmTemplateSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        if (!TryNormalizeOwner(ownerModId, out var owner, out error))
        {
            return false;
        }

        if (definition == null)
        {
            error = "Alarm template definition is required.";
            return false;
        }

        if (!TryNormalizeToken(
                definition.Id,
                "alarm id",
                MaxIdLength,
                allowWildcard: false,
                out var id,
                out error))
        {
            return false;
        }

        var prototypeIds = definition.PrototypeIds;
        if (prototypeIds == null || prototypeIds.Count == 0)
        {
            error = "At least one prototype_ids entry is required.";
            return false;
        }

        if (prototypeIds.Count > MaxPrototypeIdsPerAlarm)
        {
            error = "An alarm may target at most " +
                    MaxPrototypeIdsPerAlarm + " prototype ids.";
            return false;
        }

        var normalizedPrototypes = new List<string>(prototypeIds.Count);
        var seenPrototypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in prototypeIds)
        {
            var prototypeId = candidate?.Trim() ?? "";
            if (!TryValidatePrototype(
                    prototypeId,
                    allowWildcard: false,
                    out error))
            {
                return false;
            }

            if (!seenPrototypes.Add(prototypeId))
            {
                error = "Duplicate prototype id '" + prototypeId + "'.";
                return false;
            }

            normalizedPrototypes.Add(prototypeId);
        }

        var scope = NormalizeChoice(definition.Scope, "aggregate");
        if (scope == "per-entity")
        {
            scope = "per_entity";
        }
        if (scope != "aggregate" && scope != "per_entity")
        {
            error = "Alarm scope must be 'aggregate' or 'per_entity'.";
            return false;
        }

        var panelId = string.IsNullOrWhiteSpace(definition.PanelId)
            ? "main"
            : definition.PanelId.Trim();
        if (!TryNormalizeToken(
                panelId,
                "panel id",
                MaxIdLength,
                allowWildcard: false,
                out panelId,
                out error))
        {
            return false;
        }

        var localizationNamespace = string.IsNullOrWhiteSpace(
            definition.LocalizationNamespace)
            ? owner
            : definition.LocalizationNamespace.Trim();
        if (!TryNormalizeLocalizationNamespace(
                localizationNamespace,
                out localizationNamespace,
                out error))
        {
            error = "Invalid localization namespace. " + error;
            return false;
        }

        if (!TryNormalizeOptional(
                definition.MessageKey,
                "message key",
                MaxKeyLength,
                out var messageKey,
                out error) ||
            !TryNormalizeOptional(
                definition.MessageFallback,
                "message fallback",
                MaxFallbackLength,
                out var messageFallback,
                out error) ||
            !TryNormalizeOptional(
                definition.DetailKey,
                "detail key",
                MaxKeyLength,
                out var detailKey,
                out error) ||
            !TryNormalizeOptional(
                definition.DetailFallback,
                "detail fallback",
                MaxFallbackLength,
                out var detailFallback,
                out error))
        {
            return false;
        }

        if (messageKey.Length == 0 && messageFallback.Length == 0)
        {
            error = "An alarm requires message_key or message_fallback.";
            return false;
        }

        if (!TryValidateLocalizationKey(
                localizationNamespace,
                messageKey,
                "message key",
                out error) ||
            !TryValidateLocalizationKey(
                localizationNamespace,
                detailKey,
                "detail key",
                out error))
        {
            return false;
        }

        if (!TryNormalizeSeverity(
                definition.Severity,
                out var severity,
                out error) ||
            !TryNormalizeSound(
                definition.SoundId,
                out var soundId,
                out error) ||
            !TryNormalizeColor(
                definition.ActiveColor,
                out var activeColor,
                out error) ||
            !TryNormalizeLogic(
                definition.Logic,
                out var logic,
                out error))
        {
            return false;
        }

        var conditions = definition.Conditions;
        if (conditions == null || conditions.Count == 0)
        {
            error = "At least one alarm condition is required.";
            return false;
        }

        if (conditions.Count > MaxConditionsPerAlarm)
        {
            error = "An alarm may contain at most " +
                    MaxConditionsPerAlarm + " conditions.";
            return false;
        }

        var normalizedConditions =
            new List<ExternalAlarmConditionSnapshot>(conditions.Count);
        for (var index = 0; index < conditions.Count; index++)
        {
            if (!TryNormalizeCondition(
                    conditions[index],
                    localizationNamespace,
                    out var condition,
                    out error))
            {
                error = "Condition " + (index + 1) + ": " + error;
                return false;
            }

            normalizedConditions.Add(condition);
        }

        snapshot = new ExternalAlarmTemplateSnapshot(
            owner,
            id,
            normalizedPrototypes,
            scope,
            panelId,
            localizationNamespace,
            messageKey,
            messageFallback,
            detailKey,
            detailFallback,
            severity,
            soundId,
            activeColor,
            definition.AutoAcknowledgeOnClear,
            logic,
            normalizedConditions);
        error = "";
        return true;
    }

    internal static bool TryNormalizeState(
        string ownerModId,
        ExternalAlarmState state,
        out ExternalAlarmStateSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        if (!TryNormalizeOwner(ownerModId, out var owner, out error))
        {
            return false;
        }

        if (state == null)
        {
            error = "Alarm state is required.";
            return false;
        }

        if (!TryNormalizeToken(
                state.Id,
                "alarm id",
                MaxIdLength,
                allowWildcard: false,
                out var id,
                out error))
        {
            return false;
        }

        var instanceId = string.IsNullOrWhiteSpace(state.InstanceId)
            ? "default"
            : state.InstanceId.Trim();
        if (!TryNormalizeToken(
                instanceId,
                "alarm instance id",
                MaxIdLength,
                allowWildcard: false,
                out instanceId,
                out error))
        {
            return false;
        }

        var panelId = string.IsNullOrWhiteSpace(state.PanelId)
            ? "main"
            : state.PanelId.Trim();
        if (!TryNormalizeToken(
                panelId,
                "panel id",
                MaxIdLength,
                allowWildcard: false,
                out panelId,
                out error))
        {
            return false;
        }

        var prototypeId = state.PrototypeId?.Trim() ?? "";
        if (prototypeId.Length > 0 &&
            !TryValidatePrototype(
                prototypeId,
                allowWildcard: false,
                out error))
        {
            return false;
        }

        if (!TryNormalizeOptional(
                state.EntityKey,
                "entity key",
                MaxIdLength,
                out var entityKey,
                out error))
        {
            return false;
        }

        var localizationNamespace = string.IsNullOrWhiteSpace(
            state.LocalizationNamespace)
            ? owner
            : state.LocalizationNamespace.Trim();
        if (!TryNormalizeLocalizationNamespace(
                localizationNamespace,
                out localizationNamespace,
                out error))
        {
            error = "Invalid localization namespace. " + error;
            return false;
        }

        if (!TryNormalizeOptional(
                state.MessageKey,
                "message key",
                MaxKeyLength,
                out var messageKey,
                out error) ||
            !TryNormalizeOptional(
                state.MessageFallback,
                "message fallback",
                MaxFallbackLength,
                out var messageFallback,
                out error) ||
            !TryNormalizeOptional(
                state.DetailKey,
                "detail key",
                MaxKeyLength,
                out var detailKey,
                out error) ||
            !TryNormalizeOptional(
                state.DetailFallback,
                "detail fallback",
                MaxFallbackLength,
                out var detailFallback,
                out error))
        {
            return false;
        }

        if (messageKey.Length == 0 && messageFallback.Length == 0)
        {
            error = "An alarm state requires message_key or message_fallback.";
            return false;
        }

        if (!TryValidateLocalizationKey(
                localizationNamespace,
                messageKey,
                "message key",
                out error) ||
            !TryValidateLocalizationKey(
                localizationNamespace,
                detailKey,
                "detail key",
                out error))
        {
            return false;
        }

        if (!TryNormalizeSeverity(
                state.Severity,
                out var severity,
                out error) ||
            !TryNormalizeSound(
                state.SoundId,
                out var soundId,
                out error) ||
            !TryNormalizeColor(
                state.ActiveColor,
                out var activeColor,
                out error))
        {
            return false;
        }

        if (state.CurrentValue.HasValue &&
            (double.IsNaN(state.CurrentValue.Value) ||
             double.IsInfinity(state.CurrentValue.Value)))
        {
            error = "Current alarm value must be finite.";
            return false;
        }

        snapshot = new ExternalAlarmStateSnapshot(
            owner,
            id,
            instanceId,
            state.Active,
            panelId,
            prototypeId,
            entityKey,
            localizationNamespace,
            messageKey,
            messageFallback,
            detailKey,
            detailFallback,
            severity,
            soundId,
            activeColor,
            state.AutoAcknowledgeOnClear,
            state.CurrentValue);
        error = "";
        return true;
    }

    internal static bool TryNormalizeOwner(
        string ownerModId,
        out string owner,
        out string error)
    {
        owner = ownerModId?.Trim() ?? "";
        if (owner.Length == 0 || owner.Length > MaxOwnerLength)
        {
            error = "Provider mod id must contain 1 to " + MaxOwnerLength +
                    " characters.";
            return false;
        }

        if (!IsAsciiLetterOrDigit(owner[0]))
        {
            error = "Provider mod id must start with an ASCII letter or " +
                    "digit.";
            return false;
        }

        foreach (var character in owner)
        {
            if (!IsAsciiLetterOrDigit(character) && character != '.' &&
                character != '_' && character != '-')
            {
                error = "Provider mod id may only contain ASCII letters, " +
                        "digits, '.', '_' and '-'.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeCondition(
        ExternalAlarmConditionDefinition definition,
        string localizationNamespace,
        out ExternalAlarmConditionSnapshot snapshot,
        out string error)
    {
        snapshot = null;
        if (definition == null)
        {
            error = "Condition definition is required.";
            return false;
        }

        if (!TryNormalizeToken(
                definition.Metric,
                "metric",
                MaxIdLength,
                allowWildcard: false,
                out var metric,
                out error))
        {
            return false;
        }

        if (!TryNormalizeOperator(
                definition.Operator,
                out var comparisonOperator,
                out error))
        {
            return false;
        }

        if (double.IsNaN(definition.Threshold) ||
            double.IsInfinity(definition.Threshold))
        {
            error = "Threshold must be finite.";
            return false;
        }

        var valueMode = NormalizeChoice(definition.ValueMode, "absolute");
        switch (valueMode)
        {
            case "%":
            case "percent":
            case "percentage":
            case "percent-of-reference":
                valueMode = "percent_of_reference";
                break;
        }

        if (valueMode != "absolute" && valueMode != "percent_of_reference")
        {
            error = "Value mode must be 'absolute' or " +
                    "'percent_of_reference'.";
            return false;
        }

        if (!TryNormalizeOptional(
                definition.ReferenceMetric,
                "reference metric",
                MaxIdLength,
                out var referenceMetric,
                out error) ||
            !TryNormalizeOptional(
                definition.LabelKey,
                "condition label key",
                MaxKeyLength,
                out var labelKey,
                out error) ||
            !TryNormalizeOptional(
                definition.LabelFallback,
                "condition label fallback",
                MaxFallbackLength,
                out var labelFallback,
                out error) ||
            !TryNormalizeOptional(
                definition.ReferenceLabelKey,
                "reference label key",
                MaxKeyLength,
                out var referenceLabelKey,
                out error) ||
            !TryNormalizeOptional(
                definition.ReferenceLabelFallback,
                "reference label fallback",
                MaxFallbackLength,
                out var referenceLabelFallback,
                out error))
        {
            return false;
        }

        if (valueMode == "percent_of_reference" &&
            referenceMetric.Length == 0)
        {
            error = "reference_metric is required for percent values.";
            return false;
        }

        if (referenceMetric.Length > 0 &&
            ContainsWhitespaceOrControl(referenceMetric))
        {
            error = "reference_metric contains whitespace or control " +
                    "characters.";
            return false;
        }

        if (!TryValidateLocalizationKey(
                localizationNamespace,
                labelKey,
                "condition label key",
                out error) ||
            !TryValidateLocalizationKey(
                localizationNamespace,
                referenceLabelKey,
                "reference label key",
                out error))
        {
            return false;
        }

        snapshot = new ExternalAlarmConditionSnapshot(
            metric,
            comparisonOperator,
            definition.Threshold,
            valueMode,
            referenceMetric,
            labelKey,
            labelFallback,
            referenceLabelKey,
            referenceLabelFallback);
        error = "";
        return true;
    }

    private static bool TryNormalizeOperator(
        string candidate,
        out string normalized,
        out string error)
    {
        normalized = NormalizeChoice(candidate, "<");
        switch (normalized)
        {
            case "<":
            case "less":
                normalized = "<";
                break;
            case "<=":
            case "less_or_equal":
            case "less-or-equal":
                normalized = "<=";
                break;
            case "=":
            case "==":
            case "equal":
                normalized = "==";
                break;
            case "!=":
            case "<>":
            case "not_equal":
            case "not-equal":
                normalized = "!=";
                break;
            case ">=":
            case "greater_or_equal":
            case "greater-or-equal":
                normalized = ">=";
                break;
            case ">":
            case "greater":
                normalized = ">";
                break;
            default:
                error = "Unsupported comparison operator '" + normalized +
                        "'.";
                return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeSeverity(
        string candidate,
        out string severity,
        out string error)
    {
        severity = NormalizeChoice(candidate, "warning");
        if (severity == "info")
        {
            severity = "notice";
        }

        if (severity != "notice" && severity != "warning" &&
            severity != "critical" && severity != "emergency")
        {
            error = "Severity must be notice, warning, critical, or " +
                    "emergency.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeLogic(
        string candidate,
        out string logic,
        out string error)
    {
        logic = NormalizeChoice(candidate, "all");
        if (logic == "and")
        {
            logic = "all";
        }
        else if (logic == "or")
        {
            logic = "any";
        }

        if (logic != "all" && logic != "any")
        {
            error = "Alarm logic must be 'all' or 'any'.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeSound(
        string candidate,
        out string soundId,
        out string error)
    {
        soundId = string.IsNullOrWhiteSpace(candidate)
            ? "auto"
            : candidate.Trim();
        if (soundId.Length > 128 || ContainsControlCharacter(soundId))
        {
            error = "Sound id is invalid or too long.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeColor(
        string candidate,
        out string color,
        out string error)
    {
        color = string.IsNullOrWhiteSpace(candidate)
            ? "auto"
            : candidate.Trim();
        if (string.Equals(color, "auto", StringComparison.OrdinalIgnoreCase))
        {
            color = "auto";
            error = "";
            return true;
        }

        if ((color.Length != 7 && color.Length != 9) || color[0] != '#')
        {
            error = "Active color must be 'auto', #RRGGBB, or #RRGGBBAA.";
            return false;
        }

        for (var index = 1; index < color.Length; index++)
        {
            var character = color[index];
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
            {
                error = "Active color contains a non-hexadecimal digit.";
                return false;
            }
        }

        color = color.ToUpperInvariant();
        error = "";
        return true;
    }

    private static bool TryValidatePrototype(
        string prototypeId,
        bool allowWildcard,
        out string error)
    {
        if (prototypeId.Length == 0 ||
            prototypeId.Length > MaxPrototypeLength ||
            ContainsWhitespaceOrControl(prototypeId) ||
            ContainsInvalidSurrogateSequence(prototypeId) ||
            (!allowWildcard && prototypeId == "*"))
        {
            error = "Prototype id is empty, too long, or contains " +
                    "whitespace/control characters or invalid UTF-16.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeLocalizationNamespace(
        string candidate,
        out string normalized,
        out string error)
    {
        normalized = candidate?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > MaxOwnerLength ||
            !IsAsciiLetterOrDigit(normalized[0]))
        {
            error = "LangLib namespace must start with an ASCII letter or " +
                    "digit.";
            return false;
        }

        for (var index = 1; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (!IsAsciiLetterOrDigit(character) && character != '_' &&
                character != '-')
            {
                error = "LangLib namespace may only contain ASCII letters, " +
                        "digits, '_' and '-'.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool TryValidateLocalizationKey(
        string localizationNamespace,
        string key,
        string fieldName,
        out string error)
    {
        if (key.Length == 0)
        {
            error = "";
            return true;
        }

        var prefix = "langlib." + localizationNamespace + ".";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            key.Length == prefix.Length)
        {
            error = fieldName + " must start with '" + prefix + "'.";
            return false;
        }

        for (var index = prefix.Length; index < key.Length; index++)
        {
            var character = key[index];
            if (!IsAsciiLetterOrDigit(character) && character != '.' &&
                character != '_' && character != '-')
            {
                error = fieldName + " is not a valid LangLib key.";
                return false;
            }
        }

        if (!IsAsciiLetterOrDigit(key[prefix.Length]))
        {
            error = fieldName + " text id must start with a letter or digit.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidateAnyLocalizationKey(
        string key,
        string fieldName,
        out string error)
    {
        if (key.Length == 0)
        {
            error = "";
            return true;
        }

        const string langLibPrefix = "langlib.";
        if (!key.StartsWith(langLibPrefix, StringComparison.Ordinal))
        {
            error = fieldName + " must be a full 'langlib.<ModId>.<textId>' " +
                    "key.";
            return false;
        }

        var remainder = key.Substring(langLibPrefix.Length);
        var separator = remainder.IndexOf('.');
        if (separator <= 0 || separator == remainder.Length - 1 ||
            !TryNormalizeLocalizationNamespace(
                remainder.Substring(0, separator),
                out _,
                out _))
        {
            error = fieldName + " is not a valid LangLib key.";
            return false;
        }

        var textId = remainder.Substring(separator + 1);
        if (!IsAsciiLetterOrDigit(textId[0]))
        {
            error = fieldName + " text id must start with a letter or digit.";
            return false;
        }

        foreach (var character in textId)
        {
            if (!IsAsciiLetterOrDigit(character) && character != '.' &&
                character != '_' && character != '-')
            {
                error = fieldName + " is not a valid LangLib key.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeToken(
        string candidate,
        string fieldName,
        int maximumLength,
        bool allowWildcard,
        out string normalized,
        out string error)
    {
        normalized = candidate?.Trim() ?? "";
        if (normalized.Length == 0 || normalized.Length > maximumLength ||
            ContainsWhitespaceOrControl(normalized) ||
            ContainsInvalidSurrogateSequence(normalized) ||
            (!allowWildcard && normalized == "*"))
        {
            error = fieldName + " is empty, too long, or contains " +
                    "whitespace/control characters or invalid UTF-16.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryNormalizeOptional(
        string candidate,
        string fieldName,
        int maximumLength,
        out string normalized,
        out string error)
    {
        normalized = candidate?.Trim() ?? "";
        if (normalized.Length > maximumLength ||
            ContainsControlCharacter(normalized) ||
            ContainsInvalidSurrogateSequence(normalized))
        {
            error = fieldName + " is too long or contains control " +
                    "characters or invalid UTF-16.";
            return false;
        }

        error = "";
        return true;
    }

    private static string NormalizeChoice(string candidate, string fallback)
    {
        return string.IsNullOrWhiteSpace(candidate)
            ? fallback
            : candidate.Trim().ToLowerInvariant();
    }

    private static bool ContainsWhitespaceOrControl(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInvalidSurrogateSequence(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length ||
                    !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiLetterOrDigit(char value)
    {
        return value >= 'a' && value <= 'z' ||
               value >= 'A' && value <= 'Z' ||
               value >= '0' && value <= '9';
    }
}
