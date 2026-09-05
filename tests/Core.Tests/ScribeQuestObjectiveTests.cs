using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the QuestObjective block kind (add-progression-framework-quest-objective-subtasks): a
// depth-1 subtask modeling one Progression Framework quest objective under its parent Quest Link.
// Carries target/current like a Tracker but is excluded from carried-inventory counting and from
// completion (no checkbox) — its progress is driven exclusively by the reconcile/progress-write ops
// below, never by the carried-inventory scan. All pure-Core (no game install).
public class ScribeQuestObjectiveTests
{
    // ---- model fields + exclusions ----

    [Fact]
    public void QuestObjective_CarriesTargetAndCurrentLikeATracker()
    {
        var block = new ScribeBlock(ScribeBlockKind.QuestObjective, "",
            depth: 1, targetQuantity: 27, currentQuantity: 3, linkTarget: "obj-rope", linkLabel: "Deliver rope");

        Assert.Equal(27, block.TargetQuantity);
        Assert.Equal(3, block.CurrentQuantity);
        Assert.Equal("obj-rope", block.LinkTarget);
        Assert.Equal("Deliver rope", block.LinkLabel);
        Assert.True(block.IsQuestObjective);
    }

    [Theory]
    [InlineData(ScribeBlockKind.Tracker, true)]
    [InlineData(ScribeBlockKind.Craft, true)]
    [InlineData(ScribeBlockKind.QuestObjective, false)]
    [InlineData(ScribeBlockKind.Task, false)]
    [InlineData(ScribeBlockKind.Text, false)]
    [InlineData(ScribeBlockKind.Link, false)]
    public void IsCarriedCountTracked_ExcludesQuestObjective(ScribeBlockKind kind, bool expected)
    {
        var block = new ScribeBlock(kind, "");
        Assert.Equal(expected, block.IsCarriedCountTracked);
    }

    [Theory]
    [InlineData(ScribeBlockKind.Task, true)]
    [InlineData(ScribeBlockKind.Tracker, true)]
    [InlineData(ScribeBlockKind.Link, true)]
    [InlineData(ScribeBlockKind.Craft, true)]
    [InlineData(ScribeBlockKind.Text, false)]
    [InlineData(ScribeBlockKind.QuestObjective, false)]
    public void IsCompletable_ExcludesTextAndQuestObjective(ScribeBlockKind kind, bool expected)
    {
        var block = new ScribeBlock(kind, "");
        Assert.Equal(expected, block.IsCompletable);
    }

    // ---- reconcile ----

    [Fact]
    public void ReconcileQuestObjectives_InsertsOneChildPerObjective()
    {
        var doc = new ScribeDocument();
        bool added = doc.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope",
            "Deliver Rope", "description");
        Assert.True(added);
        var questLinkId = doc.Blocks[0].TaskId;

        bool ok = doc.ReconcileQuestObjectives(questLinkId,
            new[]
            {
                ("obj-rope", (string?)"game:rope", (string?)null, 27),
                ("obj-deliver", (string?)null, (string?)"Deliver to the harbormaster", 1),
            });

