using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Tasks 5.1-5.7 (two-view redesign): read access is always granted and never touches the
/// editor lock; the mid-session toggle (<see cref="BlockEntityScribeLectern.OnRequestAccess"/>)
/// makes the same grant/refuse decision as the initial right-click
/// (<see cref="BlockEntityScribeLectern.OnRightClick"/>), since both funnel into the same
/// shared <c>RequestAccess</c> helper. Lock state is only observable indirectly through
/// <see cref="BlockEntityScribeLectern.ApplyEdit"/>'s return value (whether a given player is
/// currently the lock holder), matching the pattern already used by
/// <see cref="SingleEditorLockScenarios"/>.
/// </summary>
public class ReadEditorAccessScenarios : AtlasScenarioBase
{
    [AtlasScenario(RollbackWorld = true)]
    public async Task Read_access_is_granted_while_another_player_holds_the_editor_lock()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var holder = await World.JoinPlayer("Reader1Holder");
        var reader = await World.JoinPlayer("Reader1Bystander");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(holder.Player, wantEditor: true, quickAdd: false); // holder acquires the lock

        // A plain (non-editor) request from the bystander must not throw and must not disturb
        // the existing lock -- read access never checks or touches lockHolderUid.
        lectern.OnRequestAccess(reader.Player, wantEditor: false, quickAdd: false);

        var holderEdit = new ScribeDocument();
        holderEdit.AddTask("Holder can still edit after a read request came in");
        Assert.True(lectern.ApplyEdit(holder.Player, ScribeDocumentCodec.Serialize(holderEdit)));

        var readerEdit = new ScribeDocument();
        readerEdit.AddTask("Reader never held the lock, so this must not apply");
        Assert.False(lectern.ApplyEdit(reader.Player, ScribeDocumentCodec.Serialize(readerEdit)));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Mid_session_toggle_grants_editor_access_when_the_lock_is_free()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var player = await World.JoinPlayer("Toggle1");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        // The in-GUI toggle entry point, not the initial right-click -- OnRequestAccess must
        // make the identical grant decision since both share the RequestAccess helper.
        lectern!.OnRequestAccess(player.Player, wantEditor: true, quickAdd: false);

        var doc = new ScribeDocument();
        doc.AddTask("Granted via the mid-session toggle");
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc)));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Mid_session_toggle_refuses_editor_access_when_locked_by_another_player()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var holder = await World.JoinPlayer("Toggle2Holder");
        var challenger = await World.JoinPlayer("Toggle2Rival");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(holder.Player, wantEditor: true, quickAdd: false);
        lectern.OnRequestAccess(challenger.Player, wantEditor: true, quickAdd: false); // must be refused

        var challengerEdit = new ScribeDocument();
        challengerEdit.AddTask("Should not apply");
        Assert.False(lectern.ApplyEdit(challenger.Player, ScribeDocumentCodec.Serialize(challengerEdit)));

        var holderEdit = new ScribeDocument();
        holderEdit.AddTask("Holder's lock survived the challenge");
        Assert.True(lectern.ApplyEdit(holder.Player, ScribeDocumentCodec.Serialize(holderEdit)));
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Switching_to_read_via_request_access_does_not_release_the_lock()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var holder = await World.JoinPlayer("Toggle3Holder");
        var second = await World.JoinPlayer("Toggle3Second");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(holder.Player, wantEditor: true, quickAdd: false);

        // Simulates the holder clicking "Done Editing" in the GUI: only ReleaseLock (sent
        // separately, on dialog close/mode-exit) frees the lock -- a plain read request must
        // not have that side effect on its own.
        lectern.OnRequestAccess(holder.Player, wantEditor: false, quickAdd: false);

        lectern.OnRequestAccess(second.Player, wantEditor: true, quickAdd: false); // must still be refused

        var secondEdit = new ScribeDocument();
        secondEdit.AddTask("Should not apply -- lock was never released");
        Assert.False(lectern.ApplyEdit(second.Player, ScribeDocumentCodec.Serialize(secondEdit)));

        var holderEdit = new ScribeDocument();
        holderEdit.AddTask("Holder still owns the lock");
        Assert.True(lectern.ApplyEdit(holder.Player, ScribeDocumentCodec.Serialize(holderEdit)));
    }
}
