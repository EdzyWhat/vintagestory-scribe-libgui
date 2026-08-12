using System;
using Scribe.Core;                       // ScribeBrightnessCurve, ScribePlayerSettings
using Vintagestory.API.Client;           // ICoreClientAPI
using Vintagestory.API.Common;           // EntityPlayer, CollectibleObject.GetLightHsv, IWorldAccessor.BlockLightLevels
using Vintagestory.API.MathTools;        // Vec4f, Vec3f, BlockPos, ColorUtil

namespace Scribe;

/// <summary>
/// Samples the light reaching the client player each frame and folds it into a single quantized
/// (brightness, tint) value that the <see cref="ScribeGlobalTint"/> widget renders the Scribe GUI at
/// (respect-local-illumination D1/D3). Read on the render/main thread only — block-accessor reads off the
/// relight background thread are unsafe.
///
/// <para><b>Read the engine's LIVE result, don't bake.</b> Every light input is read fresh from an engine
/// source each frame, never copied into a hardcoded table. So any mod that changes the light where the player
/// stands — WarmerLighting, ImmersiveLight, or a future one — is honored automatically, because we shade off the
/// engine's computed light, not a snapshot of what vanilla emits.</para>
///
/// <para><b>Three inputs, as the engine does it.</b> The engine's canonical "light the shader multiplies a
/// surface by" (<c>IRenderAPI.PreparedStandardShader</c>) combines block-grid light + ambient, and the player's
/// held light is added separately (<c>EntityPlayer.LightHsv</c>); this mirrors all three:
/// <list type="bullet">
/// <item><see cref="IBlockAccessor.GetLightRGBs"/> → a <c>Vec4f</c> whose XYZ is the block-light RGB (a
/// torch's warm hue is baked in via each block's <c>LightHsv</c>) and whose W is the sun-brightness scalar
/// (0..1).</item>
/// <item><see cref="IAmbientManager.BlendedAmbientColor"/> — the sky/daylight COLOR (absent from the block
/// grid) — and <see cref="IAmbientManager.BlendedSceneBrightness"/> (weather/rain darkening).</item>
/// <item><b>Held light</b> — the item in either hand (<c>RightHandItemSlot</c>/<c>LeftHandItemSlot</c> →
/// <c>Collectible.GetLightHsv</c>), which the block grid at the player's own position does NOT contain (it is
/// added dynamically as an entity light). Folded in by MAX (VS light is max-based, not additive), with its
/// level mapped through the live <c>BlockLightLevels</c> table and its hue taken straight from the item's
/// <c>lightHsv</c> so a held torch/lantern/oil-lamp each read their own color temperature.</item>
/// </list>
/// The block grid alone makes daylight look colorless and misses rain; the ambient alone misses torch warmth;
/// neither includes the player's own held torch. All three contribute here.</para>
///
/// <para><b>Smoothing.</b> The folded shade is eased toward its new value each frame (exponential, ~400ms) so
/// walking through changing light glides instead of snapping between quantization buckets (see
/// <see cref="SmoothingTau"/>). A transition re-records the paint cache for the few frames it takes to settle;
/// a static scene settles and then holds, so the cache stays valid at rest.</para>
///
/// <para><b>Non-linear brightness.</b> The combined RAW brightness is passed through
/// <see cref="ScribeBrightnessCurve"/> (the author-drawn response curve), NOT used linearly, so darkness is
/// punishing but a little light already reads comfortably. The curve's x=0 floor is the player's
/// <see cref="ScribePlayerSettings.IlluminationFloor"/>.</para>
///
/// <para><b>Quantization (D3).</b> LibGUI caches each dialog's paint into an <c>SKPicture</c> and only
/// re-records when a widget marks itself needing paint. A continuously-varying tint would re-record every
/// frame and defeat the cache on the pixel-art parchment backdrops. So the output is snapped to coarse
/// brightness + per-channel-hue buckets, and <see cref="Sample"/> reports whether the quantized value
/// actually CHANGED since the last frame — the widget only marks-needs-paint when it did. On a static scene
/// the value is stable and the cache stays valid.</para>
/// </summary>
internal sealed class ScribeAmbientLightSampler
{
    private readonly ICoreClientAPI capi;
    private readonly ScribePlayerSettings settings;

