// Display-only cuneiform text widget (add-cuneiform-glyph-font, Proposal A). Renders a single line of
// text as filled rectangle STROKES (not a TTF typeface) from the glyph-forge geometry parsed by
// Scribe.Core.Cuneiform. It mirrors ScribeMultilineField's 3-class LibGUI pattern:
//   • CuneiformTextRender : RenderBox — runs the Core line layout, scales grid units → pixels by the
//     requested em size, sizes itself to the line, and FILLS each stroke's oriented quad on the raw
//     Skia canvas (context.Canvas + SKPath + SharedPaint). It deliberately does NOT use DrawBox:
//     strokes are arbitrary-angle rectangles and DrawBox only draws axis-aligned (rounded) boxes.
//   • CuneiformTextRenderWidget : RenderObjectWidget — the create/update bridge.
//   • CuneiformText : StatefulWidget — owns an OPTIONAL AnimationController for a stroke-by-stroke
//     reveal; for this first prototype it idles fully revealed (static display) so proving legibility
//     never waits on animation.
//
// This is the #1 de-risk of the tablet plan: raw-canvas polygon fill inside PaintInternal is new to
// this codebase (existing widgets only ever call DrawText/DrawBox). See
// openspec/changes/add-cuneiform-glyph-font/.

using System;
using Gui.Core.Framework;         // RenderObject, RenderBox
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Animations;     // AnimationController, AnimatedBuilder
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, RenderObjectWidget
using Gui.Widgets.Input;          // ITickerProvider (via Element.Owner)
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle, CuneiformLineLayout, CuneiformLine, GlyphStroke
using SkiaSharp;                  // SKPath, SKPoint

namespace Scribe;

/// <summary>
/// Shared cuneiform rendering metrics (add-tablet-cuneiform-chrome D7). Every cuneiform render object —
/// the display <see cref="CuneiformTextRender"/>, the editable row (<see cref="ScribeCuneiformFieldRender"/>),
/// and the title field — sizes a line to <c>fontSizeEm</c> pixels. But normal TTF <c>Text</c> at the same
/// nominal font size occupies a line-height of roughly <see cref="LineHeightRatio"/>× that font size, so a
/// cuneiform glyph authored to fill its em box reads visibly SHORTER than adjacent readable text (the
/// 2026-08-02 playtest measured the footer label ~30% short). Scaling the cuneiform em by this ratio at
/// render time brings the rendered cuneiform height in line with the readable text's rendered line-height —
/// applied globally here (not as a per-call fudge) so rows, title, and labels all match. Scale-independent
/// (a ratio, not a pixel target), so it holds at any GUI scale.
/// </summary>
internal static class CuneiformMetrics
{
    /// <summary>Rendered-line-height ÷ nominal-font-size for the normal TTF body text the cuneiform sits
    /// beside; cuneiform's em→pixel scale is multiplied by this so its glyphs read at the same height as
    /// that text rather than ~30% short. ~1.4 matches the measured TTF line-height ratio; tuned against the
    /// in-game retest (task 8.6).</summary>
    public const float LineHeightRatio = 1.4f;
}

/// <summary>
/// The render object: lays a string out through the Core cuneiform engine and paints its strokes as
/// filled quads, auto-sizing to the laid-out line. The line height in pixels equals
/// <see cref="FontSizeEm"/> × <see cref="CuneiformMetrics.LineHeightRatio"/> (grid units scale to pixels by
/// <c>renderedHeight / lineHeightGridUnits</c>) so cuneiform matches adjacent readable text (D7).
/// </summary>
internal sealed class CuneiformTextRender : Gui.Core.Framework.RenderBox
{
    private string text = "";
    private float fontSizeEm = 32f;
    private Vector4 inkColor = Vector4.One;
    private GlyphBundle? bundle;
    // Fraction of the line's strokes to reveal, in authored construction order (1 = whole line). Idle
    // at 1 for the static prototype; the animation drives it 0→1 when active.
    private float revealFraction = 1f;

    // Cached from the last PerformLayout, reused by PaintInternal so layout and paint agree.
    private CuneiformLine? line;
    private float scale = 1f;

