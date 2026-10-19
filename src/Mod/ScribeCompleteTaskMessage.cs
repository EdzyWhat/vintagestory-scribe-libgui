using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client -&gt; server: toggle a task's done state, addressed by stable identity <c>(DocId, TaskId)</c> rather
/// than a block position, so one op serves the lectern read-view checkbox and the pinned-task HUD/tab checkbox.
///
/// Lock-free: any viewer may tick a task even while another player holds the editor lock. The server toggles the
/// acting player's pin, writes through to the source document's task when it resolves (an unresolvable source
/// still completes via the pin store), then applies the completion policy carried in <see cref="Policy"/>
/// (Sink keeps the pin; Unpin removes it; Delete removes task + pin). The policy is a client-local preference,
/// so the server normalizes it (unknown to Sink) via <see cref="ScribePlayerSettings.NormalizePolicy"/>.
/// </summary>
[ProtoContract]
public sealed class ScribeCompleteTaskMessage
{
    /// <summary>The owning document's <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)]
    public byte[]? DocId { get; set; }

    /// <summary>The task's <c>TaskId</c> as 16 raw bytes.</summary>
    [ProtoMember(2)]
    public byte[]? TaskId { get; set; }

    /// <summary>The acting player's completion policy as its raw byte. Default 0 is
    /// <see cref="ScribeCompletionPolicy.Sink"/>, the non-destructive choice; unrecognized values normalize to Sink.</summary>
    [ProtoMember(3)]
    public byte Policy { get; set; }

    /// <summary>The acting player's Subtask Behavior as its raw byte. Default 0 is
    /// <see cref="ScribeSubtaskBehavior.Bound"/>; unrecognized values normalize to Bound.</summary>
    [ProtoMember(4)]
    public byte SubtaskBehavior { get; set; }
}
