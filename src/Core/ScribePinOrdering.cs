namespace Scribe.Core;

/// <summary>
/// The HUD's display order for a player's pins: pin order, with completed tasks sunk below the
/// not-completed ones. Pure and game-agnostic so it can be unit-tested and shared by any surface
/// that renders the pin set. The Mod layer decides <em>when</em> a just-completed row sinks (a
/// brief undo window lives in the HUD); this only defines the resting order.
/// </summary>
public static class ScribePinOrdering
{
    /// <summary>
    /// Returns <paramref name="pins"/> reordered for display: all not-done pins first, then all
    /// done pins, each group keeping the input's relative order. The input is not mutated.
    /// </summary>
    /// <remarks>
    /// The sort is stable (a partition, really), so within each group the caller's pin order is
    /// preserved — matching "pin order, completed at the bottom." Done-ness is read from
    /// <see cref="ScribePinnedRef.LastKnownDone"/>, the same snapshot the HUD renders, so the order
    /// stays consistent with what the player sees even when a task's source is unloaded.
    /// </remarks>
    public static IReadOnlyList<ScribePinnedRef> ForDisplay(IReadOnlyList<ScribePinnedRef> pins)
    {
        ArgumentNullException.ThrowIfNull(pins);

        var ordered = new List<ScribePinnedRef>(pins.Count);
        // Not-done first, preserving order.
        foreach (var pin in pins)
        {
            if (!pin.LastKnownDone) ordered.Add(pin);
        }
        // Then done, preserving order.
        foreach (var pin in pins)
        {
            if (pin.LastKnownDone) ordered.Add(pin);
        }
        return ordered;
    }
}
