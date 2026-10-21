using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// The Editor view's completion checkbox has no dedicated message: it mutates <c>Done</c> locally and
/// relies on the whole-document flush (<see cref="BlockEntityScribeWritingStation.ApplyEdit"/> for a Lectern,
/// <see cref="ScribeModSystem.OnServerReceivedNotebookSave"/> for a Notebook/Tablet), which calls
/// <see cref="ScribeModSystem.NotifyAssignmentDoneChanged"/> so Editor-only completion (the only option on a
/// Tablet) still derives Completed on the canonical <see cref="ScribeAssignmentStore"/> record.
///
/// Calls the production entry points directly, with no network round-trip.
/// </summary>
public class EditorAssignmentCompletionSyncScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>Seeds an Accepted canonical assignment record directly (the desk/notice send flow is covered
    /// by <see cref="AssignmentCompletionDocResolutionScenarios"/>) and returns a block carrying the same
    /// TaskId + Assignment, ready to place as a real Accept would.</summary>
    private ScribeBlock SeedAcceptedAssignmentBlock(ITestPlayer assigner, ITestPlayer assignee, Guid assignmentId,
        string text = "Chop 10 logs")
    {
        var store = Mod.AssignmentStore!;
        const string date = "day 1";
        Assert.True(store.TryCreateSent(assignmentId, assigner.Player.PlayerUID, assignee.Player.PlayerUID,
            text, date, out _));
        Assert.True(store.TryMarkReceived(assignmentId, date));
        Assert.True(store.TryApplyAction(assignmentId, assignee.Player.PlayerUID, ScribeAssignmentAction.Accept));
        var record = store.TryGet(assignmentId);
        return new ScribeBlock(ScribeBlockKind.Task, text, done: false, taskId: assignmentId, assignment: record!.Assignment);
    }

    private void AssertBothPartiesCompleted(ITestPlayer assigner, ITestPlayer assignee, Guid assignmentId)
    {
        var store = Mod.AssignmentStore!;
        Assert.Equal(ScribeAssignmentState.Completed, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Completed,
            Assert.Single(store.Received(assignee.Player.PlayerUID)).Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Completed,
            Assert.Single(store.Sent(assigner.Player.PlayerUID)).Assignment!.State);
    }

    // ---------- NotifyDoneAssignmentsInDocument idempotency ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task NotifyDoneAssignmentsInDocument_is_idempotent_when_called_twice()
    {
        var assigner = await World.JoinPlayer("SyncAssigner1");
        var assignee = await World.JoinPlayer("SyncAssignee1");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);
        block.Done = true;
        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);

        Mod.NotifyDoneAssignmentsInDocument(doc);
        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
        string? completedDateAfterFirst = Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.CompletedDate;

        // A repeat call on the already-Completed document is a cheap no-op: the store gate rejects it before
        // any mutation or sync push, which is what lets the flush skip diffing old vs. new.
        Mod.NotifyDoneAssignmentsInDocument(doc);
        Assert.Equal(ScribeAssignmentState.Completed, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.State);
        Assert.Equal(completedDateAfterFirst, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.CompletedDate);
    }

    // ---------- Lectern's whole-document flush ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_an_assigned_task_via_the_Lecterns_Editor_flush_derives_completed()
    {
        var assigner = await World.JoinPlayer("SyncAssigner2");
        var assignee = await World.JoinPlayer("SyncAssignee2");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);

        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(assignee.Player, wantEditor: true, quickAdd: false); // acquire the lock

        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(doc)); // places the task, not done yet

        Assert.Equal(ScribeAssignmentState.Accepted, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.State);

        // Simulate the Editor checkbox: toggle Done locally, then flush the whole document.
        var authoritative = lectern.Document;
        int idx = authoritative.Blocks.ToList().FindIndex(b => b.TaskId == assignmentId);
        Assert.True(authoritative.ToggleTask(idx));
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(authoritative));

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }

    // ---------- Notebook/Tablet whole-document flush ----------

    private async Task<ITestPlayer> SeedAssigneeWithScribeItem(string playerName, string itemCode)
    {
        var player = await World.JoinPlayer(playerName);
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0]!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", itemCode))!, 1);
        hotbar[0]!.MarkDirty();
        return player;
    }

    private void FlushNotebookSave(ITestPlayer player, ScribeDocument doc)
    {
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        Mod.OnServerReceivedNotebookSave(player.Player, new ScribeNotebookSaveMessage
        {
            DocIdBytes = doc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(doc),
            TargetInventoryId = hotbar.InventoryID,
            TargetSlotId = 0,
        });
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_an_assigned_task_via_a_Tablets_Editor_flush_derives_completed()
    {
        var assigner = await World.JoinPlayer("SyncAssigner3");
        var assignee = await SeedAssigneeWithScribeItem("SyncAssignee3", "scribetablet-clay-red");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);

        var hotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);
        ScribeDocumentAttributes.WriteTo(hotbar[0]!.Itemstack!, doc); // as if placed there by a prior Accept
        hotbar[0]!.MarkDirty();

        // The Tablet dialog has only an Editor tab, so completion can only come from the Editor flush.
        Assert.True(doc.ToggleTask(0));
        FlushNotebookSave(assignee, doc);

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_an_assigned_task_via_a_Notebooks_Editor_flush_derives_completed()
    {
        var assigner = await World.JoinPlayer("SyncAssigner4");
        var assignee = await SeedAssigneeWithScribeItem("SyncAssignee4", "scribenotebook");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);

        var hotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);
        ScribeDocumentAttributes.WriteTo(hotbar[0]!.Itemstack!, doc);
        hotbar[0]!.MarkDirty();

        // Same flush path as the Tablet, so the behavior is not Tablet-specific.
        Assert.True(doc.ToggleTask(0));
        FlushNotebookSave(assignee, doc);

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }

    // ---------- non-Accepted store record is left unchanged ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task Editor_flush_never_resurrects_a_terminal_store_record()
    {
        var assigner = await World.JoinPlayer("SyncAssigner5");
        var assignee = await World.JoinPlayer("SyncAssignee5");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);

        var store = Mod.AssignmentStore!;
        Assert.True(store.TryApplyAction(assignmentId, assignee.Player.PlayerUID, ScribeAssignmentAction.Discard));
        Assert.Equal(ScribeAssignmentState.Discarded, store.TryGet(assignmentId)!.Assignment!.State);

        // The discard leaves the task in place and checked off, but the canonical record is terminal,
        // so the flush-time derivation must be a no-op.
        block.Done = true;
        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);

        Mod.NotifyDoneAssignmentsInDocument(doc);

        Assert.Equal(ScribeAssignmentState.Discarded, store.TryGet(assignmentId)!.Assignment!.State);
    }

    // ---------- multiple completed assigned tasks in one save ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task Editor_flush_with_two_completed_assigned_tasks_completes_both()
    {
        var assigner = await World.JoinPlayer("SyncAssigner6");
        var assignee = await World.JoinPlayer("SyncAssignee6");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var block1 = SeedAcceptedAssignmentBlock(assigner, assignee, id1, "Chop 10 logs");
        var block2 = SeedAcceptedAssignmentBlock(assigner, assignee, id2, "Mine 10 ore");
        block1.Done = true;
        block2.Done = true;

        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block1);
        doc.AppendAssignedBlock(block2);

        Mod.NotifyDoneAssignmentsInDocument(doc);

        var store = Mod.AssignmentStore!;
        Assert.Equal(ScribeAssignmentState.Completed, store.TryGet(id1)!.Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Completed, store.TryGet(id2)!.Assignment!.State);

        var received = store.Received(assignee.Player.PlayerUID);
        Assert.Equal(2, received.Count);
        Assert.All(received, b => Assert.Equal(ScribeAssignmentState.Completed, b.Assignment!.State));

        var sent = store.Sent(assigner.Player.PlayerUID);
        Assert.Equal(2, sent.Count);
        Assert.All(sent, b => Assert.Equal(ScribeAssignmentState.Completed, b.Assignment!.State));
    }

    // ---------- HUD/Pin Tab/Read-view paths are unaffected ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task HUD_style_completion_still_works_and_a_later_editor_flush_leaves_it_unaffected()
    {
        var assigner = await World.JoinPlayer("SyncAssigner7");
        var assignee = await World.JoinPlayer("SyncAssignee7");
        var assignmentId = Guid.NewGuid();
        var block = SeedAcceptedAssignmentBlock(assigner, assignee, assignmentId);

        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);
        lectern!.OnRightClick(assignee.Player, wantEditor: true, quickAdd: false);

        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(doc)); // places the task, not done yet

        // Complete it the HUD/Pin-Tab way: a dedicated completion message, never an Editor flush.
        Mod.CompleteTaskForPlayer(assignee.Player, doc.DocId, assignmentId);
        AssertBothPartiesCompleted(assigner, assignee, assignmentId);

        // A later unrelated flush of the now-Completed document must not disturb it.
        lectern.OnRightClick(assignee.Player, wantEditor: true, quickAdd: false);
        var editedDoc = lectern.Document;
        editedDoc.AddTextSection("unrelated note");
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(editedDoc));

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }
}
