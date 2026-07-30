using ProtoBuf;

namespace Scribe;

/// <summary>Server → client: delivers the current guestbook state for a specific lectern. Payload
/// is a <see cref="Scribe.Core.GuestbookStore"/> binary blob. Addressed by <see cref="DocIdBytes"/>
/// (replacing the former PosX/Y/Z fields).</summary>
[ProtoContract]
public sealed class ScribeGuestbookSyncMessage
{
    /// <summary>The lectern's document <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }

    /// <summary>Serialized <see cref="Scribe.Core.GuestbookStore"/> bytes.</summary>
    [ProtoMember(4)] public byte[]? GuestbookBytes { get; set; }
}
