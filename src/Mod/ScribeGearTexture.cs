using System;
using SkiaSharp;

namespace Scribe;

/// <summary>
/// Procedural generator for the Timer-tab escape wheel (the big "great wheel"). Draws a many-toothed
/// gear in the spirit of the small brown placeholder cog — <b>blocky</b> (flat-topped rectangular teeth,
/// a small quantized palette), <b>unevenly coloured</b> (per-element shade jitter), and full of
/// <b>negative space</b> (open sectors between the spokes, a bored-out hub) rather than a solid disc.
/// This is a placeholder meant to be judged in-game; tune the knobs below or
/// swap it for hand-drawn art — nothing else in the gearworks depends on how the bitmap is produced.
///
/// <para>Deterministic: seeded with a fixed value so the wheel is identical every launch (no
/// <c>Math.random</c>-style flicker between runs). Pure SkiaSharp — no Vintage Story API — and the
/// caller caches/disposes the result on the same path as the loaded PNGs (see
/// <c>ScribeModSystem.GetProceduralGreatWheel</c>), so it is generated at most once.</para>
/// </summary>
internal static class ScribeGearTexture
{
    /// <summary>Tooth count of the generated great wheel. Kept as a public constant so the gearworks widget
    /// can set its <c>EscapeTeeth</c> step to the SAME value (one tick advances exactly one tooth). Many small
    /// teeth so they read as proportional to the small gear's teeth and
    /// mesh cleanly. The value is 32, with teeth sized as if 38 for more space BETWEEN them
    /// — see <see cref="ToothSizeReferenceTeeth"/>. This is the default the
    /// live .geartune knob (<c>WheelTeeth</c>) also starts from and overrides at runtime.</summary>
    public const int Teeth = 32;

    /// <summary>Reference tooth count that fixes each tooth's ABSOLUTE angular width, decoupled from the actual
    /// <see cref="Teeth"/> count. The tooth half-angle is computed from THIS rather than from <c>teeth</c>, so
    /// lowering the real tooth count only widens the GAPS between teeth — the teeth themselves stay sized as if
    /// the wheel had this many (32 teeth, spacing as if 38). Set equal to <see cref="Teeth"/>
    /// to go back to count-proportional teeth.</summary>
    private const int ToothSizeReferenceTeeth = 38;

