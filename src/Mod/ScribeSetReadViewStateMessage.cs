using System.Collections.Generic;
using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: persist this Scribe item/block instance's Read View filter pill + subtask-group
/// collapse state (read-view-filter-and-collapse), addressed by the document's stable <c>DocId</c> —
/// never a block position or item slot, mirroring <see cref="ScribeSetTrackerQuantityMessage"/>. Both
/// pieces of state always ride together (the dialog owns one combined write path — see
/// <c>ScribeDialogBase.PersistReadViewState</c>), so this carries the FULL current state rather than a
/// single delta.
///
/// <see cref="DocId"/> is the raw 16-byte form of the <c>Guid</c> (protobuf-net's own <c>Guid</c> handling
/// is version-fragile — matching the sibling task messages); each entry of <see cref="CollapsedGroupIds"/>
/// is likewise a raw 16-byte TaskId. Best-effort write-through, lock-free (reuses
/// <see cref="IScribeDocumentHost.SetReadViewStateFromReader"/>): a viewer preference, not a document edit,
/// so no editor lock is required and an unresolvable document is a silent no-op.
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

    /// <summary>Every currently-collapsed subtask-group's parent TaskId, each as 16 raw bytes.</summary>
    [ProtoMember(3)]
    public List<byte[]>? CollapsedGroupIds { get; set; }
}
