using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>One pending Quest Accept/Completion notification awaiting the player's decision under
/// <see cref="ScribeQuestAcceptPolicy.Prompt"/>/<see cref="ScribeQuestCompletionPolicy.Prompt"/>
/// (add-assignment-and-quest-support §11.2/§11.4). Rendered as a small banner on the pinned-task HUD
/// (<c>HudPinsContent</c>) with Accept/Dismiss + a Settings shortcut. <see cref="IsCompletion"/>
/// distinguishes an accept-prompt (accepting creates a Quest Link) from a completion-prompt (accepting
/// marks the already-linked task done) so the HUD can word the banner correctly; both resolve through
/// <see cref="ScribeModSystem.AcceptQuestPrompt"/>. <see cref="Source"/> records which backend detected
/// this (add-progression-framework-quest-support Decision 1) so a same-code collision between backends is
/// never ambiguous.</summary>
public readonly record struct ScribeQuestPrompt(string Source, string QuestCode, string Title, bool IsCompletion);

public sealed partial class ScribeModSystem
{
    // ── Quest soft auto-detect (Layer 2) ────────────────────────────────────────────────────────────

    private ScribeQuestWatcher? questWatcher;

    private readonly List<ScribeQuestPrompt> pendingQuestPrompts = new();

    /// <summary>Fired whenever <see cref="PendingQuestPrompts"/> changes, so the HUD knows to rebuild
    /// (and, for a fresh first prompt with zero pins, self-open) — mirrors <see cref="MyPinsChanged"/>.</summary>
    public event Action? QuestPromptsChanged;

    /// <summary>Quest accept/completion notifications awaiting the player's Accept/Dismiss, oldest
    /// first. Client-side, session-only (never persisted — a relog simply drops any still-pending one,
    /// which the watcher will re-raise on its next scan if the underlying quest state is unchanged).</summary>
    public IReadOnlyList<ScribeQuestPrompt> PendingQuestPrompts => pendingQuestPrompts;

    /// <summary>Pre-formatted live progress text for a quest Link row (§11.3), or null when unavailable —
    /// neither backend installed, its state never read this session, or the quest isn't in this world's
    /// catalog. Dispatches strictly by <paramref name="source"/> (the Link's own recorded backend —
    /// add-progression-framework-quest-support: "A Quest Link's backend is explicit, never inferred") —
    /// never tries the other backend even if <paramref name="questCode"/> happens to collide. Read by
    /// <see cref="ScribeDialogBase.Layout"/> when building a read/editor row.</summary>
    public string? TryGetQuestProgressText(string source, string questCode)
    {
        if (questWatcher is null) return null;
        if (source == ScribeQuestSource.ProgressionFramework)
        {
            if (!questWatcher.TryGetPfObjectiveDefs(questCode, out var pfObjectives)) return null;
            if (!questWatcher.TryGetPfObjectiveStatus(questCode, out var statusByCode)) return null;
            return ScribeProgressionFrameworkQuestCatalog.FormatProgress(pfObjectives, statusByCode);
        }
        if (!questWatcher.TryGetObjectives(questCode, out var objectives)) return null;
        if (!questWatcher.TryGetLiveProgress(questCode, out var counts)) return null;
        return ScribeQuestCatalog.FormatProgress(objectives, counts);
    }

    private void StartQuestWatcher(ICoreClientAPI api)
        => questWatcher = new ScribeQuestWatcher(api, OnQuestAccepted, OnQuestCompleted);

    private void DisposeQuestWatcher()
    {
        questWatcher?.Dispose();
        questWatcher = null;
    }

    /// <summary>This player's current Accept-placement candidates for a Quest auto-link (Decision 3), the
    /// same shared computation Assignment's Inbox Accept control uses. Exposed publicly for the HUD banner
    /// (<see cref="HudScribePins"/>) to render the 0/1/2+ rule (disabled/plain-button/picker) exactly like
    /// the Inbox row does.</summary>
    internal List<ScribeAcceptCandidate> ComputeQuestAcceptCandidates()
        => capi is null ? new List<ScribeAcceptCandidate>() : ScribeAcceptCandidates.Compute(capi, LastOpenedScribeItemDocId);

