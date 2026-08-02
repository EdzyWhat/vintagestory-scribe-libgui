using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Basic;         // WindowFrame, Container, Text
using Gui.Widgets.Framework;     // Widget, ThemeData
using Gui.Widgets.Layout;        // Column, Padding, SizedBox, CrossAxisAlignment
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;               // ScribePlayerSettings
using Scribe.Core.Cuneiform;     // GlyphBundle
using Vintagestory.API.Client;

namespace Scribe;

/// <summary>
/// Dev-only, client-only window that renders a demo string through <see cref="CuneiformText"/> so the
/// cuneiform pseudo-font (add-cuneiform-glyph-font, Proposal A) can be judged in-game before any tablet
/// item or dialog exists — the #1 de-risk of the tablet plan. Opened by the client command
/// <c>/cuneiform [text]</c> (see <c>ScribeModSystem.DevTools.cs</c>). NOT wired into any player-facing
/// feature; it exists only to prove the render path, spacing, theme ink color, and the disable-cuneiform
/// fallback.
///
/// <para>Renders the demo line at three em sizes plus one stroke-by-stroke animated reveal, so the
/// riskiest-unknowns checklist (crisp filled strokes, box sizing, legibility/spacing, animation) can be
/// eyeballed at a glance. When the player's <see cref="ScribePlayerSettings.DisableCuneiformFont"/>
/// preference is on, every line falls back to a normal <see cref="Text"/> in the resolved task font
/// through the single <see cref="ScribeTaskFont.UseCuneiform"/> branch point — proving the accessibility
/// path in the same window.</para>
/// </summary>
public sealed class GuiDialogCuneiformHarness : GuiBase
{
    private readonly ScribeModSystem modSystem;
    private readonly string demoText;

    public GuiDialogCuneiformHarness(ICoreClientAPI capi, ScribeModSystem modSystem, string demoText) : base(capi)
    {
        this.modSystem = modSystem;
        this.demoText = string.IsNullOrWhiteSpace(demoText) ? DefaultDemoText : demoText;
    }

    /// <summary>A sentence exercising a spread of the authored glyphs (letters, a digit, and authored
    /// punctuation — colon, comma, exclamation, period) across multiple words, so inter-glyph and word-gap
    /// spacing can be judged together. Uses only authored characters: the set has no ampersand, so a demo
    /// string must avoid <c>&amp;</c> (which correctly renders as a missing-glyph gap).</summary>
    private const string DefaultDemoText = "THE SCRIBE WROTE 7 TABLETS: CLAY, WAX!";

    public override string DialogCode => "scribecuneiformharness";

    protected override WindowConfig CreateWindowConfig() => new()
    {
        Size = new Vector2(720, 420),
        Draggable = true,
        Resizable = false,
    };

    protected override Widget Build()
    {
        // Inherit the player's global LibGUI theme (like the settings window) — this is a dev surface, not
        // a themed Scribe block window. OnSurface is the readable on-panel ink color.
        var colors = ThemeData.Default.ColorScheme;
        Vector4 ink = colors.OnSurface;

        GlyphBundle? bundle = modSystem.GetCuneiformBundle();
        bool useCuneiform = ScribeTaskFont.UseCuneiform(modSystem.MySettings.DisableCuneiformFont);
        string fallbackFamily = ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily);

        // One demo line at each em size; the last one animates its stroke-by-stroke reveal on open.
        var lines = new Widget[]
        {
            DemoLine(bundle, ink, useCuneiform, fallbackFamily, fontSizeEm: 24f, animate: false),
            DemoLine(bundle, ink, useCuneiform, fallbackFamily, fontSizeEm: 40f, animate: false),
            DemoLine(bundle, ink, useCuneiform, fallbackFamily, fontSizeEm: 64f, animate: false),
            DemoLine(bundle, ink, useCuneiform, fallbackFamily, fontSizeEm: 40f, animate: true),
        };

        return new WindowFrame(
            title: "Cuneiform harness",
            onClose: () => TryClose(),
            fillHeight: true,
            child: new Container(
                style: new Gui.Widgets.Painting.BoxStyle { Color = colors.Surface },
                child: new Padding(
                    EdgeInsets.All(20),
                    child: new Column(
                        spacing: 20f,
                        crossAxisAlignment: CrossAxisAlignment.Start,
                        mainAxisSize: MainAxisSize.Min,
                        children: lines))));
    }

    /// <summary>One demo line: the cuneiform widget (or the normal-text fallback when cuneiform is
    /// disabled) at the requested em size. The cuneiform path proves the render/layout; the fallback path
    /// proves the disable-cuneiform setting resolves through <see cref="ScribeTaskFont.Resolve"/>.</summary>
    private Widget DemoLine(
        GlyphBundle? bundle, Vector4 ink, bool useCuneiform, string fallbackFamily, float fontSizeEm, bool animate)
    {
        if (!useCuneiform)
        {
            return new Text(demoText, new TextStyle
            {
                FontSize = fontSizeEm,
                Color = ink,
                FontFamily = fallbackFamily,
            });
        }

        return new CuneiformText(
            text: demoText,
            fontSizeEm: fontSizeEm,
            inkColor: ink,
            bundle: bundle,
            animateReveal: animate,
            revealDurationMs: 2000);
    }
}
