namespace Scribe.Core;

/// <summary>
/// One entry in a player's per-player pin set: a durable reference to a specific task, plus a
/// last-known snapshot so the task can still be shown when its owning block is unloaded (or gone).
///
/// The reference is <see cref="OwnerDocId"/> + <see cref="TaskId"/> — never a block position. A
/// pin therefore keeps resolving across the owning block being broken and re-placed (the ids ride
/// inside the document's serialized bytes), and can be acted on (unpinned) with no block lookup at
/// all. This type is game-agnostic (pure BCL); the Mod layer owns where the set lives and how it
/// syncs. Serialized by <see cref="ScribePinCodec"/>.
/// </summary>
public sealed class ScribePinnedRef
{
    /// <summary>The <see cref="ScribeDocument.DocId"/> of the document that owns the pinned task.</summary>
    public Guid OwnerDocId { get; set; }

    /// <summary>The <see cref="ScribeBlock.TaskId"/> of the pinned task within that document.</summary>
    public Guid TaskId { get; set; }

    /// <summary>Game time (total hours) at which the task was pinned. A game-agnostic numeric stamp;
    /// the Mod layer supplies it from the world calendar and formats it for display.</summary>
    public double PinnedAtTotalHours { get; set; }

    /// <summary>True once the referenced task is known to be permanently gone (its block was
    /// broken/removed, or the task was deleted from a saved edit). An orphaned pin keeps its
    /// last-known snapshot and stays in the player's set until they remove it; it is never set
    /// merely because the target is temporarily unresolvable (an unloaded chunk).</summary>
    public bool Orphaned { get; set; }

    /// <summary>Last-known text of the task, refreshed from the authoritative document on edit, so
    /// a client can display the pin even when the task's chunk is unloaded.</summary>
    public string LastKnownText { get; set; } = "";

    /// <summary>Last-known completed state of the task, refreshed alongside <see cref="LastKnownText"/>.</summary>
    public bool LastKnownDone { get; set; }

    /// <summary>The pinned task's kind, snapshotted so a client can render/act on the pin by kind even
    /// when the owning block is unloaded — most importantly so the HUD can treat a pinned Link's label as
    /// a Handbook hyperlink (add-tracker-link-tasks 5.5). Defaults to <see cref="ScribeBlockKind.Task"/>
    /// (the value pre-v2 pin blobs migrate to), so an ordinary pinned task is unaffected.</summary>
    public ScribeBlockKind Kind { get; set; } = ScribeBlockKind.Task;

    /// <summary>For a <see cref="ScribeBlockKind.Link"/> pin, the last-known link target (the collectible
    /// code its Handbook hyperlink opens); null for every other kind. Snapshotted alongside
    /// <see cref="Kind"/> so the HUD can open the page without resolving the (possibly unloaded) source
    /// document (add-tracker-link-tasks 5.5).</summary>
    public string? LinkTarget { get; set; }
}
