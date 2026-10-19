using System.Collections.Generic;
using System.Linq;
using System.Text;                 // StringBuilder (.scribeprobe dev dump)
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;   // JsonObject (recipe liquidContainerProps access)
using Vintagestory.GameContent;   // GuiHandbookItemStackPage.PageCodeForStack (variant-identity primitive)

namespace Scribe;

/// <summary>
/// One grid-recipe variant a Crafting Task can bind to: a stable <see cref="Signature"/>, the concrete output
/// code and per-craft yield, the counting <see cref="Ingredients"/>, non-counting <see cref="Notes"/> (liquids),
/// and a player-facing <see cref="Label"/> distinguishing it from the item's other recipes. Produced by
/// <see cref="ScribeCraftRecipeProbe"/> from the live client recipe registry.
/// </summary>
internal sealed record ScribeCraftRecipeVariant(
    string Signature,
    // The Craft parent's target: the recipe's resolved output re-encoded via ScribeItemRef.Encode
    // (attribute-preserving for lanterns/meals), so the parent resolves to the correct display name.
    string OutputCode,
    int OutputPerCraft,
    string Label,
    IReadOnlyList<ScribeCraftIngredient> Ingredients,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads the live client grid-recipe registry (<see cref="IWorldAccessor.GridRecipes"/>) and derives, for an
/// output item, the Crafting-Task recipe variants and their ingredient lists, feeding VS recipe data into the
/// API-free Core model. Client-only.
///
/// <para>VS expands variant/wildcard grid recipes into one CONCRETE <see cref="GridRecipe"/> per resolved output
/// at load time, so <see cref="GridRecipe.ResolvedIngredients"/> carry concrete codes for the chosen variant
/// (<c>{var}</c>) while a genuinely broad ingredient stays a wildcard. No variant substitution is done here.</para>
///
/// <para>Variant identity is the Handbook page code (<see cref="GuiHandbookItemStackPage.PageCodeForStack"/>),
/// which folds distinguishing attributes (a lantern's material/lining/glass) into identity;
/// <see cref="MatchingRecipes"/> and <see cref="SignatureOf"/> are built on it. <c>Output.ResolvedItemStack.Satisfies</c>
/// is avoided because its attribute-subset test over-matches across variants.</para>
///
/// <para>Signature: <c>outputPageCode|ingredientPattern|WxH</c>, stable across sessions so a variant re-resolves
/// without persisting the ingredient list. Duplicates take the first recipe in registry order; an unresolvable
/// signature degrades gracefully (parent stays a plain output tracker, children untouched).</para>
/// </summary>
internal static class ScribeCraftRecipeProbe
{
    /// <summary>All grid-recipe variants whose primary output matches <paramref name="stack"/>, deduplicated by
    /// <see cref="ScribeCraftRecipeVariant.Signature"/> and labeled. Empty when the item has no grid recipe.
    /// Never throws.
    ///
    /// <para>Takes the attributed stack, not a bare code: attribute-encoded outputs (a lantern's
    /// material/glass/lining) only match when the stack carries those attributes. Each variant's
    /// <see cref="ScribeCraftRecipeVariant.OutputCode"/> is the resolved output re-encoded via
    /// <see cref="ScribeItemRef.Encode"/>.</para></summary>
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

        // A lone recipe gets the plain label; several are distinguished by DistinguishingName.
        var result = new List<ScribeCraftRecipeVariant>(bySig.Count);
        bool single = bySig.Count == 1;
        int n = 0;
        foreach (var (sig, recipe) in bySig)
        {
            n++;
            var (ingredients, notes) = DeriveIngredients(capi, recipe);
            string label = single
                ? Lang.Get("scribe:scribe-gui-handbook-addcraft")
                : Lang.Get("scribe:scribe-gui-handbook-addcraft-variant", DistinguishingName(capi, recipe, ingredients, n));
            // Encode the resolved output stack (attributes included) as the Craft parent's target; with none,
            // fall back to the queried stack's bare code.
            string outputCode = recipe.Output?.ResolvedItemStack is { } outStack
                ? ScribeItemRef.Encode(outStack)
                : stack.Collectible.Code.ToString();
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

    /// <summary>Re-resolves the single grid recipe a persisted <paramref name="signature"/> identifies and derives
    /// its PER-CRAFT ingredients, notes, and output-per-craft; batch scaling is done by
    /// <see cref="ScribeDocument.ReconcileCraftIngredients"/>. Null when no live recipe matches. Never throws.</summary>
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

    /// <summary>The per-craft output quantity (input to the batch math): the recipe output's stack size, ≥ 1.</summary>
    public static int OutputPerCraft(GridRecipe recipe)
    {
        int q = recipe.Output?.Quantity ?? 1;
        return q < 1 ? 1 : q;
    }

    /// <summary>DEV (<c>.scribeprobe</c>): a human-readable dump of the page code, matched recipes, signatures,
    /// counting ingredients, and notes the probe sees for <paramref name="stack"/>. Client-only; never throws.</summary>
    public static string Describe(ICoreClientAPI capi, ItemStack? stack)
    {
        if (stack is null) return "scribeprobe: no item — hold one, or pass a code (e.g. .scribeprobe game:plank-oak).";

        string page = PageCodeForStack(stack);
        var matches = MatchingRecipes(capi, stack).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"scribeprobe: {stack.GetName()}  [{stack.Collectible?.Code}]");
        sb.AppendLine($"  page code: {page}");
        sb.AppendLine($"  matched recipes: {matches.Count}");
        int i = 0;
        foreach (var recipe in matches)
        {
            i++;
            var (ingredients, notes) = DeriveIngredients(capi, recipe);
            sb.AppendLine($"  [{i}] sig={SignatureOf(recipe)}  out×{OutputPerCraft(recipe)}");
            foreach (var ing in ingredients)
                sb.AppendLine(ing.IsLiquid
                    ? $"        - {ing.ItemCode} ×{ing.LitresPerCraft:0.###} L/craft (liquid)"
                    : $"        - {ing.ItemCode} ×{ing.PerCraftQuantity}");
            foreach (var note in notes)
                sb.AppendLine($"        · {note}");
        }
        return sb.ToString().TrimEnd();
    }

    // ---- internals ----

    /// <summary>Grid recipes whose PRIMARY output is the same Handbook variant as <paramref name="stack"/>
    /// (<see cref="GuiHandbookItemStackPage.PageCodeForStack"/> equality), honoring
    /// <see cref="RecipeBase.ShowInCreatedBy"/>. Byproducts are excluded. Equality, not a <c>Satisfies</c> subset
    /// test, which over-matches across attribute variants; a stack with no page code (<c>"?"</c>) matches nothing.</summary>
    private static IEnumerable<GridRecipe> MatchingRecipes(ICoreClientAPI capi, ItemStack stack)
    {
        var all = capi.World.GridRecipes;
        if (all is null) yield break;
        string want = PageCodeForStack(stack);
        if (want == UnknownPageCode) yield break;
        foreach (var recipe in all)
        {
            if (recipe?.Output is null || !recipe.ShowInCreatedBy) continue;
            if (OutputPageCode(recipe) == want)
                yield return recipe;
        }
    }

    /// <summary>Collapses a recipe's grid cells into a counting ingredient list plus non-counting notes. Cells with
    /// the same resolved code are summed; tools (<see cref="CraftingRecipeIngredient.IsTool"/>) are dropped; liquid
    /// cells become notes. A code is CONCRETE when the recipe resolved an exact stack, else the WILDCARD code.
    /// Quantities are PER-CRAFT; batch scaling is applied by <see cref="ScribeDocument.ReconcileCraftIngredients"/>.</summary>
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
                // Skip tools, non-consumed cells, and tags-only matchers (their default *:* code would encode
                // as a bogus wildcard Tracker).
                if (cell is null || cell.IsTool || !cell.Consume) continue;
                if (cell.MatchingType == EnumRecipeMatchType.TagsOnly) continue;

                var resolved = cell.ResolvedItemStack?.Collectible;
                // A liquid directly in a grid cell (rare; vanilla liquids live in containers, see TryAddLiquid) has no
                // `requiresLitres`, so it degrades to a note.
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

        // Containerized liquids (ink-and-quill, poultice, bandage, oillamp, beenade) are declared as
        // attributes.liquidContainerProps.requiresContent, not as grid cells, so the MatterState check above never
        // fires. Emitted as COUNTING litre ingredients, mirroring vanilla
        // BlockLiquidContainerBase.OnHandbookRecipeRender (VSAPI-NOTES.md § craft); unresolvable ones become notes.
        TryAddLiquid(capi, recipe, ingredients, notes, seenNotes);

        return (ingredients, notes);
    }

    /// <summary>Emits each containerized-liquid requirement declared on the recipe (not a grid cell) as a
    /// COUNTING liquid ingredient, or as a note when it cannot be resolved.
    /// Reads recipe-level <c>attributes.liquidContainerProps.requiresContent</c>, falling back to per-ingredient
    /// <c>RecipeAttributes.requiresContent</c>, as vanilla's <c>OnHandbookRecipeRender</c> does:
    /// <c>code</c>, <c>type</c> (<c>item</c>|<c>block</c>), and the SIBLING <c>requiresLitres</c> float.
    ///
    /// <para>A concrete code with <c>requiresLitres &gt; 0</c> whose stack resolves becomes a litre-counted
    /// <see cref="ScribeCraftIngredient"/>; otherwise a <c>scribe:scribe-gui-craft-liquid-note</c> note.
    /// Duplicate codes are merged by summing litres because
    /// <see cref="ScribeDocument.ReconcileCraftIngredients"/> expects distinct codes. Access is null-safe and a bad
    /// AssetLocation is caught, so a malformed recipe yields no ingredient and no note.</para></summary>
    private static void TryAddLiquid(
        ICoreClientAPI capi, GridRecipe recipe,
        List<ScribeCraftIngredient> ingredients, List<string> notes, HashSet<string> seenNotes)
    {
        // Recipe-level liquidContainerProps wins; only when absent are per-ingredient RecipeAttributes gathered.
        var reqs = new List<(string Code, string? Type, float Litres)>();

        var recipeProps = recipe.Attributes?["liquidContainerProps"];
        if (recipeProps is not null && recipeProps.Exists && recipeProps["requiresContent"].Exists)
        {
            AddLiquidRequirement(reqs, recipeProps);
        }
        else
        {
            var cells = recipe.ResolvedIngredients;
            if (cells is not null)
            {
                foreach (var cell in cells)
                {
                    var ra = cell?.RecipeAttributes;
                    if (ra is not null && ra.Exists && ra["requiresContent"].Exists)
                        AddLiquidRequirement(reqs, ra);
                }
            }
        }
        if (reqs.Count == 0) return;

        // Merge duplicate liquid codes by summing litres.
        var byCode = new Dictionary<string, (string? Type, float Litres)>();
        var codeOrder = new List<string>();
        foreach (var (code, type, litres) in reqs)
        {
            if (byCode.TryGetValue(code, out var cur))
                byCode[code] = (cur.Type ?? type, cur.Litres + litres);
            else { byCode[code] = (type, litres); codeOrder.Add(code); }
        }

        foreach (var code in codeOrder)
        {
            var (type, litres) = byCode[code];

            ItemStack? stack = code.Contains('*') ? null : ResolveLiquidStack(capi, code, type);
            if (stack is not null && litres > 0f)
            {
                ingredients.Add(new ScribeCraftIngredient(
                    code, PerCraftQuantity: 1, IsLiquid: true, LitresPerCraft: litres));
            }
            else
            {
                string liquidName = stack?.GetName() ?? code;
                string noteText = Lang.Get("scribe:scribe-gui-craft-liquid-note", liquidName);
                if (seenNotes.Add(noteText)) notes.Add(noteText);
            }
        }
    }

    /// <summary>Reads one <c>requiresContent</c> code/type plus the sibling <c>requiresLitres</c> from a
    /// liquid-container props object into <paramref name="reqs"/>; skips a missing/empty code.</summary>
    private static void AddLiquidRequirement(List<(string Code, string? Type, float Litres)> reqs, JsonObject props)
    {
        string? code = props["requiresContent"]["code"].AsString(null);
        if (string.IsNullOrEmpty(code)) return;
        string? type = props["requiresContent"]["type"].AsString(null);
        float litres = props["requiresLitres"].AsFloat(0f); // SIBLING of requiresContent, not nested inside it
        reqs.Add((code!, type, litres));
    }

    /// <summary>Resolves a concrete liquid code to a display <see cref="ItemStack"/> (item or block per
    /// <paramref name="type"/>); null on a bad location or unregistered code.</summary>
    private static ItemStack? ResolveLiquidStack(ICoreClientAPI capi, string code, string? type)
    {
        try
        {
            var loc = new AssetLocation(code);
            return string.Equals(type, "block", System.StringComparison.OrdinalIgnoreCase)
                ? (capi.World.GetBlock(loc) is { } b ? new ItemStack(b, 1) : null)
                : (capi.World.GetItem(loc) is { } it ? new ItemStack(it, 1) : null);
        }
        catch { return null; }
    }

    /// <summary>The code a counting ingredient contributes to a child Tracker: the CONCRETE resolved code for an
    /// exact ingredient, else the WILDCARD code. A wildcard with an <c>allowedVariants</c>/<c>skipVariants</c>
    /// restriction is encoded via <see cref="ScribeItemRef.EncodeWildcard"/> so the Tracker keeps the variant
    /// filter (a bare <c>game:*</c> would count every carried item). Null when neither is available.</summary>
    private static string? IngredientCode(CraftingRecipeIngredient ingredient)
    {
        if (ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.ResolvedItemStack?.Collectible?.Code is { } concrete)
            return concrete.ToString();
        if (ingredient.MatchingType == EnumRecipeMatchType.TagsOnly) return null;
        if (ingredient.Code is not { } wild) return null;
        // Code defaults to *:*; never encode that as a family Tracker (it would count every carried item).
        if (wild.Domain == "*" && wild.Path == "*") return null;
        return ScribeItemRef.EncodeWildcard(wild, ingredient.AllowedVariants, ingredient.SkipVariants);
    }

    /// <summary>Sentinel page code for a recipe/stack with no page code. Two <c>"?"</c>s never match because
    /// <see cref="MatchingRecipes"/> bails when the viewed stack itself resolves to <c>"?"</c>.</summary>
    private const string UnknownPageCode = "?";

    /// <summary>The variant signature <c>outputPageCode|ingredientPattern|WxH</c>. Pattern and dimensions survive
    /// client-side (<see cref="GridRecipe.FreeRAMServer"/> only nulls them server-side); the attribute-qualified
    /// output keeps metal/lining/glass variants distinct. A signature that no longer re-resolves leaves the parent
    /// a plain output tracker.</summary>
    private static string SignatureOf(GridRecipe recipe)
    {
        string output = OutputPageCode(recipe);
        string pattern = recipe.IngredientPattern ?? "";
        return $"{output}|{pattern}|{recipe.Width}x{recipe.Height}";
    }

    /// <summary>The attribute-qualified Handbook page code of a recipe's resolved primary output;
    /// <see cref="UnknownPageCode"/> when it has none.</summary>
    private static string OutputPageCode(GridRecipe recipe)
        => recipe.Output?.ResolvedItemStack is { } outStack ? PageCodeForStack(outStack) : UnknownPageCode;

    /// <summary>Guarded <see cref="GuiHandbookItemStackPage.PageCodeForStack"/>: returns
    /// <see cref="UnknownPageCode"/> on null or throw, so a degenerate stack never masquerades as a real variant.</summary>
    private static string PageCodeForStack(ItemStack stack)
    {
        try { return GuiHandbookItemStackPage.PageCodeForStack(stack) ?? UnknownPageCode; }
        catch { return UnknownPageCode; }
    }

    /// <summary>A distinguishing name for a multi-recipe item's link: the first counting ingredient's display
    /// name, or "Recipe N" when the recipe has no nameable counting ingredient.</summary>
    private static string DistinguishingName(ICoreClientAPI capi, GridRecipe recipe,
        IReadOnlyList<ScribeCraftIngredient> ingredients, int ordinal)
    {
        if (ingredients.Count > 0)
        {
            // A concrete code resolves to a distinguishing name; a wildcard family will not, so fall through to the ordinal.
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
