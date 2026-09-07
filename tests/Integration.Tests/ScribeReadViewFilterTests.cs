using Scribe;
using Scribe.Core;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Pure Mod-layer logic for read-view-filter-and-collapse (tasks 2.1/2.2/5.1/6.1/6.5) — no Vintage
/// Story API surface is touched by <see cref="ScribeReadViewFilter"/>, so these run as plain xUnit
/// facts (no Atlas world boot), the same way <c>Core.Tests</c> exercises pure logic, just living
/// alongside the rest of this feature's tests since the code under test is `internal` to the Mod
/// assembly (see <c>AssemblyInfo.cs</c>'s <c>InternalsVisibleTo</c>).
/// </summary>
public class ScribeReadViewFilterTests
{
    private static ScribeReadRowData Row(ScribeBlockKind kind, bool done = false, bool pinned = false, int depth = 0) =>
        new(Index: 0, Kind: kind, Done: done, Pinned: pinned, TaskId: Guid.NewGuid(), Text: "x", Depth: depth);

    // ── 2.1/2.2: category mapping across every ScribeBlockKind, and Pinned's independence ─────────

    // xUnit test methods must be public, and a public method's signature can't expose the `internal`
    // ReadViewFilterCategory as a [Theory]/[InlineData] parameter (CS0051) even with InternalsVisibleTo
    // (that grant permits ACCESS, not an accessibility-rule exception) — so this drives its cases from
    // a private local table instead of [InlineData].
    [Fact]
    public void Each_block_kind_maps_to_exactly_one_Active_Completed_Other_category()
    {
        var cases = new (ScribeBlockKind Kind, bool Done, ReadViewFilterCategory Expected)[]
        {
            (ScribeBlockKind.Text, false, ReadViewFilterCategory.Other),
            (ScribeBlockKind.QuestObjective, false, ReadViewFilterCategory.Other),
            (ScribeBlockKind.QuestObjective, true, ReadViewFilterCategory.Other), // own checked state irrelevant
            (ScribeBlockKind.Task, false, ReadViewFilterCategory.Active),
            (ScribeBlockKind.Task, true, ReadViewFilterCategory.Completed),
            (ScribeBlockKind.Tracker, false, ReadViewFilterCategory.Active),
            (ScribeBlockKind.Tracker, true, ReadViewFilterCategory.Completed),
            (ScribeBlockKind.Link, false, ReadViewFilterCategory.Active),
            (ScribeBlockKind.Link, true, ReadViewFilterCategory.Completed),
            (ScribeBlockKind.Craft, false, ReadViewFilterCategory.Active),
            (ScribeBlockKind.Craft, true, ReadViewFilterCategory.Completed),
        };

        foreach (var (kind, done, expected) in cases)
        {
            var categories = ScribeReadViewFilter.CategoriesFor(kind, done, pinned: false);

            Assert.Contains(expected, categories);
            Assert.Contains(ReadViewFilterCategory.All, categories); // always matches All
            foreach (var other in new[] { ReadViewFilterCategory.Active, ReadViewFilterCategory.Completed, ReadViewFilterCategory.Other })
            {
                if (other != expected) Assert.DoesNotContain(other, categories);
            }
            Assert.DoesNotContain(ReadViewFilterCategory.Pinned, categories); // unpinned
        }
    }

    [Fact]
    public void A_pinned_completed_row_matches_both_Pinned_and_Completed()
    {
        var categories = ScribeReadViewFilter.CategoriesFor(ScribeBlockKind.Task, done: true, pinned: true);

        Assert.Contains(ReadViewFilterCategory.Completed, categories);
        Assert.Contains(ReadViewFilterCategory.Pinned, categories);
    }

    // ── 5.1: owned-run detection over an already-rendered row list ─────────────────────────────────

