using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// fix-editor-assignment-completion-sync: the Editor view's own completion checkbox has no dedicated
/// completion message (<c>ScribeCompleteTaskMessage</c>) — it mutates <c>Done</c> locally and relies on
/// the ordinary whole-document autosave flush to persist it. Before this change, neither whole-document
/// flush handler (<see cref="BlockEntityScribeWritingStation.ApplyEdit"/> for a Lectern,
/// <see cref="ScribeModSystem.OnServerReceivedNotebookSave"/> for a Notebook/Tablet) ever called into
/// <see cref="ScribeModSystem.NotifyAssignmentDoneChanged"/>, so completing an assigned task purely
/// through the Editor never derived Completed on the canonical <see cref="ScribeAssignmentStore"/>
/// record — most visible on a Tablet, whose dialog offers no other tab to fall back on.
///
/// Exercises the production entry points directly, the same way <see cref="PinScenarios"/> and
/// <see cref="AssignmentCompletionDocResolutionScenarios"/> do: <c>ApplyEdit</c> for the Lectern, and
/// <c>OnServerReceivedNotebookSave</c> (widened to <c>internal</c> for this purpose, matching the
/// <c>OnHistoryScanTick</c> precedent) for a Notebook/Tablet — never a real network round-trip.
/// </summary>
public class EditorAssignmentCompletionSyncScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>Seeds an Accepted canonical assignment record directly (bypassing the desk/notice send
    /// flow, already covered by <see cref="AssignmentCompletionDocResolutionScenarios"/>) and returns a
    /// document block carrying the SAME TaskId + Assignment, ready to be placed onto a Lectern/Notebook/
    /// Tablet's document exactly as a real Accept-placement would.</summary>
    private ScribeBlock SeedAcceptedAssignmentBlock(ITestPlayer assigner, ITestPlayer assignee, Guid assignmentId,
        string text = "Chop 10 logs")
    {
        var store = Mod.AssignmentStore!;
        const string date = "day 1";
        Assert.True(store.TryCreateAccepted(assignmentId, assigner.Player.PlayerUID, assignee.Player.PlayerUID,
            text, date, date, out var record));
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

    // ---------- 1.2: NotifyDoneAssignmentsInDocument idempotency ----------

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

        // Calling it again on the same (already-Completed) document must be a cheap no-op: the canonical
        // store gate rejects it before any mutation or sync push, exactly like the design relies on
        // instead of diffing old vs. new.
        Mod.NotifyDoneAssignmentsInDocument(doc);
        Assert.Equal(ScribeAssignmentState.Completed, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.State);
        Assert.Equal(completedDateAfterFirst, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.CompletedDate);
    }

    // ---------- 2.1: Lectern's whole-document flush ----------

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

        // Simulate the Editor's own checkbox: toggle Done LOCALLY (exactly what ToggleEditorTask does
        // client-side), then flush the whole document -- no ScribeCompleteTaskMessage involved.
        var authoritative = lectern.Document;
        int idx = authoritative.Blocks.ToList().FindIndex(b => b.TaskId == assignmentId);
        Assert.True(authoritative.ToggleTask(idx));
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(authoritative));

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }

    // ---------- 3.1 / 3.2: Notebook/Tablet whole-document flush ----------

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

        // The Tablet's dialog has ONLY an Editor tab -- the reported bug's exact repro. Toggle Done
        // locally, then flush the whole document; no ScribeCompleteTaskMessage is ever sent.
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

        // Same whole-document flush path as the Tablet above, establishing the fix isn't Tablet-specific --
        // completed via the Notebook's Editor tab, not the HUD, Pin Tab, or Read view.
        Assert.True(doc.ToggleTask(0));
        FlushNotebookSave(assignee, doc);

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }

    // ---------- 4.1: non-Accepted store record is left unchanged ----------

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

        // The task itself is still checked off in the document -- the discard deliberately leaves the
        // task in place (NotifyAssignmentDiscardOnDelete is a separate concern) -- but the canonical
        // record is terminal, so the flush-time derivation must be a no-op.
        block.Done = true;
        var doc = new ScribeDocument();
        doc.AppendAssignedBlock(block);

        Mod.NotifyDoneAssignmentsInDocument(doc);

        Assert.Equal(ScribeAssignmentState.Discarded, store.TryGet(assignmentId)!.Assignment!.State);
    }

    // ---------- 4.2: multiple completed assigned tasks in one save ----------

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

    // ---------- 4.3: HUD/Pin Tab/Read-view paths are unaffected ----------

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

        // Complete it the HUD/Pin-Tab way -- a dedicated completion message, never an Editor flush.
        Mod.CompleteTaskForPlayer(assignee.Player, doc.DocId, assignmentId);
        AssertBothPartiesCompleted(assigner, assignee, assignmentId);

        // A later, unrelated whole-document flush of the same (now-Completed) document must not disturb
        // it -- the new flush-time derivation call is exactly as no-op here as the idempotency covered above.
        lectern.OnRightClick(assignee.Player, wantEditor: true, quickAdd: false);
        var editedDoc = lectern.Document;
        editedDoc.AddTextSection("unrelated note");
        lectern.ApplyEdit(assignee.Player, ScribeDocumentCodec.Serialize(editedDoc));

        AssertBothPartiesCompleted(assigner, assignee, assignmentId);
    }
}
