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
/// where alpha is the glow STRENGTH), a <see cref="BlurFraction"/> of the em size that becomes the blur
/// sigma at paint time, and an optional directional <see cref="OffsetXFraction"/>/<see cref="OffsetYFraction"/>
/// (also a fraction of the em) that shifts the blurred halo pass so the glow reads as a SEATED DROP rather than
/// a symmetric aura. All three are expressed as a FRACTION of the font size (not fixed pixel values) so the
/// title and the smaller task rows glow proportionally at any GUI scale. <see cref="Enabled"/> gates the whole
/// effect, so the default value (all-zero) is a safe "no glow" — every non-tablet cuneiform surface passes it
/// and pays nothing. The default offsets (0,0) reproduce a centered halo, keeping every existing caller
/// (which never set an offset) pixel-identical.
/// </summary>
public readonly record struct CuneiformGlow(
    Vector4 Color, float BlurFraction, float OffsetXFraction = 0f, float OffsetYFraction = 0f)
{
    /// <summary>True when the glow should be painted: a visible halo (alpha &gt; 0) and a non-zero blur.
    /// The all-zero default is disabled, so a surface that never sets a glow renders exactly as before.
    /// The offset does not gate the effect — a zero offset is simply a centered halo.</summary>
    public bool Enabled => Color.W > 0f && BlurFraction > 0f;
}

/// <summary>
/// The cuneiform glow lookup, now a thin adapter over the <see cref="TabletReadability"/> bundle
/// (adopt-glyph-forge-tablet-themes). The per-<c>(material, state)</c> glow values used to live here as seeds
/// (per-material dark wet halos + two shared light hard/fired halos); they now live in the bundle table
/// alongside the ink/link/stroke values for the same view, so a view can't drift across those dimensions.
/// This class survives only as the <see cref="For"/> entry point the three cuneiform call sites already use.
///
/// The glow's correct POLARITY still tracks the backdrop's luminance — WET clay backdrops are light-mid tones
/// written with dark ink (a soft DARK seating halo separates the strokes), while the darker HARD/FIRED
/// backdrops use a LIGHT halo (lifting dark ink off a dark ground) — but that is now an authoring property of
/// each bundle cell, not a rule encoded here. The two-pass render (all blurred halos first, then all crisp ink
/// on top — see the render objects' PaintInternal) is UNCHANGED and correct for either polarity: the crisp ink
/// overwrites the halo inside each glyph, so the halo shows only as a thin fringe where it spills onto the
/// backdrop — dark on wet clay, light on the darker set states.
/// </summary>
internal static class CuneiformGlowTable
{
    /// <summary>Resolve the glow for a tablet's <paramref name="material"/> variant and drying
    /// <paramref name="state"/> by reading it from that view's <see cref="TabletReadability"/> bundle. Kept as a
    /// distinct method so the existing cuneiform call sites (row glow, resting title, editing title) are
    /// untouched; it now simply forwards to the single source of truth.</summary>
    public static CuneiformGlow For(string? material, TabletState state) =>
        TabletReadability.For(material, state).Glow;
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