    /// <summary>Brightness quantization: snap the 0..1 curve output to 1/32 steps (~32 buckets). The engine's
    /// own sun-brightness is a 0..32 grid lookup, so this loses little real fidelity while keeping the paint
    /// cache stable frame-to-frame.</summary>
    private const int BrightnessSteps = 32;

    /// <summary>Hue quantization: snap each tint channel to 1/16 steps (16 buckets/channel). Coarse enough
    /// that a torch flicker's steady-state or a slow day/night sky shift only re-records the picture a handful
    /// of times across the whole transition, fine enough that the warm/neutral/cool distinction still reads.</summary>
    private const int HueSteps = 16;

    /// <summary>How much of the raw hue skew to keep (the rest is pulled back to neutral). At <c>2/3</c> the
    /// color/temperature effect is reduced by one third from the physical tint — warm light still reads warm,
    /// just less aggressively (author tuning).</summary>
    private const float TintStrength = 2f / 3f;

    /// <summary>Exponential smoothing time-constant (seconds) for the brightness + tint transition. The GUI eases
    /// toward the newly-sampled target instead of snapping between quantization buckets as the player walks
    /// through changing light (author request: smooth the V change, stretched to ~400ms). With <c>τ = 0.2</c> the
    /// value reaches ~86% of a step in 400ms and ~95% in 600ms — a soft glide, not a visible jump. Because this is
    /// a first-order ease toward the CURRENT target each frame (not a fixed-duration tween), it stays continuous
    /// even while the target keeps moving — walking into ever-brighter/darker light just keeps chasing the moving
    /// value with no restart or velocity snap; the only cost of the longer τ is a longer lag/"tail" behind abrupt
    /// changes. A light transition re-records the paint cache for the frames it takes to settle (a bounded,
    /// deliberate relaxation of D3's "only on change"); a STATIC scene still settles and then holds, so the cache
    /// stays valid at rest.</summary>
    private const float SmoothingTau = 0.2f;

    /// <summary>Mod id of Immersive Lanterns (from its <c>modinfo.json</c>). When it is installed it Harmony-
    /// Postfixes <c>CollectibleObject.GetLightHsv</c> to FLICKER a held torch/lantern/lamp's brightness index V
    /// (held only — its patch early-returns when <c>pos != null</c>; V-only, never hue). Our <see cref="TryHeldLight"/>
    /// calls that exact method with <c>pos: null</c>, so we already RECEIVE the flickered value each frame — the only
    /// thing that would erase it is our own smoothing. So when IL is active we let the HELD brightness term bypass the
    /// ease (see <see cref="Smooth"/>) and IL's flicker reappears exactly as the player configured it, with no
    /// dependency, reflection, or flicker-matching code. (unify-held-light-flicker)</summary>
    private const string FlickerModId = "immersivelanterns";

    // Continuous (pre-quantization) smoothed state, eased toward the sampled target each frame. Only the ENVIRONMENT
    // brightness + the hue tint are eased; when a flicker mod is active the HELD brightness rides on top UNSMOOTHED.
    private bool hasSmoothed;
    private float smoothedBrightness;
    private float smoothedR = 1f, smoothedG = 1f, smoothedB = 1f;

    // IL-presence gate (unify-held-light-flicker), resolved once and cached — mod enablement can't change mid-session.
    // Null until first query; when false the sampler takes exactly the pre-existing path (held folded into the one
    // smoothed brightness), so a non-IL player sees no behavioral change and no extra paint-cache churn.
    private bool? flickerModActive;

    // Last reported quantized value, so Sample() can detect a meaningful change.
    private bool hasLast;
    private int lastBrightnessQ;
    private int lastRQ, lastGQ, lastBQ;

    public ScribeAmbientLightSampler(ICoreClientAPI capi, ScribePlayerSettings settings)
    {
        this.capi = capi;
        this.settings = settings;
    }

