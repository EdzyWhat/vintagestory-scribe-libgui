using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's placed-block entity (add-assignment-and-quest-support §6). A thin
/// subclass of <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence,
/// editor-lock, and guestbook logic with the Lectern/Scriptorium/Assignment Desk and supplies only its
/// own identity/config.
///
/// <para>Reuses the Lectern's GUI page art as a placeholder backdrop (§13.2 tracks the dedicated Inbox
/// art), and its physical block model/textures are cloned from the Scriptorium's shape (see
/// <c>inbox.json</c>) pending its own §13.2 asset.</para>
///
/// <para><see cref="PageAspect"/> is fixed at the design's Decision 8 ratio (<c>W × 1.2W</c>), same as
/// <see cref="BlockEntityAssignmentDesk"/> — see design.md Decision 8.</para>
/// </summary>
public sealed class BlockEntityInbox : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-inbox";

    protected override string MeshCacheKeyPrefix => "scribeinboxmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeInbox(Pos, this, capi);

    // ── Mixed restricted/open inventory (add-inbox-inventory-tab) ────────────
    //
    // The Inbox's own 8-slot inventory: slots 0-3 accept only Scribe items, slots 4-7 accept
    // anything. Mirrors BlockEntityScriptorium's inventory verbatim — same lazy-init/persistence/
    // packet-routing shape AND the same Scribe-items-only slot restriction (ItemSlotScribeDocument)
    // for the first row, just with a mixed slot factory instead of a uniform one.

    /// <summary>8 slots: the first 4 (indices 0-3) are Scribe-items-only, the last 4 (4-7) are open —
    /// see <see cref="EnsureInventory"/>'s slot factory. Internal so <see cref="GuiDialogScribeInbox"/>
    /// can lay out the restricted/open rows without re-declaring the split.</summary>
    internal const int SlotCount = 8;

    /// <summary>Restricted slots (any Scribe item — see <see cref="ItemSlotScribeDocument"/>) occupy
    /// indices below this bound; open slots occupy the rest.</summary>
    internal const int RestrictedSlotCount = 4;

    /// <summary>Tree sub-key under which the inventory persists, kept separate from the document/lock
    /// keys so persistence is additive: an Inbox saved before this change simply lacks this sub-tree
    /// and loads with 8 empty slots.</summary>
    private const string InventoryTreeKey = "inboxInventory";

    /// <summary>Created lazily via <see cref="EnsureInventory"/> so it exists before whichever of
    /// <see cref="FromTreeAttributes"/> / <see cref="Initialize"/> the VS block-entity lifecycle runs
    /// first (chunk-load runs FromTree first; a fresh place runs Initialize first).</summary>
    private InventoryGeneric? inventory;

    /// <summary>The Inbox's mixed restricted/open inventory (the Inbox Inventory tab watches this).</summary>
    public InventoryGeneric Inventory
    {
        get
        {
            EnsureInventory();
            return inventory!;
        }
    }

    private void EnsureInventory()
    {
        inventory ??= new InventoryGeneric(SlotCount, null, null,
            (slotId, self) => slotId < RestrictedSlotCount
                ? new ItemSlotScribeDocument(self)
                : new ItemSlot(self));
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel (the network-readiness gate). Without
        // LateInitialize + Pos, LibGUI's SlotController silently drops every slot click, logging
        // "[gui] Skipped slot activation … not network-ready". Mirrors BlockEntityScriptorium.Initialize.
        inventory!.LateInitialize("scribeinbox-" + Pos, api);
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
        // Additive: an Inbox saved before this change has no inventory sub-tree → slots stay empty.
        if (tree.GetTreeAttribute(InventoryTreeKey) is { } invTree)
        {
            inventory!.FromTreeAttributes(invTree);
        }
    }

    /// <summary>Standard vanilla container packet flow (mirrors <c>BlockEntityScriptorium</c>): slot
    /// operations (packet id &lt; 1000) go to the inventory's network util; 1000/1001 open/close the
    /// inventory for the acting player. Rides the built-in block-entity packet channel, NOT the mod's
    /// "scribe" channel (which carries document edits — a separate concern).</summary>
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

    /// <summary>Drop any stored items when the block is broken, so a stored Task Notice or other item is
    /// never destroyed by breaking the block (mirrors <c>BlockEntityScriptorium.OnBlockBroken</c>).
    /// Server-only; this BE is still alive here (VS calls it from <c>SpawnDropsAndRemoveBlock</c> before
    /// removal). THIS block's own document is carried onto the block-item separately by
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
