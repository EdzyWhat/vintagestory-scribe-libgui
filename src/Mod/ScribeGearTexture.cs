using System;
using SkiaSharp;

namespace Scribe;

/// <summary>
/// Procedural generator for the Timer-tab escape wheel: a many-toothed, <b>blocky</b> gear (flat-topped
/// rectangular teeth, small quantized palette, per-element shade jitter) with <b>negative space</b> (open
/// sectors between spokes, bored-out hub). A placeholder: nothing else in the gearworks depends on how the
/// bitmap is produced.
///
/// <para>Deterministic (fixed seed) and pure SkiaSharp (no VS API); the caller caches/disposes the result
/// (see <c>ScribeModSystem.GetProceduralGreatWheel</c>).</para>
/// </summary>
internal static class ScribeGearTexture
{
    /// <summary>Tooth count of the generated great wheel. The gearworks widget sets its <c>EscapeTeeth</c> step
    /// to the SAME value (one tick = one tooth). Default for the live .geartune <c>WheelTeeth</c> knob; teeth are
    /// sized as if <see cref="ToothSizeReferenceTeeth"/>.</summary>
    public const int Teeth = 32;

    /// <summary>Reference tooth count fixing each tooth's ABSOLUTE angular width, decoupled from
    /// <see cref="Teeth"/>: lowering the real count only widens the gaps. Set equal to <see cref="Teeth"/> for
    /// count-proportional teeth.</summary>
    private const int ToothSizeReferenceTeeth = 38;

