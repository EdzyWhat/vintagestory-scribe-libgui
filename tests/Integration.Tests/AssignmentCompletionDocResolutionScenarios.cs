using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// fix-assignment-completion-doc-resolution: completing a pinned Accepted-assignment task must
/// derive Completed on the canonical <see cref="ScribeAssignmentStore"/> record even when the
/// task's owning Notebook is not currently in the completing player's inventory (previously this
/// silently never happened, since the derivation only ran inside <c>CompleteTaskForPlayer</c>'s
/// resolved-document branch). Exercises the real production path
/// (<see cref="ScribeModSystem.CompleteTaskForPlayer"/>) the same way <see cref="PinScenarios"/>
/// and <see cref="NoticeLifecycleScenarios"/> do.
/// </summary>
public class AssignmentCompletionDocResolutionScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private ItemStack BlankNotice() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);

    /// <summary>Sends, receives, and accepts a Task Notice onto a Notebook the assignee carries in
    /// their hotbar slot 1, pinning the resulting task. Returns the assignment id (== TaskId), the
    /// Notebook's DocId, and the hotbar slot holding the Notebook so the caller can remove it.</summary>
    private async Task<(Guid assignmentId, Guid docId, Vintagestory.API.Common.ItemSlot notebookSlot)>
        SeedAcceptedAndPinnedAssignment(ITestPlayer assigner, ITestPlayer assignee, Vintagestory.API.MathTools.BlockPos deskPos)
    {
        World.SetBlock("scribe:scribeassignmentdesk", deskPos);
        await World.Ticks(2);
        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(deskPos);
        Assert.NotNull(desk);

        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = deskPos.X,
            Y = deskPos.Y,
            Z = deskPos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = assignee.Player.PlayerUID,
            DeliveryChoice = (byte)ScribeDeliveryChoice.SendNotice,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        var outputSlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex];
        var sealedNotice = outputSlot.Itemstack;
        outputSlot.Itemstack = null;
        outputSlot.MarkDirty();

        var assigneeHotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        assigneeHotbar[0]!.Itemstack = sealedNotice!;
        assigneeHotbar[0]!.MarkDirty();
        Mod.MarkReceivedForCarriedNotices(assignee.Player);

        var notebookSlot = assigneeHotbar[1]!;
        notebookSlot.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
        notebookSlot.MarkDirty();

        Mod.ApplyTaskNoticeAction(assignee.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = assigneeHotbar.InventoryID,
            SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = assigneeHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });

        Assert.True(ScribeDocumentAttributes.TryReadFrom(notebookSlot.Itemstack!, out var placedDoc));
        var docId = placedDoc!.DocId;

        Mod.SetPinForPlayer(assignee.Player, docId, assignmentId, pinned: true);

        return (assignmentId, docId, notebookSlot);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_a_pinned_assignment_derives_completed_even_when_notebook_is_absent()
    {
        var assigner = await World.JoinPlayer("DocResAssigner");
        var assignee = await World.JoinPlayer("DocResAssignee");
        var (assignmentId, docId, notebookSlot) =
            await SeedAcceptedAndPinnedAssignment(assigner, assignee, World.Spawn.Offset(4, 0, 0));

        var store = Mod.AssignmentStore!;
        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(assignmentId)!.Assignment!.State);

        // Remove the Notebook from the assignee's inventory entirely -- TryResolveDocHost's fallback
        // only scans the completing player's own inventory, so the document is now unresolvable.
        notebookSlot.Itemstack = null;
        notebookSlot.MarkDirty();

        Mod.CompleteTaskForPlayer(assignee.Player, docId, assignmentId);

        // The canonical store record still derives Completed...
        Assert.Equal(ScribeAssignmentState.Completed, store.TryGet(assignmentId)!.Assignment!.State);
        // ...visible on both the Assignee's Received view and the Assigner's Sent History.
        Assert.Equal(ScribeAssignmentState.Completed,
            Assert.Single(store.Received(assignee.Player.PlayerUID)).Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Completed,
            Assert.Single(store.Sent(assigner.Player.PlayerUID)).Assignment!.State);

        // The pin's own done-state was already unconditional before this fix and still is.
        var pin = Assert.Single(Mod.PinStore!.Get(assignee.Player.PlayerUID));
        Assert.True(pin.LastKnownDone);
    }
}
