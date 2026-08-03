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

    // ---- Clay tablet backdrops (add-tablet-clay-type-backdrops) ---------------------------------------
    // Each clay type has an authored full-page 1024×1160 backdrop (same shape as the pages above, so they
    // take the identical stretch-to-fill path — NO tiling/renderer change). The three "fired" specs reuse
    // the same authored art under a per-type ceramic tint, since (a) no bespoke fired art exists yet and
    // (b) vanilla fired ceramic is not color-keyed by source clay — the tint keeps red/blue/fire
    // distinguishable once fired. When real fired art lands, each fired spec becomes a straight PNG-path
    // swap with its Tint cleared.

    private static AssetLocation Clay(string type) =>
        new("scribe", $"textures/gui/scribe-clay-tablet-{type}.png");

    /// <summary>Unfired red-clay tablet page (authored art).</summary>
    public static readonly ScribeBackdropSpec ClayRedSoft = new(Clay("red"));

    /// <summary>Unfired blue-clay tablet page (authored art).</summary>
    public static readonly ScribeBackdropSpec ClayBlueSoft = new(Clay("blue"));

    /// <summary>Unfired fire-clay tablet page (authored art).</summary>
    public static readonly ScribeBackdropSpec ClayFireSoft = new(Clay("fire"));

    /// <summary>Fired red-clay tablet page: the red art under a warm terracotta ceramic tint. Interim
    /// (unreachable in normal play this round — nothing sets <c>fired = true</c>); reachable via creative.
    /// Tint values are eyeballed and tuned in-game.</summary>
    public static readonly ScribeBackdropSpec ClayRedFired =
        new(Clay("red"), new Vector4(1.00f, 0.62f, 0.52f, 1f));

    /// <summary>Fired blue-clay tablet page: the blue art under a cool slate-ceramic tint (interim).</summary>
    public static readonly ScribeBackdropSpec ClayBlueFired =
        new(Clay("blue"), new Vector4(0.66f, 0.74f, 0.86f, 1f));

    /// <summary>Fired fire-clay tablet page: the fire art under a pale buff-ceramic tint (interim).</summary>
    public static readonly ScribeBackdropSpec ClayFireFired =
        new(Clay("fire"), new Vector4(0.96f, 0.88f, 0.70f, 1f));

    /// <summary>The wax tablet page. No bespoke wax art exists yet, so this reuses the warm fire-clay
    /// art as an interim placeholder (closest in tone to beeswax); it swaps to real diptych art later.</summary>
    public static readonly ScribeBackdropSpec Wax = new(Clay("fire"));

    /// <summary>Select the tablet backdrop for a stack's <c>material</c> variant + recorded <c>fired</c>
    /// appearance, in ONE place so the item and its dialog agree on the mapping (add-tablet-dialog D6). The
    /// clay type is the item's own registered variant (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>) —
    /// one discrete item per type — not a stack attribute; <c>wax</c> gets <see cref="Wax"/>. An unknown or
    /// absent material defaults to red + soft, so a legacy or creative-inventory stack always resolves to a
    /// valid backdrop (clay-wax-tablet-item: "consumers treat it as red + soft").</summary>
    public static ScribeBackdropSpec ForTablet(string? material, bool fired)
    {
        if (material == "wax") return Wax;
        return (material, fired) switch
        {
            ("clay-blue", false) => ClayBlueSoft,
            ("clay-blue", true)  => ClayBlueFired,
            ("clay-fire", false) => ClayFireSoft,
            ("clay-fire", true)  => ClayFireFired,
            (_, true)            => ClayRedFired,   // clay-red or any unrecognized material, fired
            _                    => ClayRedSoft,    // clay-red or any unrecognized material, soft (default)
        };
    }

    // Desk / future page specs are added here as those items ship — each is just another
    // spec plus its PNG; ScribeDialogBase.WrapBackdrop and the bitmap cache stay unchanged.
}
