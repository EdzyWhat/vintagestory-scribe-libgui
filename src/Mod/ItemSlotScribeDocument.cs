using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// A Scriptorium slot that accepts ONLY Scribe document-bearing stacks (<see cref="IScribeDocumentItem"/>
/// items and <see cref="BlockScribeWritingStation"/> items). Enforced at the slot so every move path
/// (drag, shift-click, hopper) is covered. Only incoming moves are gated; taking items out is unrestricted.
/// Each gate also defers to <c>base</c> for put-lock, storage flags and merge space.
/// </summary>
public sealed class ItemSlotScribeDocument : ItemSlot
{
    public ItemSlotScribeDocument(InventoryBase inventory) : base(inventory)
    {
    }

    /// <summary>True if the stack is a held document item or a picked-up writing-station block.</summary>
    private static bool IsScribeDocument(ItemSlot? sourceSlot) =>
        sourceSlot?.Itemstack?.Collectible is IScribeDocumentItem or BlockScribeWritingStation;

    public override bool CanHold(ItemSlot sourceSlot) =>
        IsScribeDocument(sourceSlot) && base.CanHold(sourceSlot);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) =>
        IsScribeDocument(sourceSlot) && base.CanTakeFrom(sourceSlot, priority);
}