    /// <summary>The text to render (uppercase-folded by the layout; unauthored chars advance a small
    /// gap). Relayouts on change (the line width/height depend on it).</summary>
    public string Text { get => text; set => SetProperty(ref text, value ?? "", relayout: true); }

    /// <summary>Pixel height of one em (one glyph grid). The whole line scales from this. Relayouts.</summary>
    public float FontSizeEm { get => fontSizeEm; set => SetProperty(ref fontSizeEm, value, relayout: true); }

    /// <summary>Ink color the stroke quads fill with (from the active theme). Repaint only.</summary>
    public Vector4 InkColor { get => inkColor; set => SetProperty(ref inkColor, value, repaint: true); }

    /// <summary>The parsed glyph geometry. Relayouts on change (glyphs determine the line's extent).
    /// Null until the asset is loaded — the widget then renders nothing.</summary>
    public GlyphBundle? Bundle { get => bundle; set => SetProperty(ref bundle, value, relayout: true); }

    /// <summary>Fraction (0..1) of the line's strokes to paint, in construction order. Repaint only —
    /// the geometry is unchanged, only how many strokes are drawn.</summary>
    public float RevealFraction
    {
        get => revealFraction;
        set => SetProperty(ref revealFraction, Math.Clamp(value, 0f, 1f), repaint: true);
    }

    protected override void PerformLayout()
    {
        // Match the rendered cuneiform height to adjacent readable text's line-height, not its raw em (D7).
        float renderedHeight = fontSizeEm * CuneiformMetrics.LineHeightRatio;

        if (bundle is null)
        {
            line = null;
            Size = Constraints.Constrain(new Vector2(0f, renderedHeight));
            return;
        }

        var layout = new CuneiformLineLayout(bundle);
        line = layout.Layout(text);

        // Grid units → pixels: one line-height of grid units maps to the rendered (ratio-boosted) height.
        double gridHeight = line.LineHeight > 0 ? line.LineHeight : CuneiformLineLayout.DefaultGridSize;
        scale = (float)(renderedHeight / gridHeight);

        float width = (float)(line.TotalWidth * scale);
        Size = Constraints.Constrain(new Vector2(width, renderedHeight));
    }

    protected override void PaintInternal(PaintingContext context)
    {
        // Paint the base box first (row background/frame, if any theme colors are set), then the strokes.
        base.PaintInternal(context);
        if (context.Canvas is null || line is null || line.Strokes.Count == 0)
        {
            return;
        }

        int total = line.Strokes.Count;
        int revealCount = (int)MathF.Round(revealFraction * total);
        revealCount = Math.Clamp(revealCount, 0, total);
        if (revealCount == 0)
        {
            return;
        }

        SKPaint paint = context.SharedPaint;
        SKColor prevColor = paint.Color;
        SKPaintStyle prevStyle = paint.Style;

        paint.Style = SKPaintStyle.Fill;
        paint.Color = inkColor.ToSkColor();
        paint.IsAntialias = true;

        // A reused path, cleared per stroke — allocation-light across the whole line.
        using var path = new SKPath();
        for (int i = 0; i < revealCount; i++)
        {
            GlyphStroke stroke = line.Strokes[i].Stroke;
            Scribe.Core.Cuneiform.Vec2[] corners = stroke.Corners();

            path.Reset();
            path.MoveTo((float)(corners[0].X * scale), (float)(corners[0].Y * scale));
            path.LineTo((float)(corners[1].X * scale), (float)(corners[1].Y * scale));
            path.LineTo((float)(corners[2].X * scale), (float)(corners[2].Y * scale));
            path.LineTo((float)(corners[3].X * scale), (float)(corners[3].Y * scale));
            path.Close();

            context.Canvas.DrawPath(path, paint);
        }

        // Restore the shared paint's mutated properties (SharedPaint is reused across draw ops).
        paint.Color = prevColor;
        paint.Style = prevStyle;
    }
}

