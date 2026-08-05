using OpenTK.Mathematics;        // Vector4 (optional per-spec tint)
using Vintagestory.API.Common;   // AssetLocation

namespace Scribe;

/// <summary>
/// One dialog backdrop: the texture asset painted behind a view's content, plus an OPTIONAL
/// <paramref name="Tint"/> multiplied into it. Holds no pixel size, so a view may declare art of any
/// dimensions (<c>ScribeDialogBase.WrapBackdrop</c> stretches/downsamples it to fill the dialog). When
/// <paramref name="Tint"/> is null (every full-page illustration spec) the bitmap is used verbatim; when
/// set, <see cref="ScribeModSystem.GetBackdropBitmap"/> bakes the tint into a cached copy via an
/// <c>SKColorFilter</c> modulate — the same tinting primitive LibGUI's icon renderer uses — so the same
/// source art can back several visually-distinct specs without new PNGs (used by the fired-clay tablet
/// backdrops, which reuse the soft-clay art under a per-type ceramic hue).
/// </summary>
/// <param name="Texture">The backdrop PNG asset.</param>
/// <param name="Tint">Optional RGBA multiplier baked into the bitmap; null = draw the art unchanged.</param>
public sealed record ScribeBackdropSpec(AssetLocation Texture, Vector4? Tint = null);

/// <summary>The three tablet drying states, in permanence order. <see cref="ScribeBackdrops.ForTablet"/>
/// resolves the backdrop by clay type × state; <see cref="ItemScribeTablet"/> derives the state from the
/// stack's <c>material</c> variant — the soft variant is the bare clay code, and hardening/firing swap it
/// to a <c>-hard</c>/<c>-fired</c> sibling (wire-tablet-clay-art-and-variants). Top-level (not nested in the
/// internal <see cref="ScribeBackdrops"/>) and public so it can be a parameter of the public
/// <see cref="GuiDialogScribeTablet"/> ctor.</summary>
public enum TabletState { Wet, Hard, Fired }

/// <summary>
/// The per-item backdrop specifications for Scribe's dialogs (the <c>gui-backdrop</c> capability). Each
/// item declares its OWN page spec so the dialogs are visually distinct: the Lectern, the plain Notebook,
/// and the Clockmaker's Notebook each name a different illustrated background (the plain Notebook and
/// Clockmaker share the <see cref="NotebookHost"/> class, so the Clockmaker item passes
/// <see cref="ClockmakerPage"/> to its host's ctor to override the default). The clay tablet declares six
/// clay specs (three clay types × soft/fired) plus a wax spec, selected by <see cref="ForTablet"/> from the
/// item's own <c>material</c> variant (one discrete item per clay type). Adding a new full-page backdrop is
/// only a new spec here plus its PNG — no
/// change to <c>ScribeDialogBase.WrapBackdrop</c> or the bitmap cache
/// (<see cref="ScribeModSystem.GetBackdropBitmap"/>).
/// </summary>
internal static class ScribeBackdrops
{
    /// <summary>The Lectern read/editor page: its own illustrated art (1024×1160, aspect 0.883) that the
    /// dialog is sized to so LibGUI's stretch-to-fill <c>BoxStyle.Texture</c> renders it as a uniform,
    /// distortion-free scale (scribe-notebook-frame).</summary>
    public static readonly ScribeBackdropSpec LecternPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-lectern.png"));

    /// <summary>The plain (player-held) Notebook item's read/editor page. Same 1024×1160 art size as the
    /// other pages; the default backdrop <see cref="NotebookHost"/> uses when its ctor is given no
    /// override.</summary>
    public static readonly ScribeBackdropSpec NotebookPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-notebook.png"));

    /// <summary>The Clockmaker's Notebook item's read/editor page. Passed to the shared
    /// <see cref="NotebookHost"/> ctor by <c>ItemClockmakerNotebook</c> so the Clockmaker draws distinct
    /// art from the plain Notebook (both items otherwise share the same host class).</summary>
    public static readonly ScribeBackdropSpec ClockmakerPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-clockmakers-notebook.png"));

    // ---- Clay tablet backdrops (add-tablet-clay-type-backdrops, add-tablet-firing-mechanic) -----------
    // Each clay type has authored full-page 1024×1160 backdrops (same shape as the pages above, so they
    // take the identical stretch-to-fill path — NO tiling/renderer change). Art exists per drying STATE:
    // "-soft" (wet/malleable, the editable tablet), "-hard" (dried-but-unfired, read-only until rehydrated),
    // and "-fired" (kiln-fired ceramic, permanently read-only). Each state is its own authored PNG with NO
    // tint — the design's earlier "interim tint the soft art" plan is superseded now that real per-state art
    // is authored (the same move that retired the fired tint once fired art shipped). ForTablet selects by
    // clay type × state, with fired taking precedence over hard.

