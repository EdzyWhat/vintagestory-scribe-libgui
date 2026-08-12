using System.Collections.Generic;
using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key
using SkiaSharp;                  // SKColor, SKColorFilter, SKImageFilter, SKBlendMode, SKColors

namespace Scribe;

/// <summary>
/// A transparent single-child wrapper that repaints its child (a rotated gear texture) as a solid-colour
/// SILHOUETTE — every non-transparent pixel replaced by <paramref name="tint"/>, the gear's own alpha shape
/// preserved — and optionally BLURRED. Used by the Timer-tab gearworks for two cheap 3D effects (design
/// D12/D13):
///
/// <list type="bullet">
/// <item><b>Cast shadow</b> — a dark, slightly-blurred silhouette drawn offset beneath each gear, so the
/// round/toothed outline reads as a real shadow (unlike <c>BoxStyle.BoxShadows</c>, which would shadow the
/// square box rect, not the gear silhouette).</item>
/// <item><b>Emissive glow</b> — a teal, wider-blurred silhouette drawn behind the ONE temporal gear, the
/// Skia-halo approach the mod already uses for cuneiform ink (CuneiformGlow), applied here to that gear
/// alone.</item>
/// </list>
///
/// <para><b>How.</b> LibGUI's textured-box paint (<c>DrawMaskedBox</c>) does <c>DrawBitmap(texture,
/// SharedPaint)</c> and touches only <c>FilterQuality</c> — it does NOT set the paint's <c>ColorFilter</c> or
/// <c>ImageFilter</c>. So we set those on the shared paint immediately before the child paints and restore
/// them after: a <c>SrcIn</c> blend colour-filter recolours the drawn bitmap to <paramref name="tint"/> while
/// keeping its alpha (the standard tint-to-solid recipe), and an optional blur image-filter softens it. The
/// child's <c>AnimatedRotation</c> is a canvas transform, so the silhouette rotates in lock-step with the
/// gear.</para>
///
/// <para><b>Filter lifetime.</b> Both filter kinds are cached by their parameters and NEVER disposed
/// per-frame — the same discipline as <see cref="CuneiformGlowMask"/> (disposing a filter mid-record corrupts
/// the recording canvas). Only a handful of distinct tints/sigmas ever exist, so the caches stay tiny.</para>
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

    /// <summary>Cached <c>SrcIn</c> tint colour-filters keyed by ARGB. <c>Blend(color, SrcIn)</c> yields
    /// <c>color.rgb</c> with <c>alpha = color.a × sourceAlpha</c> — a solid-colour silhouette following the
    /// bitmap's own shape. Never disposed (see class remarks).</summary>
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

            // Opaque white so the texture's DrawBitmap isn't alpha-modulated by a stale color (the DrawMaskedBox
            // leak — see ScribeResetPaintColor); the tint/alpha of the effect come from the color filter.
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
