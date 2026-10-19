using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// An Assignment Desk slot that accepts ONLY Task Notice stacks; like <see cref="ItemSlotScribeDocument"/> but
/// restricted to <see cref="ItemScribeTaskNotice"/>, and enforced on every move path. Shared by the blank-supply
/// and sealed-output slots.
/// </summary>
public sealed class ItemSlotTaskNotice : ItemSlot
{
    public ItemSlotTaskNotice(InventoryBase inventory) : base(inventory)
    {
    }

    private static bool IsTaskNotice(ItemSlot? sourceSlot) =>
        sourceSlot?.Itemstack?.Collectible is ItemScribeTaskNotice;

    public override bool CanHold(ItemSlot sourceSlot) =>
        IsTaskNotice(sourceSlot) && base.CanHold(sourceSlot);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) =>
        IsTaskNotice(sourceSlot) && base.CanTakeFrom(sourceSlot, priority);
}
