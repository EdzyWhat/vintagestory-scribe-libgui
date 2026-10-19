using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget
using SkiaSharp;                  // SKBitmap, SKImage, SKRect, SKColors, SKSamplingOptions

namespace Scribe;

/// <summary>
/// Paints a dialog backdrop bitmap behind its child with NEAREST-NEIGHBOUR sampling, so a small pixel-art
/// source scales up as crisp blocks. The stock <c>BoxStyle.Texture</c> path hard-codes smooth sampling, so
/// this is a <c>RenderProxyBox</c> (like <see cref="ScribeGearEffect"/>) that draws in local coords
/// <c>(0,0)→Size</c>, then paints the child on top.
///
/// <para><c>DrawImage</c> modulates by <c>SharedPaint.Color</c>, which an earlier op may have left
/// non-opaque (see <see cref="ScribeResetPaintColor"/>), so the paint is forced opaque white and every
/// touched field is restored afterwards. Per-spec tint is already baked into the bitmap
/// (<see cref="ScribeModSystem.GetBackdropBitmap"/>).</para>
///
/// <para>The <c>SKImage.FromBitmap</c> wrapper is built once per source bitmap and never disposed (cheap,
/// over an immutable mod-owned bitmap). Sources are <c>SetImmutable()</c> at decode
/// (<see cref="ScribeModSystem.GetBackdropSource"/>) so Skia caches the GPU upload.</para>
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
        private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

        private SKBitmap bitmap = null!;
        private SKImage? image; // built lazily; rebuilt only if the source bitmap changes

        public PixelArtBackdropRender(SKBitmap bitmap) => Configure(bitmap);

        public void Configure(SKBitmap bitmap)
        {
            if (ReferenceEquals(this.bitmap, bitmap)) return;
            this.bitmap = bitmap;
            image = null;
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

                // Nearest sampling comes from the SKSamplingOptions arg, so the paint's FilterQuality is irrelevant.
                paint.Color       = SKColors.White;
                paint.ColorFilter = null;
                paint.ImageFilter = null;

                var src = SKRect.Create(0f, 0f, image.Width, image.Height);
                var dst = SKRect.Create(0f, 0f, base.Size.X, base.Size.Y);
                canvas.DrawImage(image, src, dst, Nearest, paint);

                paint.ImageFilter = prevImageFilter;
                paint.ColorFilter = prevColorFilter;
                paint.Color       = prevColor;
            }

            base.Paint(context);
        }
    }
}
