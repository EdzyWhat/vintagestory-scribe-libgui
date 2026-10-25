using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// Pins the saved-tree key and stored-stack round trip of each block with a Scribe-items inventory, so saved
/// worlds keep loading their stored items. Stacks go in through the real accept path
/// (<see cref="ItemSlot.TryPutInto"/>).
/// </summary>
public class InventoryStationPersistenceScenarios : AtlasScenarioBase
{
    private ItemStack Notice() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);

    private void AssertKeyAndRoundTrip(BlockEntityScribeWritingStation station, InventoryGeneric inventory, string treeKey)
    {
        var moved = new DummySlot(Notice()).TryPutInto(World.Api.World, inventory[0]);
        Assert.Equal(1, moved);

        var saved = new TreeAttribute();
        station.ToTreeAttributes(saved);
        Assert.NotNull(saved.GetTreeAttribute(treeKey));

        inventory[0].Itemstack = null;
        station.FromTreeAttributes(saved, World.Api.World);
        Assert.Equal("scribe:tasknotice", inventory[0].Itemstack?.Collectible.Code.ToString());
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Scriptorium_inventory_persists_under_its_tree_key()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scriptorium", pos);
        await World.Ticks(2);

        var scriptorium = World.BlockEntityAt<BlockEntityScriptorium>(pos);
        Assert.NotNull(scriptorium);
        AssertKeyAndRoundTrip(scriptorium!, scriptorium!.Inventory, "scriptoriumInventory");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Assignment_desk_inventory_persists_under_its_tree_key()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeassignmentdesk", pos);
        await World.Ticks(2);

        var desk = World.BlockEntityAt<BlockEntityAssignmentDesk>(pos);
        Assert.NotNull(desk);
        AssertKeyAndRoundTrip(desk!, desk!.Inventory, "assignmentDeskInventory");
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task Inbox_inventory_persists_under_its_tree_key()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeinbox-up", pos);
        await World.Ticks(2);

        var inbox = World.BlockEntityAt<BlockEntityInbox>(pos);
        Assert.NotNull(inbox);
        AssertKeyAndRoundTrip(inbox!, inbox!.Inventory, "inboxInventory");
    }
}
