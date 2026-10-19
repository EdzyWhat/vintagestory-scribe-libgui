using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Exercises the per-player pin store through the real server path (SetPinForPlayer / CompleteTaskForPlayer,
/// which the network handlers delegate to), addressed by (DocId, TaskId). Restart persistence lives in
/// PersistenceScenarios.
/// </summary>
public class PinScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    /// <summary>Places a lectern with a one-task document applied through the production edit path,
    /// and returns the lectern plus its (DocId, TaskId).</summary>
    private async Task<(BlockEntityScribeLectern lectern, System.Guid docId, System.Guid taskId)> SeedLectern(
        ITestPlayer editor, Vintagestory.API.MathTools.BlockPos pos, string taskText = "Find copper")
    {
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(editor.Player, wantEditor: true, quickAdd: false); // acquire the lock
        var doc = new ScribeDocument();
        doc.AddTask(taskText);
        Assert.True(lectern.ApplyEdit(editor.Player, ScribeDocumentCodec.Serialize(doc)));

        var block = lectern.Document.Blocks[0];
        return (lectern, lectern.Document.DocId, block.TaskId);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pinning_a_task_is_server_observable()
    {
        var player = await World.JoinPlayer("PinTester1");
        var (_, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Unpinning_a_task_is_server_observable()
    {
        var player = await World.JoinPlayer("PinTester2");
        var (_, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: false);

        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Pins_are_per_player_isolated()
    {
        var alice = await World.JoinPlayer("PinAlice");
        var bob = await World.JoinPlayer("PinBob");
        var (lectern, docId, taskId) = await SeedLectern(alice, World.Spawn.Offset(5, 0, 0));

        // Add a second task so the two players pin different tasks.
        lectern.OnRightClick(alice.Player, wantEditor: true, quickAdd: false);
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.AddTask("Find tin");
        Assert.True(lectern.ApplyEdit(alice.Player, ScribeDocumentCodec.Serialize(doc)));
        var tinId = lectern.Document.Blocks[1].TaskId;

        Mod.SetPinForPlayer(alice.Player, docId, taskId, pinned: true); // Alice pins "Find copper"
        Mod.SetPinForPlayer(bob.Player, docId, tinId, pinned: true);     // Bob pins "Find tin"

        var store = Mod.PinStore!;
        Assert.True(store.IsPinned(alice.Player.PlayerUID, docId, taskId));
        Assert.False(store.IsPinned(alice.Player.PlayerUID, docId, tinId));
        Assert.True(store.IsPinned(bob.Player.PlayerUID, docId, tinId));
        Assert.False(store.IsPinned(bob.Player.PlayerUID, docId, taskId));
    }

    /// <summary>Default policy is Sink: completing a pinned task marks it done (in the store, which is
    /// authoritative) and writes through to the resolvable source document, but KEEPS the pin.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_a_pinned_task_sinks_it_by_default()
    {
        var player = await World.JoinPlayer("PinCompleter");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);

        // Sink: pin retained + marked done in the store; write-through set the source doc done too.
        var store = Mod.PinStore!;
        var pin = Assert.Single(store.Get(player.Player.PlayerUID));
        Assert.True(pin.LastKnownDone);                                     // store owns done-state
        Assert.True(store.IsPinned(player.Player.PlayerUID, docId, taskId)); // pin kept (Sink)
        Assert.True(lectern.Document.FindByTaskId(taskId)!.Done);           // write-through to source
    }

    /// <summary>Unpin policy: completing marks done and removes the completing player's pin.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_removes_the_pin_under_unpin_policy()
    {
        var player = await World.JoinPlayer("PinUnpinner");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId, ScribeCompletionPolicy.Unpin);

        Assert.True(lectern.Document.FindByTaskId(taskId)!.Done);            // write-through still happens
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId)); // pin removed
    }

    /// <summary>Delete policy: completing deletes the underlying task from its source document and
    /// removes the pin.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_deletes_the_task_under_delete_policy()
    {
        var player = await World.JoinPlayer("PinDeleter");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId, ScribeCompletionPolicy.Delete);

        Assert.Null(lectern.Document.FindByTaskId(taskId));                  // task deleted from source
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId)); // pin removed
    }

    /// <summary>Completion reconciles ONLY the acting player: another player who pinned the same task
    /// keeps their own pin and its (unchanged) done snapshot — the grief-proof, player-owned rule.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completion_reconciles_only_the_completing_player()
    {
        var alice = await World.JoinPlayer("SharedAlice");
        var bob = await World.JoinPlayer("SharedBob");
        var (_, docId, taskId) = await SeedLectern(alice, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(alice.Player, docId, taskId, pinned: true);
        Mod.SetPinForPlayer(bob.Player, docId, taskId, pinned: true);

        // Alice completes under Unpin (her client-local policy, carried in the request); only her pin is removed.
        Mod.CompleteTaskForPlayer(alice.Player, docId, taskId, ScribeCompletionPolicy.Unpin);

        var store = Mod.PinStore!;
        Assert.False(store.IsPinned(alice.Player.PlayerUID, docId, taskId)); // completer's pin removed
        var bobPin = Assert.Single(store.Get(bob.Player.PlayerUID));
        Assert.True(store.IsPinned(bob.Player.PlayerUID, docId, taskId));    // other pinner retained
        Assert.False(bobPin.LastKnownDone);                                  // and NOT marked done
    }

    /// <summary>The store owns done-state: completing a pinned task whose source is destroyed still
    /// records completion in the store (Sink keeps the now-done pin), no document required.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_a_pin_with_no_source_records_done_in_the_store()
    {
        var player = await World.JoinPlayer("SrclessCompleter");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Destroy the source so nothing resolves; the pin remains (player-owned, non-destructive).
        World.SetBlock("game:air", pos);
        await World.Ticks(2);

        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);

        var pin = Assert.Single(Mod.PinStore!.Get(player.Player.PlayerUID));
        Assert.True(pin.LastKnownDone);                                      // store recorded completion
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId)); // Sink kept it
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Unpin_works_after_the_lectern_is_removed()
    {
        var player = await World.JoinPlayer("SrclessUnpinner");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Break the lectern: the pin is kept, and unpin must still succeed by identity alone.
        World.SetBlock("game:air", pos);
        await World.Ticks(2);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: false);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    /// <summary>Player-owned: breaking a lectern does NOT clear or alter a pin; it keeps its last-known
    /// snapshot and remains completable (a re-place can restore its source).</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Breaking_a_lectern_keeps_pins_intact()
    {
        var player = await World.JoinPlayer("BreakObserver");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        World.SetBlock("game:air", pos);
        await World.Ticks(2);

        var pin = Assert.Single(Mod.PinStore!.Get(player.Player.PlayerUID));
        Assert.Equal("Find copper", pin.LastKnownText);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    /// <summary>Unpin by (DocId, TaskId) succeeds even when the owning lectern's chunk is UNLOADED —
    /// the pin stays and unpin needs no block resolution.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Unpin_works_while_the_owning_chunk_is_unloaded()
    {
        var player = await World.JoinPlayer("UnloadUnpinner");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Unload (not delete) the chunk hosting the lectern: the pin must persist and be removable by identity.
        int chunkSize = World.Api.WorldManager.ChunkSize;
        World.Api.WorldManager.UnloadChunkColumn(pos.X / chunkSize, pos.Z / chunkSize);
        await World.Ticks(2);
        Assert.Null(World.BlockEntityAt<BlockEntityScribeLectern>(pos)); // chunk really unloaded

        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId)); // pin persists
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: false);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    /// <summary>Player-owned reconcile: deleting one of their own pinned tasks in their own edit removes only
    /// that player's pin for it; their other pin is kept and its snapshot refreshed. Drives ApplyEdit with a
    /// scratch copy, as autosave does.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Deleting_my_pinned_task_in_my_edit_removes_my_pin()
    {
        var player = await World.JoinPlayer("EditDeleter");
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(player.Player, wantEditor: true, quickAdd: false);
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.AddTask("Find tin");
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc)));

        var docId = lectern.Document.DocId;
        var keepId = lectern.Document.Blocks[0].TaskId;
        var deleteId = lectern.Document.Blocks[1].TaskId;
        Mod.SetPinForPlayer(player.Player, docId, keepId, pinned: true);
        Mod.SetPinForPlayer(player.Player, docId, deleteId, pinned: true);

        // The player edits their own document to drop "Find tin".
        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var scratch));
        Assert.True(scratch!.DeleteBlock(1, out var removed));
        Assert.Equal(deleteId, removed);
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(scratch)));

        var store = Mod.PinStore!;
        Assert.True(store.IsPinned(player.Player.PlayerUID, docId, keepId));   // surviving task's pin kept
        Assert.False(store.IsPinned(player.Player.PlayerUID, docId, deleteId)); // deleted task's pin removed
    }

    /// <summary>Grief-proof: another player rewriting or deleting a task I pinned does not change my pin's
    /// captured text or remove it; my pin is reconciled only by my own actions.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Another_players_edit_does_not_change_my_pin()
    {
        var owner = await World.JoinPlayer("DocOwner");
        var pinner = await World.JoinPlayer("Pinner");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (lectern, docId, taskId) = await SeedLectern(owner, pos);

        Mod.SetPinForPlayer(pinner.Player, docId, taskId, pinned: true);

        // The owner rewrites the task text, then deletes it: a griefing sequence.
        lectern.OnRightClick(owner.Player, wantEditor: true, quickAdd: false);
        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var edited));
        Assert.True(edited!.SetBlockText(0, "You have been griefed"));
        Assert.True(lectern.ApplyEdit(owner.Player, ScribeDocumentCodec.Serialize(edited)));

        var store = Mod.PinStore!;
        var pin = Assert.Single(store.Get(pinner.Player.PlayerUID));
        Assert.Equal("Find copper", pin.LastKnownText);
        Assert.True(store.IsPinned(pinner.Player.PlayerUID, docId, taskId));

        // The owner deletes the task entirely; Pinner's pin still survives.
        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var deleted));
        Assert.True(deleted!.DeleteBlock(0));
        Assert.True(lectern.ApplyEdit(owner.Player, ScribeDocumentCodec.Serialize(deleted)));

        Assert.True(store.IsPinned(pinner.Player.PlayerUID, docId, taskId)); // still mine
        Assert.Equal("Find copper", store.Get(pinner.Player.PlayerUID)[0].LastKnownText);
    }

    /// <summary>Breaking a lectern then re-placing the dropped item restores the same document
    /// identity (DocId rides in the stack attributes); the retained pin resolves again and completing
    /// it writes through to the restored document.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Breaking_then_replacing_keeps_the_docid_and_the_pin_resolves()
    {
        var player = await World.JoinPlayer("Replacer");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (lectern, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Capture the drop as breaking the block would (GetDrops stamps the document onto the stack), then
        // remove the block; the pin is kept.
        var drop = lectern.Block.GetDrops(World.Api.World, pos, player.Player)![0];
        World.SetBlock("game:air", pos);
        await World.Ticks(2);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));

        // Re-place from the carried stack: OnBlockPlaced restores the document (same DocId) and re-registers it.
        World.SetBlock("scribe:scribelectern", pos);
        var replaced = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(replaced);
        replaced!.OnBlockPlaced(drop);
        await World.Ticks(2);

        // Same identity came back and the pin resolves again: completing it (default Sink) writes through.
        Assert.Equal(docId, replaced.Document.DocId);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);
        Assert.True(replaced.Document.FindByTaskId(taskId)!.Done);
    }
}
