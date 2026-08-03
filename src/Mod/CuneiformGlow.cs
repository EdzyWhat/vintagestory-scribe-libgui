// Per-stroke outer glow for cuneiform ink (add-tablet-clay-type-themes, cuneiform-contrast-glow). Cuneiform
// strokes are painted over a mid-tone textured clay backdrop (sampled centers ≈ #aa6f6d / #98a6af / #ccaf89),
// where dark ink can read weakly. A soft halo BEHIND the ink lifts it off the clay. It renders in two passes
// (all blurred halos first, then all crisp ink fills on top — see the render objects' PaintInternal), so
// overlapping strokes within a glyph never halo over each other; the glow shows only where it extends past
// the ink onto the backdrop.
//
// The design (D3/D4) calls for LibGUI's cached blur primitive (PaintingContext.GetOrCreateBlurMask) — but
// that method is `internal` to the Gui assembly (only Gui.Tests has InternalsVisibleTo), so the Mod cannot
// call it. This file reproduces the exact primitive (SKMaskFilter.CreateBlur(Normal, sigma)) in a small
// Mod-owned cache that follows the SAME discipline the design mandates: cache by sigma, NEVER dispose
// per-frame (disposing an SKMaskFilter mid-record corrupts the SKPictureRecorder canvas). This is the one
// deviation from the letter of D4 forced by the accessibility boundary; the behavior is identical.

using System.Collections.Generic;
using OpenTK.Mathematics;   // Vector4
using SkiaSharp;            // SKMaskFilter, SKBlurStyle

namespace Scribe;

/// <summary>
/// The resolved outer-glow parameters for one cuneiform surface: a halo <see cref="Color"/> (RGB + alpha,
/// where alpha is the glow STRENGTH) and a <see cref="BlurFraction"/> of the em size that becomes the blur
/// sigma at paint time. Expressed as a FRACTION of the font size (not a fixed pixel sigma) so the title and
/// the smaller task rows glow proportionally at any GUI scale. <see cref="Enabled"/> gates the whole effect,
/// so the default value (all-zero) is a safe "no glow" — every non-tablet cuneiform surface passes it and
/// pays nothing.
/// </summary>
public readonly record struct CuneiformGlow(Vector4 Color, float BlurFraction)
{
    /// <summary>True when the glow should be painted: a visible halo (alpha &gt; 0) and a non-zero blur.
    /// The all-zero default is disabled, so a surface that never sets a glow renders exactly as before.</summary>
    public bool Enabled => Color.W > 0f && BlurFraction > 0f;
}

/// <summary>
/// Per-material cuneiform glow seeds. All three clay backdrops are mid-tone with DARK ink, so every seed is
/// a LIGHT halo (a light halo lifts dark ink; a dark halo would be for light ink on a dark clay). The values
/// were tuned in-game via a since-removed <c>.cuneiformglow</c> dev command and baked here (tasks 6.4 / 6.7).
/// </summary>
internal static class CuneiformGlowTable
{
    // Baked per-material seeds (halo RGB, strength alpha, blur-as-fraction-of-em). Tuned in-game, then baked.
    // 2026-08-03 playtest (add-tablet-clay-type-themes 8.3): softer + wider — strength 0.55 → 0.30, blur
    // fraction 0.09 → 0.117 (~+30% radius) — so the lift reads as a diffuse aura, not a bright rim.
    private static readonly CuneiformGlow FireDefault = new(new Vector4(0.98f, 0.94f, 0.85f, 0.30f), 0.117f);
    private static readonly CuneiformGlow RedDefault  = new(new Vector4(0.98f, 0.92f, 0.88f, 0.30f), 0.117f);
    private static readonly CuneiformGlow BlueDefault = new(new Vector4(0.95f, 0.97f, 0.99f, 0.30f), 0.117f);

    /// <summary>Resolve the glow for a tablet's <c>material</c> variant.
    /// <c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c> map to their seed; <c>wax</c> and any unrecognized
    /// material ride the fire seed (its backdrop twin), mirroring the theme/backdrop fallback.</summary>
    public static CuneiformGlow For(string? material) => material switch
    {
        "clay-blue" => BlueDefault,
        "clay-red" => RedDefault,
        _ => FireDefault, // clay-fire, wax, unknown
    };
}

/// <summary>
/// Mod-owned cache of blur mask filters keyed by sigma — the primitive LibGUI keeps `internal`
/// (<c>PaintingContext.GetOrCreateBlurMask</c>), reproduced here so the cuneiform render objects can share
/// one soft-glow mask across strokes and frames. Filters are cached and NEVER disposed per-frame (a live
/// SKMaskFilter must outlast the SKPictureRecorder that references it), matching the discipline in the LibGUI
/// original and design D4.
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

    // Round to 2 decimals without pulling in System.MathF at the call site clutter; keeps the cache small.
    private static float MathHelperRound(float v) => System.MathF.Round(v, 2);
}
