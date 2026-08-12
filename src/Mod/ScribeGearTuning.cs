using System;

namespace Scribe;

/// <summary>
/// DEV-ONLY live-tuning knobs for the Timer-tab gearworks layout (add-timer-gearworks art follow-up). A
/// tiny client-local JSON bag (persisted to <c>ScribeModSystem.GearTuningConfigFileName</c>) mirroring the
/// four hand-placement constants in <see cref="ScribeGearworks"/>'s <c>Build</c>, so the author can nudge
/// gear positions live from the <c>.geartune</c> window while working on the art instead of edit → rebuild
/// → relaunch. Values are in the SAME UNSCALED pixel units as the code constants (Build multiplies each by
/// the Pixel-Art-Size <c>scale</c>), so the defaults below are exactly the current baked-in values and a
/// never-touched config renders identically to the hardcoded layout.
///
/// <para>Not part of the real data model — this is a throwaway tuning aid, so it lives in the Mod layer
/// (not Core) and has no unit tests. When the layout is finalized, fold the chosen numbers back into the
/// constants and this file + its dialog/command can be deleted.</para>
/// </summary>
public sealed class ScribeGearTuning
{
    /// <summary>Horizontal overlap of EACH flanking small gear toward the centered teal gear (the old
    /// <c>toothKiss</c>). Higher = both smalls tuck further under the teal gear's teeth (more mesh); lower =
    /// they float further out to the sides. Symmetric (applies equally to the left and right small).</summary>
    public float SmallGearOverlapX { get; set; } = DefaultSmallGearOverlapX;

    /// <summary>Vertical position of BOTH flanking small gears from the top of the region (the old
    /// <c>smallTop</c>). Higher = the smalls sit LOWER on screen. At the default their midpoint sits just
    /// below the teal gear's midpoint.</summary>
    public float SmallGearY { get; set; } = DefaultSmallGearY;

    /// <summary>How far the escape (great) wheel peeks up from the bottom edge while IDLE (the old
    /// <c>restingPeek</c>). Higher = more of the wheel shows when no timer is running.</summary>
    public float WheelIdlePeek { get; set; } = DefaultWheelIdlePeek;

    /// <summary>How far the escape (great) wheel peeks up while a timer is RUNNING/engaged (the old
    /// <c>livePeek</c>). The wheel slides between the idle and active peek on Start/Fire. Higher = the wheel
    /// rides further up (deeper under the teal gear) while the timer runs.</summary>
    public float WheelActivePeek { get; set; } = DefaultWheelActivePeek;

    /// <summary>Multiplier on the teal temporal gear's on-screen size (the code's <c>largeSize</c>). 1.0 =
    /// the baked size; &gt;1 grows it, &lt;1 shrinks it. Scales only the gear, not its placement.</summary>
    public float LargeGearScale { get; set; } = DefaultLargeGearScale;

    /// <summary>Multiplier on BOTH flanking small gears' on-screen size (the code's <c>smallSize</c>).
    /// 1.0 = the baked size. Note their horizontal mesh position shifts with size (they tuck relative to
    /// their own width), so expect to re-touch <see cref="SmallGearOverlapX"/> after changing this.</summary>
    public float SmallGearScale { get; set; } = DefaultSmallGearScale;

    /// <summary>Vertical position of the teal temporal gear from the top of the region (the old hardcoded
    /// <c>largeTop</c> = 30). Higher = the teal gear sits LOWER on screen (deeper over the escape wheel).</summary>
    public float LargeGearY { get; set; } = DefaultLargeGearY;

    /// <summary>Width of the gearworks clipping/trim box (the old hardcoded <c>regionW</c> = 200). The box is
    /// centered in the Timer tab; the horizontal gear placement is derived from it, so widening it spreads
    /// the train.</summary>
    public float TrimBoxWidth { get; set; } = DefaultTrimBoxWidth;

    /// <summary>Height of the gearworks clipping/trim box (the old hardcoded <c>regionH</c> = 130). Anything
    /// painted past this (the escape wheel's hidden body, shadows) is clipped away. Taller = more of the
    /// wheel shows from below.</summary>
    public float TrimBoxHeight { get; set; } = DefaultTrimBoxHeight;

    /// <summary>Vertical PAINT offset of the whole gear region (trim box + gears + border). Positive = down,
    /// negative = up. Applied as a <c>Transform.Translate</c>, NOT a margin/padding, so it moves ONLY the
    /// gearworks and does NOT reflow the timer form/countdown below it (a margin would consume main-axis
    /// height and shrink the content). Default 0 = no shift.</summary>
    public float TrimBoxY { get; set; } = DefaultTrimBoxY;

    /// <summary>Resting-angle offset (DEGREES) added to BOTH flanking small gears' rotation, so their teeth can
    /// be phased to mesh visually with the teal gear at rest. Added on top of the live animation angle, so it
    /// just rotates the starting position — the gears still spin normally. Default 0.</summary>
    public float SmallGearAngle { get; set; } = DefaultSmallGearAngle;

    /// <summary>Resting-angle offset (DEGREES) added to the large escape/great wheel's rotation, so its teeth
    /// can be phased to mesh visually with the teal gear at rest. Added on top of the live animation angle, so
    /// it just rotates the starting position — the wheel still spins normally. Default 0.</summary>
    public float WheelAngle { get; set; } = DefaultWheelAngle;

