using System;
using System.Collections.Generic;
using System.Linq;

namespace UNMA.Domain;

public enum AlarmAreaFilterKind
{
    All = 0,
    Unassigned = 1,
    Area = 2,
}

public readonly struct AlarmAreaFilter
{
    public AlarmAreaFilterKind Kind { get; }
    public string AreaId { get; }

    public static AlarmAreaFilter All =>
        new(AlarmAreaFilterKind.All, "");

    public static AlarmAreaFilter Unassigned =>
        new(AlarmAreaFilterKind.Unassigned, "");

    public static AlarmAreaFilter ForArea(string areaId) =>
        new(AlarmAreaFilterKind.Area, areaId);

    public AlarmAreaFilter(AlarmAreaFilterKind kind, string areaId)
    {
        Kind = kind;
        AreaId = areaId?.Trim() ?? "";
    }
}

public enum AlarmAreaMutationFailure
{
    None = 0,
    InvalidName = 1,
    NameTooLong = 2,
    DuplicateName = 3,
    InvalidId = 4,
    IdGenerationFailed = 5,
    AreaNotFound = 6,
    InvalidTargetIndex = 7,
    PanelNotFound = 8,
    PanelNotAssignable = 9,
    TooManyAreas = 10,
}

/// <summary>
/// Pure configuration policy for operator-defined alarm areas. Collection
/// order is canonical; ALL and UNASSIGNED are transient filters, never IDs.
/// </summary>
public static class AlarmAreaPolicy
{
    public const int MaximumAreaCount = 64;
    public const int MaximumDraftNameLength = 40;
    public const int MaximumStoredNameLength = 40;

    private const int MaximumIdGenerationAttempts = 128;
    private const string DefaultName = "AREA";

