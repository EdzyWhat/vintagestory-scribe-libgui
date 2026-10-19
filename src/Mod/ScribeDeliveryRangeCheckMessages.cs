using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: "is this target in range of my Assignment Desk?" (Hybrid delivery range check), sent when
/// the target-player picker changes. Server-authoritative because the client only receives entity updates
/// within its tracking distance; the server knows every online player's position (offline: last-known via
/// <see cref="Scribe.Core.ScribePlayerLocationStore"/>).
/// </summary>
[ProtoContract]
public sealed class ScribeDeliveryRangeCheckRequestMessage
{
    /// <summary>The Assignment Desk's block position.</summary>
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }

    [ProtoMember(4)]
    public string? TargetPlayerUid { get; set; }
}

/// <summary>Server → client reply to <see cref="ScribeDeliveryRangeCheckRequestMessage"/>. Echoes the target
/// uid so the client can ignore a reply for a since-superseded selection.</summary>
[ProtoContract]
public sealed class ScribeDeliveryRangeCheckReplyMessage
{
    [ProtoMember(1)]
    public string? TargetPlayerUid { get; set; }

    [ProtoMember(2)]
    public bool InRange { get; set; }
}
