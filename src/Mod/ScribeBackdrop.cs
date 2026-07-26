using Gui.Widgets.Basic;         // Container
using Gui.Widgets.Framework;     // Widget
using Gui.Widgets.Painting;      // BoxStyle
using OpenTK.Mathematics;        // Vector4
using Vintagestory.API.Client;   // ICoreClientAPI
using Vintagestory.API.Common;   // AssetLocation

namespace Scribe;

/// <summary>
/// One dialog backdrop: the texture asset painted behind a view's content. Holds ONLY an
/// <see cref="AssetLocation"/> — nothing here assumes a pixel size, so a view may declare art of any
/// dimensions (the drawing helper stretches/downsamples it, see <see cref="ScribeBackdrop.Wrap"/>).
/// </summary>
internal sealed record ScribeBackdropSpec(AssetLocation Texture);

/// <summary>
/// The per-item / per-view backdrop specifications for Scribe's dialogs (the <c>gui-backdrop</c>
/// capability). Each item (the Lectern now; Desk / Notebook / Clay Tablet as they ship) declares its own
/// specs, and within an item the read/editor page and the settings page reference DISTINCT specs so the
/// two pages are visually distinguishable even before final art is drawn. Adding a new item's backdrops
/// is only new specs here plus their PNGs — no change to <see cref="ScribeBackdrop.Wrap"/> or the bitmap
/// cache (<see cref="ScribeModSystem.GetBackdropBitmap"/>).
/// </summary>
internal static class ScribeBackdrops
{
    /// <summary>The Lectern read/editor page: the illustrated notebook art (1024×1160, aspect 0.883)
    /// that the dialog is sized to so LibGUI's stretch-to-fill <c>BoxStyle.Texture</c> renders it as a
    /// uniform, distortion-free scale (scribe-notebook-frame). The flat <c>lecternbackdrop.png</c> stays in
    /// the assets as the reserved placeholder.</summary>
    public static readonly ScribeBackdropSpec LecternPage =
        new(new AssetLocation("scribe", "textures/gui/scribe-notebook.png"));

    /// <summary>The Lectern settings page. Names its OWN texture (distinct from
    /// <see cref="LecternPage"/>); the PNG does not exist yet, so this resolves to the flat placeholder
    /// color path until its art lands — proving the distinct-per-view structure with zero art.</summary>
    public static readonly ScribeBackdropSpec LecternSettings =
        new(new AssetLocation("scribe", "textures/gui/lecternsettingsbackdrop.png"));

    // Desk / Notebook / Clay Tablet page + settings specs are added here as those items ship — each is
    // just another spec pair plus its PNGs; the Wrap helper and the cache stay unchanged.
}

/// <summary>
/// Draws a Scribe dialog backdrop behind a view's content. The whole mechanism is gated by the caller on
/// the <c>PixelArtDisplay</c> preference (this helper is only called when it is ON — see
/// <c>GuiDialogScribeLecternLibGui.Build()</c>); when OFF the caller uses the child bare with no wrap.
/// </summary>
internal static class ScribeBackdrop
{
    /// <summary>
    /// Wrap <paramref name="child"/> in a <see cref="Container"/> that paints <paramref name="spec"/>'s
    /// backdrop behind it. A <c>Container</c>/box paints its fill+texture BEFORE its child
    /// (<c>RenderObject.Paint</c>), so the art sits behind the content automatically — no <c>Stack</c> is
    /// needed. The bitmap is fetched from the shared, self-loaded cache
    /// (<see cref="ScribeModSystem.GetBackdropBitmap"/>); when it is null (asset missing/unloadable) the box
    /// falls back to the flat <paramref name="placeholder"/> color instead of a texture, so the full dialog
    /// structure is visible and testable in-game before any PNG exists (the flat-color-first strategy). The
    /// child stays fully interactive over the backdrop.
    /// </summary>
    public static Widget Wrap(ICoreClientAPI capi, ScribeBackdropSpec spec, Vector4 placeholder, Widget child)
    {
        var bmp = capi.ModLoader.GetModSystem<ScribeModSystem>().GetBackdropBitmap(spec.Texture);
        var style = bmp is not null
            ? new BoxStyle { Texture = bmp }
            : new BoxStyle { Color = placeholder };
        return new Container(style: style, child: child);
    }
}