    /// <summary>The quantized shade to render this frame: a brightness multiplier (0..1, already through the
    /// response curve + floor) and a normalized RGB tint (each 0..1, 1,1,1 = neutral) the GUI's colors are
    /// pushed toward. <see cref="Changed"/> is true only when this differs from the previous frame's value.</summary>
    internal readonly struct Shade
    {
        public readonly float Brightness;
        public readonly float TintR, TintG, TintB;
        public readonly bool Changed;

        public Shade(float brightness, float r, float g, float b, bool changed)
        {
            Brightness = brightness;
            TintR = r; TintG = g; TintB = b;
            Changed = changed;
        }
    }

    /// <summary>Sample the light at the player's position, fold it to a quantized <see cref="Shade"/>, and
    /// report whether it changed since the last call. Must run on the render/main thread. <paramref name="dt"/>
    /// is the frame time (seconds), used to ease the shade smoothly toward the new sample (see
    /// <see cref="SmoothingTau"/>).</summary>
    public Shade Sample(float dt)
    {
        var world = capi.World;
        var entity = world?.Player?.Entity;
        if (world is null || entity is null)
        {
            // No player/world yet (rare, during load) — render neutral/full so we never blank the dialog. No held
            // term here (0), so the reported brightness is the full-bright environment with or without the gate.
            return Smooth(1f, 0f, 1f, 1f, 1f, dt);
        }

        // Player BLOCK position, dimension-aware. Eye vs. feet is immaterial at block granularity, and the
        // block pos is the cheap grid-aligned key the light grid is indexed by (D1: eye/block choice is a
        // tuning detail, block chosen for the stable cache key).
        BlockPos pos = entity.Pos.AsBlockPos;

        // (1) Block-light RGB (XYZ) + sun-brightness scalar (W). GetLightRGBs reads the LIVE light grid, so any
        // mod that changes the light where the player stands (WarmerLighting, ImmersiveLight, …) is reflected
        // automatically — we sample the engine's result, we don't reconstruct light ourselves. Render-thread-safe.
        Vec4f light = world.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        float blockR = light.R, blockG = light.G, blockB = light.B;
        float sun = light.W; // 0..1 sun brightness

        // (2) Sky color + weather/rain darkening from the ambient manager (also live).
        Vec3f sky = capi.Ambient?.BlendedAmbientColor ?? new Vec3f(1f, 1f, 1f);
        float sceneBrightness = capi.Ambient?.BlendedSceneBrightness ?? 1f;

        // (3) HELD light (torch/lantern/oil-lamp in either hand). The block-light GRID does NOT include the
        // player's own held light — it's added dynamically as an entity light (EntityPlayer.LightHsv), so
        // GetLightRGBs at the player's own block misses it. Read the two hand slots exactly as the engine does
        // and fold the held source in by MAX, matching VS's max-based (non-additive) light convention.
        float gridLuma = 0.2126f * blockR + 0.7152f * blockG + 0.0722f * blockB;
        bool held = TryHeldLight(world, entity, out float heldLuma, out float heldR, out float heldG, out float heldB);

        // For the TINT (hue) path only: when the held light out-shines the grid at the player's feet it dominates
        // the block-light term, carrying its own hue (torch warm, lantern cooler — straight from item data). The
        // brightness split below folds held via curve + MAX, so its "dominance" is implicit in that MAX and needs
        // no separate test there.
        float blockLuma = gridLuma;
        if (held && heldLuma > gridLuma)
        {
            blockLuma = heldLuma;
            blockR = heldR; blockG = heldG; blockB = heldB;
        }

        // --- Brightness (split: environment vs. held — unify-held-light-flicker D1) ---
        // ENVIRONMENT = grid light + the sun channel weathered by scene brightness; curve-mapped. This term is
        // ALWAYS eased (τ=0.2s) so walking sun↔shade glides. HELD = the curve-mapped held-light contribution.
        // When a flicker mod (IL) is active the held term rides on top of the smoothed environment UNSMOOTHED
        // (see Smooth) so IL's per-frame V flicker — which TryHeldLight already receives via GetLightHsv —
        // survives instead of being flattened by the low-pass; when it isn't, the two collapse into one smoothed
        // value identical to the pre-split behavior (the curve is monotonic, so max(curve a, curve b) == curve max).
        float floor = ScribePlayerSettings.ClampIlluminationFloor(settings.IlluminationFloor);
        float sunlit = sun * Clamp01(sceneBrightness);
        float envRaw = Clamp01(MathF.Max(gridLuma, sunlit));
        float envBrightness = ScribeBrightnessCurve.Evaluate(envRaw, floor);
        // Held term is 0 (not curve(0)=floor) when no held light, so an absent held light can't floor-lift the MAX.
        float heldBrightness = held ? ScribeBrightnessCurve.Evaluate(Clamp01(heldLuma), floor) : 0f;

        // --- Hue ---
        // The tint the GUI's colors are pushed toward: block/held light carries torch warmth, sky carries
        // daylight hue. Weight block vs. sky by which is actually lighting the player (blockLuma vs. sunlit),
        // so a torch reads warm indoors and daylight reads neutral outdoors. Normalize each resulting tint so
        // its MAX channel is 1 — we already carry absolute level in Brightness, so the tint must only skew hue,
        // never re-darken (a dim-but-warm torch → a warm tint at full brightness-scale, dimmed by Brightness).
        float blockWeight = blockLuma;
        float skyWeight = sunlit;
        float wsum = blockWeight + skyWeight;
        float r, g, b;
        if (wsum <= 1e-4f)
        {
            // Effectively no light of either kind — neutral tint (brightness carries the darkness).
            r = g = b = 1f;
        }
        else
        {
            r = (blockR * blockWeight + sky.R * skyWeight) / wsum;
            g = (blockG * blockWeight + sky.G * skyWeight) / wsum;
            b = (blockB * blockWeight + sky.B * skyWeight) / wsum;
            float max = MathF.Max(r, MathF.Max(g, b));
            if (max <= 1e-4f) { r = g = b = 1f; }
            else { r /= max; g /= max; b /= max; }
        }

        // Reduce the color/temperature effect: pull each channel partway back toward neutral (1) so the hue
        // skew is gentler than the physical light (author tuning — TintStrength). Level is unaffected.
        r = 1f + (r - 1f) * TintStrength;
        g = 1f + (g - 1f) * TintStrength;
        b = 1f + (b - 1f) * TintStrength;

        return Smooth(envBrightness, heldBrightness, r, g, b, dt);
    }