    public static List<AlarmAreaDefinition> Normalize(
        IEnumerable<AlarmAreaDefinition> areas,
        Func<string> createId = null)
    {
        createId ??= () => Guid.NewGuid().ToString("N");
        var normalized = new List<AlarmAreaDefinition>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var area in areas ??
                     Enumerable.Empty<AlarmAreaDefinition>())
        {
            if (area == null)
            {
                continue;
            }
            if (normalized.Count >= MaximumAreaCount)
            {
                break;
            }

            var id = area.Id?.Trim() ?? "";
            if (id.Length == 0 || !usedIds.Add(id))
            {
                if (!TryReserveId(createId, usedIds, out id))
                {
                    id = ReserveDeterministicId(usedIds);
                }
            }

            area.Id = id;
            area.Name = CreateUniqueStoredName(area.Name, usedNames);
            normalized.Add(area);
        }
        return normalized;
    }

    public static bool ValidateReplacement(
        IEnumerable<AlarmAreaDefinition> draft,
        out List<AlarmAreaDefinition> normalized,
        out AlarmAreaMutationFailure failure)
    {
        normalized = new List<AlarmAreaDefinition>();
        failure = AlarmAreaMutationFailure.None;
        if (draft == null)
        {
            failure = AlarmAreaMutationFailure.InvalidId;
            return false;
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in draft)
        {
            if (area == null)
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.InvalidId;
                return false;
            }
            if (normalized.Count >= MaximumAreaCount)
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.TooManyAreas;
                return false;
            }

            var id = area.Id?.Trim() ?? "";
            if (id.Length == 0 || !usedIds.Add(id))
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.InvalidId;
                return false;
            }
            var name = area.Name?.Trim() ?? "";
            if (name.Length == 0)
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.InvalidName;
                return false;
            }
            if (name.Length > MaximumDraftNameLength)
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.NameTooLong;
                return false;
            }
            if (!usedNames.Add(name))
            {
                normalized.Clear();
                failure = AlarmAreaMutationFailure.DuplicateName;
                return false;
            }

            normalized.Add(new AlarmAreaDefinition
            {
                Id = id,
                Name = name,
            });
        }
        return true;
    }

    public static void NormalizePanelAssignments(
        IEnumerable<PanelDefinition> panels,
        IEnumerable<AlarmAreaDefinition> areas,
        bool discardAssignments = false)
    {
        var validIds = new HashSet<string>(
            (areas ?? Enumerable.Empty<AlarmAreaDefinition>())
            .Where(area => area != null &&
                           !string.IsNullOrWhiteSpace(area.Id))
            .Select(area => area.Id.Trim()),
            StringComparer.Ordinal);
        foreach (var panel in panels ?? Enumerable.Empty<PanelDefinition>())
        {
            if (panel == null)
            {
                continue;
            }
            var areaId = panel.AreaId?.Trim() ?? "";
            panel.AreaId = !discardAssignments &&
                           IsAssignablePanel(panel) &&
                           validIds.Contains(areaId)
                ? areaId
                : "";
        }
    }

    public static bool IsAssignablePanel(PanelDefinition panel)
    {
        return panel != null &&
               !panel.IsDashboard &&
               !PanelTopologyPolicy.IsEntityPanel(panel);
    }

    public static AlarmAreaFilter NormalizeFilter(
        AlarmAreaFilter filter,
        IEnumerable<AlarmAreaDefinition> areas)
    {
        if (filter.Kind == AlarmAreaFilterKind.Unassigned)
        {
            return AlarmAreaFilter.Unassigned;
        }
        if (filter.Kind != AlarmAreaFilterKind.Area)
        {
            return AlarmAreaFilter.All;
        }

        var areaId = filter.AreaId?.Trim() ?? "";
        return areaId.Length > 0 &&
               (areas ?? Enumerable.Empty<AlarmAreaDefinition>()).Any(area =>
                   area != null &&
                   string.Equals(
                       area.Id?.Trim(),
                       areaId,
                       StringComparison.Ordinal))
            ? AlarmAreaFilter.ForArea(areaId)
            : AlarmAreaFilter.All;
    }

    public static IReadOnlyList<PanelDefinition> Select(
        IEnumerable<PanelDefinition> panels,
        AlarmAreaFilter filter)
    {
        return SelectGlobalPanels(panels, filter);
    }

    public static IReadOnlyList<PanelDefinition> SelectGlobalPanels(
        IEnumerable<PanelDefinition> panels,
        AlarmAreaFilter filter)
    {
        var source = panels ?? Enumerable.Empty<PanelDefinition>();
        switch (filter.Kind)
        {
            case AlarmAreaFilterKind.Unassigned:
                return source.Where(panel =>
                        IsAssignablePanel(panel) &&
                        string.IsNullOrWhiteSpace(panel.AreaId))
                    .ToArray();
            case AlarmAreaFilterKind.Area:
                var areaId = filter.AreaId?.Trim() ?? "";
                if (areaId.Length == 0)
                {
                    return Array.Empty<PanelDefinition>();
                }
                return source.Where(panel =>
                        IsAssignablePanel(panel) &&
                        string.Equals(
                            panel.AreaId?.Trim(),
                            areaId,
                            StringComparison.Ordinal))
                    .ToArray();
            default:
                return source.Where(panel =>
                        panel != null &&
                        !PanelTopologyPolicy.IsEntityPanel(panel))
                    .ToArray();
        }
    }

    public static bool ValidateReplacement(
        IEnumerable<AlarmAreaDefinition> areas,
        string replacingAreaId,
        string requestedName,
        out string normalizedName,
        out AlarmAreaMutationFailure failure)
    {
        normalizedName = requestedName?.Trim() ?? "";
        failure = AlarmAreaMutationFailure.None;
        if (normalizedName.Length == 0)
        {
            failure = AlarmAreaMutationFailure.InvalidName;
            return false;
        }
        if (normalizedName.Length > MaximumDraftNameLength)
        {
            failure = AlarmAreaMutationFailure.NameTooLong;
            return false;
        }

        replacingAreaId = replacingAreaId?.Trim() ?? "";
        var candidateName = normalizedName;
        if ((areas ?? Enumerable.Empty<AlarmAreaDefinition>()).Any(area =>
                area != null &&
                !string.Equals(
                    area.Id?.Trim(),
                    replacingAreaId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    area.Name?.Trim(),
                    candidateName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            failure = AlarmAreaMutationFailure.DuplicateName;
            return false;
        }
        return true;
    }

    public static bool TryCreate(
        IList<AlarmAreaDefinition> areas,
        string requestedName,
        Func<string> createId,
        out AlarmAreaDefinition createdArea,
        out AlarmAreaMutationFailure failure)
    {
        createdArea = null;
        failure = AlarmAreaMutationFailure.None;
        if (areas == null || createId == null)
        {
            failure = AlarmAreaMutationFailure.IdGenerationFailed;
            return false;
        }
        if (areas.Count >= MaximumAreaCount)
        {
            failure = AlarmAreaMutationFailure.TooManyAreas;
            return false;
        }
        if (!ValidateReplacement(
                areas,
                "",
                requestedName,
                out var normalizedName,
                out failure))
        {
            return false;
        }

        var usedIds = new HashSet<string>(
            areas.Where(area => area != null &&
                                !string.IsNullOrWhiteSpace(area.Id))
                .Select(area => area.Id.Trim()),
            StringComparer.Ordinal);
        if (!TryReserveId(createId, usedIds, out var id))
        {
            failure = AlarmAreaMutationFailure.IdGenerationFailed;
            return false;
        }

        createdArea = new AlarmAreaDefinition
        {
            Id = id,
            Name = normalizedName,
        };
        areas.Add(createdArea);
        return true;
    }

    public static bool TryRename(
        IList<AlarmAreaDefinition> areas,
        string areaId,
        string requestedName,
        out AlarmAreaMutationFailure failure)
    {
        failure = AlarmAreaMutationFailure.None;
        areaId = areaId?.Trim() ?? "";
        if (areaId.Length == 0)
        {
            failure = AlarmAreaMutationFailure.InvalidId;
            return false;
        }
        var area = (areas ?? Array.Empty<AlarmAreaDefinition>())
            .FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(
                    candidate.Id?.Trim(),
                    areaId,
                    StringComparison.Ordinal));
        if (area == null)
        {
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }
        if (!ValidateReplacement(
                areas,
                areaId,
                requestedName,
                out var normalizedName,
                out failure))
        {
            return false;
        }
        area.Name = normalizedName;
        return true;
    }

    public static bool TryDelete(
        IList<AlarmAreaDefinition> areas,
        IEnumerable<PanelDefinition> panels,
        string areaId,
        out int unassignedPanelCount,
        out AlarmAreaMutationFailure failure)
    {
        unassignedPanelCount = 0;
        failure = AlarmAreaMutationFailure.None;
        areaId = areaId?.Trim() ?? "";
        if (areaId.Length == 0)
        {
            failure = AlarmAreaMutationFailure.InvalidId;
            return false;
        }
        if (areas == null)
        {
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }
        var areaIndex = FindAreaIndex(areas, areaId);
        if (areaIndex < 0)
        {
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }

        foreach (var panel in panels ?? Enumerable.Empty<PanelDefinition>())
        {
            if (panel != null && string.Equals(
                    panel.AreaId?.Trim(),
                    areaId,
                    StringComparison.Ordinal))
            {
                panel.AreaId = "";
                unassignedPanelCount++;
            }
        }
        areas.RemoveAt(areaIndex);
        return true;
    }

    public static bool TryMove(
        IList<AlarmAreaDefinition> areas,
        string areaId,
        int finalIndex,
        out AlarmAreaMutationFailure failure)
    {
        failure = AlarmAreaMutationFailure.None;
        areaId = areaId?.Trim() ?? "";
        if (areaId.Length == 0)
        {
            failure = AlarmAreaMutationFailure.InvalidId;
            return false;
        }
        if (areas == null)
        {
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }
        var sourceIndex = FindAreaIndex(areas, areaId);
        if (sourceIndex < 0)
        {
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }
        if (finalIndex < 0 || finalIndex >= areas.Count)
        {
            failure = AlarmAreaMutationFailure.InvalidTargetIndex;
            return false;
        }
        if (sourceIndex == finalIndex)
        {
            return true;
        }
        var area = areas[sourceIndex];
        areas.RemoveAt(sourceIndex);
        areas.Insert(finalIndex, area);
        return true;
    }

    public static bool TryAssign(
        IEnumerable<PanelDefinition> panels,
        IEnumerable<AlarmAreaDefinition> areas,
        string panelId,
        string areaId,
        out PanelDefinition assignedPanel,
        out AlarmAreaMutationFailure failure)
    {
        assignedPanel = null;
        failure = AlarmAreaMutationFailure.None;
        panelId = panelId?.Trim() ?? "";
        if (panelId.Length == 0)
        {
            failure = AlarmAreaMutationFailure.PanelNotFound;
            return false;
        }
        assignedPanel = (panels ?? Enumerable.Empty<PanelDefinition>())
            .FirstOrDefault(panel =>
                panel != null &&
                string.Equals(
                    panel.Id?.Trim(),
                    panelId,
                    StringComparison.Ordinal));
        if (assignedPanel == null)
        {
            failure = AlarmAreaMutationFailure.PanelNotFound;
            return false;
        }
        if (!IsAssignablePanel(assignedPanel))
        {
            assignedPanel = null;
            failure = AlarmAreaMutationFailure.PanelNotAssignable;
            return false;
        }

        areaId = areaId?.Trim() ?? "";
        if (areaId.Length > 0 &&
            !(areas ?? Enumerable.Empty<AlarmAreaDefinition>()).Any(area =>
                area != null && string.Equals(
                    area.Id?.Trim(),
                    areaId,
                    StringComparison.Ordinal)))
        {
            assignedPanel = null;
            failure = AlarmAreaMutationFailure.AreaNotFound;
            return false;
        }
        assignedPanel.AreaId = areaId;
        return true;
    }

    public static string CloneAreaId(PanelDefinition source)
    {
        return IsAssignablePanel(source)
            ? source.AreaId?.Trim() ?? ""
            : "";
    }

    public static string CloneAreaId(
        PanelDefinition source,
        IEnumerable<AlarmAreaDefinition> areas)
    {
        var areaId = CloneAreaId(source);
        return areaId.Length > 0 &&
               (areas ?? Enumerable.Empty<AlarmAreaDefinition>()).Any(area =>
                   area != null && string.Equals(
                       area.Id?.Trim(),
                       areaId,
                       StringComparison.Ordinal))
            ? areaId
            : "";
    }

    private static string CreateUniqueStoredName(
        string requestedName,
        ISet<string> usedNames)
    {
        var baseName = requestedName?.Trim() ?? "";
        if (baseName.Length == 0)
        {
            baseName = DefaultName;
        }
        baseName = Truncate(baseName, MaximumStoredNameLength);
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffixNumber = 2;
             suffixNumber < int.MaxValue;
             suffixNumber++)
        {
            var suffix = " (" + suffixNumber + ")";
            var prefix = Truncate(
                baseName,
                MaximumStoredNameLength - suffix.Length).TrimEnd();
            var candidate = prefix + suffix;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("No unique area name is available.");
    }

    private static string Truncate(string value, int maximumLength)
    {
        value ??= "";
        return value.Length <= maximumLength
            ? value
            : value.Substring(0, Math.Max(0, maximumLength));
    }

    private static bool TryReserveId(
        Func<string> createId,
        ISet<string> usedIds,
        out string id)
    {
        id = "";
        for (var attempt = 0;
             attempt < MaximumIdGenerationAttempts;
             attempt++)
        {
            string candidate;
            try
            {
                candidate = createId()?.Trim() ?? "";
            }
            catch (Exception)
            {
                return false;
            }
            if (candidate.Length > 0 && usedIds.Add(candidate))
            {
                id = candidate;
                return true;
            }
        }
        return false;
    }

    private static string ReserveDeterministicId(ISet<string> usedIds)
    {
        const string prefix = "area";
        if (usedIds.Add(prefix))
        {
            return prefix;
        }
        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = prefix + "-" + suffix;
            if (usedIds.Add(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("No unique area ID is available.");
    }

    private static int FindAreaIndex(
        IList<AlarmAreaDefinition> areas,
        string areaId)
    {
        for (var index = 0; index < areas.Count; index++)
        {
            if (areas[index] != null && string.Equals(
                    areas[index].Id?.Trim(),
                    areaId,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }
}
