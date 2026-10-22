using Scribe.Core;                       // ScribeBrightnessCurve, ScribePlayerSettings
using Vintagestory.API.Client;           // ICoreClientAPI
using Vintagestory.API.Common;           // EntityPlayer, CollectibleObject.GetLightHsv, IWorldAccessor.BlockLightLevels
using Vintagestory.API.MathTools;        // Vec4f, Vec3f, BlockPos, ColorUtil
using static Scribe.Core.ScribeMath;

namespace Scribe;

/// <summary>
/// Samples the light reaching the client player each frame and folds it into a single quantized
/// (brightness, tint) value that the <see cref="ScribeGlobalTint"/> widget renders the Scribe GUI at.
/// Render/main thread only: block-accessor reads off the relight background thread are unsafe.
///
/// <para>Every light input is read live from the engine each frame, never baked into a table, so any mod
/// that changes the light where the player stands is honored automatically.</para>
///
/// <para>Three inputs, mirroring the engine: <see cref="IBlockAccessor.GetLightRGBs"/> (XYZ = block-light RGB,
/// W = sun brightness 0..1); <see cref="IAmbientManager.BlendedAmbientColor"/> (sky color) and
/// <see cref="IAmbientManager.BlendedSceneBrightness"/> (weather darkening); and the held light of either hand
/// (<c>Collectible.GetLightHsv</c>), which the block grid at the player's position does NOT contain. Held light
/// is folded in by MAX (VS light is max-based), its level mapped through the live <c>BlockLightLevels</c>
/// table and its hue taken from the item's <c>lightHsv</c>.</para>
///
/// <para>Smoothing: the shade eases toward its new value (exponential, see <see cref="SmoothingTau"/>) so
/// walking through changing light glides; a static scene settles and holds.</para>
///
/// <para>Brightness passes through <see cref="ScribeBrightnessCurve"/>, whose x=0 floor is
/// <see cref="ScribePlayerSettings.IlluminationFloor"/>.</para>
///
/// <para>Quantization: LibGUI caches each dialog's paint in an <c>SKPicture</c> and re-records only when a
/// widget marks itself needing paint, so a continuously-varying tint would defeat the cache. The output is
/// snapped to coarse buckets and <see cref="Sample"/> reports whether the quantized value changed.</para>
/// </summary>
internal sealed class ScribeAmbientLightSampler
{
    private readonly ICoreClientAPI capi;
    private readonly ScribePlayerSettings settings;

    /// <summary>Brightness quantization: snap the 0..1 curve output to 1/N steps. Author-tunable via
    /// <see cref="ScribeVisualTuning"/>.</summary>
    private readonly int BrightnessSteps;

    /// <summary>Hue quantization: snap each tint channel to 1/N steps. Coarse enough that a slow sky shift
    /// re-records the picture only a few times, fine enough that warm/neutral/cool still reads.
    /// Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly int HueSteps;

    /// <summary>How much of the raw hue skew to keep (the rest is pulled back to neutral).
    /// Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float TintStrength;

    /// <summary>Exponential smoothing time-constant (seconds) for brightness + tint. A first-order ease toward
    /// the CURRENT target each frame, so it stays continuous while the target moves; a longer value only
    /// lengthens the lag behind abrupt changes. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float SmoothingTau;

    /// <summary>Mod id of Immersive Lanterns. When installed it Harmony-postfixes
    /// <c>CollectibleObject.GetLightHsv</c> to flicker a held light's brightness index V (held only, never hue).
    /// <see cref="TryHeldLight"/> already receives that value, so when IL is active the held brightness bypasses
    /// the ease (see <see cref="Smooth"/>) and the flicker survives.</summary>
    private const string FlickerModId = "immersivelanterns";

    // Continuous (pre-quantization) smoothed state. Only environment brightness and tint are eased; with a
    // flicker mod active the held brightness rides on top unsmoothed.
    private bool hasSmoothed;
    private float smoothedBrightness;
    private float smoothedR = 1f, smoothedG = 1f, smoothedB = 1f;

    // IL-presence gate, resolved once and cached (mod enablement can't change mid-session).
    private bool? flickerModActive;

    // Last reported quantized value, for change detection.
    private bool hasLast;
    private int lastBrightnessQ;
    private int lastRQ, lastGQ, lastBQ;

    public ScribeAmbientLightSampler(ICoreClientAPI capi, ScribePlayerSettings settings, ScribeVisualTuning tuning)
    {
        this.capi = capi;
        this.settings = settings;
        BrightnessSteps = tuning.BrightnessSteps;
        HueSteps = tuning.HueSteps;
        TintStrength = tuning.TintStrength;
        SmoothingTau = tuning.SmoothingTau;
    }