    /// <summary>A quest was just detected as accepted (<see cref="ScribeQuestWatcher"/>). ALWAYS posts a
    /// chat notification — Scribe watching quest state at all is not obvious, so every policy gets one —
    /// then branches on <see cref="ScribePlayerSettings.QuestAcceptPolicy"/>: Always sends the auto-link
    /// request immediately UNLESS 2+ eligible destinations are carried, in which case it falls back to a
    /// Prompt-style banner instead of silently guessing among them (Decision 3's alternative-considered
    /// resolution — add-progression-framework-quest-support); Never does nothing further; Prompt queues a
    /// HUD banner instead of acting.</summary>
    private void OnQuestAccepted(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;
        switch (MySettings.QuestAcceptPolicy)
        {
            case ScribeQuestAcceptPolicy.Always:
                var candidates = ComputeQuestAcceptCandidates();
                if (candidates.Count >= 2)
                {
                    capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-prompt", quest.Title));
                    QueuePrompt(new ScribeQuestPrompt(quest.Source, quest.QuestCode, quest.Title, IsCompletion: false));
                }
                else
                {
                    SendAutoLinkQuest(quest, candidates.Count == 1 ? candidates[0] : (ScribeAcceptCandidate?)null);
                    capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-always", quest.Title));
                }
                break;
            case ScribeQuestAcceptPolicy.Never:
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-never", quest.Title));
                break;
            default: // Prompt
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-prompt", quest.Title));
                QueuePrompt(new ScribeQuestPrompt(quest.Source, quest.QuestCode, quest.Title, IsCompletion: false));
                break;
        }
    }

    /// <summary>A quest was just detected as completed. Only an ALREADY-PINNED Quest Link for this exact
    /// (source, quest code) counts as "linked" (mirrors the HUD Tracker engine's own scope: acted on
    /// visible/live state, not every unloaded document) — a Link that exists but isn't pinned is a
    /// disclosed gap. Matching by source too (not just code) means a code collision between backends can
    /// never mark the wrong Link's pin done. The chat notification always fires regardless of whether a
    /// match was found.</summary>
    private void OnQuestCompleted(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;
        var matches = FindPinnedQuestLinks(quest.Source, quest.QuestCode);

        switch (MySettings.QuestCompletionPolicy)
        {
            case ScribeQuestCompletionPolicy.Always:
                foreach (var pin in matches) SendPinCompletion(pin);
                capi.ShowChatMessage(Lang.Get(matches.Count > 0
                    ? "scribe:scribe-quest-completed-always-linked"
                    : "scribe:scribe-quest-completed-always-nolink", quest.Title));
                break;
            case ScribeQuestCompletionPolicy.Never:
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-completed-never", quest.Title));
                break;
            default: // Prompt
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-completed-prompt", quest.Title));
                if (matches.Count > 0) QueuePrompt(new ScribeQuestPrompt(quest.Source, quest.QuestCode, quest.Title, IsCompletion: true));
                break;
        }
    }

    private List<ScribePinnedRef> FindPinnedQuestLinks(string source, string questCode)
        => MyPins.Where(p => p.Kind == ScribeBlockKind.Link
                && ScribeLinkTarget.QuestSource(p.LinkTarget) == source
                && ScribeLinkTarget.QuestCode(p.LinkTarget) == questCode)
            .ToList();

    private void QueuePrompt(ScribeQuestPrompt prompt)
    {
        if (pendingQuestPrompts.Any(p => p.Source == prompt.Source && p.QuestCode == prompt.QuestCode && p.IsCompletion == prompt.IsCompletion)) return;
        pendingQuestPrompts.Add(prompt);
        QuestPromptsChanged?.Invoke();
    }

    /// <summary>The HUD banner's Accept action: link (accept-prompt) or complete (completion-prompt) the
    /// prompted quest, then dismiss it. <paramref name="candidate"/> is the destination the HUD resolved
    /// via <see cref="ComputeQuestAcceptCandidates"/> (the sole eligible one, or the player's picker choice
    /// among 2+) — ignored for a completion-prompt, which has no destination to choose (the task is
    /// already linked). Null falls back to server-side resolution (task 6.1's backward-compatibility path).</summary>
    internal void AcceptQuestPrompt(ScribeQuestPrompt prompt, ScribeAcceptCandidate? candidate = null)
    {
        if (prompt.IsCompletion)
        {
            foreach (var pin in FindPinnedQuestLinks(prompt.Source, prompt.QuestCode)) SendPinCompletion(pin);
        }
        else
        {
            SendAutoLinkQuest(new ScribeQuestCatalogEntry(prompt.Source, prompt.QuestCode, prompt.Title, null, Array.Empty<ScribeQuestObjectiveDef>()), candidate);
        }
        DismissQuestPrompt(prompt);
    }

