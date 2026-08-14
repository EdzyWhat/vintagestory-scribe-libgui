using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget
using SkiaSharp;                  // SKBitmap, SKImage, SKRect, SKColors, SKSamplingOptions

namespace Scribe;

/// <summary>
/// Paints a dialog backdrop bitmap behind its child with NEAREST-NEIGHBOUR sampling, so a small
/// (native-resolution) pixel-art source scales up to the dialog as crisp, hard-edged blocks rather than
/// the soft blur a linear upscale would give. This is why the backdrop PNGs can ship at their authored
/// 128×145 size instead of an 8×-upscaled 1024×1160 blur.
///
/// <para><b>Why a custom widget.</b> The stock textured-box path (<c>BoxStyle.Texture</c> →
/// <c>DrawMaskedBox</c>) hard-codes <c>FilterQuality = Medium</c> (smooth) and offers no way to select
/// nearest sampling, so we draw the image ourselves. We mirror <see cref="ScribeGearEffect"/>: a
/// <c>RenderProxyBox</c> whose <c>Paint</c> draws onto the already-offset canvas in local coords
/// <c>(0,0)→Size</c> (as <c>BoxWidget.PaintInternal</c> does), then paints the child on top. No LibGUI
/// fork and no GL code of our own.</para>
///
/// <para><b>Paint-colour hygiene.</b> <c>DrawImage</c> modulates the drawn pixels by
/// <c>SharedPaint.Color</c>, which an earlier op may have left non-opaque (the transparent-backdrop leak
/// <see cref="ScribeResetPaintColor"/> guards against). We force it opaque white for our draw and restore
/// every touched field afterwards, since the shared paint outlives this subtree — same discipline as
/// <see cref="ScribeGearEffect"/>. Any per-spec tint is already baked into the bitmap
/// (<see cref="ScribeModSystem.GetBackdropBitmap"/>), so no colour filter is applied here.</para>
///
/// <para><b>SKImage lifetime.</b> Skia's nearest sampling is selected via <c>SKSamplingOptions</c> on
/// <c>DrawImage</c> (the non-deprecated path LibGUI's own <c>DrawImageCore</c> uses). The image is an
/// <c>SKImage.FromBitmap</c> wrapper built at most once per source bitmap and never disposed (a cheap
/// wrapper over an immutable, mod-owned bitmap — same never-dispose discipline as
/// <see cref="ScribeGearEffect"/>'s filter caches). The source bitmaps are marked
/// <c>SetImmutable()</c> at decode (<see cref="ScribeModSystem.GetBackdropSource"/>) so Skia caches the
/// GPU upload and reuses it across draws/opens.</para>
/// </summary>
internal sealed class ScribePixelArtBackdrop : SingleChildWidget
{
    private readonly SKBitmap bitmap;

    public ScribePixelArtBackdrop(SKBitmap bitmap, Widget child, Gui.Widgets.Framework.Key? key = null)
        : base(child, key)
    {
        this.bitmap = bitmap;
    }

    public override RenderObject CreateRenderObject() => new PixelArtBackdropRender(bitmap);

    public override void UpdateRenderObject(RenderObject renderObject)
        => ((PixelArtBackdropRender)renderObject).Configure(bitmap);

    private sealed class PixelArtBackdropRender : RenderProxyBox
    {
        // Nearest-neighbour, no mipmaps: each source texel becomes a crisp square when scaled up.
        private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

        private SKBitmap bitmap = null!;
        private SKImage? image; // FromBitmap wrapper, built lazily and rebuilt only if the source bitmap changes

        public PixelArtBackdropRender(SKBitmap bitmap) => Configure(bitmap);

        public void Configure(SKBitmap bitmap)
        {
            if (ReferenceEquals(this.bitmap, bitmap)) return;
            this.bitmap = bitmap;
            image = null; // rebuild the wrapper only when the source bitmap actually changed
        }

        public override void Paint(PaintingContext context)
        {
            var canvas = context.Canvas;
            image ??= SKImage.FromBitmap(bitmap);

            if (canvas is not null && image is not null)
            {
                var paint = context.SharedPaint;
                var prevColor       = paint.Color;
                var prevColorFilter = paint.ColorFilter;
                var prevImageFilter = paint.ImageFilter;

                // Opaque white (no colour modulation) and no filters; nearest sampling comes from the
                // SKSamplingOptions arg, so FilterQuality on the shared paint is irrelevant here.
                paint.Color       = SKColors.White;
                paint.ColorFilter = null;
                paint.ImageFilter = null;

                var src = SKRect.Create(0f, 0f, image.Width, image.Height);
                var dst = SKRect.Create(0f, 0f, base.Size.X, base.Size.Y);
                canvas.DrawImage(image, src, dst, Nearest, paint);

                // Restore so the child and later siblings paint normally.
                paint.ImageFilter = prevImageFilter;
                paint.ColorFilter = prevColorFilter;
                paint.Color       = prevColor;
            }

            base.Paint(context);   // the dialog content tree, on top of the backdrop
        }
    }
}
