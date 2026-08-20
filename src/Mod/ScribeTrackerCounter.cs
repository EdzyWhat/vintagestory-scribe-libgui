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
    ///
    /// <para>A <b>wildcard/family</b> code (one containing <c>*</c>, e.g. <c>"game:plank-*"</c> or
    /// <c>"game:bowl-*-fired"</c>) — produced by Crafting Tasks whose recipe ingredient is a genuine family
    /// rather than a concrete <c>{var}</c>-bound item (add-crafting-tasks D6) — is resolved on a separate,
    /// additive branch: it counts the WHOLE family. <see cref="CraftingRecipeIngredient.SatisfiesAsIngredient"/>
    /// matches a wildcard ingredient via <c>WildcardUtil.Match</c> and never touches
    /// <see cref="CraftingRecipeIngredient.ResolvedItemStack"/>, so a wildcard ingredient needs only its
    /// <see cref="EnumItemClass"/> + <see cref="EnumRecipeMatchType"/> set and must NOT be <c>Resolve</c>d
    /// (Resolve requires a concrete registry hit that a wildcard has no single answer for). The class is
    /// probed with the wildcard-aware <see cref="IWorldAccessor.SearchBlocks"/>/<see cref="IWorldAccessor.SearchItems"/>.
    /// The concrete-code path below is left byte-identical so existing plain-Tracker counts never regress.</para></summary>
    public static bool TryResolveIngredient(IWorldAccessor world, string? targetItemCode, out CraftingRecipeIngredient? ingredient)
    {
        ingredient = null;
        if (string.IsNullOrEmpty(targetItemCode)) return false;

        // Attribute-encoded target (support-attribute-encoded-items): the target carries a specific variant's
        // meaningful attributes (a copper lantern's material/glass/lining), so count only carried stacks of
        // that EXACT variant. ScribeItemRef.ResolveStack rebuilds the attributed stack; we hand it to the
        // ingredient as the (Exact) ResolvedItemStack, so SatisfiesAsIngredient reduces to
        // targetStack.Satisfies(carried) — i.e. the carried stack must carry every stored attribute at the
        // stored value (an iron lantern fails because material=copper isn't present). No Resolve() call: the
        // resolved stack is supplied directly, and a fresh ingredient's deduplicationIndex is -1 so the
        // ResolvedItemStack setter stores it locally.
        if (ScribeItemRef.IsAttributeEncoded(targetItemCode))
        {
            var targetStack = ScribeItemRef.ResolveStack(world, targetItemCode);
            if (targetStack is null) return false;

            ingredient = new CraftingRecipeIngredient
            {
                Type = targetStack.Class,
                Code = targetStack.Collectible.Code,
                Quantity = 1,
                MatchingType = EnumRecipeMatchType.Exact, // routes SatisfiesAsIngredient through ResolvedItemStack.Satisfies
                ResolvedItemStack = targetStack,
            };
            return true;
        }

        AssetLocation loc;
        try { loc = new AssetLocation(targetItemCode); }
        catch { return false; }

        // Wildcard/family branch (additive — the concrete path below is unchanged). A '*' anywhere in the
        // code marks a family ingredient; count every member of the family.
        if (targetItemCode.Contains('*'))
        {
            var wildClass = world.SearchBlocks(loc).Length > 0 ? EnumItemClass.Block
                : world.SearchItems(loc).Length > 0 ? EnumItemClass.Item
                : (EnumItemClass?)null;
            if (wildClass is null) return false;

            ingredient = new CraftingRecipeIngredient
            {
                Type = wildClass.Value,
                Code = loc,
                Quantity = 1,
                MatchingType = EnumRecipeMatchType.Wildcard, // routes SatisfiesAsIngredient through WildcardUtil.Match
            };
            return true; // NO Resolve: the wildcard match path does not use ResolvedItemStack
        }

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