    [Fact]
    public void OwnedRun_covers_a_Quest_Links_contiguous_objectives()
    {
        var rows = new List<ScribeReadRowData>
        {
            Row(ScribeBlockKind.Link),
            Row(ScribeBlockKind.QuestObjective, depth: 1),
            Row(ScribeBlockKind.QuestObjective, depth: 1),
            Row(ScribeBlockKind.Task),
        };

        var (start, end) = ScribeReadViewFilter.OwnedRun(rows, 0);

        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }

    [Fact]
    public void OwnedRun_covers_a_Craft_parents_generated_trackers()
    {
        var rows = new List<ScribeReadRowData>
        {
            Row(ScribeBlockKind.Craft),
            Row(ScribeBlockKind.Tracker, depth: 1),
            Row(ScribeBlockKind.Task),
        };

        var (start, end) = ScribeReadViewFilter.OwnedRun(rows, 0);

        Assert.Equal(1, start);
        Assert.Equal(2, end);
    }

    [Fact]
    public void OwnedRun_is_empty_for_a_plain_Task_with_no_run()
    {
        var rows = new List<ScribeReadRowData> { Row(ScribeBlockKind.Task), Row(ScribeBlockKind.Task) };

        var (start, end) = ScribeReadViewFilter.OwnedRun(rows, 0);

        Assert.Equal(start, end);
    }

    // ── 6.1: group-level filter visibility ──────────────────────────────────────────────────────────

    [Fact]
    public void A_group_with_one_matching_child_stays_visible_under_that_filter()
    {
        var parent = Row(ScribeBlockKind.Link, done: false);
        var completeChild = Row(ScribeBlockKind.Tracker, done: true, depth: 1);
        var incompleteChild = Row(ScribeBlockKind.Tracker, done: false, depth: 1);
        var rows = new List<ScribeReadRowData> { parent, completeChild, incompleteChild };

        var visibility = ScribeReadViewFilter.ComputeVisibility(rows, ReadViewFilterCategory.Completed, _ => false);

        Assert.Equal(ReadRowVisibility.Shadow, visibility[parent.TaskId]);   // parent itself doesn't match
        Assert.Equal(ReadRowVisibility.Normal, visibility[completeChild.TaskId]);
        Assert.Equal(ReadRowVisibility.Shadow, visibility[incompleteChild.TaskId]);
    }

    [Fact]
    public void A_group_with_no_matching_members_is_hidden()
    {
        var parent = Row(ScribeBlockKind.Link);
        var child1 = Row(ScribeBlockKind.Tracker, done: false, depth: 1);
        var child2 = Row(ScribeBlockKind.Tracker, done: false, depth: 1);
        var rows = new List<ScribeReadRowData> { parent, child1, child2 };

        var visibility = ScribeReadViewFilter.ComputeVisibility(rows, ReadViewFilterCategory.Completed, _ => false);

        Assert.Equal(ReadRowVisibility.Hidden, visibility[parent.TaskId]);
        Assert.Equal(ReadRowVisibility.Hidden, visibility[child1.TaskId]);
        Assert.Equal(ReadRowVisibility.Hidden, visibility[child2.TaskId]);
    }

    [Fact]
    public void A_collapsed_groups_parent_still_surfaces_via_a_hidden_matching_child()
    {
        var parent = Row(ScribeBlockKind.Link);
        var matchingChild = Row(ScribeBlockKind.Tracker, done: true, depth: 1);
        var rows = new List<ScribeReadRowData> { parent, matchingChild };

        var visibility = ScribeReadViewFilter.ComputeVisibility(rows, ReadViewFilterCategory.Completed, id => id == parent.TaskId);

        Assert.Equal(ReadRowVisibility.Shadow, visibility[parent.TaskId]); // surfaces, shadow (parent itself doesn't match)
        Assert.Equal(ReadRowVisibility.Hidden, visibility[matchingChild.TaskId]); // hidden by collapse
    }

    [Fact]
    public void The_All_pill_shows_every_row_at_Normal_opacity()
    {
        var parent = Row(ScribeBlockKind.Link);
        var child = Row(ScribeBlockKind.Tracker, done: false, depth: 1);
        var rows = new List<ScribeReadRowData> { parent, child };

        var visibility = ScribeReadViewFilter.ComputeVisibility(rows, ReadViewFilterCategory.All, _ => false);

        Assert.Equal(ReadRowVisibility.Normal, visibility[parent.TaskId]);
        Assert.Equal(ReadRowVisibility.Normal, visibility[child.TaskId]);
    }

