using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Drives the server packet handlers themselves, not the logic behind them: a well-formed packet reaches that logic,
/// and a packet with a malformed id or an unknown document is ignored without throwing or changing anything. The
/// other scenario classes call the inner logic directly, so this is the only coverage of the guard layer in front of it.
/// </summary>
public class ServerPacketHandlerScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    private static readonly byte[] ShortId = new byte[3];

    private async Task<(BlockEntityScribeLectern lectern, Guid docId, Guid taskId)> SeedLectern(
        ITestPlayer editor, Vintagestory.API.MathTools.BlockPos pos, string taskText = "Find copper")
    {
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(editor.Player, wantEditor: true, quickAdd: false);
        var doc = new ScribeDocument();
        doc.AddTask(taskText);
        Assert.True(lectern.ApplyEdit(editor.Player, ScribeDocumentCodec.Serialize(doc)));

        return (lectern, lectern.Document.DocId, lectern.Document.Blocks[0].TaskId);
    }

    private static ScribeSetPinMessage PinPacket(Guid docId, Guid taskId, bool pinned) => new()
    {
        DocId = docId.ToByteArray(),
        TaskId = taskId.ToByteArray(),
        Pinned = pinned,
        SnapshotText = "Find copper",
    };

    // ---------- doc + task packets ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task SetPin_packet_pins_and_unpins_the_task()
    {
        var player = await World.JoinPlayer("PktPinner");
        var (_, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: true));
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));

        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: false));
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task CompleteTask_packet_completes_the_source_task()
    {
        var player = await World.JoinPlayer("PktCompleter");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: true));

        Mod.OnServerReceivedCompleteTask(player.Player, new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)ScribeCompletionPolicy.Unpin,
        });

        Assert.True(lectern.Document.FindByTaskId(taskId)!.Done);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task DeleteTask_packet_removes_the_source_task()
    {
        var player = await World.JoinPlayer("PktDeleter");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: true));

        Mod.OnServerReceivedDeleteTask(player.Player, new ScribeDeleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
        });

        Assert.Null(lectern.Document.FindByTaskId(taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task EditPinnedTask_packet_rewrites_the_source_text()
    {
        var player = await World.JoinPlayer("PktEditor");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: true));

        Mod.OnServerReceivedEditPinnedTask(player.Player, new ScribeEditPinnedTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Text = "Find tin",
        });

        Assert.Equal("Find tin", lectern.Document.FindByTaskId(taskId)!.Text);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Doc_task_packets_with_a_malformed_id_are_ignored()
    {
        var player = await World.JoinPlayer("PktMalformed");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        var uid = player.Player.PlayerUID;

        // A well-formed pin first, so "ignored" means the existing state survives untouched.
        Mod.OnServerReceivedSetPin(player.Player, PinPacket(docId, taskId, pinned: true));

        var good = docId.ToByteArray();
        Mod.OnServerReceivedSetPin(player.Player, new ScribeSetPinMessage { DocId = ShortId, TaskId = good, Pinned = false });
        Mod.OnServerReceivedCompleteTask(player.Player, new ScribeCompleteTaskMessage { DocId = good, TaskId = ShortId });
        Mod.OnServerReceivedEditPinnedTask(player.Player, new ScribeEditPinnedTaskMessage { DocId = ShortId, TaskId = good, Text = "x" });
        Mod.OnServerReceivedDeleteTask(player.Player, new ScribeDeleteTaskMessage { DocId = good, TaskId = null });
        Mod.OnServerReceivedSetTrackerQuantity(player.Player, new ScribeSetTrackerQuantityMessage { DocId = null, TaskId = good, Quantity = 9 });
        Mod.OnServerReceivedSetQuestObjectiveProgress(player.Player, new ScribeSetQuestObjectiveProgressMessage { DocId = ShortId, TaskId = ShortId, Quantity = 9 });

        Assert.True(Mod.PinStore!.IsPinned(uid, docId, taskId));
        var task = lectern.Document.FindByTaskId(taskId)!;
        Assert.False(task.Done);
        Assert.Equal("Find copper", task.Text);
    }

    // ---------- station packets ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task RequestAccess_then_Edit_packets_update_the_lectern()
    {
        var editor = await World.JoinPlayer("PktStation1");
        var (lectern, docId, _) = await SeedLectern(editor, World.Spawn.Offset(5, 0, 0));
        var newcomer = await World.JoinPlayer("PktStation2");
        lectern.ReleaseLock(editor.Player.PlayerUID);

        Mod.OnServerReceivedRequestAccess(newcomer.Player, new ScribeRequestAccessMessage
        {
            DocIdBytes = docId.ToByteArray(),
            WantEditor = true,
        });
        var edited = new ScribeDocument();
        edited.AddTask("Written by packet");
        Mod.OnServerReceivedEdit(newcomer.Player, new ScribeEditDocumentMessage
        {
            DocIdBytes = docId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(edited),
        });

        Assert.Contains(lectern.Document.Blocks, b => b.Text == "Written by packet");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task ReleaseLock_packet_frees_the_editor_lock()
    {
        var holder = await World.JoinPlayer("PktHolder");
        var (lectern, docId, _) = await SeedLectern(holder, World.Spawn.Offset(5, 0, 0));
        var challenger = await World.JoinPlayer("PktChallenger");

        Mod.OnServerReceivedReleaseLock(holder.Player, new ScribeReleaseLockMessage { DocIdBytes = docId.ToByteArray() });

        var edited = new ScribeDocument();
        edited.AddTask("Taken over");
        lectern.OnRequestAccess(challenger.Player, wantEditor: true, quickAdd: false);
        Assert.True(lectern.ApplyEdit(challenger.Player, ScribeDocumentCodec.Serialize(edited)));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Edit_packet_from_a_non_holder_is_refused()
    {
        var holder = await World.JoinPlayer("PktLockHolder");
        var (lectern, docId, _) = await SeedLectern(holder, World.Spawn.Offset(5, 0, 0));
        var intruder = await World.JoinPlayer("PktIntruder");

        var edited = new ScribeDocument();
        edited.AddTask("Intruder text");
        Mod.OnServerReceivedEdit(intruder.Player, new ScribeEditDocumentMessage
        {
            DocIdBytes = docId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(edited),
        });

        Assert.DoesNotContain(lectern.Document.Blocks, b => b.Text == "Intruder text");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Station_packets_for_an_unknown_document_are_ignored()
    {
        var player = await World.JoinPlayer("PktUnknownDoc");
        var (lectern, _, _) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));
        var unknown = Guid.NewGuid().ToByteArray();

        Mod.OnServerReceivedEdit(player.Player, new ScribeEditDocumentMessage { DocIdBytes = unknown, DocumentBytes = new byte[] { 1, 2, 3 } });
        Mod.OnServerReceivedReleaseLock(player.Player, new ScribeReleaseLockMessage { DocIdBytes = unknown });
        Mod.OnServerReceivedRequestAccess(player.Player, new ScribeRequestAccessMessage { DocIdBytes = unknown, WantEditor = true });
        Mod.OnServerReceivedRecordVisitor(player.Player, new ScribeRecordVisitorMessage { DocIdBytes = unknown });
        Mod.OnServerReceivedEditGuestbookNote(player.Player, new ScribeEditGuestbookNoteMessage { DocIdBytes = unknown, Note = "hi", InGameDate = "d1" });
        Mod.OnServerReceivedRecordVisitor(player.Player, new ScribeRecordVisitorMessage { DocIdBytes = null });

        Assert.Equal("Find copper", lectern.Document.Blocks[0].Text);
    }

    // ---------- manual history packets ----------

    [AtlasScenario(RollbackWorld = true)]
    public async Task History_packets_with_a_malformed_entry_id_are_ignored()
    {
        var player = await World.JoinPlayer("PktHistory");

        Mod.OnServerReceivedAddHistoryEntry(player.Player, new ScribeAddHistoryEntryMessage { EntryId = ShortId, Text = "x" });
        Mod.OnServerReceivedSetHistoryEntryText(player.Player, new ScribeSetHistoryEntryTextMessage { EntryId = null, Text = "x" });
        Mod.OnServerReceivedDeleteHistoryEntry(player.Player, new ScribeDeleteHistoryEntryMessage { EntryId = ShortId });
    }
}
