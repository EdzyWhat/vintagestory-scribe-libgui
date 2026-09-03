using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client → server: "is this target in range of my Assignment Desk?" (`assignment-delivery-mode`
/// capability's Hybrid range check). Sent whenever the Create Assignments tab's target-player picker
/// selects a new target. Must be server-authoritative: the client only ever receives entity updates for
/// players within its own tracking distance, so it cannot itself determine whether a FAR-away target is
/// in range — only the server always knows every online player's true position (and, offline, their
/// last-known one via <see cref="Scribe.Core.ScribePlayerLocationStore"/>).
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

/// <summary>Server → client reply to <see cref="ScribeDeliveryRangeCheckRequestMessage"/>. Echoes the
/// target uid so a reply for a since-superseded target selection (the player picked someone else before
/// this arrived) is recognizably stale and ignored by the client.</summary>
[ProtoContract]
public sealed class ScribeDeliveryRangeCheckReplyMessage
{
    [ProtoMember(1)]
    public string? TargetPlayerUid { get; set; }

    [ProtoMember(2)]
    public bool InRange { get; set; }
}
