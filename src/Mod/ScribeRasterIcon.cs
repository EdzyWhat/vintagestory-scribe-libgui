using Gui.Core.Framework;         // RenderObject, RenderObjectWidget
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // RenderObjectWidget's own namespace
using OpenTK.Mathematics;         // Vector2
using SkiaSharp;                  // SKBitmap, SKImage, SKRect, SKColors, SKSamplingOptions

namespace Scribe;

/// <summary>
/// Paints a fixed-size, full-color raster bitmap with LINEAR sampling — the opposite tradeoff from
/// <see cref="ScribePixelArtBackdrop"/>'s nearest-neighbour: for pre-colored art authored close to its
/// display size (e.g. the assigned-task stamp) that should read as a smooth image rather than hard pixel
/// blocks. A true leaf widget with no child (mirrors LibGUI's own <c>VsIcon</c>/<c>RenderVsIcon</c>): it
/// lays itself out to size×size and draws the bitmap untinted, at its native colors.
///
/// <para><b>Paint-colour hygiene.</b> Same discipline as <see cref="ScribePixelArtBackdrop"/>: force the
/// shared paint opaque white with no filters before drawing, then restore every touched field, since the
/// shared paint outlives this subtree.</para>
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
        private SKImage? image; // FromBitmap wrapper, built lazily and rebuilt only if the source bitmap changes

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
