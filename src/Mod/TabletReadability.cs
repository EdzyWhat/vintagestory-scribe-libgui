using OpenTK.Mathematics;   // Vector4

namespace Scribe;

/// <summary>
/// The <c>(material, state)</c>-keyed source of truth for tablet cuneiform readability over its clay
/// backdrop. One bundle per view: the body <see cref="BodyInk"/> (theme <c>OnSurface</c>/<c>OnBackground</c>),
/// the <see cref="LinkInk"/> for Link/Tracker/Craft row names (<c>ScribeRowStyle.LinkColor</c>), a
/// <see cref="StrokeWeightScale"/> multiplier on cuneiform stroke weight, and the outer <see cref="Glow"/> halo.
///
/// <para><see cref="GuiDialogScribeTablet"/> resolves the bundle once per build and decomposes it into the
/// existing seams, so a view stays internally consistent.</para>
///
/// <para>Values are baked constants from glyph-forge readability exports, not a user setting or runtime
/// JSON. The stroke-weight scale is applied Mod-side at paint time; Core keeps emitting base weights.</para>
/// </summary>
internal readonly record struct TabletReadability(
    Vector4 BodyInk, Vector4 LinkInk, float StrokeWeightScale, CuneiformGlow Glow)
{
    // The 10 baked view bundles. RGB channels are 0–1; the glow's alpha is its STRENGTH and its blur/offset
    // are fractions of the em.
    //
    // Each cell is authored INDEPENDENTLY: this is data, not a rule. Do not "simplify" apparent overlap
    // (glow tints, stroke scale wet 1.2 / hard 1.0 / fired 0.95, offset X == Y) into shared constants; a
    // retune may diverge them.
    //
    // Provenance: BlueFired, RedHard, RedFired, FireHard and FireFired hold values from an earlier glyph-forge
    // export than the other five cells; those five (from the later, partial export) still need in-game verification.

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

    // Wax has only a wet state, so it has just this bundle.
    private static readonly TabletReadability WaxWet = new(
        BodyInk: new Vector4(0.408f, 0.322f, 0.231f, 1f),
        LinkInk: new Vector4(0.604f, 0.455f, 0.137f, 1f),
        StrokeWeightScale: 1.0f,
        Glow: new CuneiformGlow(new Vector4(0.965f, 0.949f, 0.898f, 0.28f), 0.215f, 0.01f, 0.01f));

    /// <summary>Resolves the bundle for a tablet's <paramref name="material"/> and <paramref name="state"/>.
    /// <c>wax</c> always resolves to its wet bundle; unrecognized materials use the <c>clay-fire</c> bundle
    /// for the same state, matching the theme/backdrop fallback.</summary>
    public static TabletReadability For(string? material, TabletState state) => material switch
    {
        "clay-blue" => state switch { TabletState.Hard => BlueHard, TabletState.Fired => BlueFired, _ => BlueWet },
        "clay-red"  => state switch { TabletState.Hard => RedHard,  TabletState.Fired => RedFired,  _ => RedWet },
        "wax"       => WaxWet, // wax has only a wet state
        _           => state switch { TabletState.Hard => FireHard, TabletState.Fired => FireFired, _ => FireWet },
    };
}