    /// <summary>Render a great wheel to a fresh <see cref="SKBitmap"/> of <paramref name="size"/>² px.
    /// The caller owns the returned bitmap (cache + dispose).
    ///
    /// <para><b>Why 512, not the displayed ~212px.</b> The wheel is drawn at <c>escapeSize = 212.5 × scale</c>
    /// LOGICAL px, and on a retina GUI canvas that's ~2× again in PHYSICAL px — so a source sized to the logical
    /// dimension gets upscaled ~2× and <c>DrawMaskedBox</c>'s bilinear resample blurs it. Generating at 512
    /// (larger than the displayed physical size at the default Pixel Art Size) means the draw only ever
    /// DOWNSAMPLES, which stays crisp. Still upscales slightly at very high Pixel Art Size settings; the
    /// complete fix would size the generation to the actual displayed physical dimension.</para></summary>
    public static SKBitmap GreatWheel(int size = 512, int teeth = Teeth, int toothSpacingRef = ToothSizeReferenceTeeth)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);   // negative space is genuinely transparent

        float cx = size / 2f, cy = size / 2f;
        float rRoot  = size * 0.42f;           // tooth roots / rim outer edge
        // Tooth length over the root is 0.042 of size, so the tip sits at rRoot + 0.042 = 0.462. Short, stubby teeth.
        float rOuter = size * 0.462f;          // tooth tips
        float rRimIn = size * 0.34f;           // rim inner edge (spokes start here)
        float rHub   = size * 0.16f;           // hub outer edge (spokes end here)
        float rBore  = size * 0.055f;          // central bore (transparent)

        // Deterministic per-element shade jitter. A fixed seed keeps the wheel stable across launches;
        // System.Random is fine here (this is app code, not a workflow script's forbidden RNG).
        var rng = new Random(0x5CB1E);

        // Metallic gray-steel palette sampled from the small gear PNG (dominant shades ≈
        // #605050/#606050/#505040/#706060), quantized to a few flat steps so the fill reads chunky, not
        // gradient. Distinctly steel (not brown, not teal) — the wheel is a plain regulator, not a temporal
        // gear. Per-element variation comes from the random pick.
        SKColor[] palette =
        {
            new(0x60, 0x50, 0x50), new(0x60, 0x58, 0x50), new(0x50, 0x48, 0x40),
            new(0x70, 0x60, 0x60), new(0x50, 0x40, 0x40), new(0x70, 0x68, 0x60),
        };
        SKColor Shade() => palette[rng.Next(palette.Length)];

        // Flat filled shapes for teeth/spokes — no outlines on THOSE (outlines read as drawn-on lines).
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // A single solid stroke, a shade darker than the fill palette, drawn ONLY on the CIRCULAR portions
        // (rim outer/inner edge + hub) — NOT teeth or spokes. Defines the round structure without
        // the "drawn-on line" look that tooth outlines would have.
        using var edge = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(1f, size * 0.008f),
            Color = new SKColor(0x38, 0x30, 0x30),   // darker gray than the #50-#70 fill palette
        };

        // ── Teeth: flat-topped RECTANGULAR blocks around the rim (blocky, each its own shade). The width is
        //    pinned to the spacing-reference count, NOT the live `teeth`, so lowering `teeth` only WIDENS the
        //    gaps and keeps each tooth the same absolute size. The tooth is a true rectangle
        //    (PARALLEL sides square to its bisector), not a radial trapezoid — a trapezoid's sides converge on
        //    the center so its flat-chord top meets them at slightly-ACUTE outer corners; parallel sides give
        //    clean 90° outer corners. ──
        float toothHalfAngle = (MathF.PI / toothSpacingRef) * 0.52f;   // gaps = the rest → negative space
        // Linear half-width of the tooth block: the chord width at the root (chord = 2·r·sin(halfAngle)).
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

        // ── Stroke the CIRCULAR edges only: rim outer (root) + rim inner + hub outer. Teeth and
        //    spokes stay strokeless. Drawn last so the outlines sit on top of the fills. ──
        canvas.DrawCircle(cx, cy, rRoot,  edge);   // rim outer edge (where teeth meet the rim)
        canvas.DrawCircle(cx, cy, rRimIn, edge);   // rim inner edge
        canvas.DrawCircle(cx, cy, rHub,   edge);   // hub outer edge

        return bmp;
    }

    /// <summary>Append a RECTANGULAR tooth centered on angle <paramref name="a"/>: a block whose two sides are
    /// PARALLEL (both offset ±<paramref name="halfWidth"/> from the bisector, square to it) and whose inner/outer
    /// ends are flat. Because the sides don't converge toward the center, the flat outer end meets them at true
    /// 90° corners (unlike <see cref="AddArcQuad"/>'s radial trapezoid, whose converging sides give acute outer
    /// corners). Used for the great wheel's teeth so they read as square-cornered blocks.</summary>
    private static void AddRectTooth(SKPath path, float cx, float cy, float rIn, float rOut, float a, float halfWidth)
    {
        // Unit vectors: radial (out along the tooth bisector) and tangential (across the tooth width).
        float rx = MathF.Cos(a),  ry = MathF.Sin(a);
        float tx = -MathF.Sin(a), ty = MathF.Cos(a);
        // Inner-left, outer-left, outer-right, inner-right — a rectangle rIn..rOut long, 2·halfWidth wide.
        path.MoveTo(cx + rIn  * rx - halfWidth * tx, cy + rIn  * ry - halfWidth * ty);
        path.LineTo(cx + rOut * rx - halfWidth * tx, cy + rOut * ry - halfWidth * ty);
        path.LineTo(cx + rOut * rx + halfWidth * tx, cy + rOut * ry + halfWidth * ty);
        path.LineTo(cx + rIn  * rx + halfWidth * tx, cy + rIn  * ry + halfWidth * ty);
        path.Close();
    }

    /// <summary>Append a 4-point quad bounded by two radii and two angles (a rim-aligned block). Used for
    /// both teeth and spokes so they share the same flat-topped, straight-sided blocky silhouette.</summary>
    private static void AddArcQuad(SKPath path, float cx, float cy, float rIn, float rOut, float a0, float a1)
    {
        path.MoveTo(cx + rIn  * MathF.Cos(a0), cy + rIn  * MathF.Sin(a0));
        path.LineTo(cx + rOut * MathF.Cos(a0), cy + rOut * MathF.Sin(a0));
        path.LineTo(cx + rOut * MathF.Cos(a1), cy + rOut * MathF.Sin(a1));
        path.LineTo(cx + rIn  * MathF.Cos(a1), cy + rIn  * MathF.Sin(a1));
        path.Close();
    }
}
