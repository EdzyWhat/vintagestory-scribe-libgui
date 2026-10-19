using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server -&gt; client: delivers the recipient player their own full pin set (never another player's).
/// Sent on join and re-sent whenever the set changes; the client decodes with
/// <c>ScribePinCodec.TryDeserializeList</c>.
/// </summary>
[ProtoContract]
public sealed class ScribePinnedSetMessage
{
    /// <summary>A <c>ScribePinCodec.SerializeList</c> blob of the player's pins.</summary>
    [ProtoMember(1)]
    public byte[]? PinnedRefBytes { get; set; }
}
