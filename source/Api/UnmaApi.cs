using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace UNMA.Api;

/// <summary>
/// Versioned extension API for alarms and entity metrics supplied by other
/// Captain of Industry mods.
/// </summary>
public static class UnmaApi
{
    public const int ApiVersion = 1;
    public const int MaxMetricsPerOwner = 256;
    public const int MaxAlarmTemplatesPerOwner = 256;
    public const int MaxAlarmStatesPerOwner = 4096;

    private static readonly object s_sync = new();
    private static readonly Dictionary<string, ExternalMetricSnapshot>
        s_metrics = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ExternalAlarmTemplateSnapshot>
        s_templates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ExternalAlarmStateSnapshot>
        s_states = new(StringComparer.Ordinal);

    private static long s_revision;
    private static ExternalRegistrySnapshot s_snapshot =
        new(0, Array.Empty<ExternalMetricSnapshot>(),
            Array.Empty<ExternalAlarmTemplateSnapshot>(),
            Array.Empty<ExternalAlarmStateSnapshot>());

    /// <summary>
    /// Registers a provider-owned metric reader. Returns false for invalid or
    /// duplicate registrations.
    /// </summary>
    public static bool RegisterMetric(
        string ownerModId,
        ExternalMetricDefinition definition)
    {
        return TryRegisterMetric(ownerModId, definition, out _);
    }

    public static bool TryRegisterMetric(
        string ownerModId,
        ExternalMetricDefinition definition,
        out string error)
    {
        if (!ExternalContractValidator.TryNormalizeMetric(
                ownerModId,
                definition,
                out var metric,
                out error))
        {
            return false;
        }

        var key = ExternalRegistrySnapshot.CreateMetricKey(
            metric.OwnerModId,
            metric.PrototypeId,
            metric.Id);
        lock (s_sync)
        {
            if (s_metrics.ContainsKey(key))
            {
                error = "A metric with this owner, prototype, and id is " +
                        "already registered.";
                return false;
            }
            if (CountOwned(s_metrics, metric.OwnerModId) >=
                MaxMetricsPerOwner)
            {
                error = "A provider may register at most " +
                        MaxMetricsPerOwner + " metrics.";
                return false;
            }

            s_metrics.Add(key, metric);
            PublishSnapshotLocked();
        }

        error = "";
        return true;
    }

    public static bool UnregisterMetric(
        string ownerModId,
        string prototypeId,
        string metricId)
    {
        if (!TryNormalizeLookupPart(ownerModId, out var owner) ||
            !TryNormalizeLookupPart(metricId, out var id))
        {
            return false;
        }

        var prototype = string.IsNullOrWhiteSpace(prototypeId)
            ? "*"
            : prototypeId.Trim();
        var key = ExternalRegistrySnapshot.CreateMetricKey(
            owner,
            prototype,
            id);
        lock (s_sync)
        {
            if (!s_metrics.Remove(key))
            {
                return false;
            }

            PublishSnapshotLocked();
            return true;
        }
    }

    /// <summary>
    /// Registers a declarative alarm template. Returns false for invalid or
    /// duplicate owner/id pairs.
    /// </summary>
    public static bool RegisterAlarmTemplate(
        string ownerModId,
        ExternalAlarmTemplateDefinition definition)
    {
        return TryRegisterAlarmTemplate(ownerModId, definition, out _);
    }

    public static bool TryRegisterAlarmTemplate(
        string ownerModId,
        ExternalAlarmTemplateDefinition definition,
        out string error)
    {
        if (!ExternalContractValidator.TryNormalizeTemplate(
                ownerModId,
                definition,
                out var template,
                out error))
        {
            return false;
        }

        var key = CreateOwnedKey(template.OwnerModId, template.Id);
        lock (s_sync)
        {
            if (s_templates.ContainsKey(key))
            {
                error = "An alarm template with this owner and id is " +
                        "already registered.";
                return false;
            }
            if (CountOwned(s_templates, template.OwnerModId) >=
                MaxAlarmTemplatesPerOwner)
            {
                error = "A provider may register at most " +
                        MaxAlarmTemplatesPerOwner + " alarm templates.";
                return false;
            }

            s_templates.Add(key, template);
            PublishSnapshotLocked();
        }

        error = "";
        return true;
    }

    public static bool UnregisterAlarmTemplate(
        string ownerModId,
        string alarmId)
    {
        if (!TryNormalizeLookupPart(ownerModId, out var owner) ||
            !TryNormalizeLookupPart(alarmId, out var id))
        {
            return false;
        }

        lock (s_sync)
        {
            if (!s_templates.Remove(CreateOwnedKey(owner, id)))
            {
                return false;
            }

            PublishSnapshotLocked();
            return true;
        }
    }

