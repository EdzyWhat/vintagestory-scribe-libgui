using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The Assignment Desk's placed-block entity (add-assignment-and-quest-support §5). A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/>: it shares all document, persistence, editor-lock, and
/// guestbook logic with the Lectern/Scriptorium and supplies only its own identity/config.
///
/// <para>Reuses the Lectern's GUI page art as a placeholder backdrop (§13.1 tracks the dedicated
/// Assignment Desk art), and its physical block model/textures are cloned from the Scriptorium's shape
/// (see <c>assignmentdesk.json</c>) pending its own §13.1 asset — the same "placeholder now, restyle
/// later" precedent the Scriptorium itself set for its GUI backdrop.</para>
///
/// <para><see cref="PageAspect"/> is fixed at the design's Decision 8 ratio (<c>W × 1.2W</c>) rather than
/// an art-derived value — this dialog's layout is not blocked on final art (see the
/// <c>assignment-desk-block</c> spec and design.md Decision 8).</para>
/// </summary>
public sealed class BlockEntityAssignmentDesk : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-assignmentdesk";

    protected override string MeshCacheKeyPrefix => "scribeassignmentdeskmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeAssignmentDesk(Pos, this, capi);

    // ── Staging slot (assignment-multi-item-creation design.md D8) ───────────
    //
    // A single Scribe-items-only slot the Create Assignments tab uses to stage an existing document
    // (Notebook, Tablet, or a picked-up Lectern/Scriptorium/Assignment Desk item) for multi-row
    // selection and batch-send. Mirrors BlockEntityScriptorium's inventory verbatim — same slot type,
    // same lazy-init/persistence/packet-routing shape — just with one slot instead of three.

    /// <summary>Staging + Task Notice supply/output slots (`assignment-delivery-mode` capability's
    /// "Send a Notice" mode — tasks.md 4.4). Growing 1→3 here is additive for persistence, matching the
    /// Scriptorium's own 2→3 growth precedent.</summary>
    private const int SlotCount = 3;

    public const int StagingSlotIndex = 0;

    /// <summary>Stacking blank-notice supply slot: the player drops blank Task Notices here before
    /// sending in "Send a Notice" mode; one is consumed per notice-delivered row on Send.</summary>
    public const int NoticeSupplySlotIndex = 1;

    /// <summary>Non-stacking sealed-notice output slot: the Send handler places the freshly-sealed
    /// notice here for the sender to collect and hand-deliver.</summary>
    public const int NoticeOutputSlotIndex = 2;

    /// <summary>Tree sub-key the inventory persists under, kept separate from the document/lock keys so
    /// persistence is additive: an Assignment Desk saved before this change simply lacks this sub-tree
    /// and loads with an empty slot.</summary>
    private const string InventoryTreeKey = "assignmentDeskInventory";

    /// <summary>Created lazily via <see cref="EnsureInventory"/> so it exists before whichever of
    /// <see cref="FromTreeAttributes"/> / <see cref="Initialize"/> the VS block-entity lifecycle runs
    /// first (chunk-load runs FromTree first; a fresh place runs Initialize first).</summary>
    private InventoryGeneric? inventory;

    /// <summary>The Assignment Desk's staging inventory (the Create Assignments tab watches this).</summary>
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
            (slotId, self) => slotId == StagingSlotIndex
                ? new ItemSlotScribeDocument(self)
                : new ItemSlotTaskNotice(self));
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel (the network-readiness gate). Without
        // LateInitialize + Pos, LibGUI's SlotController silently drops every slot click, logging
        // "[gui] Skipped slot activation … not network-ready". Mirrors BlockEntityScriptorium.Initialize.
        inventory!.LateInitialize("scribeassignmentdesk-" + Pos, api);
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

    /// <summary>Drop the staged item when the block is broken, so it is never destroyed by breaking the
    /// block (mirrors <c>BlockEntityScriptorium.OnBlockBroken</c>). THIS block's own document is carried
    /// onto the block-item separately by <see cref="BlockScribeWritingStation.GetDrops"/>.</summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api is ICoreServerAPI)
        {
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
