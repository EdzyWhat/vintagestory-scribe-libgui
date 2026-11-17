using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client -&gt; server: delete a task as a standalone action, addressed by stable identity
/// <c>(DocId, TaskId)</c> (raw 16-byte Guids) rather than a block position, so the Pin Tab can delete directly.
///
/// Best-effort and lock-free by default (reuses <see cref="BlockEntityScribeLectern.DeleteTaskFromReader"/>): when
/// the document resolves the task is removed without taking the editor lock, unless <see cref="RespectEditorLock"/>
/// is set and another player holds it; either way the acting player's pin is
/// removed and re-synced. A no-op if the pin/task is already gone. UNPIN is <see cref="ScribeSetPinMessage"/>
/// with <c>Pinned = false</c>, not this message.
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

    /// <summary>The acting player's Subtask Behavior as its raw byte. Default 0 is
    /// <see cref="ScribeSubtaskBehavior.Bound"/>; unrecognized values normalize to Bound.</summary>
    [ProtoMember(3)]
    public byte SubtaskBehavior { get; set; }

    /// <summary>When true the server refuses the delete while another player holds the document's editor lock
    /// (the read view's Delete). Default false: the Pin Tab's delete ignores the lock.</summary>
    [ProtoMember(4)]
    public bool RespectEditorLock { get; set; }
}