    /// <summary>
    /// Adds or replaces the current state of an alarm occurrence. Providers
    /// should publish Active=false when an alarm goes, and may remove it after
    /// UNMA has observed that transition.
    /// </summary>
    public static bool PublishAlarmState(
        string ownerModId,
        ExternalAlarmState state)
    {
        return TryPublishAlarmState(ownerModId, state, out _);
    }

    public static bool TryPublishAlarmState(
        string ownerModId,
        ExternalAlarmState state,
        out string error)
    {
        if (!ExternalContractValidator.TryNormalizeState(
                ownerModId,
                state,
                out var snapshot,
                out error))
        {
            return false;
        }

        var key = CreateStateKey(
            snapshot.OwnerModId,
            snapshot.Id,
            snapshot.InstanceId);
        lock (s_sync)
        {
            if (s_states.TryGetValue(key, out var existing) &&
                AlarmStatesEqual(existing, snapshot))
            {
                error = "";
                return true;
            }
            if (existing == null &&
                CountOwned(s_states, snapshot.OwnerModId) >=
                MaxAlarmStatesPerOwner)
            {
                error = "A provider may publish at most " +
                        MaxAlarmStatesPerOwner + " alarm states.";
                return false;
            }
            s_states[key] = snapshot;
            PublishSnapshotLocked();
        }

        error = "";
        return true;
    }

    /// <summary>
    /// Publishes several changed states with one immutable snapshot rebuild.
    /// This is the preferred path for fleets and other high-cardinality mods.
    /// All entries are validated before any registry state changes.
    /// </summary>
    public static bool PublishAlarmStates(
        string ownerModId,
        IEnumerable<ExternalAlarmState> states)
    {
        return TryPublishAlarmStates(ownerModId, states, out _);
    }

