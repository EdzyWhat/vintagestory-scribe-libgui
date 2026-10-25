using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// A writing station that also holds a small slot inventory (Scriptorium, Assignment Desk, Inbox). The inventory
/// is an <see cref="InventoryGeneric"/> bound to the block-entity packet channel (standard container mechanics)
/// while keeping the <see cref="BlockEntityScribeWritingStation"/> base rather than
/// <c>BlockEntityOpenableContainer</c>. Subclasses supply the slot count, tree key, channel prefix and slot factory.
/// </summary>
public abstract class BlockEntityScribeInventoryStation : BlockEntityScribeWritingStation
{
    /// <summary>Number of slots. A slot with no stored stack in the tree is left empty
    /// (<see cref="InventoryBase.SlotsFromTreeAttributes"/> reads this count, not the tree's).</summary>
    protected abstract int InventorySlotCount { get; }

    /// <summary>Tree sub-key for the inventory, which keeps the inventory's "qslots"/"slots" keys from
    /// colliding with the base's. A tree lacking it loads with empty slots.</summary>
    protected abstract string InventoryTreeKey { get; }

    /// <summary>Prefix of the inventory's network channel id; the block position is appended.</summary>
    protected abstract string InventoryChannelPrefix { get; }

    /// <summary>Creates the slot at <paramref name="slotId"/>, applying that slot's accept rule.</summary>
    protected abstract ItemSlot CreateSlot(int slotId, InventoryBase inventory);

    /// <summary>Created lazily via <see cref="EnsureInventory"/> because chunk-load runs FromTreeAttributes
    /// first while a fresh place runs Initialize first.</summary>
    private InventoryGeneric? inventory;

    /// <summary>The block's inventory (the dialog's inventory tab watches this).</summary>
    public InventoryGeneric Inventory
    {
        get
        {
            EnsureInventory();
            return inventory!;
        }
    }

    /// <summary>Constructs the inventory api-less (like vanilla containers) so a pre-Initialize
    /// <see cref="FromTreeAttributes"/> has something to populate; the api is bound in <see cref="Initialize"/>.</summary>
    private void EnsureInventory()
    {
        inventory ??= new InventoryGeneric(InventorySlotCount, null, null, CreateSlot);
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel. Without LateInitialize + Pos, LibGUI's
        // SlotController silently drops every slot click ("Skipped slot activation … not network-ready").
        inventory!.LateInitialize(InventoryChannelPrefix + Pos, api);
        inventory.Pos = Pos;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        EnsureInventory();
        var invTree = new TreeAttribute();
        inventory!.ToTreeAttributes(invTree);
        tree[InventoryTreeKey] = invTree;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        EnsureInventory();
        base.FromTreeAttributes(tree, worldForResolving);
        if (tree.GetTreeAttribute(InventoryTreeKey) is { } invTree)
        {
            inventory!.FromTreeAttributes(invTree);
        }
    }

    /// <summary>Standard vanilla container packet flow (mirrors <c>BlockEntityOpenableContainer</c>): packet
    /// id &lt; 1000 goes to the inventory's network util; 1000/1001 open/close it for the player. Rides the
    /// block-entity packet channel, NOT the mod's "scribe" channel.</summary>
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (packetid < 1000)
        {
            Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            MarkDirty(true);
            return;
        }

        if (packetid == 1000)
        {
            player.InventoryManager?.OpenInventory(Inventory);
        }
        else if (packetid == 1001)
        {
            player.InventoryManager?.CloseInventory(Inventory);
        }
    }

    /// <summary>Drops stored items when the block is broken so a stored item is never destroyed.
    /// Server-only; this BE is still alive here. The block's own document is carried onto the item by
    /// <see cref="BlockScribeWritingStation.GetDrops"/>.</summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api is ICoreServerAPI)
        {
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
