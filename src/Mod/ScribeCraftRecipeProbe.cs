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
/// So each metal lantern is its own concrete recipe whose <see cref="GridRecipe.ResolvedIngredients"/> carry concrete
/// codes for the chosen variant (birch-plank ⇒ birch-log), while a genuinely broad ingredient stays a
/// wildcard. We therefore never do variant substitution ourselves — we read the resolved codes off the
/// recipe, satisfying D6 (concrete for <c>{var}</c>, family for real wildcards) by construction.</para>
///
/// <para><b>Variant identity via the Handbook page code</b> (fix-recipe-variant-identity D1/D2): we key a
/// recipe to its output by <see cref="GuiHandbookItemStackPage.PageCodeForStack"/> — the same
/// attribute-qualified string VS itself uses per Handbook page (Tallybook's proven approach). It folds the
/// distinguishing attributes (a lantern's material/lining/glass) into identity, so the 13 metal fan-outs of
/// one code family get 13 distinct identities instead of colliding on the bare code. Both
/// <see cref="MatchingRecipes"/> (equality against the viewed page's stack) and <see cref="SignatureOf"/> are
/// built on it; we never use <c>Output.ResolvedItemStack.Satisfies</c> for output matching (that
/// attribute-subset test over-matches across variants — the exact 6.1 bug).</para>
///
/// <para><b>Signature</b> (D3): <c>outputPageCode|ingredientPattern|WxH</c> — stable, session-independent
/// fields. It re-resolves the same variant on document-open/self-heal without
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
            // The else branch is reached only when the recipe has no resolved output stack (so there is no
            // concrete output code to read); fall back to the queried page stack's own bare code.
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

    /// <summary>DEV (<c>.scribeprobe</c>, fix-recipe-variant-identity D6): a one-shot, human-readable dump of
    /// what the probe sees for <paramref name="stack"/> — its Handbook page code, and for each matched grid
    /// recipe the full <see cref="SignatureOf"/>, derived counting ingredients (code × per-craft qty), and
    /// notes. The measure-don't-theorize instrument for confirming in-game that e.g. a copper and a gold
    /// lantern resolve to DISTINCT page codes/signatures (the 6.1 collision) BEFORE the playtest gate.
    /// Client-only; never throws.</summary>
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

    /// <summary>Grid recipes whose PRIMARY output is the SAME Handbook variant as <paramref name="stack"/> (the
    /// viewed page's own attributed stack), keyed by <see cref="GuiHandbookItemStackPage.PageCodeForStack"/>
    /// equality (fix-recipe-variant-identity D2) and honoring <see cref="RecipeBase.ShowInCreatedBy"/>.
    /// Byproduct/returned-stack outputs are intentionally excluded — a Crafting Task targets a recipe's main
    /// product.
    ///
    /// <para>Page-code equality replaces the old <c>Output.ResolvedItemStack.Satisfies(stack)</c> subset test,
    /// which over-matched across attribute variants (every metal lantern satisfied every other — the 6.1 bug).
    /// Equality is exact and direction-free. A stack that yields no page code (<c>"?"</c>) matches nothing.</para></summary>
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
                // A liquid sitting directly in a GRID CELL (rare — vanilla liquids live in containers, so this
                // path effectively never fires for survival grid recipes) has no `requiresLitres` to count in
                // litres, so it degrades to a note here — the same resolve-or-note outcome the containerized
                // path (TryAddLiquid, below) reaches when a liquid can't be resolved (add-liquid-ingredient-tracker
                // 3.4). The container path reads recipe/ingredient attributes, not grid cells, so there is no
                // double-emit with this branch.
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

        // Containerized-liquid ingredients (ink-and-quill, poultice, bandage, oillamp, beenade): the liquid is
        // NOT a grid cell — it's declared on the recipe as attributes.liquidContainerProps.requiresContent, so
        // the per-cell MatterState check above never fires (the cell is the solid bowl). Emit each such liquid as
        // a COUNTING liquid ingredient (litre-based Tracker), mirroring vanilla
        // BlockLiquidContainerBase.OnHandbookRecipeRender (VSAPI-NOTES.md § craft); an unresolvable liquid
        // degrades to the old note. The bowl stays counted as a normal ingredient (it is genuinely required).
        TryAddLiquid(capi, recipe, ingredients, notes, seenNotes);

        return (ingredients, notes);
    }

    /// <summary>Emit each containerized-liquid requirement declared on the recipe (not on a grid cell) as a
    /// COUNTING liquid ingredient, falling back to the old note when it can't be resolved
    /// (add-liquid-ingredient-tracker D2/D6). The requirement lives at
    /// <c>attributes.liquidContainerProps.requiresContent</c> (recipe-level, what the survival recipes use),
    /// with a per-ingredient <c>RecipeAttributes.requiresContent</c> fallback — the same authoritative source
    /// vanilla reads in <c>BlockLiquidContainerBase.OnHandbookRecipeRender</c>: <c>requiresContent.code</c>
    /// (the liquid code), <c>requiresContent.type</c> (<c>item</c>|<c>block</c>), and the SIBLING
    /// <c>requiresLitres</c> float. The container bowl remains a normal counted ingredient; only the liquid it
    /// must hold is added here.
    ///
    /// <para>Per distinct liquid code: if the code is concrete, <c>requiresLitres &gt; 0</c>, and the item/block
    /// stack resolves, add a liquid <see cref="ScribeCraftIngredient"/> (litre-counted). Otherwise append the
    /// existing <c>scribe:scribe-gui-craft-liquid-note</c> Text note naming the liquid (graceful degrade).
    /// Duplicate liquid codes are merged by summing their litres before emission, because
    /// <see cref="ScribeDocument.ReconcileCraftIngredients"/> expects distinct codes. Defensive: every access is
    /// null-safe and the only throw risk (a bad AssetLocation) is caught, so a malformed recipe simply yields
    /// no ingredient and no note.</para></summary>
    private static void TryAddLiquid(
        ICoreClientAPI capi, GridRecipe recipe,
        List<ScribeCraftIngredient> ingredients, List<string> notes, HashSet<string> seenNotes)
    {
        // Collect requirements (code + type + litres). Recipe-level liquidContainerProps takes precedence (the
        // survival-recipe form); only when it is absent do we gather the per-ingredient RecipeAttributes across
        // every cell that carries one.
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

        // Merge duplicate liquid codes by summing litres (ReconcileCraftIngredients expects distinct codes).
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

            // Fully resolvable → a counting liquid ingredient; anything else → the fallback note.
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

    /// <summary>Read one <c>requiresContent.code</c>/<c>type</c> + sibling <c>requiresLitres</c> off a
    /// liquid-container props object (either the recipe-level <c>liquidContainerProps</c> or a cell's
    /// <c>RecipeAttributes</c>) and append it to <paramref name="reqs"/>. Skips a missing/empty code.</summary>
    private static void AddLiquidRequirement(List<(string Code, string? Type, float Litres)> reqs, JsonObject props)
    {
        string? code = props["requiresContent"]["code"].AsString(null);
        if (string.IsNullOrEmpty(code)) return;
        string? type = props["requiresContent"]["type"].AsString(null);
        float litres = props["requiresLitres"].AsFloat(0f); // SIBLING of requiresContent, not nested inside it
        reqs.Add((code!, type, litres));
    }

    /// <summary>Resolve a concrete liquid code to a display <see cref="ItemStack"/> (item vs block per
    /// <paramref name="type"/>), used both to confirm the liquid's identity and to name it. Returns null on a
    /// bad location or an unregistered code (the caller then degrades to the note).</summary>
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

    /// <summary>The code a counting ingredient contributes to a child Tracker: the CONCRETE resolved code for
    /// an exact (<c>{var}</c>-bound) ingredient, or the broad WILDCARD pattern for a genuine family ingredient
    /// (D6). Null when neither is available.</summary>
    private static string? IngredientCode(CraftingRecipeIngredient ingredient)
    {
        if (ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.ResolvedItemStack?.Collectible?.Code is { } concrete)
            return concrete.ToString();
        return ingredient.Code?.ToString();
    }

    /// <summary>Sentinel page code for a recipe/stack that yields no <see cref="GuiHandbookItemStackPage.PageCodeForStack"/>
    /// (null resolved output, or the primitive throwing). Two <c>"?"</c>s never match a real variant because
    /// <see cref="MatchingRecipes"/> bails when the viewed stack itself resolves to <c>"?"</c>.</summary>
    private const string UnknownPageCode = "?";

    /// <summary>Signature D3: <c>outputPageCode|ingredientPattern|WxH</c>. The pattern and dimensions are
    /// retained client-side (<see cref="GridRecipe.FreeRAMServer"/> only nulls them server-side). The output
    /// field is the attribute-qualified <see cref="OutputPageCode"/> (fix-recipe-variant-identity D1), so each
    /// metal/lining/glass variant gets a distinct signature instead of colliding on the bare code.
    ///
    /// <para>Format change is safe with no migration (D5): Crafting Tasks are unreleased, so no shipped save
    /// carries an old bare-code signature; an old-format string simply fails to re-resolve and the parent
    /// degrades to a plain output tracker (children intact).</para></summary>
    private static string SignatureOf(GridRecipe recipe)
    {
        string output = OutputPageCode(recipe);
        string pattern = recipe.IngredientPattern ?? "";
        return $"{output}|{pattern}|{recipe.Width}x{recipe.Height}";
    }

    /// <summary>The attribute-qualified Handbook page code of a grid recipe's resolved primary output
    /// (fix-recipe-variant-identity D1) — the variant-identity primitive VS uses per Handbook page. Returns
    /// <see cref="UnknownPageCode"/> when the recipe has no resolved output stack.</summary>
    private static string OutputPageCode(GridRecipe recipe)
        => recipe.Output?.ResolvedItemStack is { } outStack ? PageCodeForStack(outStack) : UnknownPageCode;

    /// <summary>Guarded wrapper over <see cref="GuiHandbookItemStackPage.PageCodeForStack"/>: the shared
    /// variant-identity key for both output matching and signatures. Returns <see cref="UnknownPageCode"/> if
    /// the primitive returns null or throws, so a degenerate stack can never masquerade as a real variant.</summary>
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
