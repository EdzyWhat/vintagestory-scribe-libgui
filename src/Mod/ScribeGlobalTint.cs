using System.Collections.Generic;
using Gui.Core.Framework;         // RenderProxyBox, RenderObject
using Gui.Rendering;              // PaintingContext
using Gui.Widgets.Framework;      // SingleChildWidget, Widget, Key
using SkiaSharp;                  // SKPaint, SKColorFilter, SKColors

namespace Scribe;

/// <summary>
/// Shades the whole composed Scribe dialog by the light reaching the player (respect-local-illumination
/// D2/D6). A transparent single-child wrapper placed once at the shared body/backdrop wrap point in
/// <see cref="ScribeDialogBase"/>: it renders its child subtree into an offscreen layer
/// (<c>SaveLayer</c>) and applies ONE <see cref="SKColorFilter.CreateColorMatrix"/> on composite — a single
/// filter that both scales brightness and skews color temperature. Because the filter runs on the flattened
/// layer, backdrop + chrome + text are all shaded together and every surface (lectern/notebook/tablet)
/// inherits it with no per-dialog wiring.
///
/// <para><b>Why a color matrix, not a multiply overlay.</b> A matrix can BRIGHTEN as well as darken and
/// composites predictably under premultiplied alpha; a multiply rect can only darken and chews the
/// anti-aliased text/backdrop alpha edges. Here we only ever darken/tint (brightness ≤ 1), but the matrix
/// keeps the door open and composites cleanly. Same family as the shipped
/// <see cref="ScribeGearEffect"/> (a <c>ColorFilter</c> swap on a <see cref="RenderProxyBox"/>) and generalizes
/// <c>RenderOpacity</c> (an alpha-only <c>SaveLayer</c>).</para>
///
/// <para><b>Brightness comes pre-shaped.</b> The <c>brightness</c> and <c>tint</c> passed in are already the
/// output of the author-drawn non-linear response curve + the sampler's quantization
/// (<see cref="ScribeAmbientLightSampler"/>); this widget only turns them into a matrix. Full brightness with
/// a neutral (1,1,1) tint is the identity — the pre-illumination look, so a fully-lit dialog is visually
/// unchanged.</para>
///
/// <para><b>Paint-cache discipline (D3/D6).</b> The parent only reconfigures this widget (and thus marks it
/// needing paint, forcing LibGUI to re-record its <c>SKPicture</c>) when the QUANTIZED shade changes — on a
/// static scene the value is stable and the cache stays valid. Color filters are cached by their quantized
/// key and NEVER disposed (disposing a filter mid-record corrupts the recording canvas — the
/// <see cref="ScribeGearEffect"/> discipline). The <c>SaveLayer</c> uses its OWN cached paint, so it never
/// mutates <c>SharedPaint</c> and can't leak paint state into sibling draws (the
/// tablet-transparent-backdrop-sharedpaint-leak class).</para>
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

    /// <summary>Fraction of the illumination shade applied to Scribe hover/overlay surfaces (tooltips,
    /// drop-up-style menus), versus the dialog body's full <c>1.0</c>. <c>0.9</c> = keep 90% of the shade
    /// delta, i.e. 10% less darkening — a deliberate legibility bump for transient text floating above the
    /// body (user decision, refine-scribe-hover-tooltips D1). This is the single tunable knob; every hover
    /// wrap goes through <see cref="ForHover"/> so they all share it.</summary>
    public const float HoverStrength = 0.9f;

    /// <summary>Wrap <paramref name="child"/> in the illumination shade at reduced <see cref="HoverStrength"/>.
    /// Hover surfaces render in LibGUI's Overlay layer — outside the dialog body's own <see cref="ScribeGlobalTint"/>
    /// wrap — so without this they'd stay full-brightness while the body is dimmed. The live shade is blended
    /// a fraction of the way back toward identity (no tint) so hovers track the same light but darken slightly
    /// less than the body. Identity in → identity out, so at full daylight the <see cref="IsIdentity"/>
    /// fast-path still elides the SaveLayer entirely.</summary>
    public static ScribeGlobalTint ForHover(Widget child, ScribeAmbientLightSampler.Shade shade,
        Gui.Widgets.Framework.Key? key = null)
        => new(child,
            brightness: TowardIdentity(shade.Brightness, HoverStrength),
            tintR: TowardIdentity(shade.TintR, HoverStrength),
            tintG: TowardIdentity(shade.TintG, HoverStrength),
            tintB: TowardIdentity(shade.TintB, HoverStrength),
            key);

    /// <summary>Blend one shade channel toward identity (1.0) keeping <paramref name="keep"/> of its delta:
    /// <c>keep = 1</c> → unchanged, <c>keep = 0</c> → identity. So a dark <c>0.5</c> at <c>keep = 0.9</c>
    /// becomes <c>0.55</c> (less darkening), and an identity <c>1.0</c> stays <c>1.0</c>.</summary>
    private static float TowardIdentity(float v, float keep) => 1f + (v - 1f) * keep;

    /// <summary>Cached brightness+tint color-matrix filters keyed by the packed quantized shade. Never
    /// disposed (see class remarks — only a handful of distinct buckets ever exist).</summary>
    private static readonly Dictionary<int, SKColorFilter> FilterCache = new();

    /// <summary>The <c>SaveLayer</c> paint, reused across frames. Only its <c>ColorFilter</c> changes; it is
    /// never <c>SharedPaint</c>, so it can't leak into sibling draws.</summary>
    private static readonly SKPaint LayerPaint = new() { Color = SKColors.White };

    /// <summary>Whether this shade is the identity (full brightness, neutral tint) — then we skip the
    /// SaveLayer entirely and paint the child directly, so a fully-lit dialog pays nothing and looks exactly
    /// as it did pre-illumination.</summary>
    private static bool IsIdentity(float brightness, float r, float g, float b) =>
        brightness >= 0.999f && r >= 0.999f && g >= 0.999f && b >= 0.999f;

    /// <summary>Build (or fetch) the color matrix that multiplies each channel by brightness×tintChannel.
    /// A 4×5 row-major matrix: out.r = (brightness*tintR)*in.r, etc.; alpha passes through unchanged so the
    /// dialog's edges stay anti-aliased and transparent regions stay transparent. Keyed by the quantized
    /// shade so the cache is tiny and stable.</summary>
    private static SKColorFilter FilterFor(float brightness, float r, float g, float b)
    {
        // Pack the already-quantized channels into a stable integer key (8 bits each is ample for our
        // ≤32 brightness / ≤16 hue buckets).
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

    private static int Q(float v) => (int)(System.MathF.Round((v < 0f ? 0f : v > 1f ? 1f : v) * 255f));

    private sealed class GlobalTintRender : RenderProxyBox
    {
        private float brightness, tintR, tintG, tintB;

        public GlobalTintRender(float brightness, float r, float g, float b)
            => Configure(brightness, r, g, b);

        public void Configure(float brightness, float r, float g, float b)
        {
            // Repaint only when a channel ACTUALLY changed — and, crucially, mark ourselves needing paint when
            // one does. LibGUI's RenderObjectElement.Update calls UpdateRenderObject (→ here) on EVERY reconcile
            // (any RebuildBody, for any reason) but does NOT itself invalidate the render object; a render object
            // must mark its own cached SKPicture dirty when a VISUAL field changes. Without the MarkNeedsPaint
            // below, a shade change on a dialog whose child tree is otherwise unchanged silently kept the OLD
            // tint: the picture never re-recorded. Animated surfaces (a wet tablet's live cuneiform/caret, the
            // Lectern's row animations) mark needs-paint every frame anyway, so they picked up the new tint for
            // free — which is exactly why the "never darkens" bug showed ONLY on a STATIC read-only (hard/fired)
            // tablet, most visibly one pulled empty from Creative: nothing else marked it dirty, so it stayed at
            // the bright shade it first recorded. The equality guard keeps unrelated rebuilds (which re-run this
            // with the same shade) from forcing needless re-records, preserving the paint-cache discipline (D3).
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

            // Render the whole child subtree into an offscreen layer and apply the brightness/tint matrix on
            // composite. LayerPaint is our own paint (not SharedPaint), so nothing leaks to siblings.
            LayerPaint.ColorFilter = FilterFor(brightness, tintR, tintG, tintB);
            context.Canvas.SaveLayer(LayerPaint);
            base.Paint(context);
            context.Canvas.Restore();
        }
    }
}
