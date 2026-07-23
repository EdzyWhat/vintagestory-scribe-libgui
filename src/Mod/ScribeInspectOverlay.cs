using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Draws a diagnostic "inspect element" overlay over the lectern dialog: every composed box
/// outlined in its category color, labeled (in mode 1) with its element key + pixel size and, where
/// known, the config field/formula that drives it; plus the inter-element GAPS (padding/spacing that
/// are not themselves elements) as tinted bands. This is the macOS-safe substitute for the dead
/// VSImGui tuning path (Apple Silicon caps at OpenGL 4.1; VSImGui needs 4.3) -- it draws with
/// <c>IRenderAPI.RenderRectangle</c> (a plain LineStrip, no 4.3 dependency) and a tinted white-pixel
/// blit for the gap fills, both of which render fine here.
///
/// <para>Owned by <see cref="GuiDialogScribeLectern"/> (which builds the box list live each frame and
/// calls <see cref="Render"/> from its render pass) and disposed via <see cref="Dispose"/> in the
/// dialog's <c>OnGuiClosed</c>. The only state is the label-texture cache (keyed by label string, so a
/// stable label regenerates its GL texture only once) and the shared white-pixel texture used for gap
/// fills -- both GL resources, hence the explicit dispose. See
/// <c>docs/explorations/gui-inspect-overlay.md</c> (folded into this change's design.md) for rationale.</para>
/// </summary>
public sealed class ScribeInspectOverlay
{
    /// <summary>Draw the outlines above the dialog (~500) and labels just above the outlines.</summary>
    private const float OutlineZ = 600f;
    private const float LabelZ = 601f;

    private readonly ICoreClientAPI capi;

    /// <summary>Label textures cached by their exact string, so a box whose label text is unchanged
    /// across frames reuses its GL texture instead of regenerating one every frame. Disposed wholesale
    /// on <see cref="Dispose"/>.</summary>
    private readonly Dictionary<string, LoadedTexture> labelCache = new();

    /// <summary>A 1x1 opaque-white texture, lazily baked, blitted stretched + color-tinted to draw the
    /// faint filled gap bands (RenderRectangle only strokes an outline; it can't fill).</summary>
    private LoadedTexture? whitePixel;

    /// <summary>Small font for the labels -- deliberately smaller than the row text so a dense list's
    /// labels crowd less. Built once.</summary>
    private readonly CairoFont labelFont;

    public ScribeInspectOverlay(ICoreClientAPI capi)
    {
        this.capi = capi;
        labelFont = CairoFont.WhiteSmallText().WithFontSize(11f);
    }

    /// <summary>Category of an inspected box, which selects its outline color. Colored by KIND rather
    /// than nesting depth so like things read alike down the list.</summary>
    public enum InspectCategory
    {
        /// <summary>Dialog frame / background / title-bar region.</summary>
        Chrome,
        /// <summary>The scrollable row-list viewport / content region.</summary>
        Viewport,
        /// <summary>A whole row.</summary>
        Row,
        /// <summary>A per-row pin/delete/grip/checkbox affordance or the floating edit input.</summary>
        Affordance,
        /// <summary>A control below the row list (slider, toggle, switch button, toolbar icon).</summary>
        Control,
        /// <summary>Spacing/padding that is not an element (drawn as a tinted band, not an outline).</summary>
        Gap,
    }

    /// <summary>One box to outline+label. Coordinates are SCREEN-space (the caller reads them from
    /// <c>Bounds.renderX/renderY/OuterWidth/OuterHeight</c> live each frame, so they already include the
    /// row-list scroll shift). <paramref name="Driver"/> is the config field/formula that drives this
    /// box's size/position, or null when unknown (the label then degrades to key + size).</summary>
    public readonly record struct InspectBox(
        double X, double Y, double Width, double Height,
        string Key, string? Driver, InspectCategory Category);

    /// <summary>Draws the overlay. <paramref name="mode"/> 1 = outlines + labels; 2 = outlines only.
    /// (Mode 0 is filtered out by the caller before it ever gets here.) Gap boxes always get a faint
    /// fill so the band reads; other categories are outline-only.</summary>
    public void Render(IReadOnlyList<InspectBox> boxes, int mode)
    {
        var render = capi.Render;
        foreach (var box in boxes)
        {
            if (box.Width <= 0 || box.Height <= 0) continue;

            var (r, g, b, a) = CategoryColor(box.Category);

            // A gap is padding, not an element -- fill it faintly so the band is visible, since a bare
            // outline reads the same as any element box.
            if (box.Category == InspectCategory.Gap)
            {
                FillRect(box, r, g, b, 0.18f);
            }

            int outlineColor = ColorUtil.ColorFromRgba((int)(r * 255), (int)(g * 255), (int)(b * 255), (int)(a * 255));
            render.RenderRectangle(
                (float)box.X, (float)box.Y, OutlineZ,
                (float)box.Width, (float)box.Height, outlineColor);

            if (mode == 1)
            {
                DrawLabel(box);
            }
        }
    }

