using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// The Inbox inventory has 12 slots (8 restricted + 4 open). Scenarios go through the real slot-restriction
/// path (<see cref="ItemSlot.TryPutInto"/>, which invokes <c>CanHold</c>/<c>CanTakeFrom</c>) rather than assigning
/// <c>Itemstack</c> directly, so a broken restriction fails the tests.
/// </summary>
public class InboxCapacityScenarios : AtlasScenarioBase
{
    private ItemStack ScribeItem() =>
        new(World.Api.World.GetItem(new AssetLocation("scribe", "tasknotice"))!, 1);

    private ItemStack ArbitraryItem() =>
        new(World.Api.World.GetItem(new AssetLocation("game", "stick"))!, 1);

    [AtlasScenario(RollbackWorld = true)]
    public async Task Inbox_12_slots_enforce_restriction_and_survive_a_tree_roundtrip()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeinbox-up", pos);
        await World.Ticks(2);

        var inbox = World.BlockEntityAt<BlockEntityInbox>(pos);
        Assert.NotNull(inbox);

        // A non-Scribe item is rejected from an empty restricted slot, so the rejection is the restriction itself.
        var rejected = new DummySlot(ArbitraryItem()).TryPutInto(World.Api.World, inbox!.Inventory[0]);
        Assert.Equal(0, rejected);
        Assert.Null(inbox.Inventory[0].Itemstack);

        // Fill the 8 restricted slots with a Scribe item and the 4 open slots with an arbitrary item via the real
        // accept path.
        for (int i = 0; i < BlockEntityInbox.RestrictedSlotCount; i++)
        {
            var moved = new DummySlot(ScribeItem()).TryPutInto(World.Api.World, inbox.Inventory[i]);
            Assert.Equal(1, moved);
        }
        for (int i = BlockEntityInbox.RestrictedSlotCount; i < BlockEntityInbox.SlotCount; i++)
        {
            var moved = new DummySlot(ArbitraryItem()).TryPutInto(World.Api.World, inbox.Inventory[i]);
            Assert.Equal(1, moved);
        }

        // Tree round-trip through ToTreeAttributes/FromTreeAttributes (as save/load does). Uses the live block
        // entity, not a bare `new BlockEntityInbox()`: FromTreeAttributes's base chain resolves `Block` from
        // `Api`/`Pos`, which only a placed, initialized entity has.
        var savedTree = new TreeAttribute();
        inbox.ToTreeAttributes(savedTree);
        inbox.FromTreeAttributes(savedTree, World.Api.World);

        for (int i = 0; i < BlockEntityInbox.RestrictedSlotCount; i++)
        {
            Assert.Equal("scribe:tasknotice", inbox.Inventory[i].Itemstack?.Collectible.Code.ToString());
        }
        for (int i = BlockEntityInbox.RestrictedSlotCount; i < BlockEntityInbox.SlotCount; i++)
        {
            Assert.Equal("game:stick", inbox.Inventory[i].Itemstack?.Collectible.Code.ToString());
        }
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task A_pre_change_8_slot_inbox_loads_with_items_intact_and_4_new_empty_slots()
    {
        var pos = World.Spawn.Offset(4, 0, 0);
        World.SetBlock("scribe:scribeinbox-up", pos);
        await World.Ticks(2);

        var inbox = World.BlockEntityAt<BlockEntityInbox>(pos);
        Assert.NotNull(inbox);

        // Hand-build a tree as an old 8-slot Inbox persisted it: qslots=8 and only indices 0-7 in "slots"
        // (SlotsToTreeAttributes never writes empty slots).
        var oldInvTree = new TreeAttribute();
        oldInvTree.SetInt("qslots", 8);
        var oldSlotsTree = new TreeAttribute();
        for (int i = 0; i < 8; i++)
        {
            oldSlotsTree.SetItemstack(i.ToString(), ScribeItem());
        }
        oldInvTree["slots"] = oldSlotsTree;

        var savedTree = new TreeAttribute();
        inbox!.ToTreeAttributes(savedTree);
        savedTree["inboxInventory"] = oldInvTree;

        // Same live entity as the roundtrip scenario.
        inbox.FromTreeAttributes(savedTree, World.Api.World);

        Assert.Equal(BlockEntityInbox.SlotCount, inbox.Inventory.Count);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal("scribe:tasknotice", inbox.Inventory[i].Itemstack?.Collectible.Code.ToString());
        }
        for (int i = 8; i < BlockEntityInbox.SlotCount; i++)
        {
            Assert.Null(inbox.Inventory[i].Itemstack);
        }
    }
}
