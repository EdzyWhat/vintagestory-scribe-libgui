namespace Scribe;

/// <summary>
/// Author-facing client-local tuning knobs for two purely-cosmetic rendering subsystems that have been
/// hand-tuned repeatedly across playtests, plus one layout toggle:
/// <see cref="ScribeAmbientLightSampler"/>'s quantization/smoothing
/// (<see cref="BrightnessSteps"/>, <see cref="HueSteps"/>, <see cref="TintStrength"/>,
/// <see cref="SmoothingTau"/>), <see cref="ScribeAssignmentParticleEmitter"/>'s detection/color/density
/// knobs (<see cref="DetectionRadius"/>, <see cref="RainbowRatio"/>, <see cref="CountMultiplier"/>,
/// <see cref="SeedBurstMultiplier"/>), and whether a dialog tab's header renders its Row 2 subtitle line
/// (<see cref="ShowSubtitleRow"/>). Persisted to
/// <see cref="ScribeModSystem.VisualTuningConfigFileName"/> via the engine's own
/// <c>LoadModConfig&lt;T&gt;</c>/<c>StoreModConfig</c> (the same mechanism already used for
/// <see cref="ScribeGearTuning"/>/<see cref="Scribe.Core.ScribePlayerSettings"/>), so a config-library mod
/// reading the shared <c>configlib-patches.json</c> manifest format (ConfigKit, ConfigLib) can optionally
/// provide a settings GUI that edits the same file — no assembly reference, no <c>IsModEnabled</c> check,
/// no new mod dependency of any kind (add-configkit-visual-tuning). Every default below matches the value
/// each constant held before this change, so an absent/never-touched config file reproduces today's
/// behavior exactly.
///
/// <para>Not a player-facing feature: not documented in the handbook, not surfaced in Scribe Settings. Not
/// live-reloaded — loaded once at client startup, matching <see cref="ScribeGearTuning"/>'s precedent; a
/// config-library edit takes effect on next relaunch.</para>
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

    // Defaults = the exact values each constant held before add-configkit-visual-tuning.
    public const int DefaultBrightnessSteps = 32;
    public const int DefaultHueSteps = 16;
    public const float DefaultTintStrength = 2f / 3f;
    public const float DefaultSmoothingTau = 0.2f;
    public const double DefaultDetectionRadius = 12.0;
    public const float DefaultRainbowRatio = 0.5f;
    public const float DefaultCountMultiplier = 0.6f;
    public const float DefaultSeedBurstMultiplier = 3.5f;
    public const bool DefaultShowSubtitleRow = true;
}
