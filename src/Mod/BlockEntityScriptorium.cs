using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The Scriptorium's placed-block entity. A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/> supplying only its identity/config. It reuses the Lectern's
/// page backdrop as a placeholder; its dialog is a distinct subclass so Scriptorium-only nav buttons attach
/// without touching the Lectern.
/// </summary>
public sealed class BlockEntityScriptorium : BlockEntityScribeWritingStation
{
    // Placeholder: reuses the Lectern page art (same 1024×1160 size, so the same aspect).
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1160f / 1024f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-scriptorium";

    protected override string MeshCacheKeyPrefix => "scribescriptoriummesh";

    protected override ScribeBoxTuningTarget? TuningTarget => ScribeBoxTuningTarget.Scriptorium;

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeScriptorium(Pos, this, capi);

    // ── Scribe-items-only inventory ───────────────────────────────────────
    // A tiny inventory of whole Scribe document items, surfaced as its own dialog tab: an InventoryGeneric
    // bound to the block-entity packet channel (standard container mechanics) while keeping the
    // BlockEntityScribeWritingStation base rather than BlockEntityOpenableContainer.

    /// <summary>Scribe-item storage slots: 0 = copy Original (source), 1 = copy Duplicate (target),
    /// 2 = the Import/Export source-and-target. A slot with no stored stack in the tree is left empty
    /// (<see cref="Vintagestory.API.Common.InventoryBase.SlotsFromTreeAttributes"/> reads this count, not the tree's).</summary>
    private const int SlotCount = 3;

    /// <summary>Tree sub-key for the inventory; a tree lacking it loads with empty slots.</summary>
    private const string InventoryTreeKey = "scriptoriumInventory";

    /// <summary>Created lazily via <see cref="EnsureInventory"/> because chunk-load runs FromTreeAttributes
    /// first while a fresh place runs Initialize first. <see cref="ItemSlotScribeDocument"/> slots enforce
    /// the accept rule on every move path.</summary>
    private InventoryGeneric? inventory;

    /// <summary>The Scriptorium's Scribe-items-only inventory (the dialog's inventory tab watches this).</summary>
    public InventoryGeneric Inventory
    {
        get
        {
            EnsureInventory();
            return inventory!;
        }
    }

    /// <summary>Lazily constructs the inventory api-less (like vanilla containers) so a pre-Initialize
    /// <see cref="FromTreeAttributes"/> has something to populate; the api is bound in <see cref="Initialize"/>.</summary>
    private void EnsureInventory()
    {
        inventory ??= new InventoryGeneric(SlotCount, null, null,
            (slotId, self) => new ItemSlotScribeDocument(self));
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel. Without LateInitialize + Pos, LibGUI's
        // SlotController silently drops every slot click ("Skipped slot activation … not network-ready").
        inventory!.LateInitialize("scribescriptorium-" + Pos, api);
        inventory.Pos = Pos;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        EnsureInventory();
        // Nest under a sub-tree so the inventory's "qslots"/"slots" keys never collide with the base's keys.
        var invTree = new TreeAttribute();
        inventory!.ToTreeAttributes(invTree);
        tree[InventoryTreeKey] = invTree;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        EnsureInventory();
        base.FromTreeAttributes(tree, worldForResolving);
        // A tree without the inventory sub-tree leaves the slots empty.
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

    /// <summary>Drops stored Scribe items when the block is broken so a stored document is never destroyed.
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
