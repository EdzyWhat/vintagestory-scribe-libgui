using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: update a Tracker task's live <c>CurrentQuantity</c>, addressed by the task's stable
/// identity <c>(DocId, TaskId)</c> — never a block position. The client-side count engine
/// (<see cref="ScribeDialogBase"/>'s tracker partial) recomputes a Tracker's count from the viewing
/// player's carried inventory and sends this when the count changes, so the value is persisted
/// server-side and other viewers converge — exactly like <see cref="ScribeCompleteTaskMessage"/> carries
/// the Done flag (add-tracker-link-tasks D5/4.3).
///
/// <see cref="DocId"/>/<see cref="TaskId"/> are the raw 16-byte forms of the <c>Guid</c>s (protobuf-net's
/// own <c>Guid</c> handling is version-fragile, so raw byte arrays are used, matching the sibling task
/// messages). Best-effort write-through, lock-free (reuses
/// <see cref="BlockEntityScribeWritingStation.SetTrackerCurrentQuantityFromReader"/>): when the owning
/// document resolves, the value is clamped into <c>[0, TargetQuantity]</c> and written without acquiring
/// the editor lock; a non-Tracker / unknown id / no-op change writes nothing.
/// </summary>
[ProtoContract]
public sealed class ScribeSetTrackerQuantityMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The Tracker task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The freshly-counted carried quantity. Clamped into <c>[0, TargetQuantity]</c> server-side.</summary>
    [ProtoMember(3)]
    public int Quantity { get; set; }
}
