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
/// <see cref="ScribeModSystem.AcceptQuestPrompt"/>.</summary>
public readonly record struct ScribeQuestPrompt(string QuestCode, string Title, bool IsCompletion);

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
    /// vsquest not installed, its dialog never read this session, or the quest isn't in this world's
    /// catalog. Read by <see cref="ScribeDialogBase.Layout"/> when building a read/editor row.</summary>
    public string? TryGetQuestProgressText(string questCode)
    {
        if (questWatcher is null) return null;
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

    /// <summary>A quest was just detected as accepted (<see cref="ScribeQuestWatcher"/>). ALWAYS posts a
    /// chat notification — Scribe watching quest state at all is not obvious, so every policy gets one —
    /// then branches on <see cref="ScribePlayerSettings.QuestAcceptPolicy"/>: Always sends the auto-link
    /// request immediately; Never does nothing further; Prompt queues a HUD banner instead of acting.</summary>
    private void OnQuestAccepted(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;
        switch (MySettings.QuestAcceptPolicy)
        {
            case ScribeQuestAcceptPolicy.Always:
                SendAutoLinkQuest(quest);
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-always", quest.Title));
                break;
            case ScribeQuestAcceptPolicy.Never:
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-never", quest.Title));
                break;
            default: // Prompt
                capi.ShowChatMessage(Lang.Get("scribe:scribe-quest-accepted-prompt", quest.Title));
                QueuePrompt(new ScribeQuestPrompt(quest.QuestCode, quest.Title, IsCompletion: false));
                break;
        }
    }

    /// <summary>A quest was just detected as completed. Only an ALREADY-PINNED Quest Link for this exact
    /// quest code counts as "linked" (mirrors the HUD Tracker engine's own scope: acted on visible/live
    /// state, not every unloaded document) — a Link that exists but isn't pinned is a disclosed gap. The
    /// chat notification always fires regardless of whether a match was found.</summary>
    private void OnQuestCompleted(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;
        var matches = FindPinnedQuestLinks(quest.QuestCode);

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
                if (matches.Count > 0) QueuePrompt(new ScribeQuestPrompt(quest.QuestCode, quest.Title, IsCompletion: true));
                break;
        }
    }

    private List<ScribePinnedRef> FindPinnedQuestLinks(string questCode)
        => MyPins.Where(p => p.Kind == ScribeBlockKind.Link && ScribeLinkTarget.QuestCode(p.LinkTarget) == questCode)
            .ToList();

    private void QueuePrompt(ScribeQuestPrompt prompt)
    {
        if (pendingQuestPrompts.Any(p => p.QuestCode == prompt.QuestCode && p.IsCompletion == prompt.IsCompletion)) return;
        pendingQuestPrompts.Add(prompt);
        QuestPromptsChanged?.Invoke();
    }

    /// <summary>The HUD banner's Accept action: link (accept-prompt) or complete (completion-prompt) the
    /// prompted quest, then dismiss it.</summary>
    public void AcceptQuestPrompt(ScribeQuestPrompt prompt)
    {
        if (prompt.IsCompletion)
        {
            foreach (var pin in FindPinnedQuestLinks(prompt.QuestCode)) SendPinCompletion(pin);
        }
        else
        {
            SendAutoLinkQuest(new ScribeQuestCatalogEntry(prompt.QuestCode, prompt.Title, null, Array.Empty<ScribeQuestObjectiveDef>()));
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

    private void SendAutoLinkQuest(ScribeQuestCatalogEntry quest)
    {
        if (capi is null) return;
        capi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeAutoLinkQuestMessage
        {
            QuestCode = quest.QuestCode,
            Title = quest.Title,
            Description = quest.Description,
        });
    }

    /// <summary>Server-side handler for <see cref="ScribeAutoLinkQuestMessage"/> (Quest Accept Policy =
    /// Always/Prompt-accepted). Resolves the destination itself — the sending player's first carried
    /// Notebook/Tablet, mirroring <see cref="FindNotebookInInventory"/>'s existing "single target"
    /// convention (used for History auto-recording) — and is authoritative for whether the Link is
    /// actually added: silently no-ops with no player-visible error if the player carries no Scribe
    /// document, has no capacity, or already has a Link for this exact quest code. That last check is
    /// what makes repeat detection (e.g. across a relog) idempotent with no client-side persisted state.</summary>
    private void OnServerReceivedAutoLinkQuest(IServerPlayer fromPlayer, ScribeAutoLinkQuestMessage message)
    {
        if (sapi is null) return;
        string? questCode = message.QuestCode;
        if (string.IsNullOrWhiteSpace(questCode)) return;

        var host = FindNotebookInInventory(fromPlayer);
        if (host is null)
        {
            Trace("auto-link-quest from {0}: no carried Notebook/Tablet — skipped", fromPlayer.PlayerName);
            return;
        }

        var doc = host.Document;
        bool alreadyLinked = doc.Blocks.Any(b =>
            b.Kind == ScribeBlockKind.Link && ScribeLinkTarget.QuestCode(b.LinkTarget) == questCode);
        if (alreadyLinked)
        {
            Trace("auto-link-quest from {0}: already linked ({1}) — skipped", fromPlayer.PlayerName, questCode);
            return;
        }

        if (!host.Policy.CanHold(doc.BlockCount + 1))
        {
            Trace("auto-link-quest from {0}: target has no capacity — skipped", fromPlayer.PlayerName);
            return;
        }

        doc.AddQuestLink(questCode, message.Title, message.Description);
        host.Flush();
        Trace("auto-link-quest from {0}: linked {1}", fromPlayer.PlayerName, questCode);
    }
}
