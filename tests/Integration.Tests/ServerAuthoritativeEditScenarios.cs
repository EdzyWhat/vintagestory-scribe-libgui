using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;

namespace Integration.Tests;

/// <summary>
/// Task 4.5a: server-authoritative edit round trip. A submitted edit is only applied when
/// it comes from the current lock holder, and applying it marks the block entity dirty
/// (persist + re-sync). RollbackWorld resets the world in place between scenarios, which is
/// enough here -- these scenarios only assert on in-memory server state right after the
/// edit, not on a real save/load round trip (that's PersistenceScenarios).
/// </summary>
public class ServerAuthoritativeEditScenarios : AtlasScenarioBase
{
    [AtlasScenario(RollbackWorld = true)]
    public async Task Edit_from_the_lock_holder_is_applied()
    {
        var pos = World.Spawn.Offset(3, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var player = await World.JoinPlayer("Editor1");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(player.Player, wantEditor: true, quickAdd: false); // acquires the lock

        var doc = new ScribeDocument();
        doc.AddTask("Build a forge");
        Assert.True(lectern.ApplyEdit(player.Player, ScribeDocumentCodec.Serialize(doc)));

        Assert.Single(lectern.Document.Blocks);
        Assert.Equal("Build a forge", lectern.Document.Blocks[0].Text);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Edit_from_a_non_holder_is_ignored()
    {
        var pos = World.Spawn.Offset(3, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var holder = await World.JoinPlayer("Editor2");
        var bystander = await World.JoinPlayer("Bystander");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        lectern!.OnRightClick(holder.Player, wantEditor: true, quickAdd: false); // holder acquires the lock

        var attemptedEdit = new ScribeDocument();
        attemptedEdit.AddTask("This should not be applied");

        // The bystander never acquired the lock, so their edit must be a no-op.
        Assert.False(lectern.ApplyEdit(bystander.Player, ScribeDocumentCodec.Serialize(attemptedEdit)));

        Assert.Empty(lectern.Document.Blocks);
    }
}
