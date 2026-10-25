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
public sealed class BlockEntityAssignmentDesk : BlockEntityScribeInventoryStation
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
    // multi-row selection and batch-send, plus the Task Notice supply/output slots.

    public const int StagingSlotIndex = 0;

    /// <summary>Stacking blank-notice supply slot: the player drops blank Task Notices here before
    /// sending in "Send a Notice" mode; one is consumed per notice-delivered row on Send.</summary>
    public const int NoticeSupplySlotIndex = 1;

    /// <summary>Non-stacking sealed-notice output slot: the Send handler places the freshly-sealed
    /// notice here for the sender to collect and hand-deliver.</summary>
    public const int NoticeOutputSlotIndex = 2;

    protected override int InventorySlotCount => 3;

    protected override string InventoryTreeKey => "assignmentDeskInventory";

    protected override string InventoryChannelPrefix => "scribeassignmentdesk-";

    protected override ItemSlot CreateSlot(int slotId, InventoryBase inventory) => slotId == StagingSlotIndex
        ? new ItemSlotScribeDocument(inventory)
        : new ItemSlotTaskNotice(inventory);
}
