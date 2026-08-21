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
/// Per-material cuneiform glow seeds. Every clay/wax backdrop is a LIGHT-MID tone written with DARK ink, so
/// each seed is a soft DARK halo — a tight, ink-derived outline / seating shadow that deepens the immediate
/// stroke surround and separates the thin, jittered strokes from the clay. (tablet-text-visibility, Option A.)
///
/// This CORRECTS the previous light-halo polarity, which was inverted for our case: a light halo lifts dark
/// ink only on a DARK ground; on a light-mid ground it sits between the ink and a ground of nearly its own
/// luminance, adding no separating step and bleeding into the stroke edge — softening the ink instead of
/// sharpening it. The old "a light halo lifts dark ink" comment described that opposite case. The two-pass
/// render (all blurred halos first, then all crisp ink on top — see the render objects' PaintInternal) is
/// UNCHANGED and still correct here: the crisp ink overwrites the halo inside each glyph, so a dark halo
/// shows only as a thin darkened fringe where it spills onto the clay — exactly the desired outline.
/// </summary>
internal static class CuneiformGlowTable
{
    // Per-material dark seeds (halo RGB derived from each palette's own ink, strength alpha, blur-as-fraction-
    // of-em). tablet-text-visibility: light → dark polarity flip, tighter blur (0.117 → 0.060) so the halo
    // reads as a soft engraved outline rather than a diffuse aura, and wax gets its OWN seed (below) instead of
    // riding the fire twin. The initial alpha 0.55 read as GRIME over the clay in the first in-game pass
    // (submission 2026-08-20T16-47-03), so it was dropped to 0.40 per the tuning guidance below — a fainter
    // halo that seats the stroke without dirtying the ground. Tuning guidance: keep alpha in ~0.35–0.65 (drop
    // toward 0.35 if it still reads as grime, raise toward 0.55–0.65 only if strokes smear back into the clay)
    // and the blur fraction in 0.05–0.08 (a soft outline, not an aura).
    private static readonly CuneiformGlow FireDefault = new(new Vector4(0.20f, 0.10f, 0.05f, 0.40f), 0.060f);
    private static readonly CuneiformGlow RedDefault  = new(new Vector4(0.24f, 0.10f, 0.09f, 0.40f), 0.060f);
    private static readonly CuneiformGlow BlueDefault = new(new Vector4(0.12f, 0.16f, 0.20f, 0.40f), 0.060f);
    private static readonly CuneiformGlow WaxDefault  = new(new Vector4(0.28f, 0.22f, 0.12f, 0.40f), 0.060f);

    /// <summary>Resolve the glow for a tablet's <c>material</c> variant.
    /// <c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c> each map to their own ink-derived dark seed;
    /// any unrecognized material rides the fire seed (its backdrop twin), mirroring the theme/backdrop fallback.</summary>
    public static CuneiformGlow For(string? material) => material switch
    {
        "clay-blue" => BlueDefault,
        "clay-red" => RedDefault,
        "wax" => WaxDefault,
        _ => FireDefault, // clay-fire, unknown
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
