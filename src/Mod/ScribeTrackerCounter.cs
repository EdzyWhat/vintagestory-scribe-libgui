using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;   // BlockLiquidContainerBase (litre-summing liquid count)

namespace Scribe;

/// <summary>
/// The carried-inventory count matcher. Builds a vanilla <see cref="CraftingRecipeIngredient"/> from a
/// Tracker's <c>TargetItemCode</c> and sums every carried stack satisfying it (the crafting grid's
/// equivalence, wildcard-friendly). Carried-only: hotbar + backpack via
/// <see cref="ScribeModSystem.EnumerateCarriedSlots"/>, never block-stored or world items.
///
/// Resolving touches the item/block registries, so callers cache the ingredient by code and re-run only the
/// cheap <see cref="CountCarried"/> sum.
/// </summary>
internal static class ScribeTrackerCounter
{
    /// <summary>Resolves a Tracker's target item code (e.g. <c>"game:ingot-copper"</c>) into a
    /// ready-to-match <see cref="CraftingRecipeIngredient"/>, probing items then blocks to set the
    /// <see cref="EnumItemClass"/> that <see cref="CraftingRecipeIngredient.Resolve"/> needs. Returns false
    /// for a null/empty/malformed code or one matching neither an item nor a block (the count is then 0).
    ///
    /// <para>A wildcard/family code (containing <c>*</c>, e.g. <c>"game:plank-*"</c>) counts the WHOLE family.
    /// <see cref="CraftingRecipeIngredient.SatisfiesAsIngredient"/> matches it via <c>WildcardUtil.Match</c> and
    /// never reads <see cref="CraftingRecipeIngredient.ResolvedItemStack"/>, so such an ingredient needs only
    /// its class + match type and must NOT be <c>Resolve</c>d (a wildcard has no single registry hit).</para></summary>
    public static bool TryResolveIngredient(IWorldAccessor world, string? targetItemCode, out CraftingRecipeIngredient? ingredient)
    {
        ingredient = null;
        if (string.IsNullOrEmpty(targetItemCode)) return false;

        // Attribute-encoded target (e.g. a copper lantern's material/glass/lining): count only carried stacks of
        // that EXACT variant. The rebuilt stack is supplied as the (Exact) ResolvedItemStack, so matching reduces
        // to targetStack.Satisfies(carried). No Resolve() call; a fresh ingredient's deduplicationIndex is -1,
        // so the ResolvedItemStack setter stores it locally.
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

        // Restricted-wildcard microformat: the target carries allowedVariants/skipVariants so a bare
        // "game:*" doesn't match every carried item. A representative member is resolved only to fix the
        // item CLASS (must equal the carried stack's); matching runs through WildcardUtil.Match. Must precede
        // the AssetLocation parse (the microformat contains '|') and the bare-wildcard branch.
        if (ScribeItemRef.TryParseWildcard(targetItemCode, out var wildLoc, out var allowed, out var skip)
            && wildLoc is not null)
        {
            var member = ScribeItemRef.ResolveWildcardMember(world, targetItemCode);
            if (member?.Collectible is null) return false;

            ingredient = new CraftingRecipeIngredient
            {
                Type = member.Class,
                Code = wildLoc,
                Quantity = 1,
                MatchingType = EnumRecipeMatchType.Wildcard, // routes SatisfiesAsIngredient through WildcardUtil.Match
                AllowedVariants = allowed,
                SkipVariants = skip,
            };
            return true; // NO Resolve: the wildcard match path does not use ResolvedItemStack
        }

        AssetLocation loc;
        try { loc = new AssetLocation(targetItemCode); }
        catch { return false; }

        // A '*' anywhere in the code marks a family ingredient.
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

    /// <summary>Sums the carried quantity (hotbar + backpack) of everything satisfying
    /// <paramref name="ingredient"/>; <c>checkStackSize:false</c> so any stack counts by its full amount.
    ///
    /// <para>Liquid target: a liquid only exists as <c>WaterTightContainable</c> content inside a bucket/bowl,
    /// so (<see cref="EnumMatterState.Liquid"/>) it sums <c>content.StackSize ÷ ItemsPerLitre</c> over carried
    /// liquid containers whose content satisfies the ingredient. Litres are floored (a small epsilon absorbs
    /// float error on exact multiples) while the target is <c>ceil</c>-rounded, so a hair under never falsely
    /// completes.</para></summary>
    public static int CountCarried(IClientPlayer player, CraftingRecipeIngredient ingredient)
    {
        if (ingredient.ResolvedItemStack?.Collectible?.MatterState == EnumMatterState.Liquid)
        {
            float litres = 0f;
            foreach (var slot in ScribeModSystem.EnumerateCarriedSlots(player))
            {
                var stack = slot.Itemstack;
                if (stack?.Collectible is not BlockLiquidContainerBase container) continue;
                var content = container.GetContent(stack);
                if (content is null) continue;
                if (!ingredient.SatisfiesAsIngredient(content, checkStackSize: false)) continue;
                float ipl = BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 1f;
                if (ipl <= 0f) ipl = 1f; // guard a malformed props value; vanilla's own fallback is 1f
                litres += content.StackSize / ipl;
            }
            return (int)System.Math.Floor(litres + 1e-3f);
        }

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
