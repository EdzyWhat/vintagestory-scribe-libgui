using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace Scribe;

public sealed partial class HudScribePins
{
    /// <summary>Base strengths per storm tier, copied from vanilla's <c>stormGlitchStrength</c> bases in
    /// <see cref="SystemTemporalStability"/>. Not scaled.</summary>
    private const double StormLightStrength = 0.53;
    private const double StormMediumStrength = 0.67;
    private const double StormHeavyStrength = 0.90;

    /// <summary>Low-stability ramp: nothing at/above Upper, linear to full at/below Lower.</summary>
    private const double StabilityRampUpper = 0.50;
    private const double StabilityRampLower = 0.10;

    /// <summary>Randomized re-scramble interval range (ms) while a trigger is active.</summary>
    private const long RescrambleMinMs = 0;
    private const long RescrambleMaxMs = 5000;

    /// <summary>Corruptor seed, advanced on the re-scramble cadence; fixed within a build so the whole HUD is
    /// coherent.</summary>
    private int _corruptionSeed;

    /// <summary>World elapsed ms at which the next re-scramble is due.</summary>
    private long _nextRescrambleMs;

    /// <summary>Edge-detect state so a trigger or storm transition rebuilds immediately.</summary>
    private bool _corruptionWasActive;
    private bool _stormWasActiveHud;

    public HudScribePins(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;

        // Subscribed for the object's lifetime (not per-open) so a pin arriving while closed can open the HUD; released in Dispose().
        modSystem.MyPinsChanged += OnMyPinsChanged;
        modSystem.MyTimerChanged += OnMyTimerChanged;
        modSystem.TimerDisplayTick += OnTimerDisplayTick;
        modSystem.QuestPromptsChanged += OnQuestPromptsChanged;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
    }

    /// <summary>Repaints the countdown on the shared 1 Hz tick so it matches the Notebook Timer tab; the 250 ms
    /// OnTick only interpolates _timerLocalRemaining.</summary>
    private void OnTimerDisplayTick()
    {
        var timer = modSystem.MyTimer;

        // Client-driven auto-disappear while Fired. The preference is re-read each tick so turning it off
        // mid-flash takes effect live.
        if (timer?.Status == Scribe.Core.TimerStatus.Fired)
        {
            _firedElapsedLocal += 1.0;
            if (!_firedAutoClearSent
                && modSystem.MySettings.TimerAutoDisappear
                && _firedElapsedLocal >= Scribe.Core.TimerStore.FiredAutoClearSeconds)
            {
                _firedAutoClearSent = true;
                SendClearTimer();
            }
        }

        if (timer?.Status == Scribe.Core.TimerStatus.Running && IsOpened())
            RebuildHudBody();
    }

    /// <summary>
    /// Returns the effective corruption strength (0..1) and whether a storm is active (drives only the title
    /// swap). Strength is the greater of the storm tier's vanilla glitch-strength base (while
    /// <c>StormData.nowStormActive</c>) and a linear ramp from 0 at 0.50 to 1 at 0.10 of the player's
    /// <c>temporalStability</c>. Returns <c>(0, false)</c> when the player's preference is off or the temporal
    /// system can't be resolved.
    /// </summary>
    private (double strength, bool stormActive) ComputeCorruption()
    {
        if (!modSystem.MySettings.StormCorruption) return (0.0, false);

        var stormSys = capi.ModLoader.GetModSystem<SystemTemporalStability>();
        if (stormSys is null) return (0.0, false);

        bool stormActive = stormSys.StormData.nowStormActive;
        double stormStrength = stormActive
            ? stormSys.StormData.nextStormStrength switch
            {
                EnumTempStormStrength.Heavy  => StormHeavyStrength,
                EnumTempStormStrength.Medium => StormMediumStrength,
                _                            => StormLightStrength, // Light (and any unknown) → lightest
            }
            : 0.0;

        // Linear ramp 0 at 0.50 → 1 at 0.10; default 1.0 (stable) when absent so the trigger never fires.
        double stability = capi.World.Player?.Entity?.WatchedAttributes?.GetDouble("temporalStability", 1.0) ?? 1.0;
        double stabilityStrength = Math.Clamp(
            (StabilityRampUpper - stability) / (StabilityRampUpper - StabilityRampLower), 0.0, 1.0);

        return (Math.Max(stormStrength, stabilityStrength), stormActive);
    }

    /// <summary>
    /// Corruption re-scramble tick from <see cref="OnTick"/>. While a trigger is active it advances the seed and
    /// rebuilds on a random 0–5 s cadence; trigger or storm-flag transitions rebuild immediately. Returns true if
    /// it rebuilt (so the caller skips a redundant one).
    /// </summary>
    private bool TickCorruption()
    {
        var (strength, stormActive) = ComputeCorruption();
        bool active = strength > 0.0;

        bool transition = active != _corruptionWasActive || stormActive != _stormWasActiveHud;
        _corruptionWasActive = active;
        _stormWasActiveHud = stormActive;

        long now = capi.World.ElapsedMilliseconds;

        if (transition)
        {
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { RebuildHudBody(); return true; }
            return false;
        }

        if (active && now >= _nextRescrambleMs)
        {
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { RebuildHudBody(); return true; }
        }

        return false;
    }

    /// <summary>Random re-scramble interval in [0, 5000] ms from the client RNG.</summary>
    private long NextRescrambleInterval()
        => RescrambleMinMs + (long)(capi.World.Rand.NextDouble() * (RescrambleMaxMs - RescrambleMinMs));
}
