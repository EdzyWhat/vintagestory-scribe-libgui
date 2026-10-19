using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: the player explicitly dismissed a pending quest prompt (accept or completion), keyed like the decision
/// ledger by <see cref="Source"/> + <see cref="QuestCode"/> + <see cref="IsCompletion"/>. The server records a
/// <see cref="Scribe.Core.ScribeQuestDecision.Dismissed"/> decision so the prompt is not re-raised across a relog. Sent from
/// <see cref="ScribeModSystem.DismissQuestPrompt"/> only for a genuine dismiss; the accept flow records its own Accepted decision.
/// </summary>
[ProtoContract]
public sealed class ScribeDismissQuestPromptMessage
{
    /// <summary>Backend mod the quest came from (<see cref="Scribe.Core.ScribeQuestSource"/>); null falls back to VsQuest server-side, like <c>ScribeAutoLinkQuestMessage.Source</c>.</summary>
    [ProtoMember(1)]
    public string? Source { get; set; }

    /// <summary>The quest's domain-qualified id, as recorded on the dismissed <see cref="ScribeQuestPrompt"/>.</summary>
    [ProtoMember(2)]
    public string? QuestCode { get; set; }

    /// <summary>True for a completion-prompt, false for an accept-prompt (independent decisions).</summary>
    [ProtoMember(3)]
    public bool IsCompletion { get; set; }
}