    /// <summary>Blits the shared white pixel stretched over the box, tinted by the given color, for the
    /// gap-band fill. Lazily bakes the white pixel on first use.</summary>
    private void FillRect(InspectBox box, float r, float g, float b, float alpha)
    {
        if (whitePixel is null || whitePixel.TextureId == 0)
        {
            whitePixel = BakeWhitePixel();
        }
        capi.Render.Render2DTexture(
            whitePixel.TextureId,
            (float)box.X, (float)box.Y, (float)box.Width, (float)box.Height,
            OutlineZ - 1f, new Vec4f(r, g, b, alpha));
    }

    private LoadedTexture BakeWhitePixel()
    {
        var tex = new LoadedTexture(capi);
        using var surface = new ImageSurface(Format.Argb32, 1, 1);
        using (var ctx = new Context(surface))
        {
            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Paint();
        }
        capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: false, ref tex);
        return tex;
    }

    /// <summary>Draws (and caches) the label for a box: its key + WxH, plus the driver line when known.
    /// Placed at the box's top-left inside corner; affordance labels stagger to the box bottom-left to
    /// reduce overlap with the dense per-row cluster.</summary>
    private void DrawLabel(InspectBox box)
    {
        string text = box.Driver is { Length: > 0 } d
            ? $"{box.Key} {(int)box.Width}x{(int)box.Height}\n{d}"
            : $"{box.Key} {(int)box.Width}x{(int)box.Height}";

        if (!labelCache.TryGetValue(text, out var tex) || tex is null || tex.TextureId == 0)
        {
            var background = new TextBackground
            {
                FillColor = new double[] { 0, 0, 0, 0.7 }, // opaque-ish dark so the label is legible over parchment
                Padding = 2,
                Radius = 1,
            };
            tex = capi.Gui.TextTexture.GenTextTexture(text, labelFont, background);
            labelCache[text] = tex;
        }

        // Stagger affordance labels to the box bottom so the tight pin/delete/grip/checkbox cluster
        // doesn't stack all its labels on the same top-left point.
        double labelX = box.X + 1;
        double labelY = box.Category == InspectCategory.Affordance
            ? box.Y + box.Height - tex.Height - 1
            : box.Y + 1;

        capi.Render.Render2DLoadedTexture(tex, (float)labelX, (float)labelY, LabelZ);
    }

    /// <summary>Per-category outline color (r,g,b,a in 0-1). Colored by kind, not depth:
    /// chrome=white, viewport=cyan, rows=green, affordances=orange, controls=magenta, gaps=yellow.</summary>
    private static (float r, float g, float b, float a) CategoryColor(InspectCategory category) => category switch
    {
        InspectCategory.Chrome => (1f, 1f, 1f, 0.85f),
        InspectCategory.Viewport => (0.2f, 0.85f, 0.95f, 0.9f),
        InspectCategory.Row => (0.35f, 0.9f, 0.4f, 0.9f),
        InspectCategory.Affordance => (1f, 0.6f, 0.15f, 0.95f),
        InspectCategory.Control => (0.95f, 0.35f, 0.9f, 0.9f),
        InspectCategory.Gap => (0.98f, 0.85f, 0.2f, 0.9f),
        _ => (1f, 1f, 1f, 0.85f),
    };

    /// <summary>The static key → driver-string table for the dialog's FIXED-key elements (those whose
    /// driver doesn't depend on live per-row layout). Per-row and gap drivers are computed at the call
    /// site in <c>GuiDialogScribeLectern.BuildInspectBoxes</c>, where the real <c>RowTextLayout</c> /
    /// <c>ScribeRowElement.*Fixed</c> values are in hand, so those labels can't drift from real layout.
    /// Returns null for an unknown key (the label then shows just key + size).</summary>
    public static string? DriverForFixedKey(string key) => key switch
    {
        "rowListScrollbar" => "row-list scrollbar",
        "switchModeButton" => "SwitchButtonWidth x ControlRowHeight",
        "textSizeSlider" => "TextSizeLabelWidth + slider",
        "toolPanelToggleButton" => "ToolPanelToggleWidth x ControlRowHeight",
        "rowEditInput" => "TextX / TextWidth; h = rowH - BottomOverheadBandFixed",
        _ => null,
    };

    /// <summary>Frees the cached label textures and the white-pixel texture. Called from the dialog's
    /// <c>OnGuiClosed</c> -- GenTextTexture / LoadOrUpdateCairoTexture return GL textures that leak
    /// otherwise.</summary>
    public void Dispose()
    {
        foreach (var tex in labelCache.Values)
        {
            tex?.Dispose();
        }
        labelCache.Clear();
        whitePixel?.Dispose();
        whitePixel = null;
    }
}