    /// <summary>Whether a held-light flicker mod (Immersive Lanterns) is installed, resolved once and cached — mod
    /// enablement can't change mid-session. When true the held-brightness term bypasses the ease in
    /// <see cref="Smooth"/> so IL's flicker (already present in the value <see cref="TryHeldLight"/> samples via
    /// <c>GetLightHsv</c>) survives; when false the sampler takes exactly the pre-split smoothed path.</summary>
    private bool FlickerModActive => flickerModActive ??= capi.ModLoader.IsModEnabled(FlickerModId);

    /// <summary>Read the light emitted by the item(s) in the player's two hands and convert it to a normalized
    /// (luma, RGB-hue) pair, using the SAME live sources the engine uses. Returns false when no held item emits
    /// light. Hue comes from the item's own <c>lightHsv</c> (so torch/lantern/oil-lamp differ by game data, and
    /// any lighting mod that changes an item's light is honored); luma comes from mapping the merged light-level
    /// index V through the LIVE <see cref="IWorldAccessor.BlockLightLevels"/> table (never a baked constant), so
    /// a held lantern lands on the same curve point as a placed one.</summary>
    private static bool TryHeldLight(IWorldAccessor world, EntityPlayer entity,
        out float luma, out float r, out float g, out float b)
    {
        luma = 0f; r = g = b = 1f;

        // The two hands, exactly as EntityPlayer.LightHsv reads them: active hotbar slot + offhand (slot 11).
        byte[]? rightHsv = entity.RightHandItemSlot?.Itemstack?.Collectible?
            .GetLightHsv(world.BlockAccessor, null, entity.RightHandItemSlot.Itemstack);
        byte[]? leftHsv = entity.LeftHandItemSlot?.Itemstack?.Collectible?
            .GetLightHsv(world.BlockAccessor, null, entity.LeftHandItemSlot.Itemstack);

        // Merge the two hands with the engine's own helper (V-weighted hue blend, max V for level).
        byte[] hsv = ColorUtil.MergeLightHSV(rightHsv, leftHsv);
        if (hsv is null || hsv[2] == 0) return false; // no held light / V=0

        // lightHsv stores INDICES, not 0..255: h∈0..63, s∈0..7, v∈0..31 (ColorUtil Hue/Sat/BrightQuantities).
        int v = hsv[2];

        // Brightness: map V through the LIVE block-light-level table (same table the grid uses), not V/31.
        float[] levels = world.BlockLightLevels;
        luma = (levels is not null && v < levels.Length) ? levels[v] : Clamp01(v / 31f);

        // Hue direction: convert the item's HSV (scaled to 0..255) to RGB at FULL value, then normalize so max
        // channel = 1. Level is carried by luma above, so the tint only expresses the item's color temperature.
        int rgb = ColorUtil.HsvToRgb(hsv[0] * 255 / 63, hsv[1] * 255 / 7, 255);
        float hr = ((rgb >> 16) & 0xFF) / 255f;
        float hg = ((rgb >> 8) & 0xFF) / 255f;
        float hb = (rgb & 0xFF) / 255f;
        float mx = MathF.Max(hr, MathF.Max(hg, hb));
        if (mx > 1e-4f) { r = hr / mx; g = hg / mx; b = hb / mx; }
        return true;
    }

