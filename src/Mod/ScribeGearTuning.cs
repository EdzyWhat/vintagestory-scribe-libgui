using System;

namespace Scribe;

/// <summary>
/// DEV-ONLY live-tuning knobs for the Timer-tab gearworks layout: a client-local JSON bag (persisted to
/// <c>ScribeModSystem.GearTuningConfigFileName</c>) mirroring the hand-placement constants in
/// <see cref="ScribeGearworks"/>'s <c>Build</c>, nudged live from the <c>.geartune</c> window. Values use the
/// same UNSCALED pixel units as those constants (Build multiplies by the Pixel-Art-Size <c>scale</c>); the
/// defaults are the shipping values.
///
/// <para>A throwaway tuning aid, so it lives in the Mod layer (not Core) with no unit tests.</para>
/// </summary>
public sealed class ScribeGearTuning
{
    /// <summary>Horizontal overlap of EACH flanking small gear toward the centered teal gear (the code's
    /// <c>toothKiss</c>); higher tucks them further under its teeth. Symmetric.</summary>
    public float SmallGearOverlapX { get; set; } = DefaultSmallGearOverlapX;

    /// <summary>Vertical position of BOTH small gears from the region top (the code's <c>smallTop</c>);
    /// higher = LOWER on screen.</summary>
    public float SmallGearY { get; set; } = DefaultSmallGearY;

    /// <summary>How far the escape (great) wheel peeks up from the bottom edge while IDLE (the code's
    /// <c>restingPeek</c>). Higher = more of the wheel shows when no timer is running.</summary>
    public float WheelIdlePeek { get; set; } = DefaultWheelIdlePeek;

    /// <summary>How far the wheel peeks up while a timer is RUNNING (the code's <c>livePeek</c>); it slides
    /// between the idle and active peek on Start/Fire.</summary>
    public float WheelActivePeek { get; set; } = DefaultWheelActivePeek;

    /// <summary>Multiplier on the teal temporal gear's on-screen size (the code's <c>largeSize</c>). 1.0 =
    /// the baked size; &gt;1 grows it, &lt;1 shrinks it. Scales only the gear, not its placement.</summary>
    public float LargeGearScale { get; set; } = DefaultLargeGearScale;

    /// <summary>Multiplier on BOTH small gears' on-screen size (the code's <c>smallSize</c>). Their mesh
    /// position shifts with size, so re-touch <see cref="SmallGearOverlapX"/> after changing it.</summary>
    public float SmallGearScale { get; set; } = DefaultSmallGearScale;

    /// <summary>Vertical position of the teal temporal gear from the top of the region (the code's
    /// <c>largeTop</c>). Higher = the teal gear sits LOWER on screen (deeper over the escape wheel).</summary>
    public float LargeGearY { get; set; } = DefaultLargeGearY;

    /// <summary>Width of the gearworks clipping/trim box (the code's <c>regionW</c>), centered in the Timer
    /// tab; horizontal gear placement derives from it.</summary>
    public float TrimBoxWidth { get; set; } = DefaultTrimBoxWidth;

    /// <summary>Height of the trim box (the code's <c>regionH</c>); anything painted past it is clipped.</summary>
    public float TrimBoxHeight { get; set; } = DefaultTrimBoxHeight;

    /// <summary>Vertical PAINT offset of the whole gear region (positive = down), applied as a
    /// <c>Transform.Translate</c> rather than a margin so it does NOT reflow the timer form below. Default 0.</summary>
    public float TrimBoxY { get; set; } = DefaultTrimBoxY;

    /// <summary>Resting-angle offset (DEGREES) for BOTH small gears, added to the live animation angle, to
    /// phase their teeth against the teal gear at rest. Default 0.</summary>
    public float SmallGearAngle { get; set; } = DefaultSmallGearAngle;

    /// <summary>Resting-angle offset (DEGREES) for the great wheel, added to the live animation angle, to
    /// phase its teeth against the teal gear at rest. Default 0.</summary>
    public float WheelAngle { get; set; } = DefaultWheelAngle;

    /// <summary>Tooth count of the generated great wheel (<c>ScribeGearTexture.Teeth</c>); stored as a float
    /// for the numeric field and rounded at generation. Default 32.</summary>
    public float WheelTeeth { get; set; } = DefaultWheelTeeth;

    /// <summary>Tooth-SPACING reference count (<c>ScribeGearTexture.ToothSizeReferenceTeeth</c>): teeth are
    /// sized as if the wheel had THIS many, so setting it above <see cref="WheelTeeth"/> widens the gaps.
    /// Rounded at generation. Default 38.</summary>
    public float WheelToothSpacing { get; set; } = DefaultWheelToothSpacing;

    // Defaults are the shipping layout; an untouched config renders it.
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

    // Generous ranges (unscaled px) so the author can overshoot and dial back.
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
