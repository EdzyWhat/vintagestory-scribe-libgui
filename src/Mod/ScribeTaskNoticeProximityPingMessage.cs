using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → single client: "a sealed Task Notice addressed to you was found nearby". Never broadcast.
/// Carries a double-precision world position (not a <c>BlockPos</c>) since the stack may be a dropped
/// <c>EntityItem</c> at a fractional position.
/// </summary>
[ProtoContract]
public sealed class ScribeTaskNoticeProximityPingMessage
{
    [ProtoMember(1)] public double X { get; set; }
    [ProtoMember(2)] public double Y { get; set; }
    [ProtoMember(3)] public double Z { get; set; }
}
