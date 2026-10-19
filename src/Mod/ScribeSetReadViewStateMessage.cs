using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: persist this item/block's Read View filter pill + subtask-group collapse state,
/// addressed by the document's <c>DocId</c>, never a block position or slot. Both pieces always ride
/// together (one write path, <c>ScribeDialogBase.PersistReadViewState</c>), so this carries the FULL
/// current state, not a delta.
///
/// <see cref="DocId"/> and each <see cref="CollapsedGroupIds"/> entry are raw 16-byte <c>Guid</c>s
/// (protobuf-net's own <c>Guid</c> handling is version-fragile). Best-effort and lock-free (see
/// <see cref="IScribeDocumentHost.SetReadViewStateFromReader"/>): a viewer preference, not a document
/// edit, so no editor lock is needed and an unresolvable document is a silent no-op.
/// </summary>
[ProtoContract]
public sealed class ScribeSetReadViewStateMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The selected pill, a <see cref="ReadViewFilterCategory"/> cast to byte.</summary>
    [ProtoMember(2)]
    public byte FilterCategory { get; set; }

    /// <summary>Every currently-collapsed subtask group's parent TaskId, each as 16 raw bytes.</summary>
    [ProtoMember(3)]
    public List<byte[]>? CollapsedGroupIds { get; set; }
}
