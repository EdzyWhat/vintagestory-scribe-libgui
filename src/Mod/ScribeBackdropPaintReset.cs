using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key
using SkiaSharp;                  // SKColors

namespace Scribe;

/// <summary>
/// A transparent single-child wrapper that forces <see cref="PaintingContext.SharedPaint"/>'s color to
/// opaque white immediately before its child paints, then paints the child normally. It draws nothing of
/// its own.
///
/// <para><b>Why this exists.</b> LibGUI reuses ONE <c>SKPaint</c> (<c>PaintingContext.SharedPaint</c>) for
/// every draw op AND across frames — <c>PaintingContext.Reset</c> clears the canvas stack and time but NOT
/// the paint's <c>Color</c>. Most draw ops (<c>DrawBox</c>/<c>DrawImage</c>/<c>DrawNineSlice</c>) set
/// <c>Color</c> before they paint, so they don't care what the previous op left. The ONE exception is the
/// textured-box path (<c>DrawMaskedBox</c>, used by a <c>BoxStyle.Texture</c> — i.e. our clay backdrop):
/// it sets only <c>FilterQuality</c> and then <c>DrawBitmap(texture, SharedPaint)</c>, so the bitmap is
/// MODULATED by whatever color the LAST op of the PREVIOUS frame happened to leave on the shared paint.</para>
///
/// <para>On the read-only hard/fired tablet the last box painted each frame is the always-on scrollbar
/// track (<c>Scrollbar { AutoHide = false }</c>, theme-default track alpha 0.1), so the next frame's
/// backdrop <c>DrawMaskedBox</c> multiplied the clay art by ~alpha 0.1 → a uniformly semi-transparent
/// backdrop. The wet editor and the tabbed Lectern/Notebook views paint an OPAQUE footer button last, so
/// they never leaked — which is exactly why only the read-only tablet showed it (see the
/// tablet-transparent-backdrop-sharedpaint-leak notes). The leaky ops live in the vendored
/// <c>Gui.dll</c> (can't edit), so the fix lives here in Scribe code.</para>
///
/// <para><b>Why wrapping the backdrop.</b> Placed directly around the backdrop <c>Container</c>, this
/// paints (setting the shared color opaque white) in the same frame, immediately before the child
/// backdrop's <c>DrawMaskedBox</c> — nothing paints in between (a proxy box with no fill/texture draws
/// nothing itself). So the backdrop always modulates by opaque white regardless of what any prior frame
/// left. Frame-order-independent, unlike a "paint an opaque element LAST" approach that depends on the
/// leaf paint order.</para>
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
            // Reset BEFORE the child paints, so its DrawMaskedBox modulates the bitmap by opaque white rather
            // than a stale color left by a previous op. Also clear the color/image FILTERS and restore a normal
            // SrcOver blend: DrawMaskedBox sets only FilterQuality, so a stale ColorFilter (e.g. a gear's cast
            // shadow / glow tint, or the diagnostic border's fill) or BlendMode would otherwise recolour/dim
            // the child — the see-through-gears bug (add-timer-gearworks 7.6, D16). Additive for the backdrops
            // (they never relied on a leftover filter); load-bearing for the opaque gears wrapped in this.
            var paint = context.SharedPaint;
            paint.Color = SKColors.White;
            paint.ColorFilter = null;
            paint.ImageFilter = null;
            paint.BlendMode = SKBlendMode.SrcOver;
            base.Paint(context);
        }
    }
}