    /// <summary>Render a great wheel to a fresh <see cref="SKBitmap"/> of <paramref name="size"/>² px; the
    /// caller owns it (cache + dispose).
    ///
    /// <para><b>Why 512:</b> on a retina canvas the ~212 logical px wheel is ~2× in physical px, and
    /// <c>DrawMaskedBox</c>'s bilinear resample blurs an upscaled source. At 512 the draw only DOWNSAMPLES (it
    /// still upscales at very high Pixel Art Size).</para></summary>
    public static SKBitmap GreatWheel(int size = 512, int teeth = Teeth, int toothSpacingRef = ToothSizeReferenceTeeth)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);   // negative space is genuinely transparent

        float cx = size / 2f, cy = size / 2f;
        float rRoot  = size * 0.42f;           // tooth roots / rim outer edge
        float rOuter = size * 0.462f;          // tooth tips
        float rRimIn = size * 0.34f;           // rim inner edge (spokes start here)
        float rHub   = size * 0.16f;           // hub outer edge (spokes end here)
        float rBore  = size * 0.055f;          // central bore (transparent)

        // Fixed seed keeps the wheel stable across launches.
        var rng = new Random(0x5CB1E);

        // Gray-steel palette sampled from the small gear PNG, quantized to a few flat steps so the fill reads
        // chunky. Distinctly steel: the wheel is a plain regulator, not a temporal gear.
        SKColor[] palette =
        {
            new(0x60, 0x50, 0x50), new(0x60, 0x58, 0x50), new(0x50, 0x48, 0x40),
            new(0x70, 0x60, 0x60), new(0x50, 0x40, 0x40), new(0x70, 0x68, 0x60),
        };
        SKColor Shade() => palette[rng.Next(palette.Length)];

        // Flat filled shapes for teeth/spokes — no outlines on THOSE (outlines read as drawn-on lines).
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // One solid stroke, a shade darker than the fills, on the CIRCULAR portions only (rim edges + hub);
        // teeth and spokes stay strokeless to avoid a drawn-on look.
        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(1f, size * 0.008f),
            Color = new SKColor(0x38, 0x30, 0x30),   // darker gray than the #50-#70 fill palette
        };

        // ── Teeth: flat-topped RECTANGULAR blocks. Width is pinned to the spacing-reference count, NOT the
        //    live `teeth`, so fewer teeth only WIDEN the gaps. Sides are PARALLEL (a radial trapezoid's
        //    converging sides would give acute outer corners). ──
        float toothHalfAngle = (MathF.PI / toothSpacingRef) * 0.52f;   // gaps = the rest → negative space
        float toothHalfWidth = rRoot * MathF.Sin(toothHalfAngle);
        for (int i = 0; i < teeth; i++)
        {
            float a = i * (MathF.PI * 2f / teeth);
            using var path = new SKPath();
            AddRectTooth(path, cx, cy, rRoot, rOuter, a, toothHalfWidth);
            fill.Color = Shade();
            canvas.DrawPath(path, fill);
        }

        // ── Rim: solid ring the teeth sit on (root..rimIn), one flat shade. ──
        using (var rim = new SKPath())
        {
            rim.AddCircle(cx, cy, rRoot);
            rim.AddCircle(cx, cy, rRimIn);
            rim.FillType = SKPathFillType.EvenOdd;   // annulus (hole in the middle)
            fill.Color = palette[1];
            canvas.DrawPath(rim, fill);
        }

        // ── Spokes: a handful of thick blocky arms hub→rim, leaving open sectors between them. ──
        const int spokes = 6;
        float spokeHalfAngle = 0.16f;   // narrow arms → wide negative-space sectors
        for (int i = 0; i < spokes; i++)
        {
            float a = i * (MathF.PI * 2f / spokes);
            using var path = new SKPath();
            AddArcQuad(path, cx, cy, rHub * 0.9f, rRimIn + size * 0.004f, a - spokeHalfAngle, a + spokeHalfAngle);
            fill.Color = Shade();
            canvas.DrawPath(path, fill);
        }

        // ── Hub: filled disc with a bored-out transparent centre. ──
        using (var hub = new SKPath())
        {
            hub.AddCircle(cx, cy, rHub);
            hub.AddCircle(cx, cy, rBore);
            hub.FillType = SKPathFillType.EvenOdd;
            fill.Color = palette[3];
            canvas.DrawPath(hub, fill);
        }

        // ── Stroke the CIRCULAR edges only, drawn last so the outlines sit on the fills. ──
        canvas.DrawCircle(cx, cy, rRoot,  edge);   // rim outer edge (where teeth meet the rim)
        canvas.DrawCircle(cx, cy, rRimIn, edge);   // rim inner edge
        canvas.DrawCircle(cx, cy, rHub,   edge);   // hub outer edge

        return bmp;
    }

    /// <summary>Append a RECTANGULAR tooth centered on angle <paramref name="a"/>: PARALLEL sides
    /// (±<paramref name="halfWidth"/> from the bisector) and flat ends, so the outer end has true 90° corners
    /// (unlike <see cref="AddArcQuad"/>'s radial trapezoid).</summary>
    private static void AddRectTooth(SKPath path, float cx, float cy, float rIn, float rOut, float a, float halfWidth)
    {
        // Unit vectors: radial (out along the tooth bisector) and tangential (across the tooth width).
        float rx = MathF.Cos(a),  ry = MathF.Sin(a);
        float tx = -MathF.Sin(a), ty = MathF.Cos(a);
        path.MoveTo(cx + rIn  * rx - halfWidth * tx, cy + rIn  * ry - halfWidth * ty);
        path.LineTo(cx + rOut * rx - halfWidth * tx, cy + rOut * ry - halfWidth * ty);
        path.LineTo(cx + rOut * rx + halfWidth * tx, cy + rOut * ry + halfWidth * ty);
        path.LineTo(cx + rIn  * rx + halfWidth * tx, cy + rIn  * ry + halfWidth * ty);
        path.Close();
    }

    /// <summary>Append a 4-point quad bounded by two radii and two angles (a rim-aligned block).</summary>
    private static void AddArcQuad(SKPath path, float cx, float cy, float rIn, float rOut, float a0, float a1)
    {
        path.MoveTo(cx + rIn  * MathF.Cos(a0), cy + rIn  * MathF.Sin(a0));
        path.LineTo(cx + rOut * MathF.Cos(a0), cy + rOut * MathF.Sin(a0));
        path.LineTo(cx + rOut * MathF.Cos(a1), cy + rOut * MathF.Sin(a1));
        path.LineTo(cx + rIn  * MathF.Cos(a1), cy + rIn  * MathF.Sin(a1));
        path.Close();
    }
}
