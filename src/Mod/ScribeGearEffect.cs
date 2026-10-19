using System.Collections.Generic;
using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key
using SkiaSharp;                  // SKColor, SKColorFilter, SKImageFilter, SKBlendMode, SKColors

namespace Scribe;

/// <summary>
/// A transparent single-child wrapper that repaints its child (a rotated gear texture) as a solid-colour
/// SILHOUETTE (every non-transparent pixel becomes <paramref name="tint"/>, alpha shape preserved), optionally
/// BLURRED. Used for the Timer-tab gearworks' cast shadow and the temporal gear's emissive glow; unlike
/// <c>BoxStyle.BoxShadows</c> it follows the gear outline rather than the square box.
///
/// <para><b>How.</b> LibGUI's <c>DrawMaskedBox</c> draws with the shared paint but does NOT set its
/// <c>ColorFilter</c> or <c>ImageFilter</c>. So those are set on the shared paint just before the child paints
/// and restored after: a <c>SrcIn</c> blend colour-filter recolours the bitmap while keeping its alpha, and an
/// optional blur image-filter softens it. The child's rotation is a canvas transform, so the silhouette rotates
/// in lock-step.</para>
///
/// <para><b>Filter lifetime.</b> Filters are cached by parameters and NEVER disposed per-frame (as in
/// <see cref="CuneiformGlowMask"/>; disposing a filter mid-record corrupts the recording canvas).</para>
/// </summary>
internal sealed class ScribeGearEffect : SingleChildWidget
{
    private readonly SKColor tint;
    private readonly float blurSigma;

    public ScribeGearEffect(Widget child, SKColor tint, float blurSigma = 0f, Gui.Widgets.Framework.Key? key = null) : base(child, key)
    {
        this.tint = tint;
        this.blurSigma = blurSigma;
    }

    public override RenderObject CreateRenderObject() => new GearEffectRender(tint, blurSigma);

    public override void UpdateRenderObject(RenderObject renderObject)
        => ((GearEffectRender)renderObject).Configure(tint, blurSigma);

    /// <summary>Cached <c>SrcIn</c> tint colour-filters keyed by ARGB: a solid-colour silhouette following the
    /// bitmap's own alpha. Never disposed.</summary>
    private static readonly Dictionary<uint, SKColorFilter> TintCache = new();

    /// <summary>Cached Gaussian blur image-filters keyed by sigma (rounded). Never disposed.</summary>
    private static readonly Dictionary<float, SKImageFilter> BlurCache = new();

    private static SKColorFilter TintFilter(SKColor color)
    {
        uint key = (uint)color;
        if (TintCache.TryGetValue(key, out var f)) return f;
        var made = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn);
        TintCache[key] = made;
        return made;
    }

    private static SKImageFilter? BlurFilter(float sigma)
    {
        if (sigma <= 0f) return null;
        float key = System.MathF.Round(sigma, 2);
        if (BlurCache.TryGetValue(key, out var f)) return f;
        var made = SKImageFilter.CreateBlur(key, key);
        BlurCache[key] = made;
        return made;
    }

    private sealed class GearEffectRender : RenderProxyBox
    {
        private SKColor tint;
        private float blurSigma;

        public GearEffectRender(SKColor tint, float blurSigma) { this.tint = tint; this.blurSigma = blurSigma; }

        public void Configure(SKColor tint, float blurSigma) { this.tint = tint; this.blurSigma = blurSigma; }

        public override void Paint(PaintingContext context)
        {
            var paint = context.SharedPaint;
            var prevColor = paint.Color;
            var prevColorFilter = paint.ColorFilter;
            var prevImageFilter = paint.ImageFilter;

            // Opaque white so the texture isn't alpha-modulated by a stale color (the DrawMaskedBox leak; see
            // ScribeResetPaintColor); the effect's tint/alpha come from the color filter.
            paint.Color = SKColors.White;
            paint.ColorFilter = TintFilter(tint);
            paint.ImageFilter = BlurFilter(blurSigma);

            base.Paint(context);   // child = the rotated gear texture, now recoloured to a (blurred) silhouette

            // Restore so siblings paint normally (the shared paint outlives this subtree).
            paint.ImageFilter = prevImageFilter;
            paint.ColorFilter = prevColorFilter;
            paint.Color = prevColor;
        }
    }
}
