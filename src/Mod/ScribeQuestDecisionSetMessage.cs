using ProtoBuf;

namespace Scribe;

/// <summary>
/// Server -&gt; client: delivers the recipient player their own full quest-decision ledger (never another
/// player's); mirrors <see cref="ScribePinnedSetMessage"/>. Sent on join and on every change, so the
/// client-side prompt-suppress check (<see cref="ScribeModSystem.OnQuestAccepted"/>/<c>OnQuestCompleted</c>)
/// needs no round trip. Decoded with <c>ScribeQuestDecisionCodec.TryDeserializeList</c>.
/// </summary>
[ProtoContract]
public sealed class ScribeQuestDecisionSetMessage
{
    /// <summary>A <c>ScribeQuestDecisionCodec.SerializeList</c> blob of the player's decisions.</summary>
    [ProtoMember(1)]
    public byte[]? DecisionSetBytes { get; set; }
}