    /// <summary>Number of teeth on the procedurally-generated large escape/great wheel (the code's
    /// <c>ScribeGearTexture.Teeth</c>). Live-tunable so the author can try tooth counts in-game; the wheel is
    /// regenerated per (teeth, spacing) combo. Stored as a float for the numeric field but rounded to an int at
    /// generation. Default 30.</summary>
    public float WheelTeeth { get; set; } = DefaultWheelTeeth;

    /// <summary>Tooth-SPACING reference count for the great wheel (the code's
    /// <c>ScribeGearTexture.ToothSizeReferenceTeeth</c>): each tooth is sized as if the wheel had THIS many, so
    /// setting it ABOVE <see cref="WheelTeeth"/> keeps the teeth small and widens the gaps between them. Rounded
    /// to an int at generation. Default 36.</summary>
    public float WheelToothSpacing { get; set; } = DefaultWheelToothSpacing;

    // Defaults = the values the author dialed in via .geartune and locked as the shipping layout (baked
    // 2026-08-11). An untouched config now renders the FINAL tuned gearworks, not a placeholder. .geartune
    // stays in the build (author kept it) so these can still be re-tuned live.
    public const float DefaultSmallGearOverlapX = 15f;
    public const float DefaultSmallGearY        = 39f;
    public const float DefaultWheelIdlePeek     = 40f;
    public const float DefaultWheelActivePeek   = 62f;
    public const float DefaultLargeGearScale    = 1f;
    public const float DefaultSmallGearScale    = 1.2f;
    public const float DefaultLargeGearY        = 6f;
    public const float DefaultTrimBoxWidth      = 252f;
    public const float DefaultTrimBoxHeight     = 138f;
    public const float DefaultTrimBoxY          = 24f;
    public const float DefaultSmallGearAngle    = 24f;
    public const float DefaultWheelAngle        = 0f;
    public const float DefaultWheelTeeth        = 32f;   // = ScribeGearTexture.Teeth
    public const float DefaultWheelToothSpacing = 38f;   // = ScribeGearTexture.ToothSizeReferenceTeeth

    // Generous experimentation ranges (unscaled px within the ~200×130 region). Wide on purpose — this is a
    // tuning tool, so let the author overshoot and dial back rather than fight a tight clamp.
    public const float MinX = -40f,  MaxX = 140f;   // overlap can go negative (gap) or deep
    public const float MinY = -40f,  MaxY = 140f;   // small-gear + large-gear vertical
    public const float MinPeek = 0f, MaxPeek = 200f; // wheel peek heights
    public const float MinScale = 0.25f, MaxScale = 4f;   // gear-size multipliers
    public const float MinDim = 40f,  MaxDim = 500f;      // trim-box width/height
    public const float MinOffset = -200f, MaxOffset = 200f; // trim-box vertical paint offset
    public const float MinAngle = -180f, MaxAngle = 180f;   // resting-angle offsets (degrees)
    public const float MinTeeth = 6f,  MaxTeeth = 80f;      // great-wheel tooth / spacing counts

    public static float ClampX(float v)      => Math.Clamp(v, MinX, MaxX);
    public static float ClampY(float v)      => Math.Clamp(v, MinY, MaxY);
    public static float ClampPeek(float v)   => Math.Clamp(v, MinPeek, MaxPeek);
    public static float ClampScale(float v)  => Math.Clamp(v, MinScale, MaxScale);
    public static float ClampDim(float v)    => Math.Clamp(v, MinDim, MaxDim);
    public static float ClampOffset(float v) => Math.Clamp(v, MinOffset, MaxOffset);
    public static float ClampAngle(float v)  => Math.Clamp(v, MinAngle, MaxAngle);
    public static float ClampTeeth(float v)  => Math.Clamp(MathF.Round(v), MinTeeth, MaxTeeth);

    /// <summary>Clamp every knob to its safe range in place (hand-edited JSON guard). Returns this for
    /// chaining, mirroring <see cref="Scribe.Core.ScribePlayerSettings.Normalized"/>.</summary>
    public ScribeGearTuning Normalized()
    {
        SmallGearOverlapX = ClampX(SmallGearOverlapX);
        SmallGearY        = ClampY(SmallGearY);
        WheelIdlePeek     = ClampPeek(WheelIdlePeek);
        WheelActivePeek   = ClampPeek(WheelActivePeek);
        LargeGearScale    = ClampScale(LargeGearScale);
        SmallGearScale    = ClampScale(SmallGearScale);
        LargeGearY        = ClampY(LargeGearY);
        TrimBoxWidth      = ClampDim(TrimBoxWidth);
        TrimBoxHeight     = ClampDim(TrimBoxHeight);
        TrimBoxY          = ClampOffset(TrimBoxY);
        SmallGearAngle    = ClampAngle(SmallGearAngle);
        WheelAngle        = ClampAngle(WheelAngle);
        WheelTeeth        = ClampTeeth(WheelTeeth);
        WheelToothSpacing = ClampTeeth(WheelToothSpacing);
        return this;
    }
}
