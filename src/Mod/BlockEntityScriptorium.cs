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
public sealed class BlockEntityScriptorium : BlockEntityScribeInventoryStation
{
    // Placeholder: reuses the Lectern page art (same 1024×1160 size, so the same aspect).
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1160f / 1024f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-scriptorium";

    protected override string MeshCacheKeyPrefix => "scribescriptoriummesh";

    protected override ScribeBoxTuningTarget? TuningTarget => ScribeBoxTuningTarget.Scriptorium;

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeScriptorium(Pos, this, capi);

    // Scribe-item storage slots: 0 = copy Original (source), 1 = copy Duplicate (target),
    // 2 = the Import/Export source-and-target.
    protected override int InventorySlotCount => 3;

    protected override string InventoryTreeKey => "scriptoriumInventory";

    protected override string InventoryChannelPrefix => "scribescriptorium-";

    protected override ItemSlot CreateSlot(int slotId, InventoryBase inventory) => new ItemSlotScribeDocument(inventory);
}
