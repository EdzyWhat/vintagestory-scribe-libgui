using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client -&gt; server: delete a task as a first-class standalone action, addressed by the task's stable
/// identity <c>(DocId, TaskId)</c> — never a block position — so the Pin Tab can delete a task directly
/// (not only as a side effect of the <c>Delete</c> completion policy). <see cref="DocId"/>/<see cref="TaskId"/>
/// are the raw 16-byte forms of the <c>Guid</c>s (matching the sibling pin messages).
///
/// Best-effort write-through, lock-free (reuses <see cref="BlockEntityScribeLectern.DeleteTaskFromReader"/>):
/// when the owning document resolves, the task is removed from the authoritative document without acquiring
/// the editor lock; regardless, the acting player's pin for that task is removed from their set and re-synced.
/// A safe no-op if the pin/task is already gone or the source is unresolvable.
///
/// <para>UNPIN (remove the pin without deleting the task) is NOT this message — it is
/// <see cref="ScribeSetPinMessage"/> with <c>Pinned = false</c>, which already needs no block resolution.</para>
/// </summary>
[ProtoContract]
public sealed class ScribeDeleteTaskMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The acting player's Subtask Behavior, sent as its raw byte. Defaults to 0
    /// (<see cref="ScribeSubtaskBehavior.Bound"/>), so an absent/old client that omits this field
    /// gets Bound; the server normalizes any unrecognized value back to Bound.</summary>
    [ProtoMember(3)]
    public byte SubtaskBehavior { get; set; }
}
