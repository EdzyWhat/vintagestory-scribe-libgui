using ProtoBuf;

namespace Scribe;

/// <summary>Server → client: delivers the current guestbook state for a specific lectern. Payload
/// is a <see cref="Scribe.Core.GuestbookStore"/> binary blob.</summary>
[ProtoContract]
public sealed class ScribeGuestbookSyncMessage
{
    [ProtoMember(1)] public int PosX { get; set; }
    [ProtoMember(2)] public int PosY { get; set; }
    [ProtoMember(3)] public int PosZ { get; set; }

    /// <summary>Serialized <see cref="Scribe.Core.GuestbookStore"/> bytes.</summary>
    [ProtoMember(4)] public byte[]? GuestbookBytes { get; set; }
}
