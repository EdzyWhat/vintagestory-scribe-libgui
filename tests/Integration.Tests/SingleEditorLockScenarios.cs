using Atlas.Api;
using Atlas.XUnit;
using Scribe;

namespace Integration.Tests;

/// <summary>
/// Task 4.6a: only one player may hold a lectern's editor lock at a time; a second opener is
/// refused; the lock releases on close (ReleaseLock) and on disconnect (PlayerDisconnect).
/// </summary>
public class SingleEditorLockScenarios : AtlasScenarioBase
{
    [AtlasScenario(RollbackWorld = true)]
    public async Task Second_opener_is_refused_while_locked()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var first = await World.JoinPlayer("Locker1");
        var second = await World.JoinPlayer("Locker2");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(first.Player, wantEditor: true, quickAdd: false);

        // The second player's edit must be rejected: they never held the lock, because the
        // server never granted them one (OnRightClick only replies to the requester; the
        // effect we can observe here is that their ApplyEdit is a no-op, exactly like the
        // non-holder case in ServerAuthoritativeEditScenarios).
        lectern.OnRightClick(second.Player, wantEditor: true, quickAdd: false);

        var doc = new Scribe.Core.ScribeDocument();
        doc.AddTask("Should not apply");
        Assert.False(lectern.ApplyEdit(second.Player, Scribe.Core.ScribeDocumentCodec.Serialize(doc)));

        Assert.Empty(lectern.Document.Blocks);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Lock_releases_on_explicit_release_and_second_player_can_then_edit()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var first = await World.JoinPlayer("Locker3");
        var second = await World.JoinPlayer("Locker4");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(first.Player, wantEditor: true, quickAdd: false);
        lectern.ReleaseLock(first.Player.PlayerUID); // e.g. sent when the first player's GUI closes

        lectern.OnRightClick(second.Player, wantEditor: true, quickAdd: false); // now grantable

        var doc = new Scribe.Core.ScribeDocument();
        doc.AddTask("Now this applies");
        Assert.True(lectern.ApplyEdit(second.Player, Scribe.Core.ScribeDocumentCodec.Serialize(doc)));

        Assert.Single(lectern.Document.Blocks);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Lock_releases_when_the_holder_disconnects()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var first = await World.JoinPlayer("Locker5");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(first.Player, wantEditor: true, quickAdd: false);

        // A genuine disconnect, exercising the real ICoreServerAPI.Event.PlayerDisconnect
        // path BlockEntityScribeLectern subscribes to in Initialize -- not a simulation.
        first.Player.Disconnect();
        await World.Until(() => !first.IsConnected);

        var second = await World.JoinPlayer("Locker6");
        lectern.OnRightClick(second.Player, wantEditor: true, quickAdd: false);

        var doc = new Scribe.Core.ScribeDocument();
        doc.AddTask("Lock was released");
        Assert.True(lectern.ApplyEdit(second.Player, Scribe.Core.ScribeDocumentCodec.Serialize(doc)));

        Assert.Single(lectern.Document.Blocks);
    }
}
