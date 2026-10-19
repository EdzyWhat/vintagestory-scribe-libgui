using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The Assignment Desk's placed-block entity. A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/> supplying only its identity/config. Reuses the Lectern's
/// page art as a placeholder; <see cref="PageAspect"/> is a fixed <c>W × 1.2W</c> so the dialog layout does
/// not depend on the art.
/// </summary>
public sealed class BlockEntityAssignmentDesk : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-assignmentdesk";

    protected override string MeshCacheKeyPrefix => "scribeassignmentdeskmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeAssignmentDesk(Pos, this, capi);

    protected override ScribeBoxTuningTarget? TuningTarget => ScribeBoxTuningTarget.AssignmentDesk;

    // ── Staging slot ─────────────────────────────────────────────────────────
    // A Scribe-items-only slot the Create Assignments tab uses to stage an existing document for
    // multi-row selection and batch-send, plus the Task Notice supply/output slots. Same lazy-init/
    // persistence/packet-routing shape as BlockEntityScriptorium's inventory.

    /// <summary>Staging + Task Notice supply/output slots (the "Send a Notice" delivery mode).</summary>
    private const int SlotCount = 3;

    public const int StagingSlotIndex = 0;

    /// <summary>Stacking blank-notice supply slot: the player drops blank Task Notices here before
    /// sending in "Send a Notice" mode; one is consumed per notice-delivered row on Send.</summary>
    public const int NoticeSupplySlotIndex = 1;

    /// <summary>Non-stacking sealed-notice output slot: the Send handler places the freshly-sealed
    /// notice here for the sender to collect and hand-deliver.</summary>
    public const int NoticeOutputSlotIndex = 2;

    /// <summary>Tree sub-key for the inventory; a tree lacking it loads with empty slots.</summary>
    private const string InventoryTreeKey = "assignmentDeskInventory";

    /// <summary>Created lazily via <see cref="EnsureInventory"/> because chunk-load runs FromTreeAttributes
    /// first while a fresh place runs Initialize first.</summary>
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

        // Bind the inventory to the block-entity packet channel. Without LateInitialize + Pos, LibGUI's
        // SlotController silently drops every slot click ("Skipped slot activation … not network-ready").
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

    /// <summary>Standard vanilla container packet flow (mirrors <c>BlockEntityScriptorium</c>): packet id
    /// &lt; 1000 goes to the inventory's network util; 1000/1001 open/close it for the player. Rides the
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

    /// <summary>Drops the staged item when the block is broken. The block's own document is carried onto
    /// the item by <see cref="BlockScribeWritingStation.GetDrops"/>.</summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api is ICoreServerAPI)
        {
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
