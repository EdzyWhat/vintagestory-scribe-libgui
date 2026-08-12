using System;

namespace Scribe.Core;

/// <summary>
/// Maps the raw local brightness reaching the player (0 = pitch black, 1 = full noon) to the brightness the
/// Scribe GUI should render at (0..1), following the author-drawn NON-LINEAR response curve
/// (respect-local-illumination — the user's hand-plotted graph, not a linear pass-through). The curve is
/// stored as a small set of monotonic control points and evaluated by piecewise-linear interpolation, so the
/// overall response is whatever shape the points describe while each segment stays trivial to reason about
/// and unit-test.
///
/// <para><b>The shipped curve</b> (input local brightness → output GUI brightness): <c>0.00 → floor</c>,
/// <c>0.45 → 0.50</c>, <c>0.814 → 0.85</c>, <c>1.00 → 1.00</c>. Its character: near-black in true darkness, a
/// brisk ramp so even a little light already reads comfortably, then a gentle high shoulder that reaches full
/// brightness only at the very top of the light range. So for a given amount of local light the GUI is a touch
/// BRIGHTER than a straight linear mapping would give (output slightly exceeds input in the mid-range) —
/// deliberately gentler-than-punishing once any light is present, but genuinely dark with none: "you need a
/// torch, but a torch is enough." (On the author's hand-drawn axes — GUI output horizontal, local input
/// vertical — this is the pencil curve sitting just below-right of the red identity line the whole way, never
/// crossing above it.)</para>
///
/// <para><b>Why the high anchors are at 0.814 and 1.0.</b> Vintage Story maps a light-level index V (0..31) to a
/// normalized brightness through its <c>blockLightLevels</c> table (general.json), NOT V/31. The author chose
/// the shoulder against real light sources on that table: a placed/held LARGE lantern is V=20 → table value
/// <c>0.814</c>, pinned here to <c>0.85</c> output; the table reaches <c>1.0</c> at V=26, so an input of
/// <c>1.0</c> (V≥26, i.e. near a very bright source or open noon) yields full GUI brightness. Because torch/
/// lantern/oil-lamp light is nearly white (saturation ≤4/7), the sampler's computed luma ≈ the table value, so
/// these V-anchors land on the x-axis directly. A HELD light is fed through the same table
/// (<c>IWorldAccessor.BlockLightLevels[V]</c>) so a lantern in hand and a lantern on the wall hit the identical
/// curve point.</para>
///
/// <para><b>The floor</b> is the y-value of the leftmost (x=0) control point — the GUI brightness at zero
/// light — supplied per-player from <see cref="ScribePlayerSettings.IlluminationFloor"/> (default
/// <see cref="ScribePlayerSettings.DefaultIlluminationFloor"/>, which equals the drawn curve's floor so the
/// shipped default reproduces the graph exactly). Raising the floor lifts the whole curve's dark end without
/// distorting its shape (each interior point is lifted only if it would otherwise fall below the floor, which
/// keeps the result monotonic); a floor of 1.0 flattens the curve to always-full-bright (opt out of the
/// effect). Pure BCL: no Vintage Story API reference, so it lives in Core and is unit-tested there.</para>
/// </summary>
public static class ScribeBrightnessCurve
{
    /// <summary>The interior + top control points of the author-drawn curve as (localBrightness, guiBrightness)
    /// pairs, in ascending x. The x=0 point is NOT stored here — its y is the caller-supplied floor, prepended
    /// at evaluation time (<see cref="Evaluate"/>). Points are ordered and their y-values are non-decreasing,
    /// so with any floor in [0,1] the assembled curve is monotonic (see <see cref="Evaluate"/>).</summary>
    private static readonly (float X, float Y)[] Points =
    {
        (0.45f, 0.50f),
        (0.814f, 0.85f),   // V=20 (large lantern) on VS's blockLightLevels table → 85% output
        (1.00f, 1.00f),    // V≥26 (table saturates at 1.0) → full brightness
    };

    /// <summary>Evaluate the response curve at <paramref name="localBrightness"/> (clamped to 0..1) with the
    /// given <paramref name="floor"/> as the x=0 anchor (also clamped to 0..1). Returns the GUI brightness
    /// multiplier in 0..1.
    ///
    /// <para>Below the first stored point the curve interpolates from <c>(0, floor)</c> up to
    /// <c>(0.45, 0.50)</c>; between stored points it interpolates linearly; at or past the last point it holds
    /// the final value (1.0). Each stored point's y is lifted to <c>max(y, floor)</c> so a floor raised above
    /// an interior point can't create a downward kink — the assembled curve stays monotonically
    /// non-decreasing for any floor.</para></summary>
    public static float Evaluate(float localBrightness, float floor)
    {
        float x = Math.Clamp(localBrightness, 0f, 1f);
        float f = Math.Clamp(floor, 0f, 1f);

        // Segment from the floor anchor (0, f) to the first stored point.
        float prevX = 0f;
        float prevY = f;

        for (int i = 0; i < Points.Length; i++)
        {
            float px = Points[i].X;
            // Lift the point's y to the floor so a high floor never bends the curve back down.
            float py = Math.Max(Points[i].Y, f);
            if (x <= px)
            {
                float span = px - prevX;
                if (span <= 0f) return py;             // coincident x (shouldn't happen) → take the point
                float t = (x - prevX) / span;
                return prevY + (py - prevY) * t;
            }
            prevX = px;
            prevY = py;
        }

        // Past the last control point → hold the final (already floor-lifted) value.
        return prevY;
    }
}