    public static bool TryPublishAlarmStates(
        string ownerModId,
        IEnumerable<ExternalAlarmState> states,
        out string error)
    {
        if (!ExternalContractValidator.TryNormalizeOwner(
                ownerModId,
                out var normalizedOwner,
                out error))
        {
            return false;
        }

        if (states == null)
        {
            error = "Alarm state collection is required.";
            return false;
        }

        var normalized = new List<ExternalAlarmStateSnapshot>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var state in states)
            {
                if (normalized.Count >= MaxAlarmStatesPerOwner)
                {
                    error = "A batch may contain at most " +
                            MaxAlarmStatesPerOwner + " alarm states.";
                    return false;
                }
                if (!ExternalContractValidator.TryNormalizeState(
                        normalizedOwner,
                        state,
                        out var snapshot,
                        out error))
                {
                    return false;
                }
                var key = CreateStateKey(
                    snapshot.OwnerModId,
                    snapshot.Id,
                    snapshot.InstanceId);
                if (!keys.Add(key))
                {
                    error = "A batch contains a duplicate alarm state.";
                    return false;
                }
                normalized.Add(snapshot);
            }
        }
        catch (Exception exception)
        {
            error = "Could not enumerate alarm states: " +
                    exception.Message;
            return false;
        }

        var changed = false;
        lock (s_sync)
        {
            var owner = normalized.FirstOrDefault()?.OwnerModId;
            if (!string.IsNullOrWhiteSpace(owner))
            {
                var additions = normalized.Count(state =>
                    !s_states.ContainsKey(CreateStateKey(
                        state.OwnerModId,
                        state.Id,
                        state.InstanceId)));
                if (CountOwned(s_states, owner) + additions >
                    MaxAlarmStatesPerOwner)
                {
                    error = "A provider may publish at most " +
                            MaxAlarmStatesPerOwner + " alarm states.";
                    return false;
                }
            }
            foreach (var state in normalized)
            {
                var key = CreateStateKey(
                    state.OwnerModId,
                    state.Id,
                    state.InstanceId);
                if (s_states.TryGetValue(key, out var existing) &&
                    AlarmStatesEqual(existing, state))
                {
                    continue;
                }
                s_states[key] = state;
                changed = true;
            }
            if (changed)
            {
                PublishSnapshotLocked();
            }
        }

        error = "";
        return true;
    }

    public static bool RemoveAlarmState(
        string ownerModId,
        string alarmId,
        string instanceId = "default")
    {
        if (!TryNormalizeLookupPart(ownerModId, out var owner) ||
            !TryNormalizeLookupPart(alarmId, out var id))
        {
            return false;
        }

        var instance = string.IsNullOrWhiteSpace(instanceId)
            ? "default"
            : instanceId.Trim();
        lock (s_sync)
        {
            if (!s_states.Remove(CreateStateKey(owner, id, instance)))
            {
                return false;
            }

            PublishSnapshotLocked();
            return true;
        }
    }

    /// <summary>
    /// Removes every metric, template, and pushed state owned by one provider.
    /// </summary>
    public static bool UnregisterOwner(string ownerModId)
    {
        if (!TryNormalizeLookupPart(ownerModId, out var owner))
        {
            return false;
        }

        lock (s_sync)
        {
            var changed = RemoveOwned(s_metrics, owner) |
                          RemoveOwned(s_templates, owner) |
                          RemoveOwned(s_states, owner);
            if (!changed)
            {
                return false;
            }

            PublishSnapshotLocked();
            return true;
        }
    }

    /// <summary>
    /// Returns an atomic immutable snapshot. Holding a snapshot is safe while
    /// providers register, unregister, or update data on other threads.
    /// </summary>
    public static ExternalRegistrySnapshot GetSnapshot()
    {
        return Volatile.Read(ref s_snapshot);
    }

    private static void PublishSnapshotLocked()
    {
        s_revision++;
        var metrics = s_metrics.Values
            .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
            .ThenBy(item => item.PrototypeId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var templates = s_templates.Values
            .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var states = s_states.Values
            .OrderBy(item => item.OwnerModId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.InstanceId, StringComparer.Ordinal)
            .ToArray();
        Volatile.Write(
            ref s_snapshot,
            new ExternalRegistrySnapshot(
                s_revision,
                metrics,
                templates,
                states));
    }

    private static bool RemoveOwned<T>(
        IDictionary<string, T> items,
        string owner)
    {
        var prefix = owner + "\u001f";
        var keys = items.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        foreach (var key in keys)
        {
            items.Remove(key);
        }

        return keys.Length > 0;
    }

    private static int CountOwned<T>(
        IDictionary<string, T> items,
        string owner)
    {
        var prefix = owner + "\u001f";
        return items.Keys.Count(key => key.StartsWith(
            prefix,
            StringComparison.Ordinal));
    }

    private static string CreateOwnedKey(string owner, string id)
    {
        return owner + "\u001f" + id;
    }

    private static string CreateStateKey(
        string owner,
        string id,
        string instanceId)
    {
        return owner + "\u001f" + id + "\u001f" + instanceId;
    }

    private static bool AlarmStatesEqual(
        ExternalAlarmStateSnapshot left,
        ExternalAlarmStateSnapshot right)
    {
        return left != null && right != null &&
               string.Equals(left.OwnerModId, right.OwnerModId,
                   StringComparison.Ordinal) &&
               string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               string.Equals(left.InstanceId, right.InstanceId,
                   StringComparison.Ordinal) &&
               left.Active == right.Active &&
               string.Equals(left.PanelId, right.PanelId,
                   StringComparison.Ordinal) &&
               string.Equals(left.PrototypeId, right.PrototypeId,
                   StringComparison.Ordinal) &&
               string.Equals(left.EntityKey, right.EntityKey,
                   StringComparison.Ordinal) &&
               string.Equals(left.LocalizationNamespace,
                   right.LocalizationNamespace,
                   StringComparison.Ordinal) &&
               string.Equals(left.MessageKey, right.MessageKey,
                   StringComparison.Ordinal) &&
               string.Equals(left.MessageFallback, right.MessageFallback,
                   StringComparison.Ordinal) &&
               string.Equals(left.DetailKey, right.DetailKey,
                   StringComparison.Ordinal) &&
               string.Equals(left.DetailFallback, right.DetailFallback,
                   StringComparison.Ordinal) &&
               string.Equals(left.Severity, right.Severity,
                   StringComparison.Ordinal) &&
               string.Equals(left.SoundId, right.SoundId,
                   StringComparison.Ordinal) &&
               string.Equals(left.ActiveColor, right.ActiveColor,
                   StringComparison.Ordinal) &&
               left.AutoAcknowledgeOnClear ==
               right.AutoAcknowledgeOnClear &&
               Nullable.Equals(left.CurrentValue, right.CurrentValue);
    }

    private static bool TryNormalizeLookupPart(
        string candidate,
        out string normalized)
    {
        normalized = candidate?.Trim() ?? "";
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
