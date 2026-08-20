using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// One grid-recipe variant a Crafting Task can bind to (add-crafting-tasks Group 6): a stable
/// <see cref="Signature"/> string, the concrete output code and its per-craft yield, the derived counting
/// <see cref="Ingredients"/> (Core-neutral <see cref="ScribeCraftIngredient"/>s), any non-counting
/// <see cref="Notes"/> (liquids, per D7), and a player-facing <see cref="Label"/> distinguishing this variant
/// from the item's other recipes. Produced by <see cref="ScribeCraftRecipeProbe"/> from the live client
/// recipe registry; consumed by the Handbook link builder (<see cref="ScribeHandbookPatch"/>) and the
/// generator/self-heal (<see cref="ScribeModSystem.AddCraftFromHandbook"/>).
/// </summary>
internal sealed record ScribeCraftRecipeVariant(
    string Signature,
    // The Craft parent's target string: the recipe's resolved output re-encoded via ScribeItemRef.Encode
    // (attribute-preserving for lanterns/meals; a bare code for common items). Used as the item code handed
    // to AddCraftFromHandbook so the parent resolves to the correct display name.
    string OutputCode,
    int OutputPerCraft,
    string Label,
    IReadOnlyList<ScribeCraftIngredient> Ingredients,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads the live client grid-recipe registry (<see cref="IWorldAccessor.GridRecipes"/>) and derives, for a
/// given output item, the Crafting-Task recipe variants and their ingredient shopping lists
/// (add-crafting-tasks D3/D6/D7/D9). This is the Mod-side bridge that feeds the VS recipe data into the
/// API-free Core model (<see cref="ScribeCraftIngredient"/> / <see cref="ScribeCraftMath"/>).
///
/// <para><b>Why the grid registry is enough:</b> Vintage Story expands variant/wildcard grid recipes into one
/// CONCRETE <see cref="GridRecipe"/> per resolved output at load time (the same fan-out the vanilla Handbook
/// "Created by" section iterates — see <c>CollectibleBehaviorHandbookTextAndExtraInfo.addCreatedByInfo</c>).
/// So matching a concrete output stack against <c>Output.ResolvedItemStack.Satisfies(stack)</c> yields the
/// already-<c>{var}</c>-substituted recipe: its <see cref="GridRecipe.ResolvedIngredients"/> carry concrete
/// codes for the chosen variant (birch-plank ⇒ birch-log), while a genuinely broad ingredient stays a
/// wildcard. We therefore never do variant substitution ourselves — we read the resolved codes off the
/// recipe, satisfying D6 (concrete for <c>{var}</c>, family for real wildcards) by construction.</para>
///
/// <para><b>Signature</b> (D3): <c>outputCode|ingredientPattern|WxH</c> — the stable, session-independent
/// fields Tallybook precedent uses. It re-resolves the same variant on document-open/self-heal without
/// persisting the (recipe-update-fragile) ingredient list. When several recipes share a signature we take
/// the first in registry order (deterministic); an unresolvable signature degrades gracefully (the parent
/// stays a plain output tracker, children untouched — never a crash or mass-delete).</para>
///
/// <para>Client-only (the recipe registry and the Handbook are client-side here).</para>
/// </summary>
internal static class ScribeCraftRecipeProbe
{
    /// <summary>All grid-recipe variants whose primary output satisfies <paramref name="stack"/> (the Handbook
    /// page's own attributed <see cref="ItemStack"/>), deduplicated by
    /// <see cref="ScribeCraftRecipeVariant.Signature"/> and labeled to distinguish them. Empty when the item
    /// has no grid recipe (the Handbook then shows no "Add Crafting Task" link). Never throws.
    ///
    /// <para>Takes the attributed stack rather than a bare code (support-attribute-encoded-items Fix A): an
    /// attribute-encoded output (a lantern's <c>material</c>/<c>glass</c>/<c>lining</c>) only satisfies its
    /// recipe's <c>Output.ResolvedItemStack.Satisfies(stack)</c> when the stack actually carries those
    /// attributes — the same input vanilla's "Created by" uses. Re-resolving a bare code would drop them and
    /// match nothing, so the link never appeared. Each variant's <see cref="ScribeCraftRecipeVariant.OutputCode"/>
    /// is the recipe's resolved output re-encoded via <see cref="ScribeItemRef.Encode"/>, so a Craft parent
    /// created from it names correctly.</para></summary>
    public static IReadOnlyList<ScribeCraftRecipeVariant> ProbeVariants(ICoreClientAPI capi, ItemStack? stack)
    {
        if (stack is null) return System.Array.Empty<ScribeCraftRecipeVariant>();

        var matches = MatchingRecipes(capi, stack);

        // Dedup by signature (fanned-out duplicates / shapeless twins collapse to one link).
        var bySig = new Dictionary<string, GridRecipe>();
        foreach (var recipe in matches)
        {
            string sig = SignatureOf(recipe);
            bySig.TryAdd(sig, recipe);
        }

        // Label each variant. A lone recipe gets the plain "Add Crafting Task"; with several, each is
        // distinguished by its first counting ingredient's name, falling back to "Recipe N".
        var result = new List<ScribeCraftRecipeVariant>(bySig.Count);
        bool single = bySig.Count == 1;
        int n = 0;
        foreach (var (sig, recipe) in bySig)
        {
            n++;
            var (ingredients, notes) = DeriveIngredients(capi, recipe);
            string label = single
                ? Lang.Get("scribe:scribe-gui-addcraft")
                : Lang.Get("scribe:scribe-gui-addcraft-variant", DistinguishingName(capi, recipe, ingredients, n));
            // Encode the recipe's RESOLVED output stack (attributes included) as the Craft parent's target, so
            // an attribute-encoded output (a copper lantern) names correctly; fall back to the queried stack's
            // bare code if the recipe somehow has no resolved output stack.
            string outputCode = recipe.Output?.ResolvedItemStack is { } outStack
                ? ScribeItemRef.Encode(outStack)
                : (CodeOf(recipe) ?? stack.Collectible.Code.ToString());
            result.Add(new ScribeCraftRecipeVariant(
                sig,
                outputCode,
                OutputPerCraft(recipe),
                label,
                ingredients,
                notes));
        }
        return result;
    }

    /// <summary>Re-resolve the single grid recipe a persisted <paramref name="signature"/> identifies and
    /// derive its PER-CRAFT ingredient list + notes + output-per-craft (generator + self-heal path). The
    /// caller computes <c>craftsNeeded</c> from the returned <c>OutputPerCraft</c> and hands the per-craft
    /// ingredients to <see cref="ScribeDocument.ReconcileCraftIngredients"/>, which does the batch scaling.
    /// Returns null when no live recipe matches the signature (graceful degrade — D3 risk note). Never
    /// throws.</summary>
    public static (IReadOnlyList<ScribeCraftIngredient> Ingredients, IReadOnlyList<string> Notes, int OutputPerCraft)? ResolveBySignature(
        ICoreClientAPI capi, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || capi.World.GridRecipes is null) return null;
        foreach (var recipe in capi.World.GridRecipes)
        {
            if (recipe?.Output is null) continue;
            if (SignatureOf(recipe) == signature)
            {
                var (ingredients, notes) = DeriveIngredients(capi, recipe);
                return (ingredients, notes, OutputPerCraft(recipe));
            }
        }
        return null;
    }

    /// <summary>The per-craft output quantity (D9 batch math input): the recipe output's stack size, ≥ 1.</summary>
    public static int OutputPerCraft(GridRecipe recipe)
    {
        int q = recipe.Output?.Quantity ?? 1;
        return q < 1 ? 1 : q;
    }

    // ---- internals ----

    /// <summary>Grid recipes whose PRIMARY output satisfies <paramref name="stack"/>, mirroring the vanilla
    /// Handbook's "Created by" primary-output test (<c>Output.ResolvedItemStack.Satisfies(stack)</c>) and
    /// honoring <see cref="RecipeBase.ShowInCreatedBy"/>. Byproduct/returned-stack outputs are intentionally
    /// excluded — a Crafting Task targets a recipe's main product.</summary>
    private static IEnumerable<GridRecipe> MatchingRecipes(ICoreClientAPI capi, ItemStack stack)
    {
        var all = capi.World.GridRecipes;
        if (all is null) yield break;
        foreach (var recipe in all)
        {
            if (recipe?.Output is null || !recipe.ShowInCreatedBy) continue;
            var outStack = recipe.Output.ResolvedItemStack;
            if (outStack is not null && outStack.Satisfies(stack))
                yield return recipe;
        }
    }

    /// <summary>Collapse a recipe's grid cells into a per-ingredient counting list plus non-counting notes.
    /// Cells with the same resolved code are summed (three log cells ⇒ one "log ×3"); tool ingredients
    /// (<see cref="CraftingRecipeIngredient.IsTool"/>) are dropped (not consumed); liquid ingredients become a
    /// <see cref="Notes"/> line (D7, not litre-countable). Each counting ingredient's code is CONCRETE when the
    /// recipe resolved it to an exact stack (<c>{var}</c>-bound), or the broad WILDCARD code when it is a
    /// genuine family (D6). Quantities are PER-CRAFT; batch scaling is applied later by
    /// <see cref="ScribeDocument.ReconcileCraftIngredients"/>.</summary>
    private static (IReadOnlyList<ScribeCraftIngredient>, IReadOnlyList<string>) DeriveIngredients(
        ICoreClientAPI capi, GridRecipe recipe)
    {
        var perCode = new Dictionary<string, int>();     // counting ingredient code -> per-craft quantity
        var order = new List<string>();                  // preserve first-seen order for stable child layout
        var notes = new List<string>();
        var seenNotes = new HashSet<string>();

        var cells = recipe.ResolvedIngredients;
        if (cells is not null)
        {
            foreach (var cell in cells)
            {
                if (cell is null || cell.IsTool) continue;

                var resolved = cell.ResolvedItemStack?.Collectible;
                // Liquid ingredient (e.g. honey/water portion): not litre-countable in v1 — surface as a note.
                if (resolved is not null && resolved.MatterState == EnumMatterState.Liquid)
                {
                    string noteText = Lang.Get("scribe:scribe-gui-craft-liquid-note", DisplayName(capi, cell));
                    if (seenNotes.Add(noteText)) notes.Add(noteText);
                    continue;
                }

                string? code = IngredientCode(cell);
                if (code is null) continue;

                int per = cell.Quantity < 1 ? 1 : cell.Quantity;
                if (perCode.TryGetValue(code, out int existing)) perCode[code] = existing + per;
                else { perCode[code] = per; order.Add(code); }
            }
        }

        var ingredients = order
            .Select(code => new ScribeCraftIngredient(code, perCode[code]))
            .ToList();
        return (ingredients, notes);
    }

    /// <summary>The code a counting ingredient contributes to a child Tracker: the CONCRETE resolved code for
    /// an exact (<c>{var}</c>-bound) ingredient, or the broad WILDCARD pattern for a genuine family ingredient
    /// (D6). Null when neither is available.</summary>
    private static string? IngredientCode(CraftingRecipeIngredient ingredient)
    {
        if (ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.ResolvedItemStack?.Collectible?.Code is { } concrete)
            return concrete.ToString();
        return ingredient.Code?.ToString();
    }

    /// <summary>Signature D3: <c>outputCode|ingredientPattern|WxH</c>. The pattern and dimensions are retained
    /// client-side (<see cref="GridRecipe.FreeRAMServer"/> only nulls them server-side).</summary>
    private static string SignatureOf(GridRecipe recipe)
    {
        string output = CodeOf(recipe) ?? "?";
        string pattern = recipe.IngredientPattern ?? "";
        return $"{output}|{pattern}|{recipe.Width}x{recipe.Height}";
    }

    /// <summary>The concrete output collectible code of a (resolved) grid recipe.</summary>
    private static string? CodeOf(GridRecipe recipe)
        => recipe.Output?.ResolvedItemStack?.Collectible?.Code?.ToString();

    /// <summary>A distinguishing name for a multi-recipe item's link: the first counting ingredient's display
    /// name, or "Recipe N" when the recipe has no nameable counting ingredient.</summary>
    private static string DistinguishingName(ICoreClientAPI capi, GridRecipe recipe,
        IReadOnlyList<ScribeCraftIngredient> ingredients, int ordinal)
    {
        if (ingredients.Count > 0)
        {
            // A concrete ingredient code resolves to a stack whose name distinguishes the variant; a genuine
            // wildcard family won't resolve, so fall through to the ordinal.
            var stack = ScribeItemRef.ResolveStack(capi.World, ingredients[0].ItemCode);
            if (stack is not null) return stack.GetName();
        }
        return Lang.Get("scribe:scribe-gui-craft-recipe-ordinal", ordinal);
    }

    private static string DisplayName(ICoreClientAPI capi, CraftingRecipeIngredient ingredient)
    {
        var stack = ingredient.ResolvedItemStack;
        if (stack is not null) return stack.GetName();
        return ingredient.Code?.ToString() ?? "?";
    }
}
