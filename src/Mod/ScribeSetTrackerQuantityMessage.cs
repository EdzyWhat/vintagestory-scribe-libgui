using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: update a Tracker task's live <c>CurrentQuantity</c>, addressed by
/// <c>(DocId, TaskId)</c>, never a block position. The client count engine
/// (<see cref="ScribeDialogBase"/>'s tracker partial) recomputes the count from the viewer's carried
/// inventory and sends this on change, so other viewers converge (as <see cref="ScribeCompleteTaskMessage"/>
/// does for Done).
///
/// <see cref="DocId"/>/<see cref="TaskId"/> are raw 16-byte <c>Guid</c>s (protobuf-net's own <c>Guid</c>
/// handling is version-fragile). Best-effort and lock-free (see
/// <see cref="BlockEntityScribeWritingStation.SetTrackerCurrentQuantityFromReader"/>): when the document
/// resolves the value is clamped into <c>[0, TargetQuantity]</c> and written without the editor lock; a
/// non-Tracker, unknown id or no-op change writes nothing.
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

    /// <summary>The freshly-counted carried quantity, clamped into <c>[0, TargetQuantity]</c> server-side.</summary>
    [ProtoMember(3)]
    public int Quantity { get; set; }
}