    /// <summary>The quantized shade for this frame: brightness multiplier (0..1, through the curve + floor) and
    /// a normalized RGB tint (1,1,1 = neutral). <see cref="Changed"/> is true only when it differs from the
    /// previous frame.</summary>
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

    /// <summary>Samples the light at the player, folds it to a quantized <see cref="Shade"/>, and reports
    /// whether it changed since the last call. Render/main thread only; <paramref name="dt"/> is the frame time in seconds.</summary>
    public Shade Sample(float dt)
    {
        var world = capi.World;
        var entity = world?.Player?.Entity;
        if (world is null || entity is null)
        {
            // No player/world yet (during load): render neutral/full so the dialog is never blanked.
            return Smooth(1f, 0f, 1f, 1f, 1f, dt);
        }

        // Player block position (dimension-aware); the light grid is indexed by block pos.
        BlockPos pos = entity.Pos.AsBlockPos;

        // (1) Block-light RGB (XYZ) + sun-brightness scalar (W) from the live light grid.
        Vec4f light = world.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        float blockR = light.R, blockG = light.G, blockB = light.B;
        float sun = light.W;

        // (2) Sky color + weather darkening from the ambient manager.
        Vec3f sky = capi.Ambient?.BlendedAmbientColor ?? new Vec3f(1f, 1f, 1f);
        float sceneBrightness = capi.Ambient?.BlendedSceneBrightness ?? 1f;

        // (3) Held light. The block grid omits the player's own held light (added dynamically as an entity
        // light), so read both hand slots as the engine does and fold in by MAX.
        float gridLuma = 0.2126f * blockR + 0.7152f * blockG + 0.0722f * blockB;
        bool held = TryHeldLight(world, entity, out float heldLuma, out float heldR, out float heldG, out float heldB);

        // Tint path only: a held light brighter than the grid dominates the block-light term and carries its
        // own hue. Brightness folds held via curve + MAX below, so it needs no separate test there.
        float blockLuma = gridLuma;
        if (held && heldLuma > gridLuma)
        {
            blockLuma = heldLuma;
            blockR = heldR; blockG = heldG; blockB = heldB;
        }

        // --- Brightness (environment vs. held) ---
        // Environment = grid light + weathered sun, curve-mapped and always eased. With a flicker mod active the
        // held term rides on top unsmoothed (see Smooth); otherwise the two collapse into one smoothed value
        // (the curve is monotonic, so max(curve a, curve b) == curve max).
        float floor = ScribePlayerSettings.ClampIlluminationFloor(settings.IlluminationFloor);
        float sunlit = sun * Clamp01(sceneBrightness);
        float envRaw = Clamp01(MathF.Max(gridLuma, sunlit));
        float envBrightness = ScribeBrightnessCurve.Evaluate(envRaw, floor);
        // Held term is 0 (not curve(0)=floor) with no held light, so it can't floor-lift the MAX.
        float heldBrightness = held ? ScribeBrightnessCurve.Evaluate(Clamp01(heldLuma), floor) : 0f;

        // --- Hue ---
        // Weight block vs. sky light by which actually lights the player, then normalize so the MAX channel is 1:
        // absolute level lives in Brightness, so the tint only skews hue and never re-darkens.
        float blockWeight = blockLuma;
        float skyWeight = sunlit;
        float wsum = blockWeight + skyWeight;
        float r, g, b;
        if (wsum <= 1e-4f)
        {
            // No light of either kind: neutral tint (brightness carries the darkness).
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

        // Pull each channel partway back toward neutral (TintStrength); level is unaffected.
        r = 1f + (r - 1f) * TintStrength;
        g = 1f + (g - 1f) * TintStrength;
        b = 1f + (b - 1f) * TintStrength;

        return Smooth(envBrightness, heldBrightness, r, g, b, dt);
    }

    /// <summary>Whether Immersive Lanterns is installed, resolved once and cached. When true the held brightness
    /// bypasses the ease in <see cref="Smooth"/>.</summary>
    private bool FlickerModActive => flickerModActive ??= capi.ModLoader.IsModEnabled(FlickerModId);

    /// <summary>Reads the light emitted by the player's two hands as a normalized (luma, RGB-hue) pair.
    /// Returns false when no held item emits light. Hue comes from the item's <c>lightHsv</c>; luma maps the
    /// merged index V through the live <see cref="IWorldAccessor.BlockLightLevels"/> table so a held lantern
    /// lands on the same curve point as a placed one.</summary>
    private static bool TryHeldLight(IWorldAccessor world, EntityPlayer entity,
        out float luma, out float r, out float g, out float b)
    {
        luma = 0f; r = g = b = 1f;

        // Active hotbar slot + offhand, as EntityPlayer.LightHsv reads them.
        byte[]? rightHsv = entity.RightHandItemSlot?.Itemstack?.Collectible?
            .GetLightHsv(world.BlockAccessor, null, entity.RightHandItemSlot.Itemstack);
        byte[]? leftHsv = entity.LeftHandItemSlot?.Itemstack?.Collectible?
            .GetLightHsv(world.BlockAccessor, null, entity.LeftHandItemSlot.Itemstack);

        // Engine helper: V-weighted hue blend, max V for level.
        byte[] hsv = ColorUtil.MergeLightHSV(rightHsv, leftHsv);
        if (hsv is null || hsv[2] == 0) return false; // no held light / V=0

        // lightHsv stores indices, not 0..255: h 0..63, s 0..7, v 0..31.
        int v = hsv[2];

        // Map V through the live light-level table (not V/31).
        float[] levels = world.BlockLightLevels;
        luma = (levels is not null && v < levels.Length) ? levels[v] : Clamp01(v / 31f);

        // Hue: HSV at full value to RGB, normalized so max channel = 1 (level is carried by luma).
        int rgb = ColorUtil.HsvToRgb(hsv[0] * 255 / 63, hsv[1] * 255 / 7, 255);
        float hr = ((rgb >> 16) & 0xFF) / 255f;
        float hg = ((rgb >> 8) & 0xFF) / 255f;
        float hb = (rgb & 0xFF) / 255f;
        float mx = MathF.Max(hr, MathF.Max(hg, hb));
        if (mx > 1e-4f) { r = hr / mx; g = hg / mx; b = hb / mx; }
        return true;
    }

    /// <summary>Eases the continuous shade toward the new target (time-constant <see cref="SmoothingTau"/>),
    /// then quantizes and reports. Smoothing runs before quantization so buckets step through intermediate values.
    ///
    /// <para>Only environment brightness and tint are eased. Held brightness is combined by MAX after easing:
    /// unsmoothed when <see cref="FlickerModActive"/> (so the flicker passes through), otherwise folded into
    /// the single smoothed value.</para></summary>
    private Shade Smooth(float envBrightness, float heldBrightness, float r, float g, float b, float dt)
    {
        // With no flicker mod there is nothing to protect from the low-pass: fold held into the eased value.
        bool passHeldThrough = FlickerModActive;
        float smoothTarget = passHeldThrough ? envBrightness : MathF.Max(envBrightness, heldBrightness);

        if (!hasSmoothed)
        {
            // First frame: adopt the target so the opening dialog doesn't fade up from black.
            hasSmoothed = true;
            smoothedBrightness = smoothTarget;
            smoothedR = r; smoothedG = g; smoothedB = b;
        }
        else
        {
            // alpha = 1 - e^(-dt/tau): frame-rate independent. Guard dt <= 0.
            float alpha = dt > 0f ? 1f - MathF.Exp(-dt / SmoothingTau) : 1f;
            smoothedBrightness += (smoothTarget - smoothedBrightness) * alpha;
            smoothedR += (r - smoothedR) * alpha;
            smoothedG += (g - smoothedG) * alpha;
            smoothedB += (b - smoothedB) * alpha;
        }

        // With a flicker mod active, held brightness rides on top unsmoothed; the tint stays smoothed (IL flickers V only).
        float reportBrightness = passHeldThrough
            ? MathF.Max(smoothedBrightness, heldBrightness)
            : smoothedBrightness;

        return Report(reportBrightness, smoothedR, smoothedG, smoothedB);
    }

    /// <summary>Quantizes, compares to the previous frame, and remembers it. Changed is set iff any bucket differs.</summary>
    private Shade Report(float brightness, float r, float g, float b)
    {
        int bq = QuantizeStep(brightness, BrightnessSteps);
        int rq = QuantizeStep(r, HueSteps);
        int gq = QuantizeStep(g, HueSteps);
        int bbq = QuantizeStep(b, HueSteps);

        bool changed = !hasLast || bq != lastBrightnessQ || rq != lastRQ || gq != lastGQ || bbq != lastBQ;
        hasLast = true;
        lastBrightnessQ = bq; lastRQ = rq; lastGQ = gq; lastBQ = bbq;

        // Rebuild floats from bucket indices so the widget's filter cache key is stable within a bucket.
        return new Shade(
            (float)bq / BrightnessSteps,
            (float)rq / HueSteps,
            (float)gq / HueSteps,
            (float)bbq / HueSteps,
            changed);
    }

    private static int QuantizeStep(float v, int steps) =>
        (int)MathF.Round(Clamp01(v) * steps);


    /// <summary>DEV diagnostic (<c>.scribelight</c>): an unsmoothed one-shot snapshot of what the sampler sees
    /// at the player, for calibrating the curve. Does not touch smoothing/quantization state; static so it runs
    /// without an open dialog.</summary>
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
