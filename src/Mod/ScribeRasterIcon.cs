using Gui.Core.Framework;         // RenderObject, RenderObjectWidget
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // RenderObjectWidget's own namespace
using OpenTK.Mathematics;         // Vector2
using SkiaSharp;                  // SKBitmap, SKImage, SKRect, SKColors, SKSamplingOptions

namespace Scribe;

/// <summary>
/// Paints a fixed-size, full-color raster bitmap untinted with LINEAR sampling (the opposite of
/// <see cref="ScribePixelArtBackdrop"/>'s nearest-neighbour), for pre-colored art authored near its display
/// size. A leaf widget that lays itself out to size×size.
///
/// <para>Like <see cref="ScribePixelArtBackdrop"/>, it forces the shared paint opaque white with no filters
/// and restores every touched field afterwards.</para>
/// </summary>
internal sealed class ScribeRasterIcon : RenderObjectWidget
{
    private readonly SKBitmap bitmap;
    private readonly float size;

    public ScribeRasterIcon(SKBitmap bitmap, float size, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        this.bitmap = bitmap;
        this.size = size;
    }

    public override RenderObject CreateRenderObject() => new RasterIconRender();

    public override void UpdateRenderObject(RenderObject renderObject)
        => ((RasterIconRender)renderObject).Configure(bitmap, size);

    private sealed class RasterIconRender : RenderObject
    {
        private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        private SKBitmap bitmap = null!;
        private float size;
        private SKImage? image; // built lazily; rebuilt only if the source bitmap changes

        public override bool IsHitTestTarget => false;

        public void Configure(SKBitmap bitmap, float size)
        {
            if (!ReferenceEquals(this.bitmap, bitmap)) image = null;
            this.bitmap = bitmap;
            this.size = size;
        }

        protected override void PerformLayout() => Size = Constraints.Constrain(new Vector2(size, size));

        protected override void PaintInternal(PaintingContext context)
        {
            var canvas = context.Canvas;
            image ??= SKImage.FromBitmap(bitmap);
            if (canvas is null || image is null) return;

            var paint = context.SharedPaint;
            var prevColor       = paint.Color;
            var prevColorFilter = paint.ColorFilter;
            var prevImageFilter = paint.ImageFilter;

            paint.Color       = SKColors.White;
            paint.ColorFilter = null;
            paint.ImageFilter = null;

            var src = SKRect.Create(0f, 0f, image.Width, image.Height);
            var dst = SKRect.Create(0f, 0f, Size.X, Size.Y);
            canvas.DrawImage(image, src, dst, Linear, paint);

            paint.ImageFilter = prevImageFilter;
            paint.ColorFilter = prevColorFilter;
            paint.Color       = prevColor;
        }
    }
}
