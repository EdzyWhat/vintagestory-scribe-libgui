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
        // add-task-notice-redirect-confirm 2.3: the recipient-matches-already path leaves no redirect trace.
        Assert.Null(store.TryGet(assignmentId)!.Assignment!.RedirectedFromUid);
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

    /// <summary>add-task-notice-redirect-confirm 2.1: Decline keeps today's non-recipient rejection
    /// exactly as it was — a physical holder who isn't the recorded recipient still can't decline it
    /// away from them.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task NonRecipientDecline_StillSilentlyIgnored()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("RdrDeclAssigner");
        var recipient = await World.JoinPlayer("RdrDeclRecipient");
        var holder = await World.JoinPlayer("RdrDeclHolder");

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
            TargetPlayerUid = recipient.Player.PlayerUID,
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

        // Hand it to a THIRD player instead of the recipient — a dropped/traded notice changing hands.
        // (The own-inventory proximity scan only marks a notice Received for its actual recipient, so a
        // non-recipient holder's own Accept/Decline call is the thing that proves physical receipt here.)
        var holderHotbar = holder.Player.InventoryManager.GetHotbarInventory();
        holderHotbar[0]!.Itemstack = sealedNotice!;
        holderHotbar[0]!.MarkDirty();

        Mod.ApplyTaskNoticeAction(holder.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = holderHotbar.InventoryID,
            SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Decline,
        });

        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(assignmentId)!.Assignment!.State); // ignored
        Assert.NotNull(holderHotbar[0]!.Itemstack); // notice was NOT consumed
        Assert.Null(store.TryGet(assignmentId)!.Assignment!.RedirectedFromUid);
    }

    /// <summary>add-task-notice-redirect-confirm 2.2: a non-recipient holder's confirmed Accept succeeds,
    /// redirecting the assignment's target to them and stamping RedirectedFromUid with the original
    /// recipient.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task NonRecipientAccept_RedirectsTargetAndSucceeds()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("RdrAccAssigner");
        var recipient = await World.JoinPlayer("RdrAccRecipient");
        var holder = await World.JoinPlayer("RdrAccHolder");

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
            TargetPlayerUid = recipient.Player.PlayerUID,
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

        var holderHotbar = holder.Player.InventoryManager.GetHotbarInventory();
        holderHotbar[0]!.Itemstack = sealedNotice!;
        holderHotbar[0]!.MarkDirty();
        Mod.MarkReceivedForCarriedNotices(holder.Player);

        var notebookSlot = holderHotbar[1];
        notebookSlot!.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
        notebookSlot.MarkDirty();

        Mod.ApplyTaskNoticeAction(holder.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = holderHotbar.InventoryID,
            SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = holderHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });

        var assignment = store.TryGet(assignmentId)!.Assignment!;
        Assert.Equal(ScribeAssignmentState.Accepted, assignment.State);
        Assert.Equal(holder.Player.PlayerUID, assignment.TargetPlayerUid); // redirected to the accepting player
        Assert.Equal(recipient.Player.PlayerUID, assignment.RedirectedFromUid); // original recipient preserved
        Assert.Null(holderHotbar[0]!.Itemstack); // the sealed notice was consumed on Accept
        Assert.True(ScribeDocumentAttributes.TryReadFrom(notebookSlot.Itemstack!, out var placedDoc));
        Assert.Contains(placedDoc!.Blocks, b => b.TaskId == assignmentId && b.Text == "Chop 10 logs");
    }
}
