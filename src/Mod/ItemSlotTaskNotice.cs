using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// An Assignment Desk inventory slot that accepts ONLY Task Notice stacks (`assignment-desk-block`
/// capability's supply/output slots) — mirrors <see cref="ItemSlotScribeDocument"/>'s gate, restricted
/// to <see cref="ItemScribeTaskNotice"/> specifically rather than every <see cref="IScribeDocumentItem"/>,
/// since neither slot should accept a Notebook/Tablet. Enforced server-side on every move path (drag,
/// shift-click auto-merge, hopper/automation), matching that class's own reasoning.
///
/// <para>Shared by both the stacking blank-supply slot and the non-stacking sealed-output slot — the
/// item itself (blank vs. sealed, see <see cref="ItemScribeTaskNotice"/>) already differs in whether it
/// stacks; this slot type doesn't need to distinguish the two.</para>
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
