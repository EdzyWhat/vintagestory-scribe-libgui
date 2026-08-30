using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → client: delivers the recipient player their own Assignment-tab (Sent) and Inbox
/// (Received) views — never another player's. Each payload is a
/// <see cref="Scribe.Core.ScribeAssignmentStore.SerializeList"/> blob, decoded client-side with
/// <see cref="Scribe.Core.ScribeAssignmentStore.TryDeserializeList"/>. Sent on join (initial
/// delivery) and re-sent to every affected player whenever the store changes for them, mirroring
/// <see cref="ScribePinnedSetMessage"/>'s resend discipline.
/// </summary>
[ProtoContract]
public sealed class ScribeAssignmentSyncMessage
{
    /// <summary>A <c>ScribeAssignmentStore.SerializeList</c> blob of assignments this player SENT.</summary>
    [ProtoMember(1)]
    public byte[]? SentBytes { get; set; }

    /// <summary>A <c>ScribeAssignmentStore.SerializeList</c> blob of assignments this player RECEIVED.</summary>
    [ProtoMember(2)]
    public byte[]? ReceivedBytes { get; set; }
}
