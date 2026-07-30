using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: notify the server that this client has opened a Lectern GUI. The server
/// records the visiting player and the in-game date, then syncs the updated guestbook back.
/// Addressed by <see cref="DocIdBytes"/> (replacing the former PosX/Y/Z fields).</summary>
[ProtoContract]
public sealed class ScribeRecordVisitorMessage
{
    /// <summary>The lectern's document <c>DocId</c> as 16 raw bytes.</summary>
    [ProtoMember(1)] public byte[]? DocIdBytes { get; set; }
}
