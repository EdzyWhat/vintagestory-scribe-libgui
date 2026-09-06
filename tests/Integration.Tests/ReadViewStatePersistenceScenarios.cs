using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// read-view-filter-and-collapse tasks 4.1/4.2/7.1: the Read View filter pill + subtask-group
/// collapse set persist per Scribe item/block instance. RollbackWorld (not a fixture) is enough
/// here — mirroring <see cref="ServerAuthoritativeEditScenarios"/> — because each scenario proves
/// the wire-format round trip itself (tree attributes for the block host, ItemStack attributes for
/// the item host) rather than a real save/quit/reload, which needs a checked-in fixture.
/// </summary>
public class ReadViewStatePersistenceScenarios : AtlasScenarioBase
{
    private ScribeModSystem Mod => World.Api.ModLoader.GetModSystem<ScribeModSystem>();

    [AtlasScenario(RollbackWorld = true)]
    public async Task Lectern_read_view_state_survives_a_tree_attribute_round_trip()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var player = await World.JoinPlayer("RVStateLectern");
        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        IScribeDocumentHost host = lectern!;
        var groupId = Guid.NewGuid();
        Mod.SetReadViewStateForPlayer(player.Player, lectern.Document.DocId,
            (byte)ReadViewFilterCategory.Pinned, new[] { groupId });

        Assert.Equal((byte)ReadViewFilterCategory.Pinned, host.ReadViewFilterCategory);
        Assert.Equal(new[] { groupId }, host.CollapsedGroupIds);

        // Simulate a save/reload on the SAME instance: serialize the tree exactly like a world save
        // does, then re-hydrate it back through FromTreeAttributes — the exact call VS makes on
        // chunk load — proving the wire format (SetInt/GetInt + the Guid-set byte blob) round trips.
        var tree = new TreeAttribute();
        lectern.ToTreeAttributes(tree);
        lectern.FromTreeAttributes(tree, World.Api.World);

        Assert.Equal((byte)ReadViewFilterCategory.Pinned, host.ReadViewFilterCategory);
        Assert.Equal(new[] { groupId }, host.CollapsedGroupIds);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task A_never_configured_lectern_defaults_to_All_and_fully_expanded()
    {
        var pos = World.Spawn.Offset(5, 0, 0);
        World.SetBlock("scribe:scribelectern", pos);
        await World.Ticks(2);

        var lectern = World.BlockEntityAt<BlockEntityScribeLectern>(pos);
        Assert.NotNull(lectern);

        IScribeDocumentHost host = lectern!;
        Assert.Equal((byte)ReadViewFilterCategory.All, host.ReadViewFilterCategory);
        Assert.Empty(host.CollapsedGroupIds);
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Notebook_read_view_state_survives_an_itemstack_attribute_round_trip()
    {
        var player = await World.JoinPlayer("RVStateNotebook");
        var hotbar = player.Player.InventoryManager.GetHotbarInventory();
        var slot = hotbar[0]!;
        slot.Itemstack = new ItemStack(World.Api.World.GetItem(new AssetLocation("scribe", "scribenotebook"))!, 1);
        slot.MarkDirty();

        // Seed a document (and its DocId) on the stack the way opening the dialog would — the
        // server-side inventory scan (TryResolveDocHost) can only match a DocId that already exists
        // on the stack's attributes.
        var seedHost = new NotebookHost(slot);
        var docId = seedHost.Document.DocId;

        var groupId = Guid.NewGuid();
        Mod.SetReadViewStateForPlayer(player.Player, docId, (byte)ReadViewFilterCategory.Completed, new[] { groupId });

        // Reopen: constructing a FRESH host over the same slot re-reads the persisted ItemStack
        // attributes, exactly like a real reopen after a save/reload would.
        var reopened = new NotebookHost(slot);
        Assert.Equal((byte)ReadViewFilterCategory.Completed, reopened.ReadViewFilterCategory);
        Assert.Equal(new[] { groupId }, reopened.CollapsedGroupIds);
    }
}
