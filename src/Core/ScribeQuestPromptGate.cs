namespace Scribe.Core;

/// <summary>
/// Pure decision function for whether a quest accept/completion prompt is suppressed, given the
/// player's synced decision ledger. Has no store/network/watcher awareness.
/// </summary>
public static class ScribeQuestPromptGate
{
    /// <summary>
    /// <paramref name="existingDecision"/> is the recorded decision for this <c>(Source, QuestCode,
    /// IsCompletion)</c> key, or null. No decision: never suppressed. A decision suppresses, UNLESS this is
    /// an accept-prompt (<paramref name="isCompletion"/> false) with a fresh re-accept
    /// (<paramref name="freshAcceptTransition"/>) and no live Quest Link (<paramref name="hasLiveQuestLink"/>)
    /// — a best-effort reset-clear so a stale decision from before a repeatable-quest reset does not
    /// suppress the fresh prompt. Completion-prompts have no reset signal and are never cleared.
    /// </summary>
    public static bool ShouldSuppressPrompt(
        ScribeQuestDecision? existingDecision, bool isCompletion, bool freshAcceptTransition, bool hasLiveQuestLink)
    {
        if (existingDecision is null) return false;
        if (!isCompletion && freshAcceptTransition && !hasLiveQuestLink) return false;
        return true;
    }
}
