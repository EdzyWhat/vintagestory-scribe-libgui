using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: delete the sender's OWN side of a terminal-state assignment record addressed by
/// <see cref="AssignmentId"/>; <see cref="Side"/> names the side (the delete control renders only in the Inbox
/// or Sent History view). The message is a hint: the server re-verifies the terminal-state restriction and
/// that the sender holds the claimed side via <see cref="Scribe.Core.ScribeAssignmentStore.TryDelete"/>.</summary>
[ProtoContract]
public sealed class ScribeDeleteAssignmentMessage
{
    /// <summary>Stable id of the assignment record being deleted.</summary>
    [ProtoMember(1)]
    public byte[]? AssignmentId { get; set; }

    /// <summary>Which side of the relationship this delete is for, as a
    /// <see cref="Scribe.Core.ScribeAssignmentActor"/> byte value (0 = Assigner, 1 = Assignee).</summary>
    [ProtoMember(2)]
    public byte Side { get; set; }
}
