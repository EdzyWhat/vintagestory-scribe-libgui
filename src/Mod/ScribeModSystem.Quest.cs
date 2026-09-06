using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>One pending Quest Accept/Completion notification awaiting the player's decision under
/// <see cref="ScribeQuestAcceptPolicy.PromptHud"/>/<see cref="ScribeQuestAcceptPolicy.PromptPopup"/>/
/// <see cref="ScribeQuestCompletionPolicy.Prompt"/> (add-assignment-and-quest-support §11.2/§11.4).
/// Rendered as a small banner on the pinned-task HUD (<c>HudPinsContent</c>) or, for a
/// <see cref="ScribeQuestAcceptPolicy.PromptPopup"/> accept-prompt, a center-screen modal
/// (<c>GuiDialogScribeQuestPrompt</c>) — both offer Accept/Dismiss + a Settings shortcut
/// (rework-quest-accept-notification-styles). <see cref="IsCompletion"/>
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

    /// <summary>Progression Framework's catalog objective definitions for a cataloged quest (Decision 4),
    /// exposed for the manual Quest Link picker (<c>OnClickAddQuestLink</c>) and the accept-time auto-link
    /// send (<see cref="SendAutoLinkQuest"/>) to seed a Quest Link's QuestObjective children
    /// (add-progression-framework-quest-objective-subtasks 5.3). False when the watcher hasn't started, PF
    /// isn't installed, or the quest code isn't cataloged.</summary>
    internal bool TryGetPfObjectiveDefs(string questCode, out IReadOnlyList<ScribePfObjectiveDef> objectives)
    {
        if (questWatcher is null) { objectives = Array.Empty<ScribePfObjectiveDef>(); return false; }
        return questWatcher.TryGetPfObjectiveDefs(questCode, out objectives);
    }

    /// <summary>Progression Framework's live per-objective numeric progress for a cataloged quest, exposed
    /// for the accept-time auto-link send (<see cref="SendAutoLinkQuest"/>) to seed each newly-created
    /// QuestObjective child's <c>CurrentQuantity</c> from whatever is already cached (0 if nothing yet).</summary>
    internal bool TryGetPfObjectiveProgress(string questCode, out IReadOnlyDictionary<string, int> progressByCode)
    {
        if (questWatcher is null) { progressByCode = EmptyPfProgress; return false; }
        return questWatcher.TryGetPfObjectiveProgress(questCode, out progressByCode);
    }

    private static readonly Dictionary<string, int> EmptyPfProgress = new(StringComparer.Ordinal);

    /// <summary>Whether the player has been observed to start (accept or complete) the given quest code this
    /// session, per whichever backend it belongs to (filter-quest-link-picker) — exposed for the manual Quest
    /// Link picker to filter its candidate list down to quests actually engaged with. False whenever the
    /// watcher hasn't started (neither backend installed).</summary>
    internal bool HasStartedQuest(string questCode) => questWatcher?.HasStarted(questCode) ?? false;

    private void StartQuestWatcher(ICoreClientAPI api)
        => questWatcher = new ScribeQuestWatcher(api, OnQuestAccepted, OnQuestCompleted);

    /// <summary>Client-side handler for the server's push of this player's own synced quest-decision
    /// ledger (fix-quest-prompt-persistence-and-auto-pin). Replaces the cached set wholesale — the server
    /// always sends the player's complete ledger, never a delta.</summary>
    private void OnClientReceivedQuestDecisionSet(ScribeQuestDecisionSetMessage message)
    {
        myQuestDecisions.Clear();
        if (!ScribeQuestDecisionCodec.TryDeserializeList(message.DecisionSetBytes, out var entries) || entries is null) return;
        foreach (var e in entries) myQuestDecisions.Set(e.Source, e.QuestCode, e.IsCompletion, e.Decision);
    }

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

    /// <summary>A quest was just detected as accepted (<see cref="ScribeQuestWatcher"/>). First consults the
    /// player's synced quest-decision ledger (fix-quest-prompt-persistence-and-auto-pin Decisions 2/4): if
    /// this exact accept-prompt was already decided (accepted-via-Scribe or dismissed) in an EARLIER session
    /// and isn't a genuine fresh re-accept of a now-unlinked quest, it's a stale re-detection (the underlying
    /// attribute never goes away — see <see cref="ScribeQuestWatcher"/>'s doc-comment) and is silently
    /// dropped — no chat message, no queue, no Always-policy auto-link. Otherwise ALWAYS posts a chat
    /// notification — Scribe watching quest state at all is not obvious, so every policy gets one — then
    /// branches on <see cref="ScribePlayerSettings.QuestAcceptPolicy"/>: Always sends the auto-link request
    /// immediately UNLESS 2+ eligible destinations are carried, in which case it falls back to a Prompt-style
    /// queue instead of silently guessing among them (Decision 3's alternative-considered resolution —
    /// add-progression-framework-quest-support); Never does nothing further; PromptHud/PromptPopup both
    /// queue the prompt instead of acting — which of the two presentation styles renders it is decided at
    /// render time, not here (rework-quest-accept-notification-styles).</summary>
    private void OnQuestAccepted(ScribeQuestCatalogEntry quest, bool freshTransition)
    {
        if (capi is null) return;

        var existingDecision = myQuestDecisions.Lookup(quest.Source, quest.QuestCode, isCompletion: false);
        bool hasLiveLink = FindPinnedQuestLinks(quest.Source, quest.QuestCode).Count > 0;
        if (ScribeQuestPromptGate.ShouldSuppressPrompt(existingDecision, isCompletion: false, freshTransition, hasLiveLink))
            return;

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
            default: // PromptHud / PromptPopup — queuing is identical; the render style is chosen at
                     // HUD/modal render time (rework-quest-accept-notification-styles), not here.
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-prompt", quest.Title));
                QueuePrompt(new ScribeQuestPrompt(quest.Source, quest.QuestCode, quest.Title, IsCompletion: false));
                break;
        }
    }

    /// <summary>A quest was just detected as completed. First consults the player's synced quest-decision
    /// ledger for this exact completion-prompt (fix-quest-prompt-persistence-and-auto-pin) — Decision 4's
    /// reset-clear does not apply to completion-prompts (no equivalent fresh-transition signal exists), so
    /// once decided it stays suppressed for the life of the ledger entry. Otherwise, only an ALREADY-PINNED
    /// Quest Link for this exact (source, quest code) counts as "linked" (mirrors the HUD Tracker engine's
    /// own scope: acted on visible/live state, not every unloaded document) — a Link that exists but isn't
    /// pinned is a disclosed gap. Matching by source too (not just code) means a code collision between
    /// backends can never mark the wrong Link's pin done. The chat notification always fires regardless of
    /// whether a match was found.</summary>
    private void OnQuestCompleted(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;

        var existingDecision = myQuestDecisions.Lookup(quest.Source, quest.QuestCode, isCompletion: true);
        if (ScribeQuestPromptGate.ShouldSuppressPrompt(existingDecision, isCompletion: true, freshAcceptTransition: false, hasLiveQuestLink: false))
            return;

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

    /// <summary>Re-push a single player their own full quest-decision ledger (server → client). Called on
    /// join and after any change to that player's ledger. Mirrors <see cref="PushPinsTo(IServerPlayer)"/>.</summary>
    public void PushQuestDecisionsTo(IServerPlayer player)
    {
        if (sapi is null || questDecisionStore is null) return;
        var bytes = questDecisionStore.SerializeList(player.PlayerUID);
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeQuestDecisionSetMessage { DecisionSetBytes = bytes }, player);
    }

    /// <summary>Server-side: records the player's decision on a quest prompt and re-pushes their ledger if
    /// it actually changed (fix-quest-prompt-persistence-and-auto-pin Decision 3).</summary>
    private void RecordQuestDecision(IServerPlayer player, string source, string questCode, bool isCompletion, ScribeQuestDecision decision)
    {
        if (questDecisionStore is null) return;
        if (questDecisionStore.Set(player.PlayerUID, source, questCode, isCompletion, decision))
            PushQuestDecisionsTo(player);
    }

    /// <summary>Server-side handler for <see cref="ScribeDismissQuestPromptMessage"/> — a genuine Dismiss
    /// action on a pending accept/completion prompt. Records a <see cref="ScribeQuestDecision.Dismissed"/>
    /// decision so the same prompt never re-raises across a relog.</summary>
    private void OnServerReceivedDismissQuestPrompt(IServerPlayer fromPlayer, ScribeDismissQuestPromptMessage message)
    {
        string source = string.IsNullOrWhiteSpace(message.Source) ? ScribeQuestSource.VsQuest : message.Source!;
        if (string.IsNullOrWhiteSpace(message.QuestCode)) return;
        RecordQuestDecision(fromPlayer, source, message.QuestCode!, message.IsCompletion, ScribeQuestDecision.Dismissed);
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
        // Local-only removal — NOT the public DismissQuestPrompt below: accepting already records its own
        // Accepted decision (via the AutoLinkQuest/CompleteTask round trip this just triggered), so sending
        // a Dismissed decision here too would incorrectly overwrite it.
        RemovePendingPrompt(prompt);
    }

    /// <summary>The HUD banner's / center-modal's Dismiss action: drop the prompt with no further action,
    /// AND record the dismissal server-side (fix-quest-prompt-persistence-and-auto-pin Decision 3) so the
    /// same prompt never re-raises across a relog. NOT used by <see cref="AcceptQuestPrompt"/>'s own
    /// cleanup — see <see cref="RemovePendingPrompt"/>.</summary>
    public void DismissQuestPrompt(ScribeQuestPrompt prompt)
    {
        RemovePendingPrompt(prompt);
        if (capi is null) return;
        capi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeDismissQuestPromptMessage
        {
            Source = prompt.Source,
            QuestCode = prompt.QuestCode,
            IsCompletion = prompt.IsCompletion,
        });
    }

    /// <summary>Drops the pending prompt locally with no network effect — session-only, since the
    /// watcher's own dedup means dismissing doesn't re-arm anything on THIS client this session. Shared by
    /// the genuine Dismiss action (<see cref="DismissQuestPrompt"/>, which also sends the decision) and
    /// Accept's own cleanup (which records its decision through a different round trip).</summary>
    private void RemovePendingPrompt(ScribeQuestPrompt prompt)
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
    /// matching this message's pre-picker behavior. For a Progression Framework quest, also attaches the
    /// watcher's cached catalog objective defs (+ whatever live progress it has cached) so the server can
    /// seed the new Quest Link's QuestObjective children immediately after adding it
    /// (add-progression-framework-quest-objective-subtasks 5.3) — null/empty for a VS Quest link or when
    /// nothing is cached yet (the quest's own catalog read hasn't completed this session).</summary>
    private void SendAutoLinkQuest(ScribeQuestCatalogEntry quest, ScribeAcceptCandidate? candidate)
    {
        if (capi is null) return;

        List<ScribeAutoLinkObjectiveWire>? objectives = null;
        if (quest.Source == ScribeQuestSource.ProgressionFramework
            && TryGetPfObjectiveDefs(quest.QuestCode, out var pfObjectives) && pfObjectives.Count > 0)
        {
            TryGetPfObjectiveProgress(quest.QuestCode, out var progressByCode);
            objectives = pfObjectives.Select(o => new ScribeAutoLinkObjectiveWire
            {
                Code = o.Code,
                ItemCode = o.ItemCode,
                Label = o.Label,
                Required = o.Required,
                CurrentProgress = progressByCode.TryGetValue(o.Code, out int p) ? p : 0,
            }).ToList();
        }

        capi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeAutoLinkQuestMessage
        {
            Source = quest.Source,
            QuestCode = quest.QuestCode,
            Title = quest.Title,
            Description = quest.Description,
            TargetInventoryId = candidate?.InventoryId,
            TargetSlotId = candidate?.SlotId ?? -1,
            Objectives = objectives,
            AutoPin = MySettings.AutoPinOnQuestAccept,
            PinInsert = (byte)MySettings.PinInsert,
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
        var newTaskId = doc.Blocks[^1].TaskId;
        if (message.Objectives is { Count: > 0 } objectives)
        {
            doc.ReconcileQuestObjectives(newTaskId, objectives
                .Where(o => o.Code is not null)
                .Select(o => (o.Code!, o.ItemCode, o.Label, o.Required))
                .ToList(), createMissing: true);
            foreach (var wire in objectives)
            {
                var child = doc.Blocks.FirstOrDefault(b => b.IsQuestObjective
                    && string.Equals(b.LinkTarget, wire.Code, StringComparison.Ordinal));
                if (child is not null) doc.SetQuestObjectiveProgress(child.TaskId, wire.CurrentProgress);
            }
        }
        host.Flush();

        // Decision 3: accept already round-trips through this handler, so it also records the ledger
        // entry — no separate message needed for accept, unlike dismiss.
        RecordQuestDecision(fromPlayer, source, questCode, isCompletion: false, ScribeQuestDecision.Accepted);

        // Decision 5: auto-pin reuses the just-created TaskId, through the same server-side pin-add path
        // the manual Pin control's network handler uses. The quest is accepted/linked regardless of
        // whether this follow-on pin-add succeeds (SetPinForPlayer no-ops safely on any failure).
        if (message.AutoPin)
        {
            var insertEdge = ScribePlayerSettings.NormalizePinInsert((ScribePinInsert)message.PinInsert);
            SetPinForPlayer(fromPlayer, doc.DocId, newTaskId, pinned: true, insertEdge: insertEdge);
        }

        Trace("auto-link-quest from {0}: linked {1}/{2}", fromPlayer.PlayerName, source, questCode);
    }
}
