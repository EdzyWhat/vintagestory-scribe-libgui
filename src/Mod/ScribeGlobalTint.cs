using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key, Theme, ThemeData, ColorScheme
using Gui.Widgets.Overlay;        // Tooltip
using OpenTK.Mathematics;         // Vector4
using SkiaSharp;                  // SKPaint, SKColorFilter, SKColors

namespace Scribe;

/// <summary>
/// Shades the whole composed Scribe dialog by the light reaching the player. A transparent single-child wrapper
/// placed once at the shared body/backdrop wrap point in <see cref="ScribeDialogBase"/>: it renders its child
/// into an offscreen layer (<c>SaveLayer</c>) and applies ONE <see cref="SKColorFilter.CreateColorMatrix"/> on
/// composite that scales brightness and skews color temperature. Backdrop, chrome and text are shaded together,
/// so every surface inherits it with no per-dialog wiring.
///
/// <para><b>Why a color matrix, not a multiply overlay.</b> A matrix composites predictably under premultiplied
/// alpha and can brighten as well as darken; a multiply rect can only darken and chews anti-aliased alpha edges.</para>
///
/// <para><b>Brightness comes pre-shaped.</b> <c>brightness</c> and <c>tint</c> are already the output of the
/// response curve + the sampler's quantization (<see cref="ScribeAmbientLightSampler"/>). Full brightness with a
/// neutral (1,1,1) tint is the identity, so a fully-lit dialog is unchanged.</para>
///
/// <para><b>Paint-cache discipline.</b> The parent reconfigures this widget only when the QUANTIZED shade changes,
/// so a static scene keeps its cached <c>SKPicture</c>. Color filters are cached by quantized key and NEVER
/// disposed (disposing a filter mid-record corrupts the recording canvas; see <see cref="ScribeGearEffect"/>).
/// The <c>SaveLayer</c> uses its OWN cached paint, so it never mutates <c>SharedPaint</c>.</para>
/// </summary>
internal sealed class ScribeGlobalTint : SingleChildWidget
{
    private readonly float brightness;
    private readonly float tintR, tintG, tintB;

    public ScribeGlobalTint(Widget child, float brightness, float tintR, float tintG, float tintB,
        Gui.Widgets.Framework.Key? key = null) : base(child, key)
    {
        this.brightness = brightness;
        this.tintR = tintR;
        this.tintG = tintG;
        this.tintB = tintB;
    }

    public override RenderObject CreateRenderObject() => new GlobalTintRender(brightness, tintR, tintG, tintB);

    public override void UpdateRenderObject(RenderObject renderObject)
        => ((GlobalTintRender)renderObject).Configure(brightness, tintR, tintG, tintB);

    /// <summary>Fraction of the illumination shade delta applied to hover/overlay surfaces (tooltips, drop-up
    /// menus) versus the body's full <c>1.0</c>; 0.9 = 10% less darkening, a legibility bump for transient text.
    /// Every hover wrap goes through <see cref="ForHover"/> so they share it.</summary>
    public const float HoverStrength = 0.9f;

    /// <summary>Wrap <paramref name="child"/> in the illumination shade at reduced <see cref="HoverStrength"/>.
    /// Hover surfaces render in LibGUI's Overlay layer, outside the body's <see cref="ScribeGlobalTint"/>, so they
    /// need their own shade. Identity in gives identity out, so full daylight still hits the
    /// <see cref="IsIdentity"/> fast path.</summary>
    public static ScribeGlobalTint ForHover(Widget child, ScribeAmbientLightSampler.Shade shade,
        Gui.Widgets.Framework.Key? key = null)
        => new(child,
            brightness: TowardIdentity(shade.Brightness, HoverStrength),
            tintR: TowardIdentity(shade.TintR, HoverStrength),
            tintG: TowardIdentity(shade.TintG, HoverStrength),
            tintB: TowardIdentity(shade.TintB, HoverStrength),
            key);

    /// <summary>Blend one shade channel toward identity (1.0) keeping <paramref name="keep"/> of its delta:
    /// <c>keep = 1</c> unchanged, <c>keep = 0</c> identity.</summary>
    private static float TowardIdentity(float v, float keep) => 1f + (v - 1f) * keep;

    /// <summary>Shade one solid RGB color (a tooltip bubble's <c>Background</c>/<c>Border</c>) by the shade at
    /// <see cref="HoverStrength"/>, matching <see cref="ForHover"/>'s matrix on overlay content. Alpha is
    /// preserved.</summary>
    public static Vector4 ShadeColor(Vector4 color, ScribeAmbientLightSampler.Shade shade)
    {
        float b = TowardIdentity(shade.Brightness, HoverStrength);
        return new Vector4(
            color.X * b * TowardIdentity(shade.TintR, HoverStrength),
            color.Y * b * TowardIdentity(shade.TintG, HoverStrength),
            color.Z * b * TowardIdentity(shade.TintB, HoverStrength),
            color.W);
    }

