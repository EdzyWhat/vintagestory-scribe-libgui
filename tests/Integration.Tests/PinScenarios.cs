using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// add-pinned-task-foundation: exercises the per-player pin store through the real server path
/// (the mod system's SetPinForPlayer / CompleteTaskForPlayer, which the network handlers delegate
/// to), addressed by (DocId, TaskId). RollbackWorld is enough for the in-memory assertions here;
/// restart-persistence lives in PersistenceScenarios.
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

        lectern!.OnRightClick(editor.Player, wantEditor: true); // acquire the lock
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
        lectern.OnRightClick(alice.Player, wantEditor: true);
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

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_a_pinned_task_unpins_it_by_default()
    {
        var player = await World.JoinPlayer("PinCompleter");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);

        // The task is now done in the document, and the completing player's pin was removed (default
        // CompleteUnpins = true).
        Assert.True(lectern.Document.FindByTaskId(taskId)!.Done);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_keeps_the_pin_when_opted_out()
    {
        var player = await World.JoinPlayer("PinKeeper");
        var (lectern, docId, taskId) = await SeedLectern(player, World.Spawn.Offset(5, 0, 0));

        Mod.PinStore!.SetSettings(player.Player.PlayerUID, new ScribePlayerSettings { CompleteUnpins = false });
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);

        Assert.True(lectern.Document.FindByTaskId(taskId)!.Done);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId)); // pin retained
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Completion_unpins_only_the_completing_player()
    {
        var alice = await World.JoinPlayer("SharedAlice");
        var bob = await World.JoinPlayer("SharedBob");
        var (_, docId, taskId) = await SeedLectern(alice, World.Spawn.Offset(5, 0, 0));

        Mod.SetPinForPlayer(alice.Player, docId, taskId, pinned: true);
        Mod.SetPinForPlayer(bob.Player, docId, taskId, pinned: true);

        Mod.CompleteTaskForPlayer(alice.Player, docId, taskId);

        var store = Mod.PinStore!;
        Assert.False(store.IsPinned(alice.Player.PlayerUID, docId, taskId)); // completer unpinned
        Assert.True(store.IsPinned(bob.Player.PlayerUID, docId, taskId));    // other pinner retained
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Unpin_works_after_the_lectern_is_removed()
    {
        var player = await World.JoinPlayer("OrphanUnpinner");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Break the lectern: the pin soft-orphans (kept), and unpin must still succeed by identity
        // alone with no block to resolve.
        World.SetBlock("game:air", pos);
        await World.Ticks(2);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: false);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Breaking_a_lectern_soft_orphans_pins()
    {
        var player = await World.JoinPlayer("OrphanObserver");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        World.SetBlock("game:air", pos);
        await World.Ticks(2);

        // The pin is retained (soft-orphan), flagged orphaned, and keeps its last-known snapshot.
        var pin = Assert.Single(Mod.PinStore!.Get(player.Player.PlayerUID));
        Assert.True(pin.Orphaned);
        Assert.Equal("Find copper", pin.LastKnownText);
    }

    /// <summary>7.3: unpin by (DocId, TaskId) must succeed even when the owning lectern's chunk is
    /// UNLOADED (distinct from the removed case above). Unloading is NOT a deletion — the pin stays
    /// un-orphaned — but the document is no longer resolvable, so this proves unpin needs no block
    /// resolution at all.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Unpin_works_while_the_owning_chunk_is_unloaded()
    {
        var player = await World.JoinPlayer("UnloadUnpinner");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Unload (not delete) the chunk column hosting the lectern. This fires OnBlockUnloaded, which
        // the design deliberately never hooks — so the block is gone from memory but the pin must NOT
        // orphan and must still be removable by identity alone.
        int chunkSize = World.Api.WorldManager.ChunkSize;
        World.Api.WorldManager.UnloadChunkColumn(pos.X / chunkSize, pos.Z / chunkSize);
        await World.Ticks(2);
        Assert.Null(World.BlockEntityAt<BlockEntityScribeLectern>(pos)); // chunk really unloaded

        Assert.False(Mod.PinStore!.Get(player.Player.PlayerUID)[0].Orphaned); // unload is not a deletion
        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: false);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    /// <summary>7.4: actioning (completing) an ORPHANED pin removes it from the player's set with no
    /// document to mutate — the "check it off and it leaves my list" gesture stays uniform whether or
    /// not the source still exists.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Completing_an_orphaned_pin_removes_it_with_no_document()
    {
        var player = await World.JoinPlayer("OrphanCompleter");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (_, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Break the lectern so the pin orphans and no document is resolvable.
        World.SetBlock("game:air", pos);
        await World.Ticks(2);
        Assert.True(Mod.PinStore!.Get(player.Player.PlayerUID)[0].Orphaned);

        // Completing the orphaned pin: no document exists to toggle, so it just leaves the set.
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);
        Assert.False(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
    }

    /// <summary>7.5: a saved edit that DELETES a pinned task soft-orphans the pin (keeping its
    /// last-known snapshot), while a still-present task is untouched. Drives the real ApplyEdit path
    /// with a scratch copy that preserves the DocId and the surviving task's TaskId, exactly as the
    /// editor autosave does.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Deleting_a_pinned_task_in_an_edit_soft_orphans_the_pin()
    {
        var player = await World.JoinPlayer("EditDeleter");
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(player.Player, wantEditor: true);
        var doc = new ScribeDocument();
        doc.AddTask("Find copper");
        doc.AddTask("Find tin");
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc)));

        var docId = lectern.Document.DocId;
        var keepId = lectern.Document.Blocks[0].TaskId;
        var deleteId = lectern.Document.Blocks[1].TaskId;
        Mod.SetPinForPlayer(player.Player, docId, keepId, pinned: true);
        Mod.SetPinForPlayer(player.Player, docId, deleteId, pinned: true);

        // Edit the document to drop "Find tin": round-trip the authoritative bytes (same DocId +
        // surviving TaskId), delete the pinned task, and apply — mirroring the editor's scratch copy.
        Assert.True(ScribeDocumentCodec.TryDeserialize(ScribeDocumentCodec.Serialize(lectern.Document), out var scratch));
        Assert.True(scratch!.DeleteBlock(1, out var removed));
        Assert.Equal(deleteId, removed);
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(scratch)));

        var store = Mod.PinStore!;
        var kept = store.Get(player.Player.PlayerUID).Single(p => p.TaskId == keepId);
        var orphaned = store.Get(player.Player.PlayerUID).Single(p => p.TaskId == deleteId);
        Assert.False(kept.Orphaned);                        // still-present task untouched
        Assert.True(orphaned.Orphaned);                     // deleted task's pin soft-orphaned
        Assert.Equal("Find tin", orphaned.LastKnownText);   // snapshot retained
    }

    /// <summary>7.5: breaking a lectern then re-placing the dropped item restores the same document
    /// identity (DocId rides in the stack attributes), so the pin resolves again — the server can
    /// snapshot/complete it against the restored document.</summary>
    [AtlasScenario(RollbackWorld = true)]
    public async Task Breaking_then_replacing_keeps_the_docid_and_the_pin_resolves()
    {
        var player = await World.JoinPlayer("Replacer");
        var pos = World.Spawn.Offset(5, 0, 0);
        var (lectern, docId, taskId) = await SeedLectern(player, pos);

        Mod.SetPinForPlayer(player.Player, docId, taskId, pinned: true);

        // Capture the drop the way breaking the block would (GetDrops runs while the BE is alive and
        // stamps the document onto the stack), then remove the block (orphaning the pin).
        var drop = lectern.Block.GetDrops(World.Api.World, pos, player.Player)![0];
        World.SetBlock("game:air", pos);
        await World.Ticks(2);
        Assert.True(Mod.PinStore!.Get(player.Player.PlayerUID)[0].Orphaned);

        // Re-place the lectern from the carried stack: OnBlockPlaced restores the document (same
        // DocId) and re-registers it in the live index.
        World.SetBlock("scribe:scribelectern", pos);
        var replaced = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(replaced);
        replaced!.OnBlockPlaced(drop);
        await World.Ticks(2);

        // Same identity came back, and the pin resolves again: completing it toggles the restored
        // document's task done (proving the server re-resolved DocId → block by identity).
        Assert.Equal(docId, replaced.Document.DocId);
        Assert.True(Mod.PinStore!.IsPinned(player.Player.PlayerUID, docId, taskId));
        Mod.CompleteTaskForPlayer(player.Player, docId, taskId);
        Assert.True(replaced.Document.FindByTaskId(taskId)!.Done);
    }
}
