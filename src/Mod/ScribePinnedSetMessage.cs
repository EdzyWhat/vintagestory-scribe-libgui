using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server -&gt; client: delivers the recipient player their own full pin set (never another player's).
/// The payload is a <c>ScribePinCodec</c> SPIN list blob — the same fail-safe, versioned binary form
/// used everywhere else — so the client decodes it with <c>ScribePinCodec.TryDeserializeList</c>.
/// Sent on join (initial delivery) and re-sent whenever the player's set changes (a pin added,
/// removed, orphaned, or its snapshot refreshed), mirroring the vanilla waypoints resend discipline.
/// </summary>
[ProtoContract]
public sealed class ScribePinnedSetMessage
{
    /// <summary>A <c>ScribePinCodec.SerializeList</c> blob of the player's pins.</summary>
    [ProtoMember(1)]
    public byte[]? PinnedRefBytes { get; set; }
}
