using System;

namespace Scribe.Core;

/// <summary>
/// A single ingredient a Craft task generates a subtask for, in Core-neutral terms: the item code to count
/// and how many ONE craft consumes. The Mod layer derives these from the live grid recipe (resolving
/// <c>{var}</c> bindings to concrete codes, leaving genuine wildcards broad). Callers pass the per-craft
/// amount; <see cref="ScribeDocument.ReconcileCraftIngredients"/> applies batch scaling via
/// <see cref="ScribeCraftMath"/>.
///
/// <para><b>Liquid ingredients</b> are counted in litres, not stacks: they set <paramref name="IsLiquid"/>
/// and carry <paramref name="LitresPerCraft"/>; <paramref name="PerCraftQuantity"/> is a nominal <c>1</c>
/// and unused (see <see cref="ScribeCraftMath.LitreTarget"/>).</para>
/// </summary>
/// <param name="ItemCode">Plain item code the Tracker subtask counts (e.g. <c>"game:log-birch"</c> or a
/// wildcard like <c>"game:linen-*"</c>); for a liquid, its own item/block code.</param>
/// <param name="PerCraftQuantity">How many of <paramref name="ItemCode"/> one craft consumes (≥ 1).</param>
/// <param name="IsLiquid">True for a containerized liquid counted in litres.</param>
/// <param name="LitresPerCraft">For a liquid, the litres one craft consumes (the recipe's
/// <c>requiresLitres</c>); unused for a solid.</param>
public readonly record struct ScribeCraftIngredient(
    string ItemCode, int PerCraftQuantity, bool IsLiquid = false, double LitresPerCraft = 0);

/// <summary>
/// Pure batch arithmetic for Craft tasks (no VS API, unit-testable). The crafts needed for a target is the
/// ceiling of target ÷ output-per-craft, and each ingredient's subtask target is
/// <c>perCraftQuantity × craftsNeeded</c>.
/// </summary>
public static class ScribeCraftMath
{
    /// <summary>
    /// <c>ceil(targetQuantity ÷ outputPerCraft)</c>, with both inputs clamped to ≥ 1 so the result is ≥ 1.
    /// Integer-only to keep the ceiling exact.
    /// </summary>
    public static int CraftsNeeded(int targetQuantity, int outputPerCraft)
    {
        int target = targetQuantity < 1 ? 1 : targetQuantity;
        int per = outputPerCraft < 1 ? 1 : outputPerCraft;
        // Ceiling division: (a + b - 1) / b.
        return (target + per - 1) / per;
    }

    /// <summary>
    /// The integer litre target for a liquid ingredient: <c>ceil(litresPerCraft × craftsNeeded)</c>, min 1.
    /// The ceiling applies to the BATCH TOTAL, never per craft (0.1 L × 10 crafts is 1 L, not 10). Degenerate
    /// inputs clamp to 1 so a Tracker never gets a zero target.
    /// </summary>
    public static int LitreTarget(double litresPerCraft, int craftsNeeded)
    {
        if (litresPerCraft <= 0 || craftsNeeded < 1) return 1;
        int ceil = (int)Math.Ceiling(litresPerCraft * craftsNeeded);
        return ceil < 1 ? 1 : ceil;
    }
}
