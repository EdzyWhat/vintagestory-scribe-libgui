using ProtoBuf;

namespace Scribe;

/// <summary>Client → server: notify the server that this client has opened the Lectern GUI. No
/// payload needed — the server identifies the player from the packet sender and resolves the lectern
/// from the block position in the channel context.</summary>
[ProtoContract]
public sealed class ScribeRecordVisitorMessage
{
    /// <summary>Block position of the lectern, serialized as three ints.</summary>
    [ProtoMember(1)] public int PosX { get; set; }
    [ProtoMember(2)] public int PosY { get; set; }
    [ProtoMember(3)] public int PosZ { get; set; }
}
