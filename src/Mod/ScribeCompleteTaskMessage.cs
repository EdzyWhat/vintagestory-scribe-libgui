using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client -&gt; server: mark a task complete (toggle its done state), addressed by stable identity
/// rather than a block position + list index. This replaces the retired position-addressed
/// <c>ScribeToggleTaskMessage</c>: the same server op drives the lectern read-view checkbox today
/// and a future pinned-task HUD/tab checkbox, neither of which knows a block position (a synced pin
/// carries only <c>(DocId, TaskId)</c>).
///
/// Lock-free, like the old read-view toggle: any viewer may tick a task off even while another
/// player holds the editor lock. The per-player pin store is authoritative for a pinned task's
/// done-state: the server toggles the acting player's pin done, writes through to the source
/// document's task when it resolves (reconciling only the acting player), then applies the completion
/// policy the client carried in <see cref="Policy"/> (Sink keeps the pin; Unpin removes it; Delete
/// removes the task + pin). An unresolvable/destroyed source still completes via the store alone.
///
/// The completion policy is a client-local preference (no longer server-side state), so the client
/// sends its current policy here and the server normalizes it (unknown → Sink) before applying —
/// see <see cref="ScribePlayerSettings.NormalizePolicy"/>.
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

    /// <summary>The acting player's current completion policy, sent as its raw byte so protobuf-net
    /// round-trips it robustly. Defaults to 0 (<see cref="ScribeCompletionPolicy.Sink"/>), so an
    /// absent/old value is the safe non-destructive default; the server normalizes any unrecognized
    /// value back to Sink.</summary>
    [ProtoMember(3)]
    public byte Policy { get; set; }

    /// <summary>The acting player's Subtask Behavior, sent as its raw byte. Defaults to 0
    /// (<see cref="ScribeSubtaskBehavior.Bound"/>), so an absent/old client that omits this field
    /// gets Bound; the server normalizes any unrecognized value back to Bound.</summary>
    [ProtoMember(4)]
    public byte SubtaskBehavior { get; set; }
}
