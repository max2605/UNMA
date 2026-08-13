using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UNMA.Domain;

/// <summary>
/// Defines the one Vanilla notification type whose concurrently active
/// instances form a single UNMA alarm occurrence.
/// </summary>
public static class GroupedVanillaNotificationPolicy
{
    public const string PrototypeId = "NotEnoughPowerForEntity";
    public const string OverrideId = "vanilla:" + PrototypeId;
    public const string GroupKey = "vanilla:group:" + PrototypeId;
    public const string SlotId = OverrideId;

    private const string EntitySlotPrefix = OverrideId + ":entity:";
    private const string LegacySlotPrefix = OverrideId + ":legacy:";

    public static bool IsGroupedPrototype(string prototypeId)
    {
        return string.Equals(
            prototypeId,
            PrototypeId,
            StringComparison.Ordinal);
    }

    public static bool IsGroupedOverride(string overrideId)
    {
        return string.Equals(
            overrideId,
            OverrideId,
            StringComparison.Ordinal);
    }

    public static bool IsGroupedOverrideId(string overrideId)
    {
        return IsGroupedOverride(overrideId);
    }

    public static bool IsGroupKey(string alarmKey)
    {
        return string.Equals(alarmKey, GroupKey, StringComparison.Ordinal);
    }

    public static string AlarmKeyForNotification(
        string prototypeId,
        string occurrenceKey)
    {
        return IsGroupedPrototype(prototypeId)
            ? GroupKey
            : occurrenceKey ?? "";
    }

    /// <summary>
    /// Maps every supported historical/entity spelling of the grouped slot
    /// to its type-level slot. Unrelated slots are only trimmed.
    /// </summary>
    public static string CanonicalizeSlotId(string slotId)
    {
        var candidate = slotId?.Trim() ?? "";
        if (string.Equals(candidate, GroupKey, StringComparison.Ordinal) ||
            string.Equals(candidate, OverrideId, StringComparison.Ordinal) ||
            HasNonEmptySuffix(candidate, EntitySlotPrefix) ||
            HasNonEmptySuffix(candidate, LegacySlotPrefix))
        {
            return SlotId;
        }
        return candidate;
    }

    public static bool IsGroupedSlotId(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return false;
        }
        return string.Equals(
            CanonicalizeSlotId(slotId),
            SlotId,
            StringComparison.Ordinal);
    }

    public static string FormatTitle(string representativeTitle, int count)
    {
        return AppendCount(
            string.IsNullOrWhiteSpace(representativeTitle)
                ? PrototypeId
                : representativeTitle.Trim(),
            count);
    }

    public static string FormatDetail(string representativeDetail, int count)
    {
        return AppendCount(
            string.IsNullOrWhiteSpace(representativeDetail)
                ? PrototypeId
                : representativeDetail.Trim(),
            count);
    }

    public static bool AreAllMembersSuppressed(
        GroupedVanillaNotificationSnapshot snapshot)
    {
        return snapshot != null &&
               snapshot.HasMembers &&
               snapshot.Members.All(member =>
                   member != null && member.IsSuppressed);
    }

    private static string AppendCount(string text, int count)
    {
        return count > 1 ? text + " ×" + count : text;
    }

    private static bool HasNonEmptySuffix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.Length > prefix.Length;
    }
}

/// <summary>
/// Immutable data required to represent one active Vanilla notification in
/// the grouped alarm. The notification key is the membership identity.
/// </summary>
public sealed class GroupedVanillaNotificationMemberSnapshot
{
    public string NotificationKey { get; }
    public string Title { get; }
    public string Detail { get; }
    public bool IsSuppressed { get; }
    public int EntityId { get; }
    public string EntityPrototypeId { get; }
    public string EntityTitle { get; }

    public GroupedVanillaNotificationMemberSnapshot(
        string notificationKey,
        string title,
        string detail,
        bool isSuppressed = false,
        int entityId = -1,
        string entityPrototypeId = "",
        string entityTitle = "")
    {
        NotificationKey = notificationKey?.Trim() ?? "";
        Title = title?.Trim() ?? "";
        Detail = detail?.Trim() ?? "";
        IsSuppressed = isSuppressed;
        EntityId = entityId;
        EntityPrototypeId = entityPrototypeId?.Trim() ?? "";
        EntityTitle = entityTitle?.Trim() ?? "";
    }
}

/// <summary>
/// Immutable view of the current membership and deferred-clear state.
/// </summary>
public sealed class GroupedVanillaNotificationSnapshot
{
    public IReadOnlyList<GroupedVanillaNotificationMemberSnapshot> Members
    {
        get;
    }

    public GroupedVanillaNotificationMemberSnapshot OldestRepresentative
    {
        get;
    }

    public GroupedVanillaNotificationMemberSnapshot
        PendingClearRepresentative { get; }

    public int Count => Members.Count;
    public bool HasMembers => Count > 0;
    public bool IsLastClearPending => PendingClearRepresentative != null;

