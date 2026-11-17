using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;

namespace Integration.Tests;

/// <summary>
/// Dates and repeat flags travel with copies (Scriptorium copy and append, JSON import, the clipboard codecs)
/// and never ride an assignment: the batch row has no date field and the placed task has none.
/// </summary>
public class DatedTaskTransferScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private ItemStack Notebook() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);

    private static ScribeDocument DatedDocument()
    {
        var doc = new ScribeDocument();
        doc.AddTask("Pay the toll");
        var id = doc.Blocks[0].TaskId;
        doc.TrySetTaskDate(id, ScribeDate.Create(3, 5, 25));
        doc.TrySetTaskRepeat(id, true);
        return doc;
    }

    private async Task<(BlockEntityScriptorium scriptorium, Vintagestory.API.MathTools.BlockPos pos)> SeedScriptorium()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scriptorium", pos);
        await World.Ticks(2);
        var scriptorium = World.BlockEntityAt<BlockEntityScriptorium>(pos);
        Assert.NotNull(scriptorium);
        var source = Notebook();
        ScribeDocumentAttributes.WriteTo(source, DatedDocument());
        scriptorium!.Inventory[0].Itemstack = source;
        scriptorium.Inventory[1].Itemstack = Notebook();
        return (scriptorium, pos);
    }

    private static void AssertDated(ItemStack stack)
    {
        Assert.True(ScribeDocumentAttributes.TryReadFrom(stack, out var doc));
        var block = Assert.Single(doc!.Blocks);
        Assert.Equal(ScribeDate.Create(3, 5, 25), block.Date);
        Assert.True(block.RepeatYearly);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task ScriptoriumCopy_Overwrite_KeepsDateAndRepeat()
    {
        var player = await World.JoinPlayer("XferCopy1");
        var (scriptorium, pos) = await SeedScriptorium();

        Mod.OnServerReceivedTranscribeCopy(player.Player, new ScribeTranscribeCopyMessage
        {
            X = pos.X, Y = pos.Y, Z = pos.Z, SourceSlot = 0, TargetSlot = 1, AllowOverwrite = true,
        });

        AssertDated(scriptorium.Inventory[1].Itemstack!);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task ScriptoriumCopy_Append_KeepsDateAndRepeat()
    {
        var player = await World.JoinPlayer("XferCopy2");
        var (scriptorium, pos) = await SeedScriptorium();

        Mod.OnServerReceivedTranscribeCopy(player.Player, new ScribeTranscribeCopyMessage
        {
            X = pos.X, Y = pos.Y, Z = pos.Z, SourceSlot = 0, TargetSlot = 1, Append = true,
        });

        AssertDated(scriptorium.Inventory[1].Itemstack!);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task ScriptoriumImport_FromJson_KeepsDateAndRepeat()
    {
        var player = await World.JoinPlayer("XferImport");
        var (scriptorium, pos) = await SeedScriptorium();

        Mod.OnServerReceivedTranscribeImport(player.Player, new ScribeTranscribeImportMessage
        {
            X = pos.X, Y = pos.Y, Z = pos.Z, TargetSlot = 1, AllowOverwrite = true,
            DocumentJson = ScribeDocumentJsonCodec.Serialize(DatedDocument()),
        });

        AssertDated(scriptorium.Inventory[1].Itemstack!);
    }

    [AtlasScenario]
    public void ClipboardCodecs_KeepDateAndRepeat_ThroughJsonAndTsv()
    {
        var doc = DatedDocument();

        Assert.True(ScribeDocumentJsonCodec.TryDeserialize(ScribeDocumentJsonCodec.Serialize(doc), out var viaJson));
        Assert.Equal(ScribeDate.Create(3, 5, 25), viaJson!.Blocks[0].Date);
        Assert.True(viaJson.Blocks[0].RepeatYearly);

        Assert.True(ScribeDocumentTsvCodec.TryDeserialize(ScribeDocumentTsvCodec.Serialize(doc), out var viaTsv));
        Assert.Equal(ScribeDate.Create(3, 5, 25), viaTsv!.Blocks[0].Date);
        Assert.True(viaTsv.Blocks[0].RepeatYearly);
    }

    [AtlasScenario]
    public void AssignmentBatchRow_CarriesNoDate()
    {
        var names = typeof(ScribeAssignmentBatchRow).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Date", names);
        Assert.DoesNotContain("RepeatYearly", names);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task AssignmentPlacement_DropsTheDate()
    {
        var assigner = await World.JoinPlayer("XferAssigner");
        var assignee = await World.JoinPlayer("XferAssignee");
        var deskPos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", deskPos);
        await World.Ticks(2);
        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(deskPos);
        Assert.NotNull(desk);
        desk!.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].Itemstack =
            new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);
        desk.Inventory[BlockEntityAssignmentDesk.NoticeSupplySlotIndex].MarkDirty();

        var assignmentId = Guid.NewGuid();
        Mod.SendAssignmentBatch(assigner.Player, new ScribeSendAssignmentBatchMessage
        {
            X = deskPos.X, Y = deskPos.Y, Z = deskPos.Z,
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

        var hotbar = assignee.Player.InventoryManager.GetHotbarInventory();
        hotbar[0]!.Itemstack = sealedNotice!;
        hotbar[0]!.MarkDirty();
        Mod.MarkReceivedForCarriedNotices(assignee.Player);
        hotbar[1]!.Itemstack = Notebook();
        hotbar[1]!.MarkDirty();

        Mod.ApplyTaskNoticeAction(assignee.Player, new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = hotbar.InventoryID, SourceSlotId = 0,
            Action = (byte)ScribeAssignmentAction.Accept,
            TargetInventoryId = hotbar.InventoryID, TargetSlotId = 1,
            NewTaskInsert = (byte)ScribeNewTaskInsert.Top,
        });

        Assert.True(ScribeDocumentAttributes.TryReadFrom(hotbar[1]!.Itemstack!, out var placed));
        var block = placed!.Blocks.Single(b => b.TaskId == assignmentId);
        Assert.Null(block.Date);
        Assert.False(block.RepeatYearly);
    }
}
