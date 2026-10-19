using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>A pending Quest Accept/Completion notification awaiting the player's decision
/// (<see cref="ScribeQuestAcceptPolicy.PromptHud"/>/<see cref="ScribeQuestAcceptPolicy.PromptPopup"/>/
/// <see cref="ScribeQuestCompletionPolicy.Prompt"/>). Shown as a pinned-HUD banner or, for PromptPopup
/// accept-prompts, a center modal. <see cref="IsCompletion"/> distinguishes an accept-prompt (creates a
/// Quest Link) from a completion-prompt (marks the linked task done); both resolve through
/// <see cref="ScribeModSystem.AcceptQuestPrompt"/>. <see cref="Source"/> names the detecting backend so
/// same-code collisions stay unambiguous.</summary>
public readonly record struct ScribeQuestPrompt(string Source, string QuestCode, string Title, bool IsCompletion);

public sealed partial class ScribeModSystem
{
    // ── Quest soft auto-detect (Layer 2) ────────────────────────────────────────────────────────────

    private ScribeQuestWatcher? questWatcher;

    private readonly List<ScribeQuestPrompt> pendingQuestPrompts = new();

    /// <summary>Fired when <see cref="PendingQuestPrompts"/> changes so the HUD rebuilds (and self-opens for a first prompt with zero pins).</summary>
    public event Action? QuestPromptsChanged;

    /// <summary>Quest prompts awaiting Accept/Dismiss, oldest first. Session-only; the watcher re-raises a still-pending one after a relog.</summary>
    public IReadOnlyList<ScribeQuestPrompt> PendingQuestPrompts => pendingQuestPrompts;

    /// <summary>Formatted live progress text for a quest Link row, or null when unavailable (backend missing,
    /// state unread, or quest not in the catalog). Dispatches strictly by <paramref name="source"/>, the Link's
    /// recorded backend, never trying the other even on a code collision.</summary>
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

    /// <summary>Progression Framework's catalog objective definitions for a cataloged quest, used by the Quest
    /// Link picker and <see cref="SendAutoLinkQuest"/>. False when the watcher hasn't started, PF is absent, or
    /// the code isn't cataloged.</summary>
    internal bool TryGetPfObjectiveDefs(string questCode, out IReadOnlyList<ScribePfObjectiveDef> objectives)
    {
        if (questWatcher is null) { objectives = Array.Empty<ScribePfObjectiveDef>(); return false; }
        return questWatcher.TryGetPfObjectiveDefs(questCode, out objectives);
    }

    /// <summary>Progression Framework's cached live per-objective progress, used by <see cref="SendAutoLinkQuest"/> to seed new QuestObjective children (0 if nothing cached).</summary>
    internal bool TryGetPfObjectiveProgress(string questCode, out IReadOnlyDictionary<string, int> progressByCode)
    {
        if (questWatcher is null) { progressByCode = EmptyPfProgress; return false; }
        return questWatcher.TryGetPfObjectiveProgress(questCode, out progressByCode);
    }

    private static readonly Dictionary<string, int> EmptyPfProgress = new(StringComparer.Ordinal);

    /// <summary>Whether the player has been observed to start (accept or complete) the quest this session; lets the Quest Link picker list only engaged quests. False when the watcher hasn't started.</summary>
    internal bool HasStartedQuest(string questCode) => questWatcher?.HasStarted(questCode) ?? false;

    private void StartQuestWatcher(ICoreClientAPI api)
        => questWatcher = new ScribeQuestWatcher(api, OnQuestAccepted, OnQuestCompleted);

    /// <summary>Replaces the cached quest-decision ledger wholesale; the server always sends the complete ledger, never a delta.</summary>
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

    /// <summary>This player's Accept-placement candidates for a Quest auto-link; the same computation the Inbox Accept control uses, so the HUD banner applies the same 0/1/2+ rule.</summary>
    internal List<ScribeAcceptCandidate> ComputeQuestAcceptCandidates()
        => capi is null ? new List<ScribeAcceptCandidate>() : ScribeAcceptCandidates.Compute(capi, LastOpenedScribeItemDocId);