    /// <summary>Build a <see cref="Tooltip"/> whose BUBBLE (not just its text) tracks the illumination shade.
    /// The stock <see cref="Tooltip"/> reads <c>Background</c>/<c>Border</c> from the nearest <see cref="Theme"/>
    /// ABOVE it in the main tree, outside both the body's tint layer and <see cref="ForHover"/>, so in low light
    /// the text would dim while the bubble stayed full-brightness.
    ///
    /// <para>The fix is a Theme "sandwich": the Tooltip is wrapped in a Theme whose scheme is
    /// <paramref name="baseTheme"/> with <c>Background</c>+<c>Border</c> run through <see cref="ShadeColor"/>. The
    /// trigger <paramref name="child"/> would inherit that shaded scheme and darken TWICE (again in the body's tint
    /// layer), so it is re-wrapped in the unshaded <paramref name="baseTheme"/>. Overlay content mounts in the
    /// global overlay root and never sees the wrapper, so it is shaded directly via <see cref="ForHover"/>;
    /// callers pass RAW content.</para>
    ///
    /// <para>At full daylight the shade is the identity, so the sandwich is skipped and a plain Tooltip is
    /// returned.</para></summary>
    public static Widget ShadedTooltip(Widget child, Widget content, ThemeData baseTheme,
        ScribeAmbientLightSampler.Shade shade, TimeSpan? waitDuration = null)
    {
        Widget shadedContent = ForHover(content, shade);

        // Identity hover shade: the Theme sandwich would be a no-op, so return the plain Tooltip.
        if (IsIdentity(
                TowardIdentity(shade.Brightness, HoverStrength),
                TowardIdentity(shade.TintR, HoverStrength),
                TowardIdentity(shade.TintG, HoverStrength),
                TowardIdentity(shade.TintB, HoverStrength)))
            return new Tooltip(child: child, content: shadedContent,
                waitDuration: waitDuration, useGlobalOverlay: true);

        ColorScheme shadedScheme = baseTheme.ColorScheme with
        {
            Background = ShadeColor(baseTheme.ColorScheme.Background, shade),
            Border = ShadeColor(baseTheme.ColorScheme.Border, shade),
        };
        // Only Background+Border are read (by the bubble Container).
        return new Theme(new ThemeData(colorScheme: shadedScheme),
            child: new Tooltip(
                child: new Theme(baseTheme, child: child),  // restore unshaded theme for the trigger
                content: shadedContent,
                waitDuration: waitDuration,
                useGlobalOverlay: true));
    }

    /// <summary>Cached brightness+tint color-matrix filters keyed by the packed quantized shade. Never disposed.</summary>
    private static readonly Dictionary<int, SKColorFilter> FilterCache = new();

    /// <summary>The reused <c>SaveLayer</c> paint; never <c>SharedPaint</c>, so it can't leak into sibling draws.</summary>
    private static readonly SKPaint LayerPaint = new() { Color = SKColors.White };

    /// <summary>Whether the shade is the identity; then the SaveLayer is skipped and the child paints directly.</summary>
    private static bool IsIdentity(float brightness, float r, float g, float b) =>
        brightness >= 0.999f && r >= 0.999f && g >= 0.999f && b >= 0.999f;

    /// <summary>Build (or fetch) the color matrix multiplying each channel by brightness×tintChannel. Alpha
    /// passes through unchanged so edges stay anti-aliased and transparent regions stay transparent.</summary>
    private static SKColorFilter FilterFor(float brightness, float r, float g, float b)
    {
        // Pack the quantized channels into an integer key (8 bits each).
        int key = (Q(brightness) << 24) | (Q(r) << 16) | (Q(g) << 8) | Q(b);
        if (FilterCache.TryGetValue(key, out var f)) return f;

        float sr = brightness * r;
        float sg = brightness * g;
        float sb = brightness * b;
        float[] m =
        {
            sr, 0,  0,  0, 0,
            0,  sg, 0,  0, 0,
            0,  0,  sb, 0, 0,
            0,  0,  0,  1, 0,
        };
        var made = SKColorFilter.CreateColorMatrix(m);
        FilterCache[key] = made;
        return made;
    }

    private static int Q(float v) => (int)(System.MathF.Round(Scribe.Core.ScribeMath.Clamp01(v) * 255f));

    private sealed class GlobalTintRender : RenderProxyBox
    {
        private float brightness, tintR, tintG, tintB;

        public GlobalTintRender(float brightness, float r, float g, float b)
            => Configure(brightness, r, g, b);

        public void Configure(float brightness, float r, float g, float b)
        {
            // Repaint only when a channel ACTUALLY changed. LibGUI calls UpdateRenderObject on EVERY reconcile but
            // does NOT invalidate the render object, so a changed shade must MarkNeedsPaint itself or a static
            // dialog (e.g. a read-only tablet) would keep the old tint because its picture never re-records. The
            // equality guard stops unrelated rebuilds from forcing needless re-records.
            if (brightness == this.brightness && r == tintR && g == tintG && b == tintB) return;
            this.brightness = brightness;
            tintR = r; tintG = g; tintB = b;
            MarkNeedsPaint();
        }

        public override void Paint(PaintingContext context)
        {
            if (context.Canvas is null || IsIdentity(brightness, tintR, tintG, tintB))
            {
                base.Paint(context);   // full-bright neutral → no layer, no filter, zero cost
                return;
            }

            // Render the child into an offscreen layer and apply the matrix on composite.
            LayerPaint.ColorFilter = FilterFor(brightness, tintR, tintG, tintB);
            context.Canvas.SaveLayer(LayerPaint);
            base.Paint(context);
            context.Canvas.Restore();
        }
    }
}
