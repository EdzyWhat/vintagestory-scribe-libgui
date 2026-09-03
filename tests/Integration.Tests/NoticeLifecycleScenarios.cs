using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// refine-task-notice-ux tasks.md 2.5: the full Task Notice send -> receive -> accept/decline path,
/// exercised through the real production entry points (<see cref="ScribeModSystem.SendAssignmentBatch"/>,
/// <see cref="ScribeModSystem.MarkReceivedForCarriedNotices"/>, <see cref="ScribeModSystem.ApplyTaskNoticeAction"/>)
/// rather than the underlying <see cref="ScribeAssignmentStore"/> directly (that state-machine coverage
/// already lives in Core.Tests' <c>FullTaskNoticeLifecycle_SentThenReceivedThenAccepted</c>). These three
/// methods are the same ones the "OnServerReceived*" network handlers delegate to unchanged -- calling
/// them directly here is the established seam (see <c>ScribeModSystem.PinOperations.cs</c>'s
/// <c>SetPinForPlayer</c>/<c>CompleteTaskForPlayer</c> precedent) rather than round-tripping real network
/// packets, which Atlas does not simulate.
/// </summary>
public class NoticeLifecycleScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private ItemStack BlankNotice() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);

    [AtlasScenario(RollbackWorld = true)]
    public async Task FullNoticeLifecycle_SendThenReceiveThenAccept()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("NoticeAssigner");
        var assignee = await World.JoinPlayer("NoticeAssignee");

        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(pos);
        Assert.NotNull(desk);

        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = assignee.Player.PlayerUID,
            DeliveryChoice = (byte)ScribeDeliveryChoice.SendNotice,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        // Sent History shows it immediately as Sent; the Inbox stays silent until physical receipt.
        var store = Mod.AssignmentStore!;
        Assert.Equal(ScribeAssignmentState.Sent, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Single(store.Sent(assigner.Player.PlayerUID));
        Assert.Empty(store.Received(assignee.Player.PlayerUID));

        // Hand-deliver: move the freshly-sealed notice out of the Desk's output slot and into the
        // Assignee's own inventory (the "synthetic inventory move" the design calls for), then run the
        // same own-inventory scan the proximity heartbeat runs every tick.
        var outputSlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex];
        var sealedNotice = outputSlot.Itemstack;
        Assert.NotNull(sealedNotice);
        outputSlot.Itemstack = null;
        outputSlot.MarkDirty();

        var assigneeHotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        assigneeHotbar[0]!.Itemstack = sealedNotice!;
        assigneeHotbar[0]!.MarkDirty();

        Mod.MarkReceivedForCarriedNotices(assignee.Player);

        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Single(store.Received(assignee.Player.PlayerUID));

        // Accept, placing onto a Notebook also carried by the Assignee.
        var notebookSlot = assigneeHotbar[1];
        notebookSlot!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
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

        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Null(assigneeHotbar[0]!.Itemstack); // the sealed notice was consumed on Accept
        Assert.True(ScribeDocumentAttributes.TryReadFrom(notebookSlot.Itemstack!, out var placedDoc));
        Assert.Contains(placedDoc!.Blocks, b => b.TaskId == assignmentId && b.Text == "Chop 10 logs");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task FullNoticeLifecycle_SendThenReceiveThenDecline()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("DeclineAssigner");
        var assignee = await World.JoinPlayer("DeclineAssignee");

        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(pos);
        Assert.NotNull(desk);

        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = assignee.Player.PlayerUID,
            DeliveryChoice = (byte)ScribeDeliveryChoice.SendNotice,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        var store = Mod.AssignmentStore!;
        var outputSlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex];
        var sealedNotice = outputSlot.Itemstack;
        outputSlot.Itemstack = null;
        outputSlot.MarkDirty();

        var assigneeHotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        assigneeHotbar[0]!.Itemstack = sealedNotice!;
        assigneeHotbar[0]!.MarkDirty();
        Mod.MarkReceivedForCarriedNotices(assignee.Player);
        Assert.Single(store.Received(assignee.Player.PlayerUID));

        // Decline: item consumed, record transitions to Declined -- exactly like an in-range decline,
        // with no active notification to the Assigner (their history simply reflects it passively).
        Mod.ApplyTaskNoticeAction(assignee.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = assigneeHotbar.InventoryID,
            SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Decline,
        });

        Assert.Equal(ScribeAssignmentState.Declined, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Null(assigneeHotbar[0]!.Itemstack); // the notice was consumed on Decline
        Assert.Single(store.Sent(assigner.Player.PlayerUID)); // Assigner's history still shows it, now Declined
    }
}
