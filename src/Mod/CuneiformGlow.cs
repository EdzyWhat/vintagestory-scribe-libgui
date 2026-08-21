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
/// Cuneiform glow seeds, selected by clay material AND drying state (add-tablet-state-glow-modifier).
///
/// The glow's correct POLARITY depends on the backdrop's luminance, so it splits by state:
///   • WET clay/wax backdrops are LIGHT-MID tones written with DARK ink, so each material uses a soft DARK
///     halo — a tight, ink-derived outline / seating shadow that separates the thin, jittered strokes from
///     the clay. (tablet-text-visibility, Option A. A light halo is wrong here: on a light-mid ground it sits
///     between the ink and a ground of nearly its own luminance, adding no separating step and bleeding into
///     the stroke edge — softening the ink instead of sharpening it.)
///   • HARD and FIRED backdrops are DARKER, where a dark halo sits dark-on-dark and reduces contrast
///     (playtest 00000016). There the classic polarity is correct: a LIGHT halo lifts the dark ink off the
///     dark ground. These two states share one light halo across all clay colors, distinct between hard/fired.
///
/// The two-pass render (all blurred halos first, then all crisp ink on top — see the render objects'
/// PaintInternal) is UNCHANGED and correct for either polarity: the crisp ink overwrites the halo inside each
/// glyph, so the halo shows only as a thin fringe where it spills onto the backdrop — dark on wet clay, light
/// on the darker set states.
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

    // Hardened + fired tablets have visibly DARKER backdrops than wet clay, so the dark seeds above sit
    // dark-on-dark and REDUCE contrast (playtest 00000016). On a dark ground the correct polarity is the
    // opposite: a LIGHT halo behind dark ink lifts the strokes off the ground (add-tablet-state-glow-modifier).
    // These two seeds are shared across all clay colors (blue/red/fire) — the darkened backdrops are close
    // enough in value that one light halo per state serves all three — and are DISTINCT between hard and fired
    // (fired is the darker, more vitrified surface, so it carries a slightly brighter/stronger halo). Same
    // tuning envelope as the wet seeds (alpha ~0.35–0.65, blur fraction 0.05–0.08).
    // PLACEHOLDER VALUES — tuned in-game via the glow dev command on real hard/fired tablets of each color,
    // then baked (add-tablet-state-glow-modifier tasks 4.1–4.2), exactly as the wet seeds were found.
    private static readonly CuneiformGlow HardHalo  = new(new Vector4(0.86f, 0.82f, 0.74f, 0.45f), 0.065f);
    private static readonly CuneiformGlow FiredHalo = new(new Vector4(0.94f, 0.90f, 0.82f, 0.50f), 0.065f);

    /// <summary>Resolve the glow for a tablet's <c>material</c> variant and drying <paramref name="state"/>.
    /// WET tablets use their per-material dark seed (a soft engraved outline over the light-mid wet clay). HARD
    /// and FIRED tablets have darker backdrops, so they use a shared LIGHT halo per state (dark ink lifted off a
    /// dark ground) — state wins over color for those two, which is why the switch reads state first. Wax has no
    /// hard/fired variant, so it only ever reaches the wet branch and keeps its own seed.</summary>
    public static CuneiformGlow For(string? material, TabletState state) => state switch
    {
        TabletState.Hard => HardHalo,
        TabletState.Fired => FiredHalo,
        _ => ForWetMaterial(material), // TabletState.Wet
    };

    /// <summary>The wet-clay dark seed for a <c>material</c> variant.
    /// <c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c> each map to their own ink-derived dark seed;
    /// any unrecognized material rides the fire seed (its backdrop twin), mirroring the theme/backdrop fallback.</summary>
    private static CuneiformGlow ForWetMaterial(string? material) => material switch
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