        Assert.True(ok);
        Assert.Equal(3, doc.Blocks.Count); // parent + 2 objective children
        var rope = doc.Blocks[1];
        var deliver = doc.Blocks[2];
        Assert.Equal(ScribeBlockKind.QuestObjective, rope.Kind);
        Assert.Equal(1, rope.Depth);
        Assert.Equal("obj-rope", rope.LinkTarget);
        Assert.Equal("game:rope", rope.TargetItemCode);
        Assert.Equal(27, rope.TargetQuantity);
        Assert.Equal("obj-deliver", deliver.LinkTarget);
        Assert.Null(deliver.TargetItemCode);
        Assert.Equal("Deliver to the harbormaster", deliver.LinkLabel);
    }

    [Fact]
    public void ReconcileQuestObjectives_RescalesExistingChildInPlace_PreservingProgressAndId()
    {
        var doc = new ScribeDocument();
        doc.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = doc.Blocks[0].TaskId;

        doc.ReconcileQuestObjectives(questLinkId, new[] { ("obj-rope", (string?)"game:rope", (string?)null, 20) });
        var childId = doc.Blocks[1].TaskId;
        doc.SetQuestObjectiveProgress(childId, 5);

        // The backend later reports a higher requirement for the same objective.
        doc.ReconcileQuestObjectives(questLinkId, new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) });

        Assert.Equal(2, doc.Blocks.Count); // still just parent + the one child (no duplicate)
        Assert.Equal(childId, doc.Blocks[1].TaskId);
        Assert.Equal(27, doc.Blocks[1].TargetQuantity); // rescaled
        Assert.Equal(5, doc.Blocks[1].CurrentQuantity); // live progress preserved
    }

    [Fact]
    public void ReconcileQuestObjectives_PlayerDeletedChildIsNotResurrected()
    {
        // Mirrors the real calling discipline (task 5.3/5.4): objectives are reconciled with
        // createMissing:true exactly ONCE at accept time; every later tick only pushes progress via
        // SetQuestObjectiveProgress (TaskId-addressed, so a deleted child can never come back — see
        // SetQuestObjectiveProgress_ReturnsFalse_ForUnknownTaskId). A createMissing:false reconcile
        // (Core's own non-recreating contract, mirroring RescaleCraftIngredients) never resurrects either.
        var doc = new ScribeDocument();
        doc.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = doc.Blocks[0].TaskId;
        var objectives = new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) };

        doc.ReconcileQuestObjectives(questLinkId, objectives);
        Assert.Equal(2, doc.Blocks.Count);

        doc.DeleteBlock(1, out _); // player deletes the objective subtask
        Assert.Single(doc.Blocks);

        doc.ReconcileQuestObjectives(questLinkId, objectives, createMissing: false);
        Assert.Single(doc.Blocks); // not recreated
    }

    [Fact]
    public void ReconcileQuestObjectives_NoOp_WhenObjectiveAlreadyMatches()
    {
        var doc = new ScribeDocument();
        doc.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = doc.Blocks[0].TaskId;
        var objectives = new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) };

        doc.ReconcileQuestObjectives(questLinkId, objectives);
        var childId = doc.Blocks[1].TaskId;

        doc.ReconcileQuestObjectives(questLinkId, objectives); // repeat call, nothing changed

        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal(childId, doc.Blocks[1].TaskId);
        Assert.Equal(27, doc.Blocks[1].TargetQuantity);
    }

    [Fact]
    public void ReconcileQuestObjectives_ReturnsFalse_WhenNoMatchingQuestLink()
    {
        var doc = new ScribeDocument();
        doc.AddTask("not a quest link");
        bool ok = doc.ReconcileQuestObjectives(Guid.NewGuid(),
            new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) });
        Assert.False(ok);
    }

    [Fact]
    public void ReconcileQuestObjectives_ReturnsFalse_ForAPlainItemLink()
    {
        // A plain item Link is not a quest Link, so it must never grow QuestObjective children.
        var doc = new ScribeDocument();
        doc.AddLink("game:ingot-copper");
        var linkId = doc.Blocks[0].TaskId;
        bool ok = doc.ReconcileQuestObjectives(linkId,
            new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) });
        Assert.False(ok);
    }

    // ---- progress write ----

    [Fact]
    public void SetQuestObjectiveProgress_ClampsToTargetQuantity_UnlikeTrackerOverflow()
    {
        var block = new ScribeBlock(ScribeBlockKind.QuestObjective, "", targetQuantity: 10);
        var doc = new ScribeDocument();
        doc.ReplaceBlocks(new[] { block });

        bool ok = doc.SetQuestObjectiveProgress(block.TaskId, 15); // backend can't over-report, but clamp anyway

        Assert.True(ok);
        Assert.Equal(10, doc.Blocks[0].CurrentQuantity); // clamped to target, unlike Tracker's overflow (7.14)
    }

    [Fact]
    public void SetQuestObjectiveProgress_IgnoresATrackerWithTheSameId_NotGatedOnIsCarriedCountTracked()
    {
        var doc = new ScribeDocument();
        var id = doc.AddTracker("game:rope", 27) ? doc.Blocks[0].TaskId : Guid.Empty;

        bool ok = doc.SetQuestObjectiveProgress(id, 5);

        Assert.False(ok); // a Tracker's count is not writable through this QuestObjective-only op
        Assert.Equal(0, doc.Blocks[0].CurrentQuantity);
    }

    [Fact]
    public void SetQuestObjectiveProgress_ReturnsFalse_ForUnknownTaskId()
    {
        var doc = new ScribeDocument();
        Assert.False(doc.SetQuestObjectiveProgress(Guid.NewGuid(), 5));
    }

    // ---- codec round-trips ----

    [Fact]
    public void BinaryCodec_RoundTripsQuestObjectiveFields()
    {
        var original = new ScribeDocument();
        original.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = original.Blocks[0].TaskId;
        original.ReconcileQuestObjectives(questLinkId,
            new[]
            {
                ("obj-rope", (string?)"game:rope", (string?)null, 27),
                ("obj-deliver", (string?)null, (string?)"Deliver to the harbormaster", 1),
            });
        original.SetQuestObjectiveProgress(original.Blocks[1].TaskId, 5);

        byte[] bytes = ScribeDocumentCodec.Serialize(original);
        bool ok = ScribeDocumentCodec.TryDeserialize(bytes, out var restored);

        Assert.True(ok);
        var rope = restored!.Blocks[1];
        Assert.Equal(ScribeBlockKind.QuestObjective, rope.Kind);
        Assert.Equal(1, rope.Depth);
        Assert.Equal("obj-rope", rope.LinkTarget);
        Assert.Equal("game:rope", rope.TargetItemCode);
        Assert.Equal(27, rope.TargetQuantity);
        Assert.Equal(5, rope.CurrentQuantity);

        var deliver = restored.Blocks[2];
        Assert.Equal("obj-deliver", deliver.LinkTarget);
        Assert.Null(deliver.TargetItemCode);
        Assert.Equal("Deliver to the harbormaster", deliver.LinkLabel);
    }

    [Fact]
    public void JsonCodec_RoundTripsQuestObjectiveFields()
    {
        var original = new ScribeDocument();
        original.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = original.Blocks[0].TaskId;
        original.ReconcileQuestObjectives(questLinkId,
            new[] { ("obj-rope", (string?)"game:rope", (string?)null, 27) });

        string json = ScribeDocumentJsonCodec.Serialize(original);
        bool ok = ScribeDocumentJsonCodec.TryDeserialize(json, out var restored);

        Assert.True(ok);
        var rope = restored!.Blocks[1];
        Assert.Equal(ScribeBlockKind.QuestObjective, rope.Kind);
        Assert.Equal("obj-rope", rope.LinkTarget);
        Assert.Equal("game:rope", rope.TargetItemCode);
        Assert.Equal(27, rope.TargetQuantity);
    }

    [Fact]
    public void TsvCodec_RoundTripsQuestObjective_ItemBackedAndLabelOnly()
    {
        var original = new ScribeDocument();
        original.AddQuestLink(ScribeQuestSource.ProgressionFramework, "seafarer:deliver-rope", "Deliver Rope", null);
        var questLinkId = original.Blocks[0].TaskId;
        original.ReconcileQuestObjectives(questLinkId,
            new[]
            {
                ("obj-rope", (string?)"game:rope", (string?)null, 27),
                ("obj-deliver", (string?)null, (string?)"Deliver to the harbormaster", 1),
            });

        string tsv = ScribeDocumentTsvCodec.Serialize(original);
        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var restored);

        Assert.True(ok);
        var rope = restored!.Blocks[1];
        Assert.Equal(ScribeBlockKind.QuestObjective, rope.Kind);
        Assert.Equal("game:rope", rope.TargetItemCode);
        Assert.Equal(27, rope.TargetQuantity);

        var deliver = restored.Blocks[2];
        Assert.Equal(ScribeBlockKind.QuestObjective, deliver.Kind);
        Assert.Null(deliver.TargetItemCode);
        Assert.Equal("Deliver to the harbormaster", deliver.LinkLabel);
    }

    [Fact]
    public void TsvCodec_ParsesQuestObjectiveTokenAndDepth()
    {
        // A hand-authored table (loose import): a label-only questobjective row at depth 1.
        string tsv =
            "Type\tDone\tText\tSpecial\tCount\tDepth\n" +
            "link\t\tDeliver Rope\tquest:progressionframework/seafarer:deliver-rope\t\t0\n" +
            "questobjective\t\tDeliver to the harbormaster\t\t1\t1\n";

        bool ok = ScribeDocumentTsvCodec.TryDeserialize(tsv, out var doc);

        Assert.True(ok);
        Assert.Equal(2, doc!.Blocks.Count);
        Assert.Equal(ScribeBlockKind.QuestObjective, doc.Blocks[1].Kind);
        Assert.Equal("Deliver to the harbormaster", doc.Blocks[1].LinkLabel);
        Assert.Equal(1, doc.Blocks[1].Depth);
    }
}