    /// <summary>The HUD banner's Dismiss action (or Accept's own cleanup): drop the prompt with no
    /// further action. Session-only — the watcher's own dedup means dismissing doesn't re-arm anything.</summary>
    public void DismissQuestPrompt(ScribeQuestPrompt prompt)
    {
        if (pendingQuestPrompts.Remove(prompt)) QuestPromptsChanged?.Invoke();
    }

    private void SendPinCompletion(ScribePinnedRef pin)
    {
        if (capi is null) return;
        capi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = pin.OwnerDocId.ToByteArray(),
            TaskId = pin.TaskId.ToByteArray(),
            Policy = (byte)MySettings.CompletionPolicy,
            SubtaskBehavior = (byte)MySettings.SubtaskBehavior,
        });
    }

    /// <summary>Sends the auto-link request, naming the resolved destination (assignment-state-machine's
    /// placement requirement, extended to Quest auto-link — Decision 3). <paramref name="candidate"/> null
    /// is the defensive/legacy path: the server falls back to <c>FindNotebookInInventory</c> (task 6.4),
    /// matching this message's pre-picker behavior.</summary>
    private void SendAutoLinkQuest(ScribeQuestCatalogEntry quest, ScribeAcceptCandidate? candidate)
    {
        if (capi is null) return;
        capi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeAutoLinkQuestMessage
        {
            Source = quest.Source,
            QuestCode = quest.QuestCode,
            Title = quest.Title,
            Description = quest.Description,
            TargetInventoryId = candidate?.InventoryId,
            TargetSlotId = candidate?.SlotId ?? -1,
        });
    }

    /// <summary>Server-side handler for <see cref="ScribeAutoLinkQuestMessage"/> (Quest Accept Policy =
    /// Always/Prompt-accepted). Resolves the destination the client named
    /// (<see cref="ScribeAutoLinkQuestMessage.TargetInventoryId"/>/<see cref="ScribeAutoLinkQuestMessage.TargetSlotId"/>)
    /// and RE-VALIDATES it (writeable, has capacity) exactly like <c>TryPlaceAcceptedAssignment</c> does —
    /// never trusts the client's choice as proof of eligibility. Falls back to
    /// <see cref="FindNotebookInInventory"/> only when no target was sent, or the sent one didn't resolve
    /// to something writeable (task 6.4's defensive compatibility path for a legacy/absent target). Is
    /// authoritative for whether the Link is actually added: silently no-ops with no player-visible error
    /// if the player carries no Scribe document, has no capacity, or already has a Link for this exact
    /// (source, quest code). That last check is what makes repeat detection (e.g. across a relog)
    /// idempotent with no client-side persisted state.</summary>
    private void OnServerReceivedAutoLinkQuest(IServerPlayer fromPlayer, ScribeAutoLinkQuestMessage message)
    {
        if (sapi is null) return;
        string source = string.IsNullOrWhiteSpace(message.Source) ? ScribeQuestSource.VsQuest : message.Source!;
        string? questCode = message.QuestCode;
        if (string.IsNullOrWhiteSpace(questCode)) return;

        NotebookHost? host = null;
        if (message.TargetInventoryId is not null)
        {
            var slot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
            if (slot?.Itemstack?.Collectible is IScribeDocumentItem item && item.IsSlotWriteable(slot))
            {
                host = slot.Itemstack.Collectible is ItemScribeTablet ? new TabletHost(slot) : new NotebookHost(slot);
                host.AttachServerContext(sapi, fromPlayer);
            }
        }
        host ??= FindNotebookInInventory(fromPlayer);
        if (host is null)
        {
            Trace("auto-link-quest from {0}: no carried Notebook/Tablet — skipped", fromPlayer.PlayerName);
            return;
        }

        var doc = host.Document;
        bool alreadyLinked = doc.Blocks.Any(b => b.Kind == ScribeBlockKind.Link
            && ScribeLinkTarget.QuestSource(b.LinkTarget) == source
            && ScribeLinkTarget.QuestCode(b.LinkTarget) == questCode);
        if (alreadyLinked)
        {
            Trace("auto-link-quest from {0}: already linked ({1}/{2}) — skipped", fromPlayer.PlayerName, source, questCode);
            return;
        }

        if (!host.Policy.CanHold(doc.BlockCount + 1))
        {
            Trace("auto-link-quest from {0}: target has no capacity — skipped", fromPlayer.PlayerName);
            return;
        }

        doc.AddQuestLink(source, questCode, message.Title, message.Description);
        host.Flush();
        Trace("auto-link-quest from {0}: linked {1}/{2}", fromPlayer.PlayerName, source, questCode);
    }
}
