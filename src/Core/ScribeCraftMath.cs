namespace Scribe.Core;

/// <summary>
/// A single ingredient a Craft task should generate a subtask for, in Core-neutral terms: the item code
/// to count and how many of it ONE craft of the recipe consumes. The Mod layer derives these from the live
/// grid recipe (resolving <c>{var}</c> bindings to concrete codes, leaving genuine wildcards broad — see the
/// craft-task design), keeping this model free of any Vintage Story API type. Batch scaling
/// (<c>PerCraftQuantity × craftsNeeded</c>) is applied by <see cref="ScribeDocument.ReconcileCraftIngredients"/>
/// via <see cref="ScribeCraftMath"/>, so callers pass the per-craft amount, not the total.
/// </summary>
/// <param name="ItemCode">The plain item code the generated Tracker subtask counts (e.g.
/// <c>"game:log-birch"</c> or a family wildcard like <c>"game:linen-*"</c>).</param>
/// <param name="PerCraftQuantity">How many of <paramref name="ItemCode"/> one craft consumes (≥ 1).</param>
public readonly record struct ScribeCraftIngredient(string ItemCode, int PerCraftQuantity);

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
}
