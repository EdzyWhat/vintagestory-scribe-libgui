using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// fix-offline-assignment-send-validation tasks.md §3: <see cref="ScribeModSystem.SendAssignmentBatch"/>'s
/// target-validation guard must accept a currently-offline player who is only known via the persisted
/// <see cref="ScribeKnownPlayersStore"/> (persist-known-players-for-assignment), through BOTH delivery
/// paths — and must still reject a target that is neither online nor known at all, exactly as before this
/// change. A target uid is seeded directly into <c>KnownPlayersStore</c> and deliberately never joined as
/// a live player, so it never enters the engine's online-player table and reproduces the exact
/// "offline but previously known" condition.
/// </summary>
public class OfflineAssignmentSendScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private ItemStack BlankNotice() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);

    [AtlasScenario(RollbackWorld = true)]
    public async Task SendNotice_ToOfflineKnownPlayer_Succeeds()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("OffNoticeAssign");

        var fakeUid = "offline-" + Guid.NewGuid();
        Mod.KnownPlayersStore!.Upsert(fakeUid, "SomeOfflinePlayer");

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
            TargetPlayerUid = fakeUid,
            DeliveryChoice = (byte)ScribeDeliveryChoice.SendNotice,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        // Previously: the target-validation guard rejected this uid outright (never online this session),
        // so the batch was silently dropped before either delivery path ran — no notice, no store record.
        var outputSlot = desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex];
        Assert.NotNull(outputSlot.Itemstack);
        Assert.Equal(ScribeAssignmentState.Sent, Mod.AssignmentStore!.TryGet(assignmentId)!.Assignment!.State);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task SendLocalInboxes_ToOfflineKnownPlayer_Succeeds()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("OffInboxAssign");

        var fakeUid = "offline-" + Guid.NewGuid();
        Mod.KnownPlayersStore!.Upsert(fakeUid, "SomeOfflinePlayer");

        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = fakeUid,
            DeliveryChoice = (byte)ScribeDeliveryChoice.LocalInboxes,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        var received = Mod.AssignmentStore!.Received(fakeUid);
        Assert.Single(received);
        Assert.Equal(assignmentId, received[0].TaskId);
    }

    /// <summary>Regression lock-in: a uid that is neither joined this session nor upserted into
    /// <c>KnownPlayersStore</c> is still rejected exactly as before this change — no assignment record
    /// created, no notice placed in the output slot.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task SendNotice_ToTrulyUnknownPlayer_StillSilentlyRejected()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var assigner = await World.JoinPlayer("UnknownTgtAssign");

        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(pos);
        Assert.NotNull(desk);
        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack = BlankNotice();
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var trulyUnknownUid = "never-joined-or-known-" + Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            StagingSlot = BlockEntityAssignmentDesk.StagingSlotIndex,
            TargetPlayerUid = trulyUnknownUid,
            DeliveryChoice = (byte)ScribeDeliveryChoice.SendNotice,
            Rows = new List<ScribeAssignmentBatchRow>
            {
                new() { AssignmentId = assignmentId.ToByteArray(), Kind = (byte)ScribeBlockKind.Task, Text = "Chop 10 logs" },
            },
        });

        Assert.Null(desk.Inventory[BlockEntityAssignmentDesk.NoticeOutputSlotIndex].Itemstack);
        Assert.Null(Mod.AssignmentStore!.TryGet(assignmentId));
    }
}
