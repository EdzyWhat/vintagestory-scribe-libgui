using Vintagestory.API.Common;   // AssetLocation

namespace Scribe;

/// <summary>
/// One dialog backdrop: the texture asset painted behind a view's content. Holds ONLY an
/// <see cref="AssetLocation"/> — nothing here assumes a pixel size, so a view may declare art of any
/// dimensions (<c>ScribeDialogBase.WrapBackdrop</c> stretches/downsamples it to fill the dialog).
/// </summary>
public sealed record ScribeBackdropSpec(AssetLocation Texture);

/// <summary>
/// The per-item backdrop specifications for Scribe's dialogs (the <c>gui-backdrop</c> capability). Each
/// item declares its OWN page spec so the dialogs are visually distinct: the Lectern, the plain Notebook,
/// and the Clockmaker's Notebook each name a different illustrated background (the plain Notebook and
/// Clockmaker share the <see cref="NotebookHost"/> class, so the Clockmaker item passes
/// <see cref="ClockmakerPage"/> to its host's ctor to override the default). Adding a new item's backdrop
/// is only a new spec here plus its PNG — no change to <c>ScribeDialogBase.WrapBackdrop</c> or the bitmap
/// cache (<see cref="ScribeModSystem.GetBackdropBitmap"/>).
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

    // Desk / Clay Tablet / future page specs are added here as those items ship — each is just another
    // spec plus its PNG; ScribeDialogBase.WrapBackdrop and the bitmap cache stay unchanged.
}
