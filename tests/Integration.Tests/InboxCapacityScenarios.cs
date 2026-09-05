using Atlas.Api;
using Atlas.XUnit;
using Scribe;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Integration.Tests;

/// <summary>
/// redesign-inbox-block-placement-and-capacity tasks.md 1.2/1.3: the Inbox's inventory grew from 8
/// slots (4 restricted + 4 open) to 12 (8 restricted + 4 open). Both scenarios exercise the real
/// slot-restriction path (<see cref="ItemSlot.TryPutInto"/>, which invokes <c>CanHold</c>/<c>CanTakeFrom</c>)
/// rather than assigning <c>Itemstack</c> directly, so a regression in the restriction itself would
/// actually fail these tests, not just a miscounted slot total.
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

        // A non-Scribe item is rejected from a restricted slot (checked while it's still empty, so
        // the rejection can only be the slot's own restriction, not merely "already occupied").
        var rejected = new DummySlot(ArbitraryItem()).TryPutInto(World.Api.World, inbox!.Inventory[0]);
        Assert.Equal(0, rejected);
        Assert.Null(inbox.Inventory[0].Itemstack);

        // Fill all 8 restricted slots with a Scribe item and all 4 open slots with an arbitrary item,
        // through the real accept path (proves the open slots truly accept anything, not just that a
        // direct assignment would have "worked" regardless of any restriction).
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

        // Tree round-trip: serialize then feed straight back through FromTreeAttributes (the same call a
        // real save/load makes), then confirm every slot's contents survived unchanged. Uses the same
        // live block entity rather than a bare `new BlockEntityInbox()`: FromTreeAttributes's base chain
        // resolves `Block` from `Api`/`Pos`, which only a placed, `Initialize`d entity has.
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

        // Hand-build a tree exactly as an old, pre-this-change 8-slot Inbox would have persisted it —
        // qslots=8 and only indices 0-7 present in the "slots" sub-tree (SlotsToTreeAttributes never
        // writes empty slots, so a real old save would look identical to this by construction).
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

        // Same live entity, not a bare `new BlockEntityInbox()` — see the roundtrip scenario's note above.
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
