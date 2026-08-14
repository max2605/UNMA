using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UNMA.Domain;

namespace UNMA.Ui;

/// <summary>
/// Keeps the full candidate context for a vanilla behavior interaction while
/// the notification-options UI is alive. The runtime intentionally persists
/// only the field needed by the selected scope, so the next snapshot can be
/// entity-only, prototype-only, or notification-only. Context is restored
/// only through that exact persisted rule identity; unrelated candidates that
/// share an override id can never lend each other entity data.
/// </summary>
public sealed class VanillaBehaviorCandidateCache
{
    private readonly Dictionary<string, AlarmView> m_interactions =
        new(StringComparer.Ordinal);

    public void RememberInteraction(
        AlarmView candidate,
        VanillaNotificationScope scope)
    {
        var identity = RuleIdentity(candidate, scope);
        if (identity.Length == 0)
        {
            return;
        }
        m_interactions[identity] = Clone(candidate);
    }

    public IReadOnlyList<AlarmView> Merge(
        IEnumerable<AlarmView> candidates)
    {
        var restored = (candidates ?? Enumerable.Empty<AlarmView>())
            .Where(candidate => candidate != null)
            .Select(RestoreInteractionContext)
            .ToArray();

        var deduplicated = restored
            .GroupBy(CandidateIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        return deduplicated
            .Where(candidate => !IsCoveredFallback(
                candidate,
                deduplicated))
            .OrderBy(candidate => candidate.Source ?? "",
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Name ?? "",
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.OverrideId ?? "",
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.EntityId)
            .ThenBy(candidate => candidate.EntityPrototypeId ?? "",
                StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SlotId ?? "",
                StringComparer.Ordinal)
            .Select(Clone)
            .ToArray();
    }

    private AlarmView RestoreInteractionContext(AlarmView observed)
    {
        var candidate = Clone(observed);
        if (!IsVanillaCandidate(candidate))
        {
            return candidate;
        }

        var scope = PartialScope(candidate);
        if (!scope.HasValue)
        {
            return candidate;
        }
        var identity = RuleIdentity(candidate, scope.Value);
        if (!m_interactions.TryGetValue(identity, out var cached))
        {
            return candidate;
        }

        if (candidate.EntityId < 0)
        {
            candidate.EntityId = cached.EntityId;
        }
        if (string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            candidate.EntityPrototypeId = cached.EntityPrototypeId;
        }

        // Retain the original row's stable UI identity and useful labels. A
        // persisted fallback otherwise uses the override id as its slot/name.
        if (!string.IsNullOrWhiteSpace(cached.SlotId))
        {
            candidate.SlotId = cached.SlotId;
        }
        if (!string.IsNullOrWhiteSpace(cached.Name))
        {
            candidate.Name = cached.Name;
        }
        if (!string.IsNullOrWhiteSpace(cached.Detail))
        {
            candidate.Detail = cached.Detail;
        }
        if (!string.IsNullOrWhiteSpace(cached.EntityTitle))
        {
            candidate.EntityTitle = cached.EntityTitle;
        }
        return candidate;
    }

    private static VanillaNotificationScope? PartialScope(
        AlarmView candidate)
    {
        if (candidate.EntityId >= 0 &&
            string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            return VanillaNotificationScope.Entity;
        }
        if (candidate.EntityId < 0 &&
            !string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            return VanillaNotificationScope.EntityPrototype;
        }
        if (candidate.EntityId < 0 &&
            string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            return VanillaNotificationScope.NotificationType;
        }
        return null;
    }

    private static bool IsCoveredFallback(
        AlarmView candidate,
        IReadOnlyList<AlarmView> candidates)
    {
        if (!IsVanillaCandidate(candidate) || candidate.EntityId >= 0)
        {
            return false;
        }

        var overrideId = candidate.OverrideId.Trim();
        var prototypeId = candidate.EntityPrototypeId?.Trim() ?? "";
        if (prototypeId.Length > 0)
        {
            return candidates.Any(other =>
                !ReferenceEquals(other, candidate) &&
                IsSameVanillaOverride(other, overrideId) &&
                other.EntityId >= 0 &&
                string.Equals(
                    other.EntityPrototypeId?.Trim() ?? "",
                    prototypeId,
                    StringComparison.Ordinal));
        }

        return candidates.Any(other =>
            !ReferenceEquals(other, candidate) &&
            IsSameVanillaOverride(other, overrideId) &&
            (other.EntityId >= 0 ||
             !string.IsNullOrWhiteSpace(other.EntityPrototypeId)));
    }

    private static string RuleIdentity(
        AlarmView candidate,
        VanillaNotificationScope scope)
    {
        if (!IsVanillaCandidate(candidate) ||
            scope == VanillaNotificationScope.Entity &&
            candidate.EntityId < 0 ||
            scope == VanillaNotificationScope.EntityPrototype &&
            string.IsNullOrWhiteSpace(candidate.EntityPrototypeId))
        {
            return "";
        }
        return VanillaNotificationSuppressionPolicy.RuleIdentity(
            new VanillaNotificationRule
            {
                AlarmId = candidate.OverrideId,
                Scope = scope,
                EntityId = candidate.EntityId,
                EntityPrototypeId = candidate.EntityPrototypeId,
            });
    }

    private static string CandidateIdentity(AlarmView candidate)
    {
        if (candidate == null)
        {
            return "";
        }
        return string.Join(
            "|",
            candidate.Source?.Trim() ?? "",
            candidate.OverrideId?.Trim() ?? "",
            candidate.EntityId.ToString(CultureInfo.InvariantCulture),
            candidate.EntityPrototypeId?.Trim() ?? "",
            candidate.SlotId?.Trim() ?? "");
    }

    private static bool IsVanillaCandidate(AlarmView candidate) =>
        candidate != null &&
        string.Equals(
            candidate.Source,
            "vanilla",
            StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(candidate.OverrideId);

    private static bool IsSameVanillaOverride(
        AlarmView candidate,
        string overrideId) =>
        IsVanillaCandidate(candidate) &&
        string.Equals(
            candidate.OverrideId.Trim(),
            overrideId,
            StringComparison.Ordinal);

    private static AlarmView Clone(AlarmView source)
    {
        return new AlarmView
        {
            Key = source.Key,
            Name = source.Name,
            Detail = source.Detail,
            Source = source.Source,
            PanelId = source.PanelId,
            ActiveColor = source.ActiveColor,
            SoundId = source.SoundId,
            OverrideId = source.OverrideId,
            OccurrenceId = source.OccurrenceId,
            SlotId = source.SlotId,
            OccurrencePriority = source.OccurrencePriority,
            EntityId = source.EntityId,
            EntityPrototypeId = source.EntityPrototypeId,
            EntityTitle = source.EntityTitle,
            Sequence = source.Sequence,
            Severity = source.Severity,
            IsActive = source.IsActive,
            IsAcknowledged = source.IsAcknowledged,
            IsGoneUnacknowledged = source.IsGoneUnacknowledged,
            IsMissingSource = source.IsMissingSource,
            LastValue = source.LastValue,
        };
    }
}
