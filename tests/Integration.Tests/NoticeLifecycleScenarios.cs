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

    /// <summary>Sends one notice row to <paramref name="assignee"/> and receives it into their hotbar slot
    /// 0 (transitioning the store record Sent -> Unaccepted), leaving a Notebook in hotbar slot 1 for
    /// Accept-time placement. Positions <paramref name="assignee"/>'s entity at a fixed, known point so
    /// callers can deterministically place things in or out of <c>NoticeScanRadius</c> of them.</summary>
    private async Task<(Guid assignmentId, IInventory assigneeHotbar)> SeedReceivedNotice(
        ITestPlayer assigner, ITestPlayer assignee, Vintagestory.API.MathTools.BlockPos deskPos, Vintagestory.API.MathTools.BlockPos assigneePos)
    {
        World.SetBlock("scribe:scribeassignmentdesk", deskPos);
        await World.Ticks(2);
        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(deskPos);
        Assert.NotNull(desk);
        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        assignee.Player.Entity.Pos.SetPos(assigneePos.X, assigneePos.Y, assigneePos.Z);

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

        return (assignmentId, assigneeHotbar);
    }

    /// <summary>consume-tasknotice-on-inbox-accept 2.1/3.1: accepting through the generic Inbox-tab action
    /// path (<see cref="ScribeModSystem.ApplyAssignmentAction"/>, NOT the notice's own dialog) proactively
    /// consumes the sealed notice still carried in the Assignee's own hotbar.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task AcceptViaInbox_ConsumesCarriedNotice()
    {
        var deskPos = World.Spawn.Offset(4, 0, 0);
        var assigner = await World.JoinPlayer("InboxAccAssigner");
        var assignee = await World.JoinPlayer("InboxAccAssignee");
        var (assignmentId, assigneeHotbar) = await SeedReceivedNotice(assigner, assignee, deskPos, deskPos);

        var store = Mod.AssignmentStore!;
        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Equal(1, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));

        Mod.ApplyAssignmentAction(assignee.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = assignmentId.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = assigneeHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });

        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Null(assigneeHotbar[0]!.Itemstack); // the carried notice was proactively consumed
        Assert.True(ScribeDocumentAttributes.TryReadFrom(assigneeHotbar[1]!.Itemstack!, out var placedDoc));
        Assert.Contains(placedDoc!.Blocks, b => b.TaskId == assignmentId && b.Text == "Chop 10 logs");
        Assert.Equal(0, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID)); // bookkeeping parity (3.2)
    }

    /// <summary>consume-tasknotice-on-inbox-accept 2.1 (multi-row gating): a two-row notice is NOT
    /// consumed while only one of its rows has resolved -- it's still a live carrier for the other row --
    /// and IS consumed once every row has resolved, even though each row resolved through a different
    /// path (one via the Inbox tab, one via the notice's own dialog).</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task MultiRowNotice_OnlyConsumedOnceEveryRowHasResolved()
    {
        var deskPos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", deskPos);
        await World.Ticks(2);
        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(deskPos);
        Assert.NotNull(desk);
        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var assigner = await World.JoinPlayer("MultiRowAssigner");
        var assignee = await World.JoinPlayer("MultiRowAssignee");
        assignee.Player.Entity.Pos.SetPos(deskPos.X, deskPos.Y, deskPos.Z);

        var row1Id = Guid.NewGuid();
        var row2Id = Guid.NewGuid();
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
                new() { AssignmentId = row1Id.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
                new() { AssignmentId = row2Id.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Mine 5 ore" },
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

        var store = Mod.AssignmentStore!;

        // Resolve row 1 via the Inbox tab -- the notice still carries row 2, unresolved, so it must stay put.
        Mod.ApplyAssignmentAction(assignee.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = row1Id.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = assigneeHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });
        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(row1Id)!.Assignment!.State);
        Assert.Equal(ScribeAssignmentState.Unaccepted, store.TryGet(row2Id)!.Assignment!.State);
        Assert.NotNull(assigneeHotbar[0]!.Itemstack); // still a live carrier for row 2 -- NOT consumed

        // Resolve row 2 via the SAME Inbox path (a different path -- the notice's own dialog -- is already
        // covered by ApplyTaskNoticeAction's own unconditional-clear tests above) -- now every row has
        // resolved, so the notice is finally consumed.
        Mod.ApplyAssignmentAction(assignee.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = row2Id.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = assigneeHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });
        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(row2Id)!.Assignment!.State);
        Assert.Null(assigneeHotbar[0]!.Itemstack); // every row resolved -- now consumed
        Assert.Equal(0, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));
    }

    /// <summary>consume-tasknotice-on-inbox-accept 2.1/3.1: declining through the generic Inbox-tab action
    /// path consumes the sealed notice even after it moved out of the Assignee's hands into a nearby
    /// container, as long as that container is within the discovery ping's own scan radius of them.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task DeclineViaInbox_ConsumesNearbyContainerNotice()
    {
        var deskPos = World.Spawn.Offset(4, 0, 0);
        var assigner = await World.JoinPlayer("InboxDecAssigner");
        var assignee = await World.JoinPlayer("InboxDecAssignee");
        var (assignmentId, assigneeHotbar) = await SeedReceivedNotice(assigner, assignee, deskPos, deskPos);

        // Move the (already-received) notice out of the hotbar and into a chest a few blocks away --
        // still well within NoticeScanRadius (12 blocks) of the assignee's own position.
        var chestPos = deskPos.AddCopy(3, 0, 0);
        World.SetBlock("game:chest-north", chestPos);
        await World.Ticks(2);
        var chest = (IBlockEntityContainer)World.Api.World.BlockAccessor.GetBlockEntity(chestPos)!;
        chest.Inventory[0]!.Itemstack = assigneeHotbar[0]!.Itemstack;
        chest.Inventory[0]!.MarkDirty();
        assigneeHotbar[0]!.Itemstack = null;
        assigneeHotbar[0]!.MarkDirty();

        var store = Mod.AssignmentStore!;
        Mod.ApplyAssignmentAction(assignee.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = assignmentId.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Decline,
        });

        Assert.Equal(ScribeAssignmentState.Declined, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Null(chest.Inventory[0]!.Itemstack); // the container-stored notice was proactively consumed
        Assert.Equal(0, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));
    }

    /// <summary>consume-tasknotice-on-inbox-accept 2.1/3.1: an Assigner-initiated Cancel still anchors the
    /// search on the Assignee (the only party who could ever be physically carrying the notice), not the
    /// player who sent the action packet.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task CancelViaInbox_AssignerInitiated_ConsumesAssigneeCarriedNotice()
    {
        var deskPos = World.Spawn.Offset(4, 0, 0);
        var assigner = await World.JoinPlayer("InboxCxlAssigner");
        var assignee = await World.JoinPlayer("InboxCxlAssignee");
        var (assignmentId, assigneeHotbar) = await SeedReceivedNotice(assigner, assignee, deskPos, deskPos);

        var store = Mod.AssignmentStore!;
        Mod.ApplyAssignmentAction(assigner.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = assignmentId.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Cancel,
        });

        Assert.Equal(ScribeAssignmentState.Cancelled, store.TryGet(assignmentId)!.Assignment!.State);
        Assert.Null(assigneeHotbar[0]!.Itemstack); // the Assignee's carried notice was proactively consumed
        Assert.Equal(0, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));
    }

    /// <summary>consume-tasknotice-on-inbox-accept: a notice sitting in a container well outside
    /// NoticeScanRadius of the Assignee's live position is left untouched by the proactive search --
    /// exactly today's behavior -- and still self-consumes the moment anyone directly interacts with it
    /// (<see cref="ScribeModSystem.ApplyTaskNoticeAction"/>'s pre-existing unconditional slot-clear).</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task AcceptViaInbox_NoticeOutOfScanRange_LeftForLazySelfConsumption()
    {
        var deskPos = World.Spawn.Offset(4, 0, 0);
        var assigner = await World.JoinPlayer("InboxFarAssigner");
        var assignee = await World.JoinPlayer("InboxFarAssignee");
        var (assignmentId, assigneeHotbar) = await SeedReceivedNotice(assigner, assignee, deskPos, deskPos);

        // Move the notice into a chest far outside NoticeScanRadius (12 blocks) of the assignee.
        var farChestPos = deskPos.AddCopy(60, 0, 60);
        World.SetBlock("game:chest-north", farChestPos);
        await World.Ticks(2);
        var farChest = (IBlockEntityContainer)World.Api.World.BlockAccessor.GetBlockEntity(farChestPos)!;
        farChest.Inventory[0]!.Itemstack = assigneeHotbar[0]!.Itemstack;
        farChest.Inventory[0]!.MarkDirty();
        assigneeHotbar[0]!.Itemstack = null;
        assigneeHotbar[0]!.MarkDirty();

        var store = Mod.AssignmentStore!;
        Mod.ApplyAssignmentAction(assignee.Player, new ScribeAssignmentActionMessage
        {
            AssignmentId = assignmentId.ToByteArray(),
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = assigneeHotbar.InventoryID,
            TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });

        // The transition itself is unaffected by physical notice location...
        Assert.Equal(ScribeAssignmentState.Accepted, store.TryGet(assignmentId)!.Assignment!.State);
        // ...but the far-away notice is untouched, and the outstanding-notice count is NOT decremented
        // (nothing was actually removed).
        Assert.NotNull(farChest.Inventory[0]!.Itemstack);
        Assert.Equal(1, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));

        // Direct interaction with the stale notice still self-consumes it unconditionally, exactly as
        // before this change (the documented fallback, not a regression) -- simulated here by the
        // Assignee walking back and picking it up (ApplyTaskNoticeAction resolves its held-slot identity
        // through the player's OWN InventoryManager, so the source slot must be one they've actually
        // carried, not an unopened remote chest -- see ResolveTaskNoticeSlot).
        var recoveredNotice = farChest.Inventory[0]!.Itemstack;
        farChest.Inventory[0]!.Itemstack = null;
        farChest.Inventory[0]!.MarkDirty();
        assigneeHotbar[0]!.Itemstack = recoveredNotice;
        assigneeHotbar[0]!.MarkDirty();

        Mod.ApplyTaskNoticeAction(assignee.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = assigneeHotbar.InventoryID,
            SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Decline,
        });
        Assert.Null(assigneeHotbar[0]!.Itemstack);
        Assert.Equal(0, Mod.OutstandingNoticeCount(assignee.Player.PlayerUID));
    }
}
