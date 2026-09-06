using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;    // Vector4
using Scribe.Core;           // ScribeBlockKind

namespace Scribe;

/// <summary>
/// Read View's five filter-pill categories (read-view-filter-pills). <c>Active</c>/<c>Completed</c>/
/// <c>Other</c> are mutually exclusive per row (design.md's Decision); <c>Pinned</c> is independent and
/// may co-occur with either. <c>All</c> applies no filtering.
/// </summary>
internal enum ReadViewFilterCategory
{
    All,
    Active,
    Completed,
    Pinned,
    Other,
}

/// <summary>How a row renders under the active filter pill (read-view-subtask-collapse): fully visible,
/// visible-but-deemphasized ("shadow" — shown only for its subtask group's context), or removed from the
/// rendered list entirely.</summary>
internal enum ReadRowVisibility
{
    Hidden,
    Normal,
    Shadow,
}

/// <summary>
/// Pure Mod-layer helpers for the Read View filter-pill row and subtask-group collapse toggle — no
/// Vintage Story API dependency, so these are covered by ordinary unit tests. Operates directly over the
/// already-resolved <see cref="ScribeReadRowData"/> list a dialog builds for <c>ScribeReadContent</c>,
/// mirroring <see cref="ScribeDocument.OwnedRun"/> (Core) but over that rendered snapshot rather than the
/// live document, since the row list already carries <see cref="ScribeReadRowData.Depth"/>/<see
/// cref="ScribeReadRowData.Done"/>/<see cref="ScribeReadRowData.Pinned"/>.
/// </summary>
internal static class ScribeReadViewFilter
{
    private static readonly ReadViewFilterCategory[] SelectablePills =
        (ReadViewFilterCategory[])Enum.GetValues(typeof(ReadViewFilterCategory));

    /// <summary>Every pill, in display order (All first).</summary>
    public static IReadOnlyList<ReadViewFilterCategory> AllPills => SelectablePills;

    /// <summary>
    /// The set of filter categories a row matches, given its block kind, completion state, and the
    /// viewing player's pin state (read-view-filter-pills' block-kind-mapping requirement).
    /// <see cref="ReadViewFilterCategory.All"/> is always included, so callers can test membership
    /// uniformly (including for the All pill) without a special case. <c>Text</c>/<c>QuestObjective</c>
    /// always match <see cref="ReadViewFilterCategory.Other"/>; every other kind matches
    /// <see cref="ReadViewFilterCategory.Active"/> or <see cref="ReadViewFilterCategory.Completed"/> by
    /// its Done flag. <see cref="ReadViewFilterCategory.Pinned"/> is added independently when
    /// <paramref name="pinned"/> is true, so a pinned+completed row matches both.
    /// </summary>
    public static IReadOnlySet<ReadViewFilterCategory> CategoriesFor(ScribeBlockKind kind, bool done, bool pinned)
    {
        var categories = new HashSet<ReadViewFilterCategory> { ReadViewFilterCategory.All };
        categories.Add(kind is ScribeBlockKind.Text or ScribeBlockKind.QuestObjective
            ? ReadViewFilterCategory.Other
            : done ? ReadViewFilterCategory.Completed : ReadViewFilterCategory.Active);
        if (pinned) categories.Add(ReadViewFilterCategory.Pinned);
        return categories;
    }

    /// <summary>Whether <paramref name="row"/> individually matches <paramref name="category"/> — the
    /// "normal opacity" / "counts toward the pill" predicate everywhere below.</summary>
    public static bool Matches(ScribeReadRowData row, ReadViewFilterCategory category) =>
        CategoriesFor(row.Kind, row.Done, row.Pinned).Contains(category);

    /// <summary>The bracketed count a pill shows (read-view-filter-pills: "shown only when > 0"). Counts
    /// only rows that INDIVIDUALLY match — a shadow row rendered solely for its group's context never
    /// inflates any pill's count (read-view-subtask-collapse's shadow-count requirement), because that
    /// requirement is independent of visibility/grouping entirely: it is simply never counted here.</summary>
    public static int CountFor(IReadOnlyList<ScribeReadRowData> rows, ReadViewFilterCategory category) =>
        rows.Count(r => Matches(r, category));

    /// <summary>Lang key + pill color for a category, mirroring
    /// <see cref="ScribeAssignmentFilterGroups.LabelAndColor"/>'s shape but this feature's own distinct
    /// category set and color aliases (design.md's Decision: All→NavActiveSettings, Completed→NavActiveRead,
    /// Active→NavActivePinned, Pinned→NavActiveTranscribe, Other→NavActiveGuestbook).</summary>
    public static (string LangKey, Vector4 Color) LabelAndColor(ReadViewFilterCategory category) => category switch
    {
        ReadViewFilterCategory.All       => ("scribe:scribe-read-filter-all", ScribeRowConstants.ReadFilterAll),
        ReadViewFilterCategory.Active    => ("scribe:scribe-read-filter-active", ScribeRowConstants.ReadFilterActive),
        ReadViewFilterCategory.Completed => ("scribe:scribe-read-filter-completed", ScribeRowConstants.ReadFilterCompleted),
        ReadViewFilterCategory.Pinned    => ("scribe:scribe-read-filter-pinned", ScribeRowConstants.ReadFilterPinned),
        ReadViewFilterCategory.Other     => ("scribe:scribe-read-filter-other", ScribeRowConstants.ReadFilterOther),
        _ => ("scribe:scribe-read-filter-all", ScribeRowConstants.ReadFilterAll),
    };

