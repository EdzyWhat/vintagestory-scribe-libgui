namespace Scribe;

/// <summary>
/// Client-local cosmetic tuning knobs: <see cref="ScribeAmbientLightSampler"/> quantization/smoothing,
/// <see cref="ScribeAssignmentParticleEmitter"/> detection/color/density, and the tab-header subtitle row
/// toggle. Persisted to <see cref="ScribeModSystem.VisualTuningConfigFileName"/> via
/// <c>LoadModConfig&lt;T&gt;</c>/<c>StoreModConfig</c> (as <see cref="ScribeGearTuning"/> does), so a
/// config-library mod can edit the same file with no assembly reference or dependency. A missing file yields
/// the defaults below.
///
/// <para>Not player-facing and not live-reloaded: loaded once at client startup, so edits apply on relaunch.</para>
/// </summary>
public sealed class ScribeVisualTuning
{
    /// <summary>Brightness quantization step count for <see cref="ScribeAmbientLightSampler"/> (snap the
    /// 0..1 curve output to 1/N steps).</summary>
    public int BrightnessSteps { get; set; } = DefaultBrightnessSteps;

    /// <summary>Hue quantization step count per channel for <see cref="ScribeAmbientLightSampler"/>.</summary>
    public int HueSteps { get; set; } = DefaultHueSteps;

    /// <summary>How much of the raw hue skew <see cref="ScribeAmbientLightSampler"/> keeps (the rest is
    /// pulled back to neutral).</summary>
    public float TintStrength { get; set; } = DefaultTintStrength;

    /// <summary>Exponential smoothing time-constant (seconds) for <see cref="ScribeAmbientLightSampler"/>'s
    /// brightness + tint transition.</summary>
    public float SmoothingTau { get; set; } = DefaultSmoothingTau;

    /// <summary>How close (blocks) the player must be to an Inbox-capable block for
    /// <see cref="ScribeAssignmentParticleEmitter"/>'s indicator to consider spawning.</summary>
    public double DetectionRadius { get; set; } = DefaultDetectionRadius;

    /// <summary>Fraction of each tick's spawned motes that get a randomized full-range hue instead of the
    /// base amber band, in <see cref="ScribeAssignmentParticleEmitter"/>.</summary>
    public float RainbowRatio { get; set; } = DefaultRainbowRatio;

    /// <summary>Scales <see cref="ScribeAssignmentParticleEmitter"/>'s base per-tick mote count.</summary>
    public float CountMultiplier { get; set; } = DefaultCountMultiplier;

    /// <summary>One-time multiplier <see cref="ScribeAssignmentParticleEmitter"/> applies on the tick a
    /// player's proximity+unseen-assignment trigger first turns true.</summary>
    public float SeedBurstMultiplier { get; set; } = DefaultSeedBurstMultiplier;

    /// <summary>Whether a dialog tab's header renders its Row 2 subtitle line (see
    /// <c>ScribeTabHeader.Build</c>). Off hides the subtitle uniformly across every tab.</summary>
    public bool ShowSubtitleRow { get; set; } = DefaultShowSubtitleRow;

    public const int DefaultBrightnessSteps = 32;
    public const int DefaultHueSteps = 32;
    public const float DefaultTintStrength = 2f / 3f;
    public const float DefaultSmoothingTau = 0.2f;
    public const double DefaultDetectionRadius = 12.0;
    public const float DefaultRainbowRatio = 0.5f;
    public const float DefaultCountMultiplier = 0.6f;
    public const float DefaultSeedBurstMultiplier = 3.5f;
    public const bool DefaultShowSubtitleRow = true;
}
