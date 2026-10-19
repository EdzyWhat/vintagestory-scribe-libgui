using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key
using SkiaSharp;                  // SKColors

namespace Scribe;

/// <summary>
/// Transparent single-child wrapper that sets <see cref="PaintingContext.SharedPaint"/>'s color to opaque
/// white immediately before its child paints. Draws nothing itself.
///
/// <para>LibGUI reuses ONE <c>SKPaint</c> across draw ops and frames, and <c>PaintingContext.Reset</c> does not
/// clear its <c>Color</c>. <c>DrawMaskedBox</c> (a textured <c>BoxStyle</c>, e.g. the clay backdrop) never sets
/// Color, so it modulates the bitmap by whatever the previous frame's last op left. On the read-only tablet that
/// is the always-on scrollbar track (alpha ~0.1), which renders the backdrop semi-transparent. The leaky ops
/// live in the vendored <c>Gui.dll</c>, so the reset lives here.</para>
///
/// <para>Wrapped directly around the backdrop, nothing paints between the reset and its <c>DrawMaskedBox</c>,
/// so the result does not depend on leaf paint order.</para>
/// </summary>
internal sealed class ScribeResetPaintColor : SingleChildWidget
{
    public ScribeResetPaintColor(Widget child, Gui.Widgets.Framework.Key? key = null) : base(child, key) { }

    public override RenderObject CreateRenderObject() => new ResetPaintColorRender();

    public override void UpdateRenderObject(RenderObject renderObject) { /* no per-build state */ }

    /// <summary>The render half: a pass-through proxy (<see cref="RenderProxyBox"/> lays out and paints its
    /// single child) that resets the shared paint's color to opaque white before the child paints.</summary>
    private sealed class ResetPaintColorRender : RenderProxyBox
    {
        public override void Paint(PaintingContext context)
        {
            // Reset BEFORE the child paints. Also clear the color/image filters and restore SrcOver: DrawMaskedBox
            // sets only FilterQuality, so a stale ColorFilter or BlendMode (e.g. a gear's shadow/glow tint) would
            // otherwise recolour or dim the child.
            var paint = context.SharedPaint;
            paint.Color = SKColors.White;
            paint.ColorFilter = null;
            paint.ImageFilter = null;
            paint.BlendMode = SKBlendMode.SrcOver;
            base.Paint(context);
        }
    }
}