    private static AssetLocation Clay(string type, string state) =>
        new("scribe", $"textures/gui/scribe-clay-tablet-{type}-{state}.png");

    /// <summary>Wet red-clay tablet page — malleable soft-clay art (the editable tablet).</summary>
    public static readonly ScribeBackdropSpec ClayRedSoft = new(Clay("red", "soft"));

    /// <summary>Wet blue-clay tablet page — malleable soft-clay art (the editable tablet).</summary>
    public static readonly ScribeBackdropSpec ClayBlueSoft = new(Clay("blue", "soft"));

    /// <summary>Wet fire-clay tablet page — malleable soft-clay art (the editable tablet).</summary>
    public static readonly ScribeBackdropSpec ClayFireSoft = new(Clay("fire", "soft"));

    /// <summary>Dried (hard, unfired) red-clay tablet page — authored dried-clay art (read-only until
    /// rehydrated). Its own PNG, no tint (tablet-clay-hardening).</summary>
    public static readonly ScribeBackdropSpec ClayRedHard = new(Clay("red", "hard"));

    /// <summary>Dried (hard, unfired) blue-clay tablet page — authored dried-clay art, its own PNG.</summary>
    public static readonly ScribeBackdropSpec ClayBlueHard = new(Clay("blue", "hard"));

    /// <summary>Dried (hard, unfired) fire-clay tablet page — authored dried-clay art, its own PNG.</summary>
    public static readonly ScribeBackdropSpec ClayFireHard = new(Clay("fire", "hard"));

    /// <summary>Fired red-clay tablet page — authored kiln-fired ceramic art (its own PNG, no tint).
    /// Interim-unreachable in normal play this round (nothing sets <c>fired = true</c>); reachable via
    /// creative.</summary>
    public static readonly ScribeBackdropSpec ClayRedFired = new(Clay("red", "fired"));

    /// <summary>Fired blue-clay tablet page — authored kiln-fired ceramic art (its own PNG, no tint).</summary>
    public static readonly ScribeBackdropSpec ClayBlueFired = new(Clay("blue", "fired"));

    /// <summary>Fired fire-clay tablet page — authored kiln-fired ceramic art (its own PNG, no tint).</summary>
    public static readonly ScribeBackdropSpec ClayFireFired = new(Clay("fire", "fired"));

    /// <summary>The wax tablet page — bespoke authored wax art (its own PNG, no tint), replacing the
    /// earlier fire-clay-soft placeholder.</summary>
    public static readonly ScribeBackdropSpec Wax =
        new(new AssetLocation("scribe", "textures/gui/scribe-wax-tablet.png"));

    /// <summary>Select the tablet backdrop for a stack's <c>material</c> variant + drying <c>state</c>, in
    /// ONE place so the item and its dialog agree on the mapping (add-tablet-dialog D6, add-tablet-firing-
    /// mechanic). The clay type is the item's own registered variant (<c>clay-red</c>/<c>clay-blue</c>/
    /// <c>clay-fire</c>) — one discrete item per type — not a stack attribute; <c>wax</c> gets
    /// <see cref="Wax"/> and has no hard/fired states (it neither dries nor fires). An unknown or absent
    /// material defaults to red, so a legacy or creative-inventory stack always resolves to a valid backdrop
    /// (clay-wax-tablet-item: "consumers treat it as red + soft").</summary>
    public static ScribeBackdropSpec ForTablet(string? material, TabletState state)
    {
        if (material == "wax") return Wax;
        return (material, state) switch
        {
            ("clay-blue", TabletState.Wet)   => ClayBlueSoft,
            ("clay-blue", TabletState.Hard)  => ClayBlueHard,
            ("clay-blue", TabletState.Fired) => ClayBlueFired,
            ("clay-fire", TabletState.Wet)   => ClayFireSoft,
            ("clay-fire", TabletState.Hard)  => ClayFireHard,
            ("clay-fire", TabletState.Fired) => ClayFireFired,
            (_, TabletState.Hard)            => ClayRedHard,   // clay-red or unrecognized, hard
            (_, TabletState.Fired)           => ClayRedFired,  // clay-red or unrecognized, fired
            _                                => ClayRedSoft,   // clay-red or unrecognized, wet (default)
        };
    }

    // Desk / future page specs are added here as those items ship — each is just another
    // spec plus its PNG; ScribeDialogBase.WrapBackdrop and the bitmap cache stay unchanged.
}
