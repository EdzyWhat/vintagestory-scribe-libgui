using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The Scriptorium's placed-block entity. A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence, editor-lock,
/// guestbook, and placement logic with the Lectern and supplies only its own identity/config.
///
/// <para>It reuses the Lectern's GUI page backdrop as a placeholder, and its dialog is a distinct
/// subclass so Scriptorium-only nav buttons can be attached without touching the Lectern.</para>
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
    //
    // The Scriptorium owns a tiny inventory of Scribe document items, surfaced as its own dialog tab.
    // It holds and returns whole items. It follows the standard vanilla container mechanics — an
    // InventoryGeneric bound to the block-entity packet channel — even though this BE keeps its
    // BlockEntityScribeWritingStation base rather than reparenting to BlockEntityOpenableContainer.

    /// <summary>Number of Scribe-item storage slots on the Scriptorium: slot 0 = copy Original (source),
    /// slot 1 = copy Duplicate (target), slot 2 = the Import/Export source-and-target.
    /// A slot with no stored stack in the tree is left empty by
    /// <see cref="Vintagestory.API.Common.InventoryBase.SlotsFromTreeAttributes"/> (it reads
    /// this constant's slot count, not the tree's).</summary>
    private const int SlotCount = 3;

    /// <summary>Tree sub-key under which the inventory persists, kept separate from the document keys. A
    /// block whose tree lacks this sub-tree loads with empty slots.</summary>
    private const string InventoryTreeKey = "scriptoriumInventory";

    /// <summary>The Scriptorium's Scribe-items-only inventory. Created lazily via <see cref="EnsureInventory"/>
    /// so it exists before whichever of <see cref="FromTreeAttributes"/> / <see cref="Initialize"/> the VS
    /// block-entity lifecycle runs first (chunk-load runs FromTree first; a fresh place runs Initialize
    /// first). Populated with <see cref="ItemSlotScribeDocument"/> slots so the Scribe-only accept rule is
    /// enforced at the slot on every move path.</summary>
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

    /// <summary>Lazily construct the inventory api-less (like vanilla containers), so a pre-Initialize
    /// <see cref="FromTreeAttributes"/> read has something to populate. The api and instance id are bound
    /// later in <see cref="Initialize"/> via <c>LateInitialize</c>.</summary>
    private void EnsureInventory()
    {
        inventory ??= new InventoryGeneric(SlotCount, null, null,
            (slotId, self) => new ItemSlotScribeDocument(self));
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel (the network-readiness gate). Without
        // LateInitialize + Pos, LibGUI's SlotController silently drops every slot click, logging
        // "[gui] Skipped slot activation … not network-ready". Mirrors BlockEntityContainer.Initialize.
        inventory!.LateInitialize("scribescriptorium-" + Pos, api);
        inventory.Pos = Pos;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        EnsureInventory();
        // Nest under a dedicated sub-tree so the inventory's own "qslots"/"slots" keys never collide with
        // the document/lock/guestbook keys the base writes.
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

    /// <summary>Standard vanilla container packet flow (mirrors <c>BlockEntityOpenableContainer</c>): slot
    /// operations (packet id &lt; 1000) go to the inventory's network util; 1000/1001 open/close the
    /// inventory for the acting player. This rides the built-in block-entity packet channel — NOT the
    /// mod's "scribe" channel, which carries document edits (a separate concern).</summary>
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

    /// <summary>Drop any stored Scribe items when the block is broken, so a stored document is never
    /// destroyed by breaking the block (mirrors <c>BlockEntityContainer.OnBlockBroken</c>). Server-only;
    /// this BE is still alive here (VS calls it from <c>SpawnDropsAndRemoveBlock</c> before removal). THIS
    /// block's own document is carried onto the block-item separately by
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
