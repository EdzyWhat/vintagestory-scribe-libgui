using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: this client opened a Lectern GUI. The server records the visitor and in-game
/// date, then syncs the guestbook back.</summary>
[ProtoContract]
public sealed class ScribeRecordVisitorMessage
{
    /// <summary>The lectern's document <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }
}
