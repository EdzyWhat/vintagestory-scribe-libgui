using OpenTK.Mathematics;   // Vector4

namespace Scribe;

/// <summary>
/// The single <c>(material, state)</c>-keyed source of truth for how tablet cuneiform text is made
/// readable over its clay backdrop (adopt-glyph-forge-tablet-themes). One bundle carries every readability
/// dimension for one tablet view: the body <see cref="BodyInk"/> (the theme's <c>OnSurface</c>/<c>OnBackground</c>
/// ink), the <see cref="LinkInk"/> for Link/Tracker/Craft row names (the <c>ScribeRowStyle.LinkColor</c>), a
/// per-view <see cref="StrokeWeightScale"/> multiplier on the cuneiform stroke weight, and the outer
/// <see cref="Glow"/> halo (color/strength/blur/offset).
///
/// <para>This replaces four separate switches that keyed the same view differently (theme by material, link
/// by material, glow by material+state, stroke not at all) — the long-deferred "one bundle per view" refactor
/// (<c>[[tablet-styling-fragmentation-refactor]]</c>). <see cref="GuiDialogScribeTablet"/> resolves the bundle
/// ONCE per build and decomposes it into those existing seams, so a view is internally consistent by
/// construction and can never drift across the four dimensions again.</para>
///
/// <para>Values are BAKED from the glyph-forge tool's 10 readability exports (the authoring record, not a
/// shipped asset), in the manner of the jitter/reveal constants. They are NOT a persisted user setting and no
/// JSON is loaded at runtime. <c>src/Core/</c> is untouched: the stroke-weight scale is applied Mod-side at
/// paint time, so Core keeps emitting its base stroke weights.</para>
/// </summary>
internal readonly record struct TabletReadability(
    Vector4 BodyInk, Vector4 LinkInk, float StrokeWeightScale, CuneiformGlow Glow)
{
    // ---------------------------------------------------------------------------------------------------
    // The 10 baked view bundles (glyph-forge exports). RGB channels are 0–1; the glow's alpha channel is the
    // glow STRENGTH and its blur/offset are fractions of the em.
    //
    // IMPORTANT — each cell is authored INDEPENDENTLY. The table permits any (material, state) cell to differ
    // from any other, including two views in the same state. This is DATA, NOT A RULE: the 2026-08-21 retune
    // proved it in practice — the three wet clays, which once shared one near-black seating halo, now each
    // carry their OWN tinted glow, and the stroke-weight scale, which once ran wet 0.9 / hard 1.0 / fired 1.1,
    // now runs wet 1.2 / hard 1.0 / fired 0.95 (wet heaviest, fired lightest) — all without any code or
    // structure change, because nothing here factors "wet" or "state" into a shared constant that would couple
    // the cells. The only surviving coincidences are the hard/fired glow offsets (~0.04–0.05) and offset
    // X == offset Y in every export; wet + wax offsets stay near-zero. Do not "simplify" any apparent overlap
    // into a shared field — the next retune may diverge it again.
    //
    // A second, PARTIAL glyph-forge retune (same day, 2026-08-21, ~12:15–12:18) re-exported only 5 of the 10
    // views — BlueWet, BlueHard, RedWet, FireWet, WaxWet — with new ink/link/glow values (wax's ink/link held,
    // only its glow strength/blur moved). BlueFired/RedHard/RedFired/FireHard/FireFired were NOT re-exported and
    // still carry the first retune's values. Re-verify the 5 changed views in-game (§9); the other 5 are
    // unaffected.
    // ---------------------------------------------------------------------------------------------------

    private static readonly TabletReadability BlueWet = new(
        BodyInk: new Vector4(0.176f, 0.180f, 0.184f, 1f),
        LinkInk: new Vector4(0.176f, 0.278f, 0.345f, 1f),
        StrokeWeightScale: 1.2f,
        Glow: new CuneiformGlow(new Vector4(0.247f, 0.278f, 0.314f, 0.82f), 0.225f, 0f, 0f));

    private static readonly TabletReadability BlueHard = new(
        BodyInk: new Vector4(0.192f, 0.200f, 0.208f, 1f),
        LinkInk: new Vector4(0.129f, 0.271f, 0.373f, 1f),
        StrokeWeightScale: 1.0f,
        Glow: new CuneiformGlow(new Vector4(0.831f, 0.851f, 0.871f, 0.92f), 0.125f, 0.05f, 0.05f));

    private static readonly TabletReadability BlueFired = new(
        BodyInk: new Vector4(0.133f, 0.141f, 0.149f, 1f),
        LinkInk: new Vector4(0.063f, 0.247f, 0.376f, 1f),
        StrokeWeightScale: 0.95f,
        Glow: new CuneiformGlow(new Vector4(0.780f, 0.808f, 0.839f, 0.58f), 0.115f, 0.05f, 0.05f));

    private static readonly TabletReadability RedWet = new(
        BodyInk: new Vector4(0.149f, 0.133f, 0.129f, 1f),
        LinkInk: new Vector4(0.439f, 0.200f, 0.200f, 1f),
        StrokeWeightScale: 1.2f,
        Glow: new CuneiformGlow(new Vector4(0.373f, 0.243f, 0.208f, 0.86f), 0.265f, 0f, 0f));

    private static readonly TabletReadability RedHard = new(
        BodyInk: new Vector4(0.173f, 0.129f, 0.110f, 1f),
        LinkInk: new Vector4(0.455f, 0.102f, 0.102f, 1f),
        StrokeWeightScale: 1.0f,
        Glow: new CuneiformGlow(new Vector4(0.839f, 0.780f, 0.780f, 1.00f), 0.145f, 0.05f, 0.05f));

    private static readonly TabletReadability RedFired = new(
        BodyInk: new Vector4(0.192f, 0.125f, 0.086f, 1f),
        LinkInk: new Vector4(0.376f, 0.063f, 0.063f, 1f),
        StrokeWeightScale: 0.95f,
        Glow: new CuneiformGlow(new Vector4(0.839f, 0.780f, 0.780f, 0.44f), 0.110f, 0.04f, 0.04f));

    private static readonly TabletReadability FireWet = new(
        BodyInk: new Vector4(0.176f, 0.145f, 0.082f, 1f),
        LinkInk: new Vector4(0.471f, 0.282f, 0.071f, 1f),
        StrokeWeightScale: 1.2f,
        Glow: new CuneiformGlow(new Vector4(0.294f, 0.243f, 0.165f, 0.92f), 0.235f, 0f, 0f));

    private static readonly TabletReadability FireHard = new(
        BodyInk: new Vector4(0.173f, 0.129f, 0.110f, 1f),
        LinkInk: new Vector4(0.455f, 0.275f, 0.102f, 1f),
        StrokeWeightScale: 1.0f,
        Glow: new CuneiformGlow(new Vector4(0.839f, 0.816f, 0.780f, 1.00f), 0.145f, 0.05f, 0.05f));

    private static readonly TabletReadability FireFired = new(
        BodyInk: new Vector4(0.133f, 0.067f, 0.027f, 1f),
        LinkInk: new Vector4(0.318f, 0.200f, 0.024f, 1f),
        StrokeWeightScale: 0.95f,
        Glow: new CuneiformGlow(new Vector4(0.804f, 0.725f, 0.659f, 0.70f), 0.110f, 0.04f, 0.04f));

    // Wax has only a wet life-cycle state (no hardened/fired), so it authors just this one bundle; the state
    // arm below always resolves wax to it regardless of the requested state.
    private static readonly TabletReadability WaxWet = new(
        BodyInk: new Vector4(0.408f, 0.322f, 0.231f, 1f),
        LinkInk: new Vector4(0.604f, 0.455f, 0.137f, 1f),
        StrokeWeightScale: 1.0f,
        Glow: new CuneiformGlow(new Vector4(0.965f, 0.949f, 0.898f, 0.28f), 0.215f, 0.01f, 0.01f));

    /// <summary>Resolve the readability bundle for a tablet's <paramref name="material"/> variant and drying
    /// <paramref name="state"/>. <c>clay-blue</c>/<c>clay-red</c>/<c>clay-fire</c> each have their own wet/hard/
    /// fired bundles; <c>wax</c> resolves to its single wet bundle for any state (it has no hardened/fired
    /// form); any unrecognized material rides the <c>clay-fire</c> bundle for the SAME state (mirroring the
    /// theme/backdrop fallback, so theme, backdrop, and readability always agree).</summary>
    public static TabletReadability For(string? material, TabletState state) => material switch
    {
        "clay-blue" => state switch { TabletState.Hard => BlueHard, TabletState.Fired => BlueFired, _ => BlueWet },
        "clay-red"  => state switch { TabletState.Hard => RedHard,  TabletState.Fired => RedFired,  _ => RedWet },
        "wax"       => WaxWet, // wax has only a wet state
        _           => state switch { TabletState.Hard => FireHard, TabletState.Fired => FireFired, _ => FireWet },
    };
}