    /// <summary>A quest was just detected as accepted. A prompt already decided (accepted or dismissed) in an
    /// EARLIER session is a stale re-detection (the underlying attribute never goes away, see
    /// <see cref="ScribeQuestWatcher"/>) unless it is a fresh re-accept of a now-unlinked quest; it is dropped
    /// silently. Otherwise posts a chat notification (every policy gets one), then branches on
    /// <see cref="ScribePlayerSettings.QuestAcceptPolicy"/>: Always auto-links immediately unless 2+ eligible
    /// destinations are carried (then it queues a prompt rather than guess); Never does nothing more;
    /// PromptHud/PromptPopup both queue (the presentation style is chosen at render time).</summary>
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
            default: // PromptHud / PromptPopup — same queuing; render style is chosen at render time
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-prompt", quest.Title));
                QueuePrompt(new ScribeQuestPrompt(quest.Source, quest.QuestCode, quest.Title, IsCompletion: false));
                break;
        }
    }

    /// <summary>A quest was just detected as completed. A completion-prompt already decided stays suppressed
    /// for the life of the ledger entry (no fresh-transition signal exists to reset it). Only an ALREADY-PINNED
    /// Quest Link for this exact (source, quest code) counts as linked, matching the HUD Tracker engine's
    /// visible/live scope; an unpinned Link is a disclosed gap, and matching by source stops a cross-backend code
    /// collision from completing the wrong pin. The chat notification always fires.</summary>
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

    /// <summary>Re-pushes a player their own full quest-decision ledger (server → client) on join and after any change; mirrors <see cref="PushPinsTo(IServerPlayer)"/>.</summary>
    public void PushQuestDecisionsTo(IServerPlayer player)
    {
        if (sapi is null || questDecisionStore is null) return;
        var bytes = questDecisionStore.SerializeList(player.PlayerUID);
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeQuestDecisionSetMessage { DecisionSetBytes = bytes }, player);
    }

    /// <summary>Server-side: records the player's decision on a quest prompt and re-pushes their ledger if it changed.</summary>
    private void RecordQuestDecision(IServerPlayer player, string source, string questCode, bool isCompletion, ScribeQuestDecision decision)
    {
        if (questDecisionStore is null) return;
        if (questDecisionStore.Set(player.PlayerUID, source, questCode, isCompletion, decision))
            PushQuestDecisionsTo(player);
    }

    /// <summary>Server handler for <see cref="ScribeDismissQuestPromptMessage"/>: records a <see cref="ScribeQuestDecision.Dismissed"/> decision so the prompt never re-raises across a relog.</summary>
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

    /// <summary>The HUD banner's Accept: links (accept-prompt) or completes (completion-prompt) the quest, then
    /// removes the prompt. <paramref name="candidate"/> is the destination chosen via
    /// <see cref="ComputeQuestAcceptCandidates"/>; ignored for a completion-prompt; null falls back to
    /// server-side resolution.</summary>
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
        // Local-only removal, not DismissQuestPrompt: accepting records its own Accepted decision via the round trip above, and a Dismissed decision would overwrite it.
        RemovePendingPrompt(prompt);
    }

    /// <summary>The banner's/modal's Dismiss: drops the prompt and records the dismissal server-side so it never re-raises across a relog. Not used by <see cref="AcceptQuestPrompt"/>; see <see cref="RemovePendingPrompt"/>.</summary>
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

    /// <summary>Drops the pending prompt locally with no network effect (session-only; the watcher's dedup
    /// means nothing re-arms this session). Shared by Dismiss, which also sends the decision, and Accept, which
    /// records its decision through a different round trip.</summary>
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

    /// <summary>Sends the auto-link request naming the resolved destination. A null <paramref name="candidate"/>
    /// is the legacy path: the server falls back to <c>FindNotebookInInventory</c>. Attaches objective snapshots
    /// so the server can seed the new Link's QuestObjective children: VS Quest entries carry static criteria from
    /// their catalog; Progression Framework entries also carry cached live progress. Null/empty when the catalog
    /// read hasn't completed.</summary>
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
        else if (quest.Source == ScribeQuestSource.VsQuest)
        {
            IReadOnlyList<ScribeQuestObjectiveDef> vsObjectives = quest.Objectives;
            if (vsObjectives.Count == 0
                && questWatcher?.TryGetObjectives(quest.QuestCode, out var cachedObjectives) == true)
            {
                vsObjectives = cachedObjectives;
            }

            objectives = ScribeQuestCatalog.BuildStaticObjectives(capi, vsObjectives)
                .Select(o => new ScribeAutoLinkObjectiveWire
                {
                    Code = o.Code,
                    ItemCode = o.ItemCode,
                    Label = o.Label,
                    Required = o.Required,
                    CurrentProgress = 0,
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

    /// <summary>Server handler for <see cref="ScribeAutoLinkQuestMessage"/> (Accept Policy = Always / Prompt-accepted).
    /// Resolves the client-named destination (inventory id + slot id) and RE-VALIDATES it (writeable, has
    /// capacity) like <c>TryPlaceAcceptedAssignment</c>; the client's choice is never proof of eligibility. Falls
    /// back to <see cref="FindNotebookInInventory"/> when no target was sent or it isn't writeable. Authoritative
    /// for whether the Link is added: silently no-ops if the player carries no Scribe document, has no capacity,
    /// or already has a Link for this (source, quest code), which makes repeat detection idempotent without
    /// client-side state.</summary>
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
            if (source == ScribeQuestSource.ProgressionFramework)
            {
                foreach (var wire in objectives)
                {
                    var child = doc.Blocks.FirstOrDefault(b => b.IsQuestObjective
                        && string.Equals(b.LinkTarget, wire.Code, StringComparison.Ordinal));
                    if (child is not null) doc.SetQuestObjectiveProgress(child.TaskId, wire.CurrentProgress);
                }
            }
        }
        host.Flush();

        // Accept already round-trips here, so the ledger entry is recorded here too (unlike dismiss).
        RecordQuestDecision(fromPlayer, source, questCode, isCompletion: false, ScribeQuestDecision.Accepted);

        // Auto-pin reuses the new TaskId via the manual Pin path; the quest is linked even if the pin-add fails (SetPinForPlayer no-ops safely).
        if (message.AutoPin)
        {
            var insertEdge = ScribePlayerSettings.NormalizePinInsert((ScribePinInsert)message.PinInsert);
            SetPinForPlayer(fromPlayer, doc.DocId, newTaskId, pinned: true, insertEdge: insertEdge);
        }

        Trace("auto-link-quest from {0}: linked {1}/{2}", fromPlayer.PlayerName, source, questCode);
    }
}
