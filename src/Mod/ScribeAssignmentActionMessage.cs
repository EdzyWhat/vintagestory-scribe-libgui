using ProtoBuf;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// Client → server: request one of the state-machine actions for an assignment. The server derives
/// the actor from the authenticated sender, verifies the sender's role and current state, and applies
/// <see cref="Action"/> through <see cref="ScribeAssignmentTransitions"/>; the action byte is never
/// trusted as proof of authorization.
/// </summary>
[ProtoContract]
public sealed class ScribeAssignmentActionMessage
{
    /// <summary>Stable id of the assignment being acted on.</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>
    /// Requested action: Accept, Decline, Cancel, or Discard. Completed is intentionally not a valid
    /// wire action because completion is derived from the underlying task's done flag.
    /// </summary>
    [ProtoMember(2)]
    public byte Action { get; set; }
}
