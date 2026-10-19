namespace Scribe.Core;

/// <summary>Server admin policy for how a new assignment reaches its target when they are far from the
/// sending Assignment Desk. Persisted as a byte in server config; values MUST stay stable (append only).</summary>
public enum ScribeDeliveryMode : byte
{
    /// <summary>Every send is a normal <see cref="ScribeAssignmentStore"/> record; no notice UI is shown.</summary>
    AlwaysInstant = 0,

    /// <summary>Every send requires a Task Notice regardless of distance; the toggle is never shown.</summary>
    AlwaysPhysical = 1,

    /// <summary>The default: a one-time Assign-time range check pre-selects a player-overridable toggle
    /// between the two paths (see <see cref="ScribeDeliveryPolicy"/>).</summary>
    Hybrid = 2,
}

/// <summary>Which delivery path a new assignment takes: the two positions of the Create Assignments toggle.</summary>
public enum ScribeDeliveryChoice : byte
{
    /// <summary>A normal <see cref="ScribeAssignmentStore"/> record, acceptable from any Inbox-capable block
    /// on the server.</summary>
    LocalInboxes = 0,

    /// <summary>A physical Task Notice item must be hand-carried to the recipient.</summary>
    SendNotice = 1,
}

/// <summary>A minimal game-agnostic 3D world position for the Hybrid range check, keeping the Vintage Story
/// API's BlockPos/EntityPos out of Core. The Mod layer converts at the boundary.</summary>
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

/// <summary>Pure decision logic for the Hybrid delivery-mode toggle: the Assign-time range check, its
/// admin-configurable radius, and which <see cref="ScribeDeliveryMode"/> values show the toggle. No Vintage
/// Story API reference.</summary>
public static class ScribeDeliveryPolicy
{
    /// <summary>Default radius (blocks) for the Hybrid range check.</summary>
    public const int DefaultRadiusBlocks = 200;

    /// <summary>Inclusive lower bound a configured radius is clamped to on read.</summary>
    public const int MinRadiusBlocks = 10;

    /// <summary>Inclusive upper bound a configured radius is clamped to on read.</summary>
    public const int MaxRadiusBlocks = 100_000;

    /// <summary>Clamps an admin-configured radius to <see cref="MinRadiusBlocks"/>..<see cref="MaxRadiusBlocks"/>.</summary>
    public static int ClampRadius(int value) => Math.Clamp(value, MinRadiusBlocks, MaxRadiusBlocks);

    /// <summary>Whether <paramref name="target"/> is within <paramref name="radiusBlocks"/> of
    /// <paramref name="deskPosition"/>; exactly at the radius counts as in range.</summary>
    public static bool IsInRange(ScribeWorldPosition deskPosition, ScribeWorldPosition target, double radiusBlocks)
        => deskPosition.DistanceTo(target) <= radiusBlocks;

    /// <summary>Resolves the toggle's default position for a newly-selected target. The Always* modes
    /// resolve to a fixed choice regardless of range (they show no toggle, see <see cref="ShowsToggle"/>) so
    /// callers get a definite answer; only <see cref="ScribeDeliveryMode.Hybrid"/> consults
    /// <paramref name="targetInRange"/>.</summary>
    public static ScribeDeliveryChoice ResolveDefault(ScribeDeliveryMode mode, bool targetInRange) => mode switch
    {
        ScribeDeliveryMode.AlwaysPhysical => ScribeDeliveryChoice.SendNotice,
        ScribeDeliveryMode.Hybrid => targetInRange ? ScribeDeliveryChoice.LocalInboxes : ScribeDeliveryChoice.SendNotice,
        _ => ScribeDeliveryChoice.LocalInboxes,
    };

    /// <summary>Whether the Create Assignments tab shows the "Local Inboxes"/"Send a Notice" toggle: only
    /// in Hybrid, since the Always* modes have one path.</summary>
    public static bool ShowsToggle(ScribeDeliveryMode mode) => mode == ScribeDeliveryMode.Hybrid;

    /// <summary>Whether sending requires consuming a blank Task Notice: always true under AlwaysPhysical,
    /// always false under AlwaysInstant (both ignore <paramref name="choice"/>), and follows
    /// <paramref name="choice"/> under Hybrid.</summary>
    public static bool RequiresNotice(ScribeDeliveryMode mode, ScribeDeliveryChoice choice) => mode switch
    {
        ScribeDeliveryMode.AlwaysPhysical => true,
        ScribeDeliveryMode.AlwaysInstant => false,
        _ => choice == ScribeDeliveryChoice.SendNotice,
    };
}
