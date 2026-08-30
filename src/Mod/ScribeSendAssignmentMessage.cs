using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: create and send a new player-to-player assignment. The server is authoritative
/// for the assigner identity, assigned date, initial state, and destination; the client supplies only
/// the stable assignment id, target player, and task text from the Assignment Desk form.
/// </summary>
[ProtoContract]
public sealed class ScribeSendAssignmentMessage
{
    /// <summary>Stable id shared by the assigner's record and the recipient's record.</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>UID of the player who should receive the assignment.</summary>
    [ProtoMember(2)]
    public string? TargetPlayerUid { get; set; }

    /// <summary>Task text to place in the recipient's document.</summary>
    [ProtoMember(3)]
    public string? TaskText { get; set; }
}
