namespace Scribe.Core;

/// <summary>
/// Pure decision function for whether a quest accept/completion prompt should be suppressed, given the
/// player's synced decision ledger (design.md Decisions 2 and 4 — fix-quest-prompt-persistence-and-auto-pin).
/// Kept as a standalone, game-agnostic function (no store/network/watcher awareness) so the suppress logic
/// — including Decision 4's approximate reset-clear — is exercised directly against stubbed inputs rather
/// than through a live client/server round trip.
/// </summary>
public static class ScribeQuestPromptGate
{
    /// <summary>
    /// <paramref name="existingDecision"/> is the player's recorded decision for this exact
    /// <c>(Source, QuestCode, IsCompletion)</c> key, or null if none was ever recorded. With no decision on
    /// record, the prompt is never suppressed.
    ///
    /// With a decision on record, it normally suppresses — UNLESS this is an accept-prompt
    /// (<paramref name="isCompletion"/> false) AND the watcher just observed a genuine fresh re-accept of
    /// the underlying quest (<paramref name="freshAcceptTransition"/>) AND the player holds no live Quest
    /// Link for it (<paramref name="hasLiveQuestLink"/>) — Decision 4's best-effort reset-clear, scoped only
    /// to accept-prompts: a stale decision from BEFORE a genuine repeatable-quest reset must not suppress
    /// the fresh prompt. Completion-prompts have no equivalent reset signal (see design.md) and are never
    /// cleared this way.
    /// </summary>
    public static bool ShouldSuppressPrompt(
        ScribeQuestDecision? existingDecision, bool isCompletion, bool freshAcceptTransition, bool hasLiveQuestLink)
    {
        if (existingDecision is null) return false;
        if (!isCompletion && freshAcceptTransition && !hasLiveQuestLink) return false;
        return true;
    }
}
