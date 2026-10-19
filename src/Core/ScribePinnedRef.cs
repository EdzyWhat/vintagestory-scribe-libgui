namespace Scribe.Core;

/// <summary>
/// One entry in a player's pin set: a durable reference to a task plus a last-known snapshot so it can be
/// shown when its owning block is unloaded or gone. The reference is <see cref="OwnerDocId"/> +
/// <see cref="TaskId"/>, never a block position, so it survives the block being broken and re-placed.
/// Pure BCL; serialized by <see cref="ScribePinCodec"/>.
/// </summary>
public sealed class ScribePinnedRef
{
    /// <summary>The <see cref="ScribeDocument.DocId"/> of the document that owns the pinned task.</summary>
    public Guid OwnerDocId { get; set; }

    /// <summary>The <see cref="ScribeBlock.TaskId"/> of the pinned task within that document.</summary>
    public Guid TaskId { get; set; }

    /// <summary>Game time (total hours) when pinned; supplied by the Mod layer from the world calendar.</summary>
    public double PinnedAtTotalHours { get; set; }

    /// <summary>True once the task is known to be permanently gone (block removed or task deleted). An
    /// orphaned pin keeps its snapshot until the player removes it; it is never set merely because a chunk is unloaded.</summary>
    public bool Orphaned { get; set; }

    /// <summary>Last-known task text, refreshed from the authoritative document on edit.</summary>
    public string LastKnownText { get; set; } = "";

    /// <summary>Last-known completed state of the task, refreshed alongside <see cref="LastKnownText"/>.</summary>
    public bool LastKnownDone { get; set; }

    /// <summary>The pinned task's kind, snapshotted so a client can act on the pin by kind with its block
    /// unloaded (e.g. the HUD treats a pinned Link's label as a Handbook hyperlink). Pre-v2 blobs migrate to Task.</summary>
    public ScribeBlockKind Kind { get; set; } = ScribeBlockKind.Task;

    /// <summary>For a Link pin, the last-known link target; null for other kinds.</summary>
    public string? LinkTarget { get; set; }

    /// <summary>For a Tracker pin, the last-known target item code (what it counts and renders); null for other kinds.</summary>
    public string? TargetItemCode { get; set; }

    /// <summary>For a Tracker pin, the last-known target quantity (the "need" side). Defaults to 1.</summary>
    public int TargetQuantity { get; set; } = 1;

    /// <summary>For a Tracker pin, the last-known current quantity (the "have" side), the persisted fallback.
    /// The live count is recomputed by the client engine watching the carried inventory (an open Scribe
    /// dialog's read view, or the HUD's own count engine).</summary>
    public int CurrentQuantity { get; set; }

    /// <summary>For a guide-page Link pin (<c>"page:"</c> <see cref="LinkTarget"/>), the last-known display
    /// title, since a guide page has no item to name it. Null for item Links and other kinds.</summary>
    public string? LinkLabel { get; set; }

    /// <summary>The pinned task's subtask depth (0 or 1, clamped like <see cref="ScribeBlock.Depth"/>), so the
    /// HUD and Pin Tab indent a subtask like the editor does.</summary>
    public int Depth { get; set; }

    /// <summary>True when the task carries an Accepted assignment (<see cref="ScribeAssignmentState.Accepted"/>),
    /// snapshotted so the HUD and Pin Tab can render the assignment marker without the source document.</summary>
    public bool IsAcceptedAssignment { get; set; }

    /// <summary>When <see cref="IsAcceptedAssignment"/>, the assigner's player uid; empty otherwise.
    /// Snapshotted (with <see cref="AssignedDate"/>/<see cref="AcceptedDate"/>) for the marker's tooltip.</summary>
    public string AssignerUid { get; set; } = "";

    /// <summary>When <see cref="IsAcceptedAssignment"/>, the in-game date the assignment was sent; empty otherwise.</summary>
    public string AssignedDate { get; set; } = "";

    /// <summary>When <see cref="IsAcceptedAssignment"/>, the in-game date the assignment was accepted; null otherwise.</summary>
    public string? AcceptedDate { get; set; }

    /// <summary>Numeric timestamp for <see cref="AssignedDate"/>; null on a pre-v9 blob (those rows show the
    /// baked string). <c>0</c> is a real value, not a missing sentinel.</summary>
    public double? AssignedTimestamp { get; set; }

    /// <summary>Numeric timestamp for <see cref="AcceptedDate"/>; null on a pre-v9 blob or when never stamped.</summary>
    public double? AcceptedTimestamp { get; set; }

    /// <summary>Last-known <see cref="ScribeBlock.ExtraInfo"/> (an external mod's opaque hover-detail
    /// string), snapshotted for the Pin Tab's hover-info icon. Null for most pins.</summary>
    public string? ExtraInfo { get; set; }
}
