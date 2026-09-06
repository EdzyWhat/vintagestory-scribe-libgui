using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server -&gt; client: delivers the recipient player their own full quest-decision ledger (never another
/// player's) — mirrors <see cref="ScribePinnedSetMessage"/> exactly. The payload is a
/// <c>ScribeQuestDecisionCodec</c> SQDL list blob, decoded client-side with
/// <c>ScribeQuestDecisionCodec.TryDeserializeList</c>. Sent on join (initial delivery) and re-sent
/// whenever the player's ledger changes, so the client-side prompt-suppress check
/// (<see cref="ScribeModSystem.OnQuestAccepted"/>/<c>OnQuestCompleted</c>) never needs a round trip.
/// </summary>
[ProtoContract]
public sealed class ScribeQuestDecisionSetMessage
{
    /// <summary>A <c>ScribeQuestDecisionCodec.SerializeList</c> blob of the player's decisions.</summary>
    [ProtoMember(1)]
    public byte[]? DecisionSetBytes { get; set; }
}