/// <summary>RenderObjectWidget bridge: forwards text, em size, ink color, bundle, and reveal fraction.</summary>
internal sealed class CuneiformTextRenderWidget : RenderObjectWidget
{
    public CuneiformTextRenderWidget(
        string text, float fontSizeEm, Vector4 inkColor, GlyphBundle? bundle, float revealFraction)
    {
        Text = text;
        FontSizeEm = fontSizeEm;
        InkColor = inkColor;
        Bundle = bundle;
        RevealFraction = revealFraction;
    }

    public string Text { get; }
    public float FontSizeEm { get; }
    public Vector4 InkColor { get; }
    public GlyphBundle? Bundle { get; }
    public float RevealFraction { get; }

    public override RenderObject CreateRenderObject() => new CuneiformTextRender();

    public override void UpdateRenderObject(RenderObject renderObject)
    {
        var ro = (CuneiformTextRender)renderObject;
        ro.Text = Text;
        ro.FontSizeEm = FontSizeEm;
        ro.InkColor = InkColor;
        ro.Bundle = Bundle;
        ro.RevealFraction = RevealFraction;
    }
}

/// <summary>
/// A display-only cuneiform text line. Optionally reveals its strokes one-by-one in authored
/// construction order via an owned <see cref="AnimationController"/>; when <see cref="AnimateReveal"/>
/// is false (the default for this prototype) it renders the whole line statically. Owns and disposes
/// its controller per the LibGUI lifecycle (created in <see cref="State.InitState"/>, disposed in
/// <see cref="State.Dispose"/>) — see <c>docs/libgui-reference.md</c>.
/// </summary>
public sealed class CuneiformText : StatefulWidget
{
    public CuneiformText(
        string text,
        float fontSizeEm = 32f,
        Vector4? inkColor = null,
        GlyphBundle? bundle = null,
        bool animateReveal = false,
        int revealDurationMs = 1200,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Text = text ?? "";
        FontSizeEm = fontSizeEm;
        InkColor = inkColor ?? Vector4.One;
        Bundle = bundle;
        AnimateReveal = animateReveal;
        RevealDurationMs = revealDurationMs;
    }

    /// <summary>The line of text to render.</summary>
    public string Text { get; }

    /// <summary>Pixel height of one em (one glyph grid).</summary>
    public float FontSizeEm { get; }

    /// <summary>Ink color the strokes fill with (usually the theme's on-surface color).</summary>
    public Vector4 InkColor { get; }

    /// <summary>The parsed glyph geometry; null renders nothing (asset not yet loaded).</summary>
    public GlyphBundle? Bundle { get; }

    /// <summary>Whether to play the stroke-by-stroke reveal on mount. Default false = static full line.</summary>
    public bool AnimateReveal { get; }

    /// <summary>Reveal duration in ms when <see cref="AnimateReveal"/> is on.</summary>
    public int RevealDurationMs { get; }

    public override State CreateState() => new CuneiformTextState();
}

internal sealed class CuneiformTextState : State<CuneiformText>
{
    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        if (Widget.AnimateReveal)
        {
            // ITickerProvider comes from the owner, exactly like ScribeCollapsible's controller.
            controller = new AnimationController(
                TimeSpan.FromMilliseconds(Widget.RevealDurationMs), Element.Owner!.GetTickerProvider());
            controller.OnValueChanged += OnValueChanged;
            controller.Forward();
        }
    }

    private void OnValueChanged(double _) => Element.MarkNeedsBuild();

    public override void Dispose()
    {
        if (controller is not null)
        {
            controller.OnValueChanged -= OnValueChanged;
            controller.Dispose();
            controller = null;
        }
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
    {
        // No animation: render the whole line statically (reveal fraction 1).
        if (controller is null)
        {
            return BuildLine(1f);
        }

        // Animated: rebuild each tick, mapping the controller's 0→1 value to the reveal fraction.
        return new AnimatedBuilder(controller, _ => BuildLine((float)controller.Value));
    }

    private Widget BuildLine(float revealFraction) => new CuneiformTextRenderWidget(
        text: Widget.Text,
        fontSizeEm: Widget.FontSizeEm,
        inkColor: Widget.InkColor,
        bundle: Widget.Bundle,
        revealFraction: revealFraction);
}