    /// <summary>
    /// The contiguous depth-1 run owned by <paramref name="rows"/>[<paramref name="parentIndex"/>]:
    /// half-open <c>[start, end)</c>, mirroring <see cref="ScribeDocument.OwnedRun"/> exactly but over this
    /// already-rendered row snapshot (task 5.1). An invalid index or a parent with no following depth-1
    /// rows yields an empty run (<c>start == end</c>).
    /// </summary>
    public static (int Start, int End) OwnedRun(IReadOnlyList<ScribeReadRowData> rows, int parentIndex)
    {
        if (parentIndex < 0 || parentIndex >= rows.Count) return (0, 0);
        int start = parentIndex + 1;
        int end = start;
        while (end < rows.Count && rows[end].Depth == 1) end++;
        return (start, end);
    }

    /// <summary>
    /// Computes each row's rendered <see cref="ReadRowVisibility"/> under the active filter pill and the
    /// current collapse set, per read-view-subtask-collapse's rules:
    /// <list type="number">
    /// <item>a subtask group (a depth-0 parent + its owned run) is a unit for FILTER visibility — hidden
    /// entirely unless at least one member individually matches;</item>
    /// <item>within a visible group, each member's opacity (Normal/Shadow) depends only on whether THAT
    /// member individually matches — independent of collapse state;</item>
    /// <item>collapsing only removes the owned run's rows from the render list; the parent's own
    /// visibility/opacity is unaffected and still reflects a matching hidden child;</item>
    /// <item>under <see cref="ReadViewFilterCategory.All"/>, every row is Normal (never Hidden/Shadow),
    /// since <see cref="Matches"/> is always true for it.</item>
    /// </list>
    /// A depth-1 row with no preceding depth-0 parent (not reachable through normal editing, per
    /// task-subtasks' invariants) is defensively treated as its own standalone group of one.
    /// </summary>
    public static IReadOnlyDictionary<Guid, ReadRowVisibility> ComputeVisibility(
        IReadOnlyList<ScribeReadRowData> rows,
        ReadViewFilterCategory activeCategory,
        Func<Guid, bool> isGroupCollapsed)
    {
        var result = new Dictionary<Guid, ReadRowVisibility>(rows.Count);
        int i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Depth != 0)
            {
                // Defensive: an orphaned depth-1 row is its own singleton group.
                result[rows[i].TaskId] = Matches(rows[i], activeCategory) ? ReadRowVisibility.Normal : ReadRowVisibility.Shadow;
                i++;
                continue;
            }

            var (start, end) = OwnedRun(rows, i);
            bool anyMatch = false;
            for (int m = i; m < end; m++)
            {
                if (Matches(rows[m], activeCategory)) { anyMatch = true; break; }
            }

            if (!anyMatch)
            {
                for (int m = i; m < end; m++) result[rows[m].TaskId] = ReadRowVisibility.Hidden;
                i = end;
                continue;
            }

            result[rows[i].TaskId] = Matches(rows[i], activeCategory) ? ReadRowVisibility.Normal : ReadRowVisibility.Shadow;

            bool collapsed = end > start && isGroupCollapsed(rows[i].TaskId);
            for (int m = start; m < end; m++)
            {
                result[rows[m].TaskId] = collapsed
                    ? ReadRowVisibility.Hidden
                    : Matches(rows[m], activeCategory) ? ReadRowVisibility.Normal : ReadRowVisibility.Shadow;
            }

            i = end;
        }
        return result;
    }

    /// <summary>Packs a set of group-parent TaskIds into a flat byte blob (16 bytes each), for the
    /// same tree-attribute/ItemStack-attribute storage shape the document codec's other byte fields use.
    /// Order is not preserved (a set has none); <see cref="DeserializeGuidSet"/> rebuilds an equivalent set.</summary>
    public static byte[] SerializeGuidSet(IReadOnlyCollection<Guid> ids)
    {
        var bytes = new byte[ids.Count * 16];
        int offset = 0;
        foreach (var id in ids)
        {
            id.ToByteArray().CopyTo(bytes, offset);
            offset += 16;
        }
        return bytes;
    }

    /// <summary>Inverse of <see cref="SerializeGuidSet"/>. A null/short/malformed-length blob (e.g. an
    /// absent attribute on a pre-existing save) yields an empty set — fully expanded, per the
    /// "defaults to expanded" persistence requirement.</summary>
    public static HashSet<Guid> DeserializeGuidSet(byte[]? bytes)
    {
        var result = new HashSet<Guid>();
        if (bytes is null || bytes.Length < 16) return result;
        for (int offset = 0; offset + 16 <= bytes.Length; offset += 16)
        {
            result.Add(new Guid(bytes.AsSpan(offset, 16)));
        }
        return result;
    }
}
