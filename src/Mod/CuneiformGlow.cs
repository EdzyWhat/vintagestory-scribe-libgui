// Per-stroke outer glow for cuneiform ink. Strokes are painted over a mid-tone clay backdrop where dark
// ink can read weakly; a soft halo BEHIND the ink lifts it off the clay. It renders in two passes (all
// blurred halos, then all crisp ink fills; see the render objects' PaintInternal), so overlapping strokes
// never halo over each other.
//
// LibGUI's cached blur primitive (PaintingContext.GetOrCreateBlurMask) is `internal` to the Gui assembly,
// so this file reproduces it (SKMaskFilter.CreateBlur(Normal, sigma)) in a Mod-owned cache by sigma.
// NEVER dispose per-frame: disposing an SKMaskFilter mid-record corrupts the SKPictureRecorder canvas.

using System.Collections.Generic;
using OpenTK.Mathematics;   // Vector4
using SkiaSharp;            // SKMaskFilter, SKBlurStyle

namespace Scribe;

/// <summary>
/// Resolved outer-glow parameters for one cuneiform surface: a halo <see cref="Color"/> (alpha is the glow
/// STRENGTH), a <see cref="BlurFraction"/> of the em size that becomes the blur sigma, and an optional
/// <see cref="OffsetXFraction"/>/<see cref="OffsetYFraction"/> (also em fractions) shifting the halo pass so
/// it reads as a seated drop rather than a symmetric aura. Fractions (not pixels) keep title and task rows
/// proportional at any GUI scale. The all-zero default is "no glow" (see <see cref="Enabled"/>).
/// </summary>
public readonly record struct CuneiformGlow(
    Vector4 Color, float BlurFraction, float OffsetXFraction = 0f, float OffsetYFraction = 0f)
{
    /// <summary>True when the glow should be painted: a visible halo (alpha &gt; 0) and a non-zero blur.
    /// The offset does not gate the effect.</summary>
    public bool Enabled => Color.W > 0f && BlurFraction > 0f;
}

/// <summary>
/// The cuneiform glow lookup, a thin adapter over the <see cref="TabletReadability"/> bundle, whose
/// per-<c>(material, state)</c> cells hold the glow alongside ink/link/stroke values. <see cref="For"/> is
/// the entry point for the three cuneiform call sites.
///
/// Glow polarity tracks backdrop luminance (an authoring property of each bundle cell): wet clay is
/// light-mid with dark ink, so a dark seating halo; the darker hard/fired backdrops use a light halo. The
/// two-pass render works for either polarity: crisp ink overwrites the halo inside each glyph, leaving only
/// a fringe on the backdrop.
/// </summary>
internal static class CuneiformGlowTable
{
    /// <summary>Resolves the glow for a tablet's <paramref name="material"/> and drying
    /// <paramref name="state"/> from its <see cref="TabletReadability"/> bundle.</summary>
    public static CuneiformGlow For(string? material, TabletState state) =>
        TabletReadability.For(material, state).Glow;
}

/// <summary>
/// Mod-owned cache of blur mask filters keyed by sigma, reproducing the primitive LibGUI keeps `internal`
/// (<c>PaintingContext.GetOrCreateBlurMask</c>). Filters are NEVER disposed per-frame: a live SKMaskFilter
/// must outlast the SKPictureRecorder that references it.
/// </summary>
internal static class CuneiformGlowMask
{
    private static readonly Dictionary<float, SKMaskFilter> Cache = new();

    /// <summary>A cached normal blur mask for <paramref name="sigma"/> (rounded to 0.01 to bound the cache).
    /// Callers must NOT dispose the returned filter. Returns null for a non-positive sigma (no blur).</summary>
    public static SKMaskFilter? ForSigma(float sigma)
    {
        if (sigma <= 0f) return null;
        float key = MathHelperRound(sigma);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, key);
        Cache[key] = filter;
        return filter;
    }

    // Round to 2 decimals to keep the cache small.
    private static float MathHelperRound(float v) => System.MathF.Round(v, 2);
}
