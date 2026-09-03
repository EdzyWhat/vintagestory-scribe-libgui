using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: delete the sender's OWN side of a terminal-state assignment record
/// (manage-terminal-assignment-records / split-assignment-delete-by-viewer), addressed by
/// <see cref="AssignmentId"/>. <see cref="Side"/> names which side the delete came from (the client
/// always knows this unambiguously — the delete control only ever renders inside the Inbox view or
/// the Sent Assignment History view, never both). The server still independently re-verifies the
/// terminal-state restriction AND that the sender actually holds the claimed side through
/// <see cref="Scribe.Core.ScribeAssignmentStore.TryDelete"/> — this whole message is a hint, never
/// trusted proof of authorization.</summary>
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
