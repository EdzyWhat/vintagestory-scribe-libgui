using System;

namespace Scribe.Core;

/// <summary>
/// Maps the raw local brightness reaching the player (0 = pitch black, 1 = full noon) to the brightness the
/// Scribe GUI renders at (0..1) via a NON-LINEAR piecewise-linear curve of monotonic control points.
///
/// <para><b>Shipped curve</b> (input to output): <c>0.00 → floor</c>, <c>0.45 → 0.53</c>, <c>0.90 → 1.00</c>,
/// <c>1.00 → 1.00</c>: dim but visible in darkness, a brisk mid ramp, full brightness at a bright-but-not-noon
/// source (the 0.90 knee sits between a large lantern and a very bright source), flat above.</para>
///
/// <para><b>Light-level mapping.</b> Vintage Story maps light-level index V (0..31) to brightness via its
/// <c>blockLightLevels</c> table (general.json), NOT V/31; the table reaches <c>1.0</c> at V=26. V=20 (large
/// lantern) ≈ 0.814; V=15 (torch nearby) ≈ 0.60. A held light goes through the same table so hand and wall
/// sources hit identical curve points.</para>
///
/// <para><b>The floor</b> is the y of the x=0 point, supplied per-player from
/// <see cref="ScribePlayerSettings.IlluminationFloor"/> (default
/// <see cref="ScribePlayerSettings.DefaultIlluminationFloor"/>, equal to the drawn curve's floor). Raising it
/// lifts the dark end without distorting the shape (interior points are lifted only if they would fall below
/// it, keeping the curve monotonic); 1.0 flattens the curve to always-full-bright. Pure BCL.</para>
/// </summary>
public static class ScribeBrightnessCurve
{
    /// <summary>The interior and top control points as (localBrightness, guiBrightness) in ascending x. The
    /// x=0 point is not stored: its y is the caller's floor, prepended in <see cref="Evaluate"/>. Y values are
    /// non-decreasing, so any floor in [0,1] yields a monotonic curve.</summary>
    private static readonly (float X, float Y)[] Points =
    {
        (0.45f, 0.53f),
        (0.90f, 1.00f),
        (1.00f, 1.00f),
    };

    /// <summary>Evaluates the curve at <paramref name="localBrightness"/> (clamped to 0..1) with
    /// <paramref name="floor"/> (clamped to 0..1) as the x=0 anchor. Returns the GUI brightness multiplier in
    /// 0..1. Interpolates linearly between points and holds the final value past the last. Each stored y is
    /// lifted to <c>max(y, floor)</c> so a high floor can't create a downward kink.</summary>
    public static float Evaluate(float localBrightness, float floor)
    {
        float x = Math.Clamp(localBrightness, 0f, 1f);
        float f = Math.Clamp(floor, 0f, 1f);

        // Segment starts at the floor anchor (0, f).
        float prevX = 0f;
        float prevY = f;

        for (int i = 0; i < Points.Length; i++)
        {
            float px = Points[i].X;
            // Lift to the floor so a high floor never bends the curve back down.
            float py = Math.Max(Points[i].Y, f);
            if (x <= px)
            {
                float span = px - prevX;
                if (span <= 0f) return py;             // coincident x: take the point
                float t = (x - prevX) / span;
                return prevY + (py - prevY) * t;
            }
            prevX = px;
            prevY = py;
        }

        // Past the last control point: hold the final value.
        return prevY;
    }
}
