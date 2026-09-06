using ProtoBuf;

namespace Scribe;

/// <summary>
/// Client -&gt; server: the sending player explicitly dismissed a pending quest prompt (accept or
/// completion), keyed the same way <see cref="ScribeQuestPrompt"/>/the decision ledger key itself is —
/// <see cref="Source"/> + <see cref="QuestCode"/> + <see cref="IsCompletion"/>. The server records a
/// <see cref="Scribe.Core.ScribeQuestDecision.Dismissed"/> decision so the same prompt is never re-raised
/// across a relog (fix-quest-prompt-persistence-and-auto-pin Decision 3). Sent from
/// <see cref="ScribeModSystem.DismissQuestPrompt"/> only for a genuine dismiss action — the accept flow's
/// own cleanup removes the pending prompt locally without sending this, since accepting already records
/// its own <see cref="Scribe.Core.ScribeQuestDecision.Accepted"/> decision via the existing
/// <c>ScribeAutoLinkQuestMessage</c>/<c>ScribeCompleteTaskMessage</c> round trips.
/// </summary>
[ProtoContract]
public sealed class ScribeDismissQuestPromptMessage
{
    /// <summary>Which backend mod this quest came from (<see cref="Scribe.Core.ScribeQuestSource"/>).
    /// Null/absent defensively falls back to <see cref="Scribe.Core.ScribeQuestSource.VsQuest"/>
    /// server-side, mirroring <c>ScribeAutoLinkQuestMessage.Source</c>.</summary>
    [ProtoMember(1)]
    public string? Source { get; set; }

    /// <summary>The quest's domain-qualified id, as recorded on the dismissed <see cref="ScribeQuestPrompt"/>.</summary>
    [ProtoMember(2)]
    public string? QuestCode { get; set; }

    /// <summary>Whether the dismissed prompt was a completion-prompt (true) or an accept-prompt (false) —
    /// the two are independent decisions (quest-auto-detect's "Decisions are independent per prompt kind").</summary>
    [ProtoMember(3)]
    public bool IsCompletion { get; set; }
}
