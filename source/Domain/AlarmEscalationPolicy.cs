using System;
using System.Collections.Generic;

namespace UNMA.Domain;

/// <summary>
/// Immutable presentation selected for one custom alarm evaluation. The
/// escalation latch itself remains runtime-only and is fed back through
/// <paramref name="wasEscalated"/> on the next evaluation.
/// </summary>
public readonly struct AlarmEscalationEvaluation
{
    public bool IsEscalated { get; }
    public bool JustEscalated { get; }
    public AlarmSeverity Severity { get; }
    public string SoundId { get; }
    public AlarmOperatorAction OperatorAction { get; }

    internal AlarmEscalationEvaluation(
        bool isEscalated,
        bool justEscalated,
        AlarmSeverity severity,
        string soundId,
        AlarmOperatorAction operatorAction)
    {
        IsEscalated = isEscalated;
        JustEscalated = justEscalated;
        Severity = severity;
        SoundId = soundId;
        OperatorAction = operatorAction;
    }
}

/// <summary>
/// Pure game-time policy for a single, sticky custom-alarm escalation.
/// Escalation begins only after the timed rule itself became active, survives
/// game pauses and clock rollback once raised, and resets when the rule clears
/// or its escalation definition is disabled.
/// </summary>
public static class AlarmEscalationPolicy
{
    public const int BaseOccurrencePriority = 0;
    public const int EscalatedOccurrencePriority = 1;
    public const string EscalatedOccurrenceSuffix = ":escalated";

    public static AlarmEscalationDefinition LegacyMigrationDefaults => new();

    public static AlarmEscalationDefinition Clone(
        AlarmEscalationDefinition source)
    {
        if (source == null)
        {
            return new AlarmEscalationDefinition();
        }
        return new AlarmEscalationDefinition
        {
            Enabled = source.Enabled,
            AfterTicks = source.AfterTicks,
            Severity = source.Severity,
            SoundId = source.SoundId,
            OperatorAction = source.OperatorAction,
        };
    }

    public static AlarmEscalationDefinition Normalize(
        AlarmEscalationDefinition definition,
        AlarmSeverity baseSeverity)
    {
        definition ??= new AlarmEscalationDefinition();
        var baseSeverityIsValid = IsSeverity(baseSeverity);
        var targetSeverityIsValid = IsSeverity(definition.Severity);
        var targetSeverity = targetSeverityIsValid
            ? definition.Severity
            : DefaultTargetSeverity(baseSeverity);
        var afterTicks = Math.Max(
            0,
            Math.Min(
                AlarmTimingPolicy.MaximumTimingTicks,
                definition.AfterTicks));
        var operatorAction = NormalizeOperatorAction(
            definition.OperatorAction);
        var soundId = definition.SoundId?.Trim() ?? "";
        var enabled = definition.Enabled &&
                      afterTicks > 0 &&
                      baseSeverityIsValid &&
                      targetSeverityIsValid &&
                      targetSeverity > baseSeverity &&
                      baseSeverity < AlarmSeverity.Emergency;
        return new AlarmEscalationDefinition
        {
            Enabled = enabled,
            AfterTicks = afterTicks,
            Severity = targetSeverity,
            SoundId = soundId,
            OperatorAction = operatorAction,
        };
    }

    public static AlarmOperatorAction NormalizeOperatorAction(
        AlarmOperatorAction operatorAction)
    {
        return Enum.IsDefined(
            typeof(AlarmOperatorAction),
            operatorAction)
            ? operatorAction
            : AlarmOperatorAction.None;
    }

    public static AlarmEscalationEvaluation Evaluate(
        AlarmEscalationDefinition definition,
        AlarmSeverity baseSeverity,
        string baseSoundId,
        bool wasEscalated,
        bool isAlarmActive,
        long activeSinceGameTick,
        long currentGameTick)
    {
        var normalizedBaseSeverity = IsSeverity(baseSeverity)
            ? baseSeverity
            : AlarmSeverity.Warning;
        var normalizedBaseSoundId = string.IsNullOrWhiteSpace(baseSoundId)
            ? "auto"
            : baseSoundId.Trim();
        var normalized = Normalize(definition, baseSeverity);
        if (!isAlarmActive || !normalized.Enabled)
        {
            return Base(
                normalizedBaseSeverity,
                normalizedBaseSoundId);
        }

        var isEscalated = wasEscalated;
        if (!isEscalated &&
            activeSinceGameTick >= 0 &&
            currentGameTick >= activeSinceGameTick &&
            currentGameTick - activeSinceGameTick >= normalized.AfterTicks)
        {
            isEscalated = true;
        }
        if (!isEscalated)
        {
            return Base(
                normalizedBaseSeverity,
                normalizedBaseSoundId);
        }

        var justEscalated = !wasEscalated;
        return new AlarmEscalationEvaluation(
            isEscalated: true,
            justEscalated,
            normalized.Severity,
            string.IsNullOrWhiteSpace(normalized.SoundId)
                ? normalizedBaseSoundId
                : normalized.SoundId,
            justEscalated
                ? normalized.OperatorAction
                : AlarmOperatorAction.None);
    }

