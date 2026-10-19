using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: pin or unpin a task for the sending player, addressed by the task's stable
/// identity, never a block position. <see cref="DocId"/>/<see cref="TaskId"/> are raw 16-byte
/// <c>Guid</c>s (protobuf-net's own <c>Guid</c> handling is version-fragile).
///
/// An <b>unpin</b> needs no block resolution (the server removes it from the per-player store), so it
/// works even when the owning lectern is broken or unloaded. A <b>pin</b> resolves the owning block (via
/// the store's DocId-to-position index) only to snapshot text/done. Lock-free either way: pinning never
/// touches the document, its edit lock, or autosave (see
/// <see cref="BlockEntityScribeLectern.SetTaskDoneFromReader"/>).
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

    // The Snapshot* fields are client-supplied fallbacks used when the server cannot resolve the task from
    // a registered host (e.g. a Notebook whose host is not server-side).

    /// <summary>Text snapshot, used when the registry lookup returns no matching block.</summary>
    [ProtoMember(4)]
    public string? SnapshotText { get; set; }

    /// <summary>Done snapshot.</summary>
    [ProtoMember(5)]
    public bool SnapshotDone { get; set; }

    /// <summary>Task-kind snapshot (<see cref="Scribe.Core.ScribeBlockKind"/> byte), so a pinned Link still
    /// reaches the HUD as a Link. Defaults to 0 (<c>Task</c>) for an old client.</summary>
    [ProtoMember(6)]
    public byte SnapshotKind { get; set; }

    /// <summary>Link-target snapshot for a Link task (null otherwise): the collectible code the Handbook hyperlink opens.</summary>
    [ProtoMember(7)]
    public string? SnapshotLinkTarget { get; set; }

    /// <summary>Target-item-code snapshot for a Tracker task (null otherwise): the collectible it counts
    /// and whose icon/name the HUD and Pin Tab render.</summary>
    [ProtoMember(8)]
    public string? SnapshotTargetItemCode { get; set; }

    /// <summary>Target-quantity snapshot for a Tracker task (the "need" side). Defaults to 0 for an old
    /// client; the server clamps a Tracker's stored target to at least 1.</summary>
    [ProtoMember(9)]
    public int SnapshotTargetQuantity { get; set; }

    /// <summary>Current-quantity snapshot for a Tracker task (the "have" side).</summary>
    [ProtoMember(10)]
    public int SnapshotCurrentQuantity { get; set; }

    /// <summary>Link-label snapshot for a guide-page Link (null otherwise): the guide's display title,
    /// needed because a guide page has no item to resolve a name from.</summary>
    [ProtoMember(11)]
    public string? SnapshotLinkLabel { get; set; }

    /// <summary>Subtask-depth snapshot (0 = top-level, 1 = subtask), so a pinned subtask keeps its indent
    /// for an unresolvable host. Defaults to 0 for an old client.</summary>
    [ProtoMember(12)]
    public int SnapshotDepth { get; set; }

    /// <summary>The acting player's client-local <see cref="Scribe.Core.ScribePinInsert"/> preference (as
    /// a byte), read only when <see cref="Pinned"/> is true: where an unrelated (no pinned-parent) pin
    /// lands in the list. Placement is decided server-side (<see cref="ScribePinStore.SetPin"/>), so it
    /// travels with the request like <c>ScribeCompleteTaskMessage.Policy</c>. Defaults to 0
    /// (<see cref="Scribe.Core.ScribePinInsert.Bottom"/>), the legacy always-append behavior.</summary>
    [ProtoMember(13)]
    public byte PinInsert { get; set; }
}