    internal GroupedVanillaNotificationSnapshot(
        IReadOnlyList<GroupedVanillaNotificationMemberSnapshot> members,
        GroupedVanillaNotificationMemberSnapshot pendingClearRepresentative)
    {
        Members = members ??
            new ReadOnlyCollection<
                GroupedVanillaNotificationMemberSnapshot>(
                Array.Empty<
                    GroupedVanillaNotificationMemberSnapshot>());
        OldestRepresentative = Members.FirstOrDefault();
        PendingClearRepresentative = pendingClearRepresentative;
    }
}

/// <summary>
/// Tracks the active members of the grouped Vanilla alarm. Removing the last
/// member starts a deferred clear. A replacement added before that clear is
/// consumed cancels it, preserving acknowledgement and occurrence identity.
/// </summary>
public sealed class GroupedVanillaNotificationTracker
{
    private sealed class TrackedMember
    {
        public long Order;
        public GroupedVanillaNotificationMemberSnapshot Snapshot;
    }

    private readonly object m_gate = new();
    private readonly Dictionary<string, TrackedMember> m_members =
        new(StringComparer.Ordinal);
    private long m_nextOrder;
    private GroupedVanillaNotificationMemberSnapshot
        m_pendingClearRepresentative;

    public GroupedVanillaNotificationSnapshot GetSnapshot()
    {
        lock (m_gate)
        {
            return CreateSnapshotLocked();
        }
    }

    public GroupedVanillaNotificationSnapshot Add(
        GroupedVanillaNotificationMemberSnapshot member)
    {
        lock (m_gate)
        {
            if (member == null ||
                string.IsNullOrWhiteSpace(member.NotificationKey))
            {
                return CreateSnapshotLocked();
            }

            m_pendingClearRepresentative = null;
            if (m_members.TryGetValue(
                    member.NotificationKey,
                    out var existing))
            {
                // A replay or duplicate event refreshes metadata without
                // changing membership order or count.
                existing.Snapshot = member;
            }
            else
            {
                m_members.Add(
                    member.NotificationKey,
                    new TrackedMember
                    {
                        Order = ++m_nextOrder,
                        Snapshot = member,
                    });
            }
            return CreateSnapshotLocked();
        }
    }

    public GroupedVanillaNotificationSnapshot Remove(string notificationKey)
    {
        notificationKey = notificationKey?.Trim() ?? "";
        lock (m_gate)
        {
            if (notificationKey.Length == 0 ||
                !m_members.TryGetValue(notificationKey, out var removed))
            {
                return CreateSnapshotLocked();
            }

            var representativeBeforeRemoval = OldestLocked();
            m_members.Remove(notificationKey);
            if (m_members.Count == 0)
            {
                m_pendingClearRepresentative =
                    representativeBeforeRemoval ?? removed.Snapshot;
            }
            return CreateSnapshotLocked();
        }
    }

    /// <summary>
    /// Commits a deferred last-member removal. Callers can postpone this
    /// until the end of their notification batch so remove/add replacement
    /// pairs do not reset the alarm occurrence.
    /// </summary>
    public bool TryTakePendingLastClear(
        out GroupedVanillaNotificationMemberSnapshot representative)
    {
        lock (m_gate)
        {
            if (m_members.Count > 0 ||
                m_pendingClearRepresentative == null)
            {
                representative = null;
                return false;
            }
            representative = m_pendingClearRepresentative;
            m_pendingClearRepresentative = null;
            return true;
        }
    }

    public bool Contains(string notificationKey)
    {
        notificationKey = notificationKey?.Trim() ?? "";
        lock (m_gate)
        {
            return notificationKey.Length > 0 &&
                   m_members.ContainsKey(notificationKey);
        }
    }

    public IReadOnlyList<string> GetNotificationKeys()
    {
        lock (m_gate)
        {
            return new ReadOnlyCollection<string>(m_members.Values
                .OrderBy(member => member.Order)
                .Select(member => member.Snapshot.NotificationKey)
                .ToArray());
        }
    }

    public void Clear()
    {
        lock (m_gate)
        {
            m_members.Clear();
            m_pendingClearRepresentative = null;
            m_nextOrder = 0;
        }
    }

    private GroupedVanillaNotificationSnapshot CreateSnapshotLocked()
    {
        var members = new ReadOnlyCollection<
            GroupedVanillaNotificationMemberSnapshot>(
            m_members.Values
                .OrderBy(member => member.Order)
                .Select(member => member.Snapshot)
                .ToArray());
        return new GroupedVanillaNotificationSnapshot(
            members,
            m_pendingClearRepresentative);
    }

    private GroupedVanillaNotificationMemberSnapshot OldestLocked()
    {
        return m_members.Values
            .OrderBy(member => member.Order)
            .Select(member => member.Snapshot)
            .FirstOrDefault();
    }
}
