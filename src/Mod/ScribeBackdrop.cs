using OpenTK.Mathematics;        // Vector4 (optional per-spec tint)
using Vintagestory.API.Common;   // AssetLocation

namespace Scribe;

/// <summary>
/// One dialog backdrop: the texture asset painted behind a view's content, plus an optional
/// <paramref name="Tint"/> multiplied into it. Holds no pixel size (<c>ScribeDialogBase.WrapBackdrop</c>
/// stretches it to fill). A null tint uses the bitmap verbatim; otherwise
/// <see cref="ScribeModSystem.GetBackdropBitmap"/> bakes the tint into a cached copy via an <c>SKColorFilter</c>
/// modulate, so the same art can back several specs.
/// </summary>
/// <param name="Texture">The backdrop PNG asset.</param>
/// <param name="Tint">Optional RGBA multiplier baked into the bitmap; null = draw the art unchanged.</param>
public sealed record ScribeBackdropSpec(AssetLocation Texture, Vector4? Tint = null);

/// <summary>The three tablet drying states, in permanence order. <see cref="ScribeBackdrops.ForTablet"/>
/// resolves the backdrop by clay type and state; <see cref="ItemScribeTablet"/> derives the state from the
/// stack's <c>material</c> variant (soft is the bare clay code; hardening/firing swap it to <c>-hard</c>/<c>-fired</c>).
/// Public and top-level so it can be a parameter of the public <see cref="GuiDialogScribeTablet"/> ctor.</summary>
public enum TabletState { Wet, Hard, Fired }

/// <summary>
/// The per-item backdrop specifications for Scribe's dialogs. Each item declares its own page spec; the
/// plain Notebook and Clockmaker share <see cref="NotebookHost"/>, and the Clockmaker passes
/// <see cref="ClockmakerPage"/> to override the default. The clay tablet declares clay specs per type and state
/// plus a wax spec, selected by <see cref="ForTablet"/>. A new full-page backdrop is only a new spec plus its
/// PNG; <c>ScribeDialogBase.WrapBackdrop</c> and the bitmap cache are unchanged.
/// </summary>
internal static class ScribeBackdrops
{
    /// <summary>The Lectern read/editor page (1024x1160, aspect 0.883); the dialog is sized to that aspect so
    /// stretch-to-fill renders without distortion.</summary>
    public static readonly ScribeBackdropSpec LecternPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-lectern.png"));

    /// <summary>The plain Notebook's page (same 1024x1160 size); <see cref="NotebookHost"/>'s default backdrop.</summary>
    public static readonly ScribeBackdropSpec NotebookPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-notebook.png"));

    /// <summary>The Chalkboard page (<c>scribe-chalkboard.png</c>, 128x145). The block entity is sized to this aspect.</summary>
    public static readonly ScribeBackdropSpec ChalkboardPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-chalkboard.png"));

    /// <summary>The Clockmaker's Notebook page, passed to the shared <see cref="NotebookHost"/> by
    /// <c>ItemClockmakerNotebook</c> so it differs from the plain Notebook.</summary>
    public static readonly ScribeBackdropSpec ClockmakerPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-clockmakers-notebook.png"));

    /// <summary>The Task Notice's Accept dialog backdrop (105x130, a taller-than-wide scroll), used directly
    /// by <see cref="GuiDialogTaskNotice"/>, which has no <see cref="IScribeDocumentHost"/>.</summary>
    public static readonly ScribeBackdropSpec TaskNoticePage =
        new(new AssetLocation("scribe", "textures/gui/scribe-task-notice.png"));

    // ---- Clay tablet backdrops ----
    // Full-page 1024x1160 art per drying state ("-soft" editable, "-hard" dried read-only until rehydrated,
    // "-fired" permanently read-only), each its own untinted PNG on the same stretch-to-fill path.

    private static AssetLocation Clay(string type, string state) =>
        new("scribe", $"textures/gui/scribe-clay-tablet-{type}-{state}.png");

    /// <summary>Wet red-clay tablet page (editable).</summary>
    public static readonly ScribeBackdropSpec ClayRedSoft = new(Clay("red", "soft"));

    /// <summary>Wet blue-clay tablet page (editable).</summary>
    public static readonly ScribeBackdropSpec ClayBlueSoft = new(Clay("blue", "soft"));

    /// <summary>Wet fire-clay tablet page (editable).</summary>
    public static readonly ScribeBackdropSpec ClayFireSoft = new(Clay("fire", "soft"));

    /// <summary>Dried (hard, unfired) red-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayRedHard = new(Clay("red", "hard"));

    /// <summary>Dried (hard, unfired) blue-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayBlueHard = new(Clay("blue", "hard"));

    /// <summary>Dried (hard, unfired) fire-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayFireHard = new(Clay("fire", "hard"));

    /// <summary>Fired red-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayRedFired = new(Clay("red", "fired"));

    /// <summary>Fired blue-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayBlueFired = new(Clay("blue", "fired"));

    /// <summary>Fired fire-clay tablet page.</summary>
    public static readonly ScribeBackdropSpec ClayFireFired = new(Clay("fire", "fired"));

    /// <summary>The wax tablet page.</summary>
    public static readonly ScribeBackdropSpec Wax =
        new(new AssetLocation("scribe", "textures/gui/scribe-wax-tablet.png"));

    /// <summary>Selects the tablet backdrop for a stack's <c>material</c> variant and drying state, so the item
    /// and its dialog agree on the mapping. The clay type is the item's registered variant
    /// (<c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>); <c>wax</c> gets <see cref="Wax"/> and has no hard/fired
    /// states. Any other material resolves to red.</summary>
    public static ScribeBackdropSpec ForTablet(string material, TabletState state)
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

    // New page specs are added here: a spec plus its PNG.
}