    /// <summary>Ease the continuous shade toward the freshly-sampled target (exponential smoothing, time-constant
    /// <see cref="SmoothingTau"/>), then quantize + report. Smoothing runs BEFORE quantization so the reported
    /// bucket steps through intermediate values over ~400ms instead of snapping, giving a soft glide as the
    /// player moves through changing light.
    ///
    /// <para><b>Environment vs. held (unify-held-light-flicker D1).</b> Only the ENVIRONMENT brightness
    /// (<paramref name="envBrightness"/>) and the hue tint are eased. The HELD brightness
    /// (<paramref name="heldBrightness"/>) is combined by MAX AFTER easing: when <see cref="FlickerModActive"/>
    /// it is taken UNSMOOTHED (adopt-per-frame) so Immersive Lanterns' V flicker — already present in the value
    /// <see cref="TryHeldLight"/> samples — passes straight through; otherwise the held term is smoothed with the
    /// same τ (folded into <c>smoothedBrightness</c>), reproducing the pre-split single-value behavior exactly.</para></summary>
    private Shade Smooth(float envBrightness, float heldBrightness, float r, float g, float b, float dt)
    {
        // When no flicker mod is installed there is nothing to protect from the low-pass, so smooth the combined
        // brightness exactly as before — the held term is folded into the one eased value. This keeps the vanilla
        // path structurally identical (not a runtime branch that merely happens to match).
        bool passHeldThrough = FlickerModActive;
        float smoothTarget = passHeldThrough ? envBrightness : MathF.Max(envBrightness, heldBrightness);

        if (!hasSmoothed)
        {
            // First frame — adopt the target directly so the opening dialog isn't seen fading up from black.
            hasSmoothed = true;
            smoothedBrightness = smoothTarget;
            smoothedR = r; smoothedG = g; smoothedB = b;
        }
        else
        {
            // alpha = 1 - e^(-dt/τ): frame-rate independent, ~86% of a step in one τ. Guard dt ≤ 0.
            float alpha = dt > 0f ? 1f - MathF.Exp(-dt / SmoothingTau) : 1f;
            smoothedBrightness += (smoothTarget - smoothedBrightness) * alpha;
            smoothedR += (r - smoothedR) * alpha;
            smoothedG += (g - smoothedG) * alpha;
            smoothedB += (b - smoothedB) * alpha;
        }

        // With a flicker mod active, the held brightness rides on top of the smoothed environment UNSMOOTHED, so
        // IL's per-frame flicker survives; the tint stays smoothed regardless (IL flickers V only, never hue).
        float reportBrightness = passHeldThrough
            ? MathF.Max(smoothedBrightness, heldBrightness)
            : smoothedBrightness;

        return Report(reportBrightness, smoothedR, smoothedG, smoothedB);
    }

