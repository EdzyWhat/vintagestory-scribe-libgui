using System;

namespace Scribe.Core;

/// <summary>
/// A single ingredient a Craft task should generate a subtask for, in Core-neutral terms: the item code
/// to count and how many of it ONE craft of the recipe consumes. The Mod layer derives these from the live
/// grid recipe (resolving <c>{var}</c> bindings to concrete codes, leaving genuine wildcards broad — see the
/// craft-task design), keeping this model free of any Vintage Story API type. Batch scaling
/// (<c>PerCraftQuantity × craftsNeeded</c>) is applied by <see cref="ScribeDocument.ReconcileCraftIngredients"/>
/// via <see cref="ScribeCraftMath"/>, so callers pass the per-craft amount, not the total.
///
/// <para><b>Liquid ingredients</b> (add-liquid-ingredient-tracker): a containerized liquid a recipe requires
/// (the dye an ink-and-quill needs) is counted in <b>litres</b>, not stacks. Such an ingredient sets
/// <paramref name="IsLiquid"/> and carries its per-craft litre amount in <paramref name="LitresPerCraft"/>;
/// its <paramref name="PerCraftQuantity"/> is a nominal <c>1</c> and is NOT used for the target (the litre
/// path bypasses the integer per-craft multiply — see <see cref="ScribeCraftMath.LitreTarget"/>). Both fields
/// are defaulted, so every existing solid-ingredient construction is unchanged.</para>
/// </summary>
/// <param name="ItemCode">The plain item code the generated Tracker subtask counts (e.g.
/// <c>"game:log-birch"</c> or a family wildcard like <c>"game:linen-*"</c>). For a liquid, the liquid's own
/// item/block code (e.g. <c>"game:blackdye"</c>).</param>
/// <param name="PerCraftQuantity">How many of <paramref name="ItemCode"/> one craft consumes (≥ 1). Ignored
/// for a liquid ingredient, whose per-craft amount is <paramref name="LitresPerCraft"/>.</param>
/// <param name="IsLiquid">True when this ingredient is a containerized liquid counted in litres rather than a
/// solid counted by stack size.</param>
/// <param name="LitresPerCraft">For a liquid ingredient, the litres one craft consumes (a float from the
/// recipe's <c>requiresLitres</c>); zero/unused for a solid.</param>
public readonly record struct ScribeCraftIngredient(
    string ItemCode, int PerCraftQuantity, bool IsLiquid = false, double LitresPerCraft = 0);

/// <summary>
/// Pure batch arithmetic for Craft tasks, kept in Core (no VS API) so it is unit-testable without a game
/// install. A Craft task targets <c>TargetQuantity</c> of an output whose recipe yields
/// <c>outputPerCraft</c> per craft; the number of crafts the player must perform is the ceiling of that
/// ratio, and each ingredient's subtask target is <c>perCraftQuantity × craftsNeeded</c>.
/// </summary>
public static class ScribeCraftMath
{
    /// <summary>
    /// The number of crafts needed to reach <paramref name="targetQuantity"/> outputs when each craft yields
    /// <paramref name="outputPerCraft"/> — <c>ceil(targetQuantity ÷ outputPerCraft)</c>. Both inputs are
    /// treated as ≥ 1 (a non-positive value clamps to 1) so the result is always ≥ 1; integer-only, no
    /// floating point, to keep the ceiling exact.
    /// </summary>
    public static int CraftsNeeded(int targetQuantity, int outputPerCraft)
    {
        int target = targetQuantity < 1 ? 1 : targetQuantity;
        int per = outputPerCraft < 1 ? 1 : outputPerCraft;
        // Ceiling division without floating point: (a + b - 1) / b.
        return (target + per - 1) / per;
    }

    /// <summary>
    /// The integer litre target for a liquid ingredient subtask (add-liquid-ingredient-tracker D3):
    /// <c>ceil(litresPerCraft × craftsNeeded)</c>, clamped to ≥ 1. Ceiling is applied to the BATCH TOTAL,
    /// never per craft — a 0.1 L × 10-craft recipe is <c>ceil(1.0) = 1</c> L, not <c>ceil(0.1) × 10 = 10</c>.
    /// Non-positive inputs (zero/negative litres or crafts) clamp to a target of <c>1</c>, so a degenerate
    /// recipe never yields a zero-target Tracker. Pure BCL (no VS API) so it stays unit-testable in Core.
    /// </summary>
    public static int LitreTarget(double litresPerCraft, int craftsNeeded)
    {
        if (litresPerCraft <= 0 || craftsNeeded < 1) return 1;
        int ceil = (int)Math.Ceiling(litresPerCraft * craftsNeeded);
        return ceil < 1 ? 1 : ceil;
    }
}
