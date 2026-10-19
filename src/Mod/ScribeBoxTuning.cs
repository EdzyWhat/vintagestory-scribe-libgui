using System;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// DEV-ONLY live-tuning knobs for the 5 writing-station box targets (Inbox ground, Inbox wall, Scriptorium,
/// Assignment Desk, Chalkboard). A client-local JSON bag persisted to
/// <c>ScribeModSystem.BoxTuningConfigFileName</c>, mirroring <see cref="ScribeGearTuning"/>, edited live from
/// the <c>.boxtune</c> window. Defaults are values baked back from a <c>.boxtune</c> session. The Chalkboard's
/// fields are selection-only (see <see cref="HasCollisionBox"/>).
///
/// <para>A throwaway tuning aid, so it lives in the Mod layer with no unit tests; finalized numbers belong in
/// the blocktype JSON.</para>
/// </summary>
public sealed class ScribeBoxTuning
{
    // Inbox (ground); mirrored in inbox.json
    public float InboxX1 { get; set; } = 0.21f;
    public float InboxY1 { get; set; } = 0f;
    public float InboxZ1 { get; set; } = 0.28f;
    public float InboxX2 { get; set; } = 0.79f;
    public float InboxY2 { get; set; } = 0.8f;
    public float InboxZ2 { get; set; } = 0.7f;

    // Inbox (wall-mounted); inbox.json ships the ground box only
    public float InboxWallX1 { get; set; } = 0.21f;
    public float InboxWallY1 { get; set; } = 0.06f;
    public float InboxWallZ1 { get; set; } = 0f;
    public float InboxWallX2 { get; set; } = 0.79f;
    public float InboxWallY2 { get; set; } = 0.84f;
    public float InboxWallZ2 { get; set; } = 0.4f;

    // Scriptorium; mirrored in scriptorium.json
    public float ScriptoriumX1 { get; set; } = 0f;
    public float ScriptoriumY1 { get; set; } = 0f;
    public float ScriptoriumZ1 { get; set; } = 0f;
    public float ScriptoriumX2 { get; set; } = 1f;
    public float ScriptoriumY2 { get; set; } = 1.2f;
    public float ScriptoriumZ2 { get; set; } = 1f;

    // Assignment Desk; mirrored in assignmentdesk.json
    public float AssignmentDeskX1 { get; set; } = 0f;
    public float AssignmentDeskY1 { get; set; } = 0f;
    public float AssignmentDeskZ1 { get; set; } = 0f;
    public float AssignmentDeskX2 { get; set; } = 1f;
    public float AssignmentDeskY2 { get; set; } = 1f;
    public float AssignmentDeskZ2 { get; set; } = 0.875f;

    // Chalkboard: selection only (see HasCollisionBox); mirrored in chalkboard.json
    public float ChalkboardX1 { get; set; } = 0.125f;
    public float ChalkboardY1 { get; set; } = 0.05f;
    public float ChalkboardZ1 { get; set; } = 0f;
    public float ChalkboardX2 { get; set; } = 0.875f;
    public float ChalkboardY2 { get; set; } = 0.925f;
    public float ChalkboardZ2 { get; set; } = 0.06f;

    // Clamp range for every axis.
    public const float MinAxis = 0f;
    public const float MaxAxis = 2f;

    public static float ClampAxis(float v) => Math.Clamp(v, MinAxis, MaxAxis);

    /// <summary>Clamps all 30 axis values to <c>[0, 2]</c> in place (guards hand-edited JSON) and returns this.</summary>
    public ScribeBoxTuning Normalized()
    {
        InboxX1 = ClampAxis(InboxX1);
        InboxY1 = ClampAxis(InboxY1);
        InboxZ1 = ClampAxis(InboxZ1);
        InboxX2 = ClampAxis(InboxX2);
        InboxY2 = ClampAxis(InboxY2);
        InboxZ2 = ClampAxis(InboxZ2);

        InboxWallX1 = ClampAxis(InboxWallX1);
        InboxWallY1 = ClampAxis(InboxWallY1);
        InboxWallZ1 = ClampAxis(InboxWallZ1);
        InboxWallX2 = ClampAxis(InboxWallX2);
        InboxWallY2 = ClampAxis(InboxWallY2);
        InboxWallZ2 = ClampAxis(InboxWallZ2);

        ScriptoriumX1 = ClampAxis(ScriptoriumX1);
        ScriptoriumY1 = ClampAxis(ScriptoriumY1);
        ScriptoriumZ1 = ClampAxis(ScriptoriumZ1);
        ScriptoriumX2 = ClampAxis(ScriptoriumX2);
        ScriptoriumY2 = ClampAxis(ScriptoriumY2);
        ScriptoriumZ2 = ClampAxis(ScriptoriumZ2);

        AssignmentDeskX1 = ClampAxis(AssignmentDeskX1);
        AssignmentDeskY1 = ClampAxis(AssignmentDeskY1);
        AssignmentDeskZ1 = ClampAxis(AssignmentDeskZ1);
        AssignmentDeskX2 = ClampAxis(AssignmentDeskX2);
        AssignmentDeskY2 = ClampAxis(AssignmentDeskY2);
        AssignmentDeskZ2 = ClampAxis(AssignmentDeskZ2);

        ChalkboardX1 = ClampAxis(ChalkboardX1);
        ChalkboardY1 = ClampAxis(ChalkboardY1);
        ChalkboardZ1 = ClampAxis(ChalkboardZ1);
        ChalkboardX2 = ClampAxis(ChalkboardX2);
        ChalkboardY2 = ClampAxis(ChalkboardY2);
        ChalkboardZ2 = ClampAxis(ChalkboardZ2);

        return this;
    }

    /// <summary>Builds the <see cref="Cuboidf"/> for a target from its 6 properties. Used for both collision and
    /// selection; <see cref="HasCollisionBox"/> carves out the Chalkboard instead of a second set of fields.</summary>
    public Cuboidf CollisionBoxFor(ScribeBoxTuningTarget target) => target switch
    {
        ScribeBoxTuningTarget.Inbox => new Cuboidf(InboxX1, InboxY1, InboxZ1, InboxX2, InboxY2, InboxZ2),
        ScribeBoxTuningTarget.InboxWall => new Cuboidf(InboxWallX1, InboxWallY1, InboxWallZ1, InboxWallX2, InboxWallY2, InboxWallZ2),
        ScribeBoxTuningTarget.Scriptorium => new Cuboidf(ScriptoriumX1, ScriptoriumY1, ScriptoriumZ1, ScriptoriumX2, ScriptoriumY2, ScriptoriumZ2),
        ScribeBoxTuningTarget.AssignmentDesk => new Cuboidf(AssignmentDeskX1, AssignmentDeskY1, AssignmentDeskZ1, AssignmentDeskX2, AssignmentDeskY2, AssignmentDeskZ2),
        ScribeBoxTuningTarget.Chalkboard => new Cuboidf(ChalkboardX1, ChalkboardY1, ChalkboardZ1, ChalkboardX2, ChalkboardY2, ChalkboardZ2),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    /// <summary>False only for <see cref="ScribeBoxTuningTarget.Chalkboard"/>, which has no collision box
    /// (walk-through) regardless of its tuned axes; only its selection box is tunable. Consulted by
    /// <see cref="BlockEntityScribeWritingStation.RotatedBox"/>.</summary>
    public static bool HasCollisionBox(ScribeBoxTuningTarget target) => target != ScribeBoxTuningTarget.Chalkboard;
}