    /// <summary>Quantize, compare to the previous frame, and remember it. Returns the quantized shade with its
    /// Changed flag set iff any bucket differs from the last reported value.</summary>
    private Shade Report(float brightness, float r, float g, float b)
    {
        int bq = QuantizeStep(brightness, BrightnessSteps);
        int rq = QuantizeStep(r, HueSteps);
        int gq = QuantizeStep(g, HueSteps);
        int bbq = QuantizeStep(b, HueSteps);

        bool changed = !hasLast || bq != lastBrightnessQ || rq != lastRQ || gq != lastGQ || bbq != lastBQ;
        hasLast = true;
        lastBrightnessQ = bq; lastRQ = rq; lastGQ = gq; lastBQ = bbq;

        // Reconstruct the snapped floats from the bucket indices so the widget's filter cache key (and the
        // rendered result) is stable across frames that land in the same bucket.
        return new Shade(
            (float)bq / BrightnessSteps,
            (float)rq / HueSteps,
            (float)gq / HueSteps,
            (float)bbq / HueSteps,
            changed);
    }

    private static int QuantizeStep(float v, int steps) =>
        (int)MathF.Round(Clamp01(v) * steps);

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    /// <summary>DEV diagnostic (the <c>.scribelight</c> command): a one-shot, unsmoothed snapshot of what the
    /// sampler sees RIGHT NOW at the player, so the response-curve anchors can be calibrated against real
    /// numbers instead of guessed (measure-don't-theorize). Returns a human-readable multi-line string; does not
    /// touch the smoothing/quantization state. Static (takes capi+settings) so it can run without an open dialog.</summary>
    public static string Describe(ICoreClientAPI capi, ScribePlayerSettings settings)
    {
        var world = capi.World;
        var entity = world?.Player?.Entity;
        if (world is null || entity is null) return "scribelight: no world/player yet.";

        BlockPos pos = entity.Pos.AsBlockPos;
        Vec4f light = world.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        float gridLuma = 0.2126f * light.R + 0.7152f * light.G + 0.0722f * light.B;
        float sun = light.W;
        float sceneBrightness = capi.Ambient?.BlendedSceneBrightness ?? 1f;
        float sunlit = sun * Clamp01(sceneBrightness);

        bool held = TryHeldLight(world, entity, out float heldLuma, out float hr, out float hg, out float hb);
        float blockLuma = held && heldLuma > gridLuma ? heldLuma : gridLuma;
        float raw = Clamp01(MathF.Max(blockLuma, sunlit));
        float floor = ScribePlayerSettings.ClampIlluminationFloor(settings.IlluminationFloor);
        float outBrightness = ScribeBrightnessCurve.Evaluate(raw, floor);

        return
            $"scribelight @ ({pos.X},{pos.Y},{pos.Z}):\n" +
            $"  grid RGB=({light.R:0.00},{light.G:0.00},{light.B:0.00}) luma={gridLuma:0.000}  sunW={sun:0.00} scene={sceneBrightness:0.00} → sunlit={sunlit:0.000}\n" +
            $"  held light: {(held ? $"yes luma={heldLuma:0.000} tint=({hr:0.00},{hg:0.00},{hb:0.00})" : "none")}\n" +
            $"  RAW input to curve = {raw:0.000}  (floor={floor:0.00})\n" +
            $"  → curve OUTPUT brightness = {outBrightness:0.000}  ({outBrightness * 100f:0}% )";
    }
}
