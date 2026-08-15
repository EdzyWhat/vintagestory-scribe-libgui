using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// The carried-inventory count engine's matcher (add-tracker-link-tasks D5/4.1). Given a Tracker's
/// <c>TargetItemCode</c>, it builds a vanilla <see cref="CraftingRecipeIngredient"/> and sums the sizes of
/// every carried stack that satisfies it — the "Tallybook" pattern. Matching goes through
/// <see cref="CraftingRecipeIngredient.SatisfiesAsIngredient"/> with <c>checkStackSize:false</c>, so it is
/// wildcard-friendly and honors the same equivalence the crafting grid uses (rather than a raw code
/// compare). Carried-only: it scans the player's hotbar + backpack (via
/// <see cref="ScribeModSystem.EnumerateCarriedSlots"/>) and never any block-stored or world items.
///
/// Resolving an ingredient touches the item/block registries, so callers cache the resolved ingredient by
/// code (see the count engine) and re-run only the cheap <see cref="CountCarried"/> sum each recompute.
/// </summary>
internal static class ScribeTrackerCounter
{
    /// <summary>Resolve a Tracker's target item code (e.g. <c>"game:ingot-copper"</c>) into a ready-to-match
    /// <see cref="CraftingRecipeIngredient"/>. Probes the item registry first, then blocks, to set the
    /// ingredient <see cref="EnumItemClass"/> before <see cref="CraftingRecipeIngredient.Resolve"/> (which
    /// needs the class to pick the right registry). Returns false for a null/empty/malformed code or one
    /// that resolves to neither an item nor a block — the caller then treats the Tracker's count as 0.
    /// Wildcard codes (deferred per the change's open question) fail this concrete probe and count as 0.</summary>
    public static bool TryResolveIngredient(IWorldAccessor world, string? targetItemCode, out CraftingRecipeIngredient? ingredient)
    {
        ingredient = null;
        if (string.IsNullOrEmpty(targetItemCode)) return false;

        AssetLocation loc;
        try { loc = new AssetLocation(targetItemCode); }
        catch { return false; }

        var itemClass = world.GetItem(loc) != null ? EnumItemClass.Item
            : world.GetBlock(loc) != null ? EnumItemClass.Block
            : (EnumItemClass?)null;
        if (itemClass is null) return false;

        var ingred = new CraftingRecipeIngredient
        {
            Type = itemClass.Value,
            Code = loc,
            Quantity = 1,
        };
        if (!ingred.Resolve(world, "scribe:tracker")) return false;

        ingredient = ingred;
        return true;
    }

    /// <summary>Sum the stack sizes of every carried stack (hotbar + backpack) that satisfies
    /// <paramref name="ingredient"/>. <c>checkStackSize:false</c> so a stack of any size counts by its full
    /// <see cref="ItemStack.StackSize"/> (we're tallying total held quantity, not testing craftability).</summary>
    public static int CountCarried(IClientPlayer player, CraftingRecipeIngredient ingredient)
    {
        int total = 0;
        foreach (var slot in ScribeModSystem.EnumerateCarriedSlots(player))
        {
            var stack = slot.Itemstack;
            if (stack is null) continue;
            if (ingredient.SatisfiesAsIngredient(stack, checkStackSize: false))
                total += stack.StackSize;
        }
        return total;
    }
}
