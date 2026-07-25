using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: mark a task complete (toggle its done state), addressed by stable identity
/// rather than a block position + list index. This replaces the retired position-addressed
/// <c>ScribeToggleTaskMessage</c>: the same server op drives the lectern read-view checkbox today
/// and a future pinned-task HUD/tab checkbox, neither of which knows a block position (a synced pin
/// carries only <c>(DocId, TaskId)</c>).
///
/// Lock-free, like the old read-view toggle: any viewer may tick a task off even while another
/// player holds the editor lock. The server resolves the document via the store's live
/// DocId→position index, toggles the task's done flag on its own authoritative document, and — per
/// the completing player's <c>CompleteUnpins</c> setting — may then remove that player's pin. An
/// unresolvable/orphaned target completes nothing and simply removes the pin (the "check it off and
/// it leaves my list" gesture stays uniform whether or not the source still exists).
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
}
