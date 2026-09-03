using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server → single client: "a sealed Task Notice addressed to you was found nearby" (`task-notice-
/// proximity-signal` capability, tasks.md 5.1-5.4). Sent only to the one player whose scan just found a
/// match — this is a player-specific ambient discovery cue, never broadcast. Carries a double-precision
/// world position (not a <c>BlockPos</c>) since the found stack may be a dropped <c>EntityItem</c>
/// sitting at a fractional position, not necessarily aligned to a block center.
/// </summary>
[ProtoContract]
public sealed class ScribeTaskNoticeProximityPingMessage
{
    [ProtoMember(1)] public double X { get; set; }
    [ProtoMember(2)] public double Y { get; set; }
    [ProtoMember(3)] public double Z { get; set; }
}
