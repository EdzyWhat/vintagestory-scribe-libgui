using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the pure quest-prompt suppression rule (design.md Decisions 2 and 4 —
// fix-quest-prompt-persistence-and-auto-pin), exercised against stubbed decision values rather than a
// live ledger/watcher round trip.
public class ScribeQuestPromptGateTests
{
    [Fact]
    public void NoDecision_NeverSuppresses()
    {
        Assert.False(ScribeQuestPromptGate.ShouldSuppressPrompt(
            existingDecision: null, isCompletion: false, freshAcceptTransition: false, hasLiveQuestLink: false));
        Assert.False(ScribeQuestPromptGate.ShouldSuppressPrompt(
            existingDecision: null, isCompletion: true, freshAcceptTransition: false, hasLiveQuestLink: true));
    }

    [Fact]
    public void AcceptedDecision_WithNoFreshTransition_Suppresses()
    {
        // quest-auto-detect: "Accepting suppresses the accept-prompt across a relog."
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Accepted, isCompletion: false, freshAcceptTransition: false, hasLiveQuestLink: true));
    }

    [Fact]
    public void DismissedDecision_WithNoFreshTransition_Suppresses()
    {
        // quest-auto-detect: "Dismissing suppresses the accept-prompt across a relog."
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Dismissed, isCompletion: false, freshAcceptTransition: false, hasLiveQuestLink: false));
    }

    [Fact]
    public void CompletionDecision_IgnoresFreshTransition_AlwaysSuppressesOnceDecided()
    {
        // Decision 4's reset-clear is scoped to accept-prompts only (IsCompletion: false) — a completion
        // decision suppresses regardless of the (irrelevant) transition/link inputs.
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Dismissed, isCompletion: true, freshAcceptTransition: true, hasLiveQuestLink: false));
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Accepted, isCompletion: true, freshAcceptTransition: true, hasLiveQuestLink: false));
    }

    [Fact]
    public void AcceptDecision_FreshTransitionButStillLinked_StillSuppresses()
    {
        // A live Quest Link still exists — not a genuine reset (Risk: "deleted the task, quest resets" is
        // the case that legitimately clears; merely re-observing an already-linked quest must not).
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Accepted, isCompletion: false, freshAcceptTransition: true, hasLiveQuestLink: true));
    }

    [Fact]
    public void AcceptDecision_FreshTransitionAndNoLiveLink_ClearsSuppression()
    {
        // Decision 4: a genuine reset (fresh re-accept fingerprint change) with no surviving Link means the
        // stale decision no longer applies — the prompt is raised again.
        Assert.False(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Accepted, isCompletion: false, freshAcceptTransition: true, hasLiveQuestLink: false));
        Assert.False(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Dismissed, isCompletion: false, freshAcceptTransition: true, hasLiveQuestLink: false));
    }

    [Fact]
    public void AcceptDecision_NoLiveLinkButNoFreshTransition_StillSuppresses()
    {
        // No live link alone (e.g. the player just deleted the pinned task, no genuine game-side reset) must
        // NOT clear the decision — only paired with a real fresh transition (the disclosed Risk/Trade-off).
        Assert.True(ScribeQuestPromptGate.ShouldSuppressPrompt(
            ScribeQuestDecision.Accepted, isCompletion: false, freshAcceptTransition: false, hasLiveQuestLink: false));
    }
}
