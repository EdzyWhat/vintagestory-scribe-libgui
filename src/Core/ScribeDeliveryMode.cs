namespace Scribe.Core;

/// <summary>Server admin policy for how a new assignment reaches its target when they are far from
/// (or offline relative to) the sending Assignment Desk — see the `assignment-delivery-mode`
/// capability. Persisted as a byte in server config; values MUST remain stable across versions
/// (append only, never renumber).</summary>
public enum ScribeDeliveryMode : byte
{
    /// <summary>The shipped in-range-only behavior — every send is a normal
    /// <see cref="ScribeAssignmentStore"/> record; no notice-related UI is ever shown.</summary>
    AlwaysInstant = 0,

    /// <summary>Every send requires a Task Notice regardless of distance; there is only one delivery
    /// path, so the toggle described below is never shown.</summary>
    AlwaysPhysical = 1,

    /// <summary>The default: a one-time Assign-time range check pre-selects a player-overridable
    /// toggle between the two delivery paths (see <see cref="ScribeDeliveryPolicy"/>).</summary>
    Hybrid = 2,
}

/// <summary>Which delivery path a new assignment takes — the two positions of the Create Assignments
/// tab's toggle (`assignment-desk-block` capability).</summary>
public enum ScribeDeliveryChoice : byte
{
    /// <summary>A normal <see cref="ScribeAssignmentStore"/> record, Accept/Decline-able from any
    /// Inbox-capable block anywhere on the server, with zero physical footprint.</summary>
    LocalInboxes = 0,

    /// <summary>A physical Task Notice item must be hand-carried to the recipient.</summary>
    SendNotice = 1,
}

/// <summary>A minimal, game-agnostic 3D world position — just enough for the Hybrid range check
/// (`assignment-delivery-mode` capability) without pulling the Vintage Story API's BlockPos/EntityPos
/// into Core (per this project's Core-must-stay-API-free convention). The Mod layer converts to/from
/// its own position types at the boundary.</summary>
public readonly struct ScribeWorldPosition
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public ScribeWorldPosition(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Straight-line distance to another position.</summary>
    public double DistanceTo(ScribeWorldPosition other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

/// <summary>Pure decision logic for the Hybrid delivery-mode toggle (`assignment-delivery-mode` +
/// `assignment-desk-block` capabilities): the one-time Assign-time range check, its admin-configurable
/// radius, and which <see cref="ScribeDeliveryMode"/> values show the toggle at all. No Vintage Story
/// API reference, so this is exercised directly from Core.Tests.</summary>
public static class ScribeDeliveryPolicy
{
    /// <summary>Default radius (blocks) for the Hybrid range check, when a server admin hasn't
    /// overridden it.</summary>
    public const int DefaultRadiusBlocks = 200;

    /// <summary>Inclusive lower bound an admin-configured radius is clamped to on read — a hand-edited
    /// value can't shrink the check to a meaningless sliver.</summary>
    public const int MinRadiusBlocks = 10;

    /// <summary>Inclusive upper bound an admin-configured radius is clamped to on read — a hand-edited
    /// value can't blow the check out past any realistic base-to-base distance.</summary>
    public const int MaxRadiusBlocks = 100_000;

    /// <summary>Clamps an admin-configured radius to <see cref="MinRadiusBlocks"/>..<see cref="MaxRadiusBlocks"/>.</summary>
    public static int ClampRadius(int value) => Math.Clamp(value, MinRadiusBlocks, MaxRadiusBlocks);

    /// <summary>Whether <paramref name="target"/> is within <paramref name="radiusBlocks"/> of
    /// <paramref name="deskPosition"/> — the one-time range check run at Assign time. Exactly at the
    /// radius counts as in-range (an inclusive boundary).</summary>
    public static bool IsInRange(ScribeWorldPosition deskPosition, ScribeWorldPosition target, double radiusBlocks)
        => deskPosition.DistanceTo(target) <= radiusBlocks;

    /// <summary>Resolves the delivery-mode toggle's computed default position for a newly-selected
    /// target, per the `assignment-delivery-mode` capability's range-check requirement.
    /// <see cref="ScribeDeliveryMode.AlwaysInstant"/> always resolves to LocalInboxes and
    /// <see cref="ScribeDeliveryMode.AlwaysPhysical"/> always resolves to SendNotice regardless of
    /// range — neither mode shows a toggle at all (see <see cref="ShowsToggle"/>), but a caller that
    /// still wants a definite choice (e.g. to decide whether the Send control requires a notice) gets a
    /// well-defined answer here rather than having to special-case those two modes itself. Only
    /// <see cref="ScribeDeliveryMode.Hybrid"/> actually consults <paramref name="targetInRange"/>.</summary>
    public static ScribeDeliveryChoice ResolveDefault(ScribeDeliveryMode mode, bool targetInRange) => mode switch
    {
        ScribeDeliveryMode.AlwaysPhysical => ScribeDeliveryChoice.SendNotice,
        ScribeDeliveryMode.Hybrid => targetInRange ? ScribeDeliveryChoice.LocalInboxes : ScribeDeliveryChoice.SendNotice,
        _ => ScribeDeliveryChoice.LocalInboxes,
    };

    /// <summary>Whether the Create Assignments tab shows the "Local Inboxes"/"Send a Notice" toggle at
    /// all — only in <see cref="ScribeDeliveryMode.Hybrid"/> (`assignment-desk-block` capability); the
    /// two Always* modes have exactly one path, so there is nothing to switch between.</summary>
    public static bool ShowsToggle(ScribeDeliveryMode mode) => mode == ScribeDeliveryMode.Hybrid;

    /// <summary>Whether sending in <paramref name="choice"/> under <paramref name="mode"/> requires a
    /// blank Task Notice to be consumed (`task-notice-item` capability): unconditionally true under
    /// <see cref="ScribeDeliveryMode.AlwaysPhysical"/> and unconditionally false under
    /// <see cref="ScribeDeliveryMode.AlwaysInstant"/> — both ignore <paramref name="choice"/>, since
    /// neither mode shows a toggle for it to reflect (see <see cref="ShowsToggle"/>) — and follows
    /// <paramref name="choice"/> only under <see cref="ScribeDeliveryMode.Hybrid"/>.</summary>
    public static bool RequiresNotice(ScribeDeliveryMode mode, ScribeDeliveryChoice choice) => mode switch
    {
        ScribeDeliveryMode.AlwaysPhysical => true,
        ScribeDeliveryMode.AlwaysInstant => false,
        _ => choice == ScribeDeliveryChoice.SendNotice,
    };
}
