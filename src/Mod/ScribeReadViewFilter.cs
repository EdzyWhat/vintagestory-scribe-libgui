using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;    // Vector4
using Scribe.Core;           // ScribeBlockKind

namespace Scribe;

/// <summary>
/// Read View's five filter-pill categories. <c>Active</c>/<c>Completed</c>/<c>Other</c> are mutually
/// exclusive per row; <c>Pinned</c> is independent and may co-occur with either. <c>All</c> applies no filtering.
/// </summary>
internal enum ReadViewFilterCategory
{
    All,
    Active,
    Completed,
    Pinned,
    Other,
}

/// <summary>How a row renders under the active filter pill: fully visible, deemphasized ("shadow", shown only
/// for its subtask group's context), or removed from the rendered list.</summary>
internal enum ReadRowVisibility
{
    Hidden,
    Normal,
    Shadow,
}

/// <summary>
/// Pure helpers (no Vintage Story API) for the Read View filter pills and subtask-group collapse, operating
/// over the resolved <see cref="ScribeReadRowData"/> snapshot rather than the live document.
/// </summary>
internal static class ScribeReadViewFilter
{
    private static readonly ReadViewFilterCategory[] SelectablePills =
        (ReadViewFilterCategory[])Enum.GetValues(typeof(ReadViewFilterCategory));

    /// <summary>Every pill, in display order (All first).</summary>
    public static IReadOnlyList<ReadViewFilterCategory> AllPills => SelectablePills;

    /// <summary>
    /// The filter categories a row matches. <see cref="ReadViewFilterCategory.All"/> is always included.
    /// <c>Text</c>/<c>QuestObjective</c> match <see cref="ReadViewFilterCategory.Other"/>; every other kind
    /// matches Active or Completed by its Done flag. Pinned is added independently, so a pinned+completed row
    /// matches both.
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

    /// <summary>Whether <paramref name="row"/> individually matches <paramref name="category"/>.</summary>
    public static bool Matches(ScribeReadRowData row, ReadViewFilterCategory category) =>
        CategoriesFor(row.Kind, row.Done, row.Pinned).Contains(category);

    /// <summary>The count a pill shows. Counts only rows that INDIVIDUALLY match, so a shadow row shown for
    /// group context never inflates a count.</summary>
    public static int CountFor(IReadOnlyList<ScribeReadRowData> rows, ReadViewFilterCategory category) =>
        rows.Count(r => Matches(r, category));

    /// <summary>Lang key + pill color for a category; same shape as
    /// <see cref="ScribeAssignmentFilterGroups.LabelAndColor"/>.</summary>
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
    /// The contiguous depth-1 run owned by <paramref name="rows"/>[<paramref name="parentIndex"/>] as half-open
    /// <c>[start, end)</c>, mirroring <see cref="ScribeDocument.OwnedRun"/>. An invalid index or no following
    /// depth-1 rows yields an empty run.
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
    /// Computes each row's <see cref="ReadRowVisibility"/> under the active pill and collapse set:
    /// <list type="number">
    /// <item>a subtask group (depth-0 parent + owned run) is hidden entirely unless a member individually matches;</item>
    /// <item>within a visible group, a member is Normal/Shadow by whether THAT member matches, independent of collapse;</item>
    /// <item>collapsing only removes the owned run; the parent's visibility is unaffected;</item>
    /// <item>under All, every row is Normal.</item>
    /// </list>
    /// An orphaned depth-1 row is defensively treated as its own group of one.
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

    /// <summary>Packs group-parent TaskIds into a flat blob (16 bytes each). Order is not preserved;
    /// <see cref="DeserializeGuidSet"/> rebuilds an equivalent set.</summary>
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

    /// <summary>Inverse of <see cref="SerializeGuidSet"/>. A null/short blob (e.g. absent attribute) yields an
    /// empty set, i.e. fully expanded.</summary>
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

    /// <summary>Whether a Read View refresh should accept the host's persisted filter-pill mirror.
    /// <paramref name="pending"/> is the local pick not yet echoed back by the server (or <c>null</c>). The
    /// mirror is accepted when nothing is pending or once it equals the pending value, and rejected while
    /// stale so a synchronous refresh can't stomp a just-made selection.</summary>
    public static bool ShouldAcceptHostFilterCategory(ReadViewFilterCategory? pending, ReadViewFilterCategory hostValue) =>
        pending is null || pending.Value == hostValue;

    /// <summary>The collapse-set counterpart of <see cref="ShouldAcceptHostFilterCategory"/>:
    /// <paramref name="pending"/> is a snapshot of the local set after the last toggle (or <c>null</c>),
    /// compared to <paramref name="hostValue"/> by set membership.</summary>
    public static bool ShouldAcceptHostCollapsedGroupIds(IReadOnlySet<Guid>? pending, IReadOnlyCollection<Guid> hostValue) =>
        pending is null || pending.SetEquals(hostValue);
}