    // ── 6.5: a pill's count never includes a shadow-rendered sibling ───────────────────────────────

    [Fact]
    public void A_shadow_sibling_does_not_inflate_the_pills_count()
    {
        var completeChild = Row(ScribeBlockKind.Tracker, done: true, depth: 1);
        var incompleteChild = Row(ScribeBlockKind.Tracker, done: false, depth: 1);
        var rows = new List<ScribeReadRowData> { Row(ScribeBlockKind.Link), completeChild, incompleteChild };

        int count = ScribeReadViewFilter.CountFor(rows, ReadViewFilterCategory.Completed);

        Assert.Equal(1, count);
    }

    // ── Guid-set byte codec (the wire shape ToTreeAttributes/ItemStack attributes persist) ─────────

    [Fact]
    public void Guid_set_round_trips_through_the_byte_blob_codec()
    {
        var ids = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var bytes = ScribeReadViewFilter.SerializeGuidSet(ids);
        var roundTripped = ScribeReadViewFilter.DeserializeGuidSet(bytes);

        Assert.Equal(ids, roundTripped);
    }

    [Fact]
    public void An_absent_byte_blob_deserializes_to_an_empty_fully_expanded_set()
    {
        Assert.Empty(ScribeReadViewFilter.DeserializeGuidSet(null));
        Assert.Empty(ScribeReadViewFilter.DeserializeGuidSet(Array.Empty<byte>()));
    }

    // ── read-view-collapse-affordance-fixes 1.1/1.2: pending-until-confirmed refresh guard ──────────

    [Fact]
    public void With_no_pending_pick_the_host_mirror_is_always_accepted()
    {
        Assert.True(ScribeReadViewFilter.ShouldAcceptHostFilterCategory(null, ReadViewFilterCategory.Completed));
    }

    [Fact]
    public void A_pending_pick_rejects_a_still_stale_host_mirror()
    {
        // Simulates the bug: the player just selected Completed, but a row mutation's synchronous
        // RefreshReadView lands before the server's echo — the host mirror is still whatever it was before
        // (e.g. All) — so the pending pick must NOT be overwritten by that stale value.
        bool accept = ScribeReadViewFilter.ShouldAcceptHostFilterCategory(ReadViewFilterCategory.Completed, ReadViewFilterCategory.All);

        Assert.False(accept);
    }

    [Fact]
    public void A_pending_pick_is_accepted_once_the_host_mirror_catches_up()
    {
        bool accept = ScribeReadViewFilter.ShouldAcceptHostFilterCategory(ReadViewFilterCategory.Completed, ReadViewFilterCategory.Completed);

        Assert.True(accept);
    }

    [Fact]
    public void With_no_pending_collapse_snapshot_the_host_mirror_is_always_accepted()
    {
        Assert.True(ScribeReadViewFilter.ShouldAcceptHostCollapsedGroupIds(null, new[] { Guid.NewGuid() }));
    }

    [Fact]
    public void A_pending_collapse_snapshot_rejects_a_still_stale_host_mirror()
    {
        var pending = new HashSet<Guid> { Guid.NewGuid() };
        var staleHostMirror = Array.Empty<Guid>();

        bool accept = ScribeReadViewFilter.ShouldAcceptHostCollapsedGroupIds(pending, staleHostMirror);

        Assert.False(accept);
    }

    [Fact]
    public void A_pending_collapse_snapshot_is_accepted_once_the_host_mirror_matches_by_membership_not_order()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var pending = new HashSet<Guid> { idA, idB };
        var hostMirror = new[] { idB, idA }; // same members, different order

        bool accept = ScribeReadViewFilter.ShouldAcceptHostCollapsedGroupIds(pending, hostMirror);

        Assert.True(accept);
    }
}