    public static string GetOccurrenceId(
        string ruleId,
        bool isEscalated)
    {
        var normalizedRuleId = ruleId?.Trim() ?? "";
        return isEscalated
            ? normalizedRuleId + EscalatedOccurrenceSuffix
            : normalizedRuleId;
    }

    public static bool IsEscalatedOccurrenceId(
        string ruleId,
        string occurrenceId)
    {
        return string.Equals(
            occurrenceId?.Trim(),
            GetOccurrenceId(ruleId, isEscalated: true),
            StringComparison.Ordinal);
    }

    private static AlarmEscalationEvaluation Base(
        AlarmSeverity severity,
        string soundId)
    {
        return new AlarmEscalationEvaluation(
            isEscalated: false,
            justEscalated: false,
            severity,
            soundId,
            AlarmOperatorAction.None);
    }

    private static AlarmSeverity DefaultTargetSeverity(
        AlarmSeverity baseSeverity)
    {
        return IsSeverity(baseSeverity) &&
               baseSeverity < AlarmSeverity.Emergency
            ? (AlarmSeverity)((int)baseSeverity + 1)
            : AlarmSeverity.Emergency;
    }

    private static bool IsSeverity(AlarmSeverity severity)
    {
        return Enum.IsDefined(typeof(AlarmSeverity), severity);
    }
}

/// <summary>
/// Immutable hand-off from alarm evaluation to the UI. It contains only
/// presentation intent; it cannot mutate simulation state.
/// </summary>
public readonly struct AlarmAttentionRequest
{
    public string AlarmKey { get; }
    public long Sequence { get; }
    public string PanelId { get; }
    public string SlotId { get; }
    public AlarmSeverity Severity { get; }
    public AlarmOperatorAction OperatorAction { get; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(AlarmKey) &&
        Sequence > 0 &&
        Enum.IsDefined(typeof(AlarmSeverity), Severity) &&
        AlarmEscalationPolicy.NormalizeOperatorAction(OperatorAction) !=
        AlarmOperatorAction.None;

    public AlarmAttentionRequest(
        string alarmKey,
        long sequence,
        string panelId,
        string slotId,
        AlarmSeverity severity,
        AlarmOperatorAction operatorAction)
    {
        AlarmKey = alarmKey?.Trim() ?? "";
        Sequence = sequence;
        PanelId = panelId?.Trim() ?? "";
        SlotId = slotId?.Trim() ?? "";
        Severity = severity;
        OperatorAction = operatorAction;
    }
}

/// <summary>
/// Bounded, deterministic queue operations. Callers supply the current-alarm
/// predicate while holding their own alarm-state lock; the policy itself owns
/// no threads, timers, callbacks or persistence.
/// </summary>
public static class AlarmAttentionQueuePolicy
{
    public const int MaximumPendingRequests = 64;

    public static bool TryEnqueue(
        IList<AlarmAttentionRequest> requests,
        AlarmAttentionRequest request)
    {
        if (requests == null || !request.IsValid)
        {
            return false;
        }

        for (var index = requests.Count - 1; index >= 0; index--)
        {
            var existing = requests[index];
            if (!existing.IsValid)
            {
                requests.RemoveAt(index);
                continue;
            }
            if (!string.Equals(
                    existing.AlarmKey,
                    request.AlarmKey,
                    StringComparison.Ordinal))
            {
                continue;
            }
            if (existing.Sequence > request.Sequence)
            {
                return false;
            }
            requests.RemoveAt(index);
        }

        while (requests.Count >= MaximumPendingRequests)
        {
            requests.RemoveAt(0);
        }
        requests.Add(request);
        return true;
    }

    public static bool TryTakeBest(
        IList<AlarmAttentionRequest> requests,
        Func<AlarmAttentionRequest, bool> isStillRelevant,
        out AlarmAttentionRequest request)
    {
        request = default;
        if (requests == null)
        {
            return false;
        }

        for (var index = requests.Count - 1; index >= 0; index--)
        {
            var candidate = requests[index];
            if (!candidate.IsValid ||
                isStillRelevant != null && !isStillRelevant(candidate))
            {
                requests.RemoveAt(index);
            }
        }
        if (requests.Count == 0)
        {
            return false;
        }

        var bestIndex = 0;
        for (var index = 1; index < requests.Count; index++)
        {
            if (Compare(requests[index], requests[bestIndex]) > 0)
            {
                bestIndex = index;
            }
        }
        request = requests[bestIndex];
        requests.RemoveAt(bestIndex);
        return true;
    }

    private static int Compare(
        AlarmAttentionRequest left,
        AlarmAttentionRequest right)
    {
        var severityComparison = left.Severity.CompareTo(right.Severity);
        if (severityComparison != 0)
        {
            return severityComparison;
        }
        var actionComparison = left.OperatorAction.CompareTo(
            right.OperatorAction);
        if (actionComparison != 0)
        {
            return actionComparison;
        }
        return left.Sequence.CompareTo(right.Sequence);
    }
}
