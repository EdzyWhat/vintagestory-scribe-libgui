using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: pin or unpin a task for the sending player, addressed by the task's stable
/// identity — never a block position. <see cref="DocId"/>/<see cref="TaskId"/> are the raw 16-byte
/// forms of the document's and task's <c>Guid</c>s (protobuf-net's own <c>Guid</c> handling is
/// version-fragile, so raw byte arrays are used instead).
///
/// Because a pin is identity-addressed, an <b>unpin</b> needs no block resolution at all — the
/// server removes it straight from the per-player store — so it works even when the owning lectern
/// has been broken or its chunk is unloaded. A <b>pin</b> resolves the owning block (via the store's
/// live DocId→position index) only to capture a text/done snapshot. Lock-free either way: pinning
/// never touches the document, its edit lock, or autosave (see
/// <see cref="BlockEntityScribeLectern.SetTaskDoneFromReader"/> for the sibling lock-free path).
/// </summary>
[ProtoContract]
public sealed class ScribeSetPinMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>True to pin, false to unpin.</summary>
    [ProtoMember(3)]
    public bool Pinned { get; set; }

    /// <summary>Client-supplied text snapshot — used when the server cannot resolve the task from a
    /// registered host (e.g. a Notebook whose host is not server-side). The server falls back to this
    /// when the registry lookup returns no matching block.</summary>
    [ProtoMember(4)]
    public string? SnapshotText { get; set; }

    /// <summary>Client-supplied done snapshot, parallel to <see cref="SnapshotText"/>.</summary>
    [ProtoMember(5)]
    public bool SnapshotDone { get; set; }

    /// <summary>Client-supplied task-kind snapshot (the <see cref="Scribe.Core.ScribeBlockKind"/> byte),
    /// parallel to <see cref="SnapshotText"/> — used when the server can't resolve the task from a
    /// registered host, so a pinned Link still reaches the HUD as a Link (add-tracker-link-tasks 5.5).
    /// Defaults to 0 (<c>Task</c>) for an old client that never sets it.</summary>
    [ProtoMember(6)]
    public byte SnapshotKind { get; set; }

    /// <summary>Client-supplied link-target snapshot for a Link task (null otherwise), parallel to
    /// <see cref="SnapshotText"/> — the collectible code the pinned Link's Handbook hyperlink opens.</summary>
    [ProtoMember(7)]
    public string? SnapshotLinkTarget { get; set; }

    /// <summary>Client-supplied target-item-code snapshot for a Tracker task (null otherwise), parallel to
    /// <see cref="SnapshotText"/> — the collectible the pinned Tracker counts and whose icon/name the HUD
    /// and Pin Tab render (add-tracker-link-tasks 7.8).</summary>
    [ProtoMember(8)]
    public string? SnapshotTargetItemCode { get; set; }

    /// <summary>Client-supplied target-quantity snapshot for a Tracker task (the "need" side of the
    /// have/need counter), parallel to <see cref="SnapshotText"/>. Defaults to 0 for an old client; the
    /// server clamps a Tracker's stored target to ≥ 1, so a defaulted 0 is harmless.</summary>
    [ProtoMember(9)]
    public int SnapshotTargetQuantity { get; set; }

    /// <summary>Client-supplied current-quantity snapshot for a Tracker task (the "have" side of the
    /// have/need counter), parallel to <see cref="SnapshotText"/> (add-tracker-link-tasks 7.8).</summary>
    [ProtoMember(10)]
    public int SnapshotCurrentQuantity { get; set; }

    /// <summary>Client-supplied link-label snapshot for a guide-page Link task (null otherwise), parallel
    /// to <see cref="SnapshotText"/> — the guide's display title, which the HUD and Pin Tab render because
    /// a guide page has no item to resolve a name from (add-tracker-link-tasks 7.6).</summary>
    [ProtoMember(11)]
    public string? SnapshotLinkLabel { get; set; }

    /// <summary>Client-supplied subtask-depth snapshot (0 = top-level, 1 = subtask), parallel to
    /// <see cref="SnapshotText"/> — so a pinned subtask reaches the HUD/Pin Tab at the right indent even
    /// for a host the server can't resolve. Defaults to 0 for an old client that never sets it
    /// (add-crafting-tasks / task-subtasks 5.1).</summary>
    [ProtoMember(12)]
    public int SnapshotDepth { get; set; }
}
