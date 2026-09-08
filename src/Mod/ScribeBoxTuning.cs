using System;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// DEV-ONLY live-tuning knobs for the 5 Scribe writing-station box targets (add-scribe-block-box-tuning):
/// the Inbox block's ground placement, its wall-mounted placement, the Scriptorium, the Assignment
/// Desk, and the Chalkboard. A tiny client-local JSON bag (persisted to
/// <c>ScribeModSystem.BoxTuningConfigFileName</c>) mirroring <see cref="ScribeGearTuning"/>'s style, so
/// the author can nudge a block's collision/selection box live from the <c>.boxtune</c> window instead
/// of edit JSON → rebuild → relaunch. Defaults below are the values baked back from a <c>.boxtune</c>
/// session on 2026-09-07 (Inbox and Inbox-Wall now diverge; see each block's comment for how far the
/// blocktype JSON itself mirrors these). The Chalkboard's fields are selection-only — see
/// <see cref="HasCollisionBox"/>.
///
/// <para>Not part of the real data model — this is a throwaway tuning aid, so it lives in the Mod layer
/// (not Core) and has no unit tests. When a target's box is finalized, fold the chosen numbers back into
/// its blocktype JSON.</para>
/// </summary>
public sealed class ScribeBoxTuning
{
    // ── Inbox (ground) — defaults baked in from .boxtune, now mirrored in inbox.json ────────────
    public float InboxX1 { get; set; } = 0.21f;
    public float InboxY1 { get; set; } = 0f;
    public float InboxZ1 { get; set; } = 0.28f;
    public float InboxX2 { get; set; } = 0.79f;
    public float InboxY2 { get; set; } = 0.8f;
    public float InboxZ2 { get; set; } = 0.7f;

    // ── Inbox (wall-mounted) — defaults baked in from .boxtune; now diverges from the ground Inbox
    // (inbox.json's own collisionbox/selectionbox still ships the ground box only — see its comment) ─
    public float InboxWallX1 { get; set; } = 0.21f;
    public float InboxWallY1 { get; set; } = 0.06f;
    public float InboxWallZ1 { get; set; } = 0f;
    public float InboxWallX2 { get; set; } = 0.79f;
    public float InboxWallY2 { get; set; } = 0.84f;
    public float InboxWallZ2 { get; set; } = 0.4f;

    // ── Scriptorium — defaults baked in from .boxtune, now mirrored in scriptorium.json ─────────
    public float ScriptoriumX1 { get; set; } = 0f;
    public float ScriptoriumY1 { get; set; } = 0f;
    public float ScriptoriumZ1 { get; set; } = 0f;
    public float ScriptoriumX2 { get; set; } = 1f;
    public float ScriptoriumY2 { get; set; } = 1.2f;
    public float ScriptoriumZ2 { get; set; } = 1f;

    // ── Assignment Desk — defaults baked in from .boxtune, now mirrored in assignmentdesk.json ──
    public float AssignmentDeskX1 { get; set; } = 0f;
    public float AssignmentDeskY1 { get; set; } = 0f;
    public float AssignmentDeskZ1 { get; set; } = 0f;
    public float AssignmentDeskX2 { get; set; } = 1f;
    public float AssignmentDeskY2 { get; set; } = 1f;
    public float AssignmentDeskZ2 { get; set; } = 0.875f;

    // ── Chalkboard — SELECTION ONLY (see HasCollisionBox); defaults baked in from .boxtune
    // (2026-09-07), now mirrored in chalkboard.json's selectionbox. The block's collisionbox is
    // null and stays null at every tuned value ─────────────────────────────────────────────────
    public float ChalkboardX1 { get; set; } = 0.125f;
    public float ChalkboardY1 { get; set; } = 0.05f;
    public float ChalkboardZ1 { get; set; } = 0f;
    public float ChalkboardX2 { get; set; } = 0.875f;
    public float ChalkboardY2 { get; set; } = 0.925f;
    public float ChalkboardZ2 { get; set; } = 0.06f;

    // Per-requirement clamp range for every axis (block-box-tuning spec: "clamped to [0, 2]").
    public const float MinAxis = 0f;
    public const float MaxAxis = 2f;

    public static float ClampAxis(float v) => Math.Clamp(v, MinAxis, MaxAxis);

    /// <summary>Clamp every one of the 30 axis values to <c>[0, 2]</c> in place (hand-edited JSON guard).
    /// Returns this for chaining, mirroring <see cref="ScribeGearTuning.Normalized"/>.</summary>
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

    /// <summary>Builds the <see cref="Cuboidf"/> for a given target from its 6 matching properties. The
    /// same box is used for both collision and selection for every target except <see cref="
    /// ScribeBoxTuningTarget.Chalkboard"/> (design.md Non-Goals: this design keeps that coupling, not
    /// 12 fields per target, with Chalkboard as the sole carve-out gated by <see cref="HasCollisionBox"/>
    /// instead of a second set of fields).</summary>
    public Cuboidf CollisionBoxFor(ScribeBoxTuningTarget target) => target switch
    {
        ScribeBoxTuningTarget.Inbox => new Cuboidf(InboxX1, InboxY1, InboxZ1, InboxX2, InboxY2, InboxZ2),
        ScribeBoxTuningTarget.InboxWall => new Cuboidf(InboxWallX1, InboxWallY1, InboxWallZ1, InboxWallX2, InboxWallY2, InboxWallZ2),
        ScribeBoxTuningTarget.Scriptorium => new Cuboidf(ScriptoriumX1, ScriptoriumY1, ScriptoriumZ1, ScriptoriumX2, ScriptoriumY2, ScriptoriumZ2),
        ScribeBoxTuningTarget.AssignmentDesk => new Cuboidf(AssignmentDeskX1, AssignmentDeskY1, AssignmentDeskZ1, AssignmentDeskX2, AssignmentDeskY2, AssignmentDeskZ2),
        ScribeBoxTuningTarget.Chalkboard => new Cuboidf(ChalkboardX1, ChalkboardY1, ChalkboardZ1, ChalkboardX2, ChalkboardY2, ChalkboardZ2),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    /// <summary>True for every target except <see cref="ScribeBoxTuningTarget.Chalkboard"/>: whether
    /// <see cref="CollisionBoxFor"/>'s value for this target should be surfaced as a COLLISION box at
    /// all. The Chalkboard ships with no collision box (painting-style, walk-through) and stays that
    /// way regardless of its tuned axes — only its selection box (<see cref="CollisionBoxFor"/>, read
    /// unconditionally by <c>RotatedSelectionBox</c>) is tunable. Consulted by
    /// <see cref="BlockEntityScribeWritingStation.RotatedBox"/> so this stays a one-target carve-out
    /// instead of a second box-resolution path (design.md Decisions).</summary>
    public static bool HasCollisionBox(ScribeBoxTuningTarget target) => target != ScribeBoxTuningTarget.Chalkboard;
}
