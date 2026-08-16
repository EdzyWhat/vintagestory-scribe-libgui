using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// A Scriptorium inventory slot that accepts ONLY Scribe document-bearing stacks — the held document
/// items implementing <see cref="IScribeDocumentItem"/> (the Notebook, the Clockmaker's Notebook, and
/// the Tablet in any state), AND the placed writing stations broken back into item form
/// (<see cref="BlockScribeWritingStation"/> — the Lectern and the Scriptorium itself), whose stack
/// carries a Scribe document just the same. Every other item is refused at the slot itself, so the
/// accept rule is enforced server-side on every move path (drag, shift-click auto-merge,
/// hopper/automation), not just in the dialog — a dialog-only filter would be bypassable
/// (add-scriptorium-inventory D3).
///
/// <para>Only <em>incoming</em> moves are gated: <see cref="CanHold"/> (drag/place onto the slot) and
/// <see cref="CanTakeFrom"/> (merge a source stack into the slot) both require a Scribe item. Taking an
/// item back OUT of the slot is unrestricted — that's governed by the source slot, not these methods.
/// Each gate also defers to <c>base</c> so the standard slot plumbing (put-lock, storage flags, merge
/// space) still applies; Scribe items carry the default <c>General</c> storage flag, so a valid
/// Notebook/Tablet is never rejected by that base check.</para>
/// </summary>
public sealed class ItemSlotScribeDocument : ItemSlot
{
    public ItemSlotScribeDocument(InventoryBase inventory) : base(inventory)
    {
    }

    /// <summary>A stack "carries a Scribe document" if it is one of the held document items
    /// (<see cref="IScribeDocumentItem"/>) or a picked-up writing-station block
    /// (<see cref="BlockScribeWritingStation"/>: Lectern / Scriptorium). Both persist a Scribe document on
    /// their stack, so both belong in the Scriptorium's document storage.</summary>
    private static bool IsScribeDocument(ItemSlot? sourceSlot) =>
        sourceSlot?.Itemstack?.Collectible is IScribeDocumentItem or BlockScribeWritingStation;

    public override bool CanHold(ItemSlot sourceSlot) =>
        IsScribeDocument(sourceSlot) && base.CanHold(sourceSlot);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) =>
        IsScribeDocument(sourceSlot) && base.CanTakeFrom(sourceSlot, priority);
}
