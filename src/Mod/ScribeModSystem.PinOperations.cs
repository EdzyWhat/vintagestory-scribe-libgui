using System;
using System.Collections.Generic;
using System.Linq;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>
    /// Pins/unpins by (DocId, TaskId). Unpin removes from the store without resolving the block, so it works
    /// when the lectern is broken or unloaded. Pin snapshots text/done from the server's own document (never
    /// a client snapshot), recording an empty snapshot if unresolvable. Lock-free; re-pushes the player when
    /// their set changed. Public so the block-entity layer and integration suite use the production path.
    /// </summary>
    public void SetPinForPlayer(IServerPlayer player, Guid docId, Guid taskId, bool pinned,
        string? fallbackText = null, bool fallbackDone = false,
        ScribeBlockKind fallbackKind = ScribeBlockKind.Task, string? fallbackLinkTarget = null,
        string? fallbackTargetItemCode = null, int fallbackTargetQuantity = 1, int fallbackCurrentQuantity = 0,
        string? fallbackLinkLabel = null, int fallbackDepth = 0,
        ScribePinInsert insertEdge = ScribePinInsert.Bottom)
    {
        if (sapi is null || pinStore is null) return;

        bool changed;
        if (pinned)
        {
            string text = fallbackText ?? "";
            bool done = fallbackDone;
            ScribeBlockKind kind = fallbackKind;
            string? linkTarget = fallbackLinkTarget;
            string? targetItemCode = fallbackTargetItemCode;
            int targetQuantity = fallbackTargetQuantity;
            int currentQuantity = fallbackCurrentQuantity;
            string? linkLabel = fallbackLinkLabel;
            int depth = fallbackDepth;
            bool isAcceptedAssignment = false;
            string assignerUid = "";
            string assignedDate = "";
            string? acceptedDate = null;
            double? assignedTimestamp = null;
            double? acceptedTimestamp = null;
            ScribeDocument? source = null;
            // Prefer the server's document; fall back to the client snapshot for hosts not registered
            // server-side (e.g. Notebooks). The same document drives pin placement.
            if (TryResolveDocHost(docId, out var docHost, player))
            {
                source = docHost!.Document;
                if (source.FindByTaskId(taskId) is { } block)
                {
                    text = block.Text;
                    done = block.Done;
                    kind = block.Kind;
                    linkTarget = block.LinkTarget;
                    targetItemCode = block.TargetItemCode;
                    targetQuantity = block.TargetQuantity;
                    currentQuantity = block.CurrentQuantity;
                    linkLabel = block.LinkLabel;
                    depth = block.Depth;
                    // No client fallback: this field is always resolvable from the document.
                    isAcceptedAssignment = block.Assignment?.State == ScribeAssignmentState.Accepted;
                    if (isAcceptedAssignment)
                    {
                        assignerUid = block.Assignment!.AssignerUid;
                        assignedDate = block.Assignment!.AssignedDate;
                        acceptedDate = block.Assignment!.AcceptedDate;
                        assignedTimestamp = block.Assignment!.AssignedTimestamp;
                        acceptedTimestamp = block.Assignment!.AcceptedTimestamp;
                    }
                }
            }
            changed = pinStore.SetPin(player.PlayerUID, docId, taskId, sapi.World.Calendar.TotalHours, text, done, kind, linkTarget,
                targetItemCode, targetQuantity, currentQuantity, linkLabel, depth, source, insertEdge, isAcceptedAssignment,
                assignerUid, assignedDate, acceptedDate, assignedTimestamp, acceptedTimestamp);
        }
        else
        {
            changed = pinStore.RemovePin(player.PlayerUID, docId, taskId);
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Completes a pinned task by identity. Store-first with write-through: toggles the pin's done-state
    /// in the store (authoritative, so it works with an unresolvable source), writes it through to the source
    /// document when it resolves (acting player only), then applies <paramref name="policy"/>: <c>Sink</c>
    /// keeps the done pin, <c>Unpin</c> removes it, <c>Delete</c> removes the task and pin. Removal fires only
    /// on a transition INTO done. The policy is the caller's already-normalized client preference.
    /// Re-pushes once if the set changed. Public so the block-entity layer and integration suite use the production path.
    /// </summary>
    public void CompleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy = ScribeCompletionPolicy.Sink,
        ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound)
    {
        if (sapi is null || pinStore is null) return;
        behavior = ScribePlayerSettings.NormalizeSubtaskBehavior(behavior);

        bool? current = pinStore.GetPinDone(player.PlayerUID, docId, taskId);
        if (current is null)
        {
            // Not pinned: toggle the shared document directly; the policy still applies there (Sink→bottom, Delete→remove).
            CompleteUnpinnedTaskAtSource(player, docId, taskId, policy, behavior);
            return;
        }
        bool nowDone = !current.Value;

        // A completion-prompt "accept" is marking the linked quest task done. Read from the pin's own
        // snapshot so it works with an unresolvable source; only a transition INTO done counts.
        if (nowDone && pinStore.GetPin(player.PlayerUID, docId, taskId) is { Kind: ScribeBlockKind.Link } questPin
            && ScribeLinkTarget.IsQuest(questPin.LinkTarget))
        {
            string questSource = ScribeLinkTarget.QuestSource(questPin.LinkTarget) ?? ScribeQuestSource.VsQuest;
            string? questCode = ScribeLinkTarget.QuestCode(questPin.LinkTarget);
            if (questCode is not null)
                RecordQuestDecision(player, questSource, questCode, isCompletion: true, ScribeQuestDecision.Accepted);
        }

        bool changed = pinStore.SetPinDone(player.PlayerUID, docId, taskId, nowDone);
        Trace("  complete: {0}'s pin on task {1} done {2} -> {3}", player.PlayerName, taskId, current.Value, nowDone);

        bool resolved = TryResolveDocHost(docId, out var docHost, player);
        policy = CollapsePolicyForReadOnlySource(policy, resolved ? docHost : null);

        if (resolved)
        {
            // Captured BEFORE the mutation: Delete can remove the block, but the Assignment survives independently.
            var assignmentOnBlock = docHost!.Document.FindByTaskId(taskId)?.Assignment;
            var outcome = ScribeCompletion.ApplyGivenDone(docHost.Document, taskId, nowDone, policy, behavior);
            if (outcome.DocChanged) docHost.PersistFromReader();
            NotifyAssignmentDoneChanged(taskId, nowDone, assignmentOnBlock);

            foreach (var id in outcome.DeletedTaskIds)
                changed |= pinStore.RemovePin(player.PlayerUID, docId, id);

            if (outcome.ShouldRemovePin)
            {
                foreach (var id in outcome.AffectedTaskIds)
                    changed |= pinStore.RemovePin(player.PlayerUID, docId, id);
                Trace("  policy {0}: removed {1}'s pins on mutated tasks", policy, player.PlayerName);
            }
            else
            {
                foreach (var id in outcome.AffectedTaskIds)
                    changed |= pinStore.SetPinDone(player.PlayerUID, docId, id, nowDone);
            }
        }
        else
        {
            // Unresolvable (e.g. Notebook not in inventory): nothing to mutate, but assignment-completion
            // derivation is addressed by taskId alone and still runs.
            Trace("  complete: doc {0} unresolvable for task {1} — deriving assignment completion from the store only", docId, taskId);
            NotifyAssignmentDoneChanged(taskId, nowDone, null);

            if (ScribeCompletion.Decide(nowDone, policy).ShouldRemovePin)
                changed |= pinStore.RemovePin(player.PlayerUID, docId, taskId);
        }

        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Edits a pinned task's text by identity; best-effort write-through like <see cref="CompleteTaskForPlayer"/>.
    /// Sets the source task's text lock-free via <see cref="BlockEntityScribeLectern.SetTaskTextFromReader"/>
    /// when it resolves, and always refreshes the acting player's pin snapshot. Only that player's pin is
    /// touched; a blank edit is rejected end-to-end. Re-pushes when changed. Public for the integration suite.
    /// </summary>
    public void EditPinnedTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId, string text)
    {
        if (sapi is null || pinStore is null) return;
        if (string.IsNullOrWhiteSpace(text)) return; // reject blank/whitespace-only end-to-end

        // Only edit through pins the player actually holds — an edit is a pin action, not a document RPC.
        if (pinStore.GetPinDone(player.PlayerUID, docId, taskId) is null)
        {
            Trace("  edit: {0} has no pin on task {1} — ignored", player.PlayerName, taskId);
            return;
        }

        // Best-effort; a gone source just skips this.
        if (TryResolveDocHost(docId, out var docHost, player)) docHost!.SetTaskTextFromReader(taskId, text);

        bool changed = pinStore.SetPinText(player.PlayerUID, docId, taskId, text);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Deletes a pinned task by identity, independent of completion policy: removes it lock-free from the
    /// source via <see cref="BlockEntityScribeLectern.DeleteTaskFromReader"/> when it resolves, and always
    /// removes the acting player's pin. Public for the integration suite.
    /// </summary>
    public void DeleteTaskForPlayer(IServerPlayer player, Guid docId, Guid taskId,
        ScribeSubtaskBehavior behavior = ScribeSubtaskBehavior.Bound)
    {
        if (sapi is null || pinStore is null) return;
        behavior = ScribePlayerSettings.NormalizeSubtaskBehavior(behavior);

        IReadOnlyList<Guid> deleted = new[] { taskId };
        if (TryResolveDocHost(docId, out var docHost, player))
        {
            // Captured BEFORE the delete — the block is about to be removed from the document.
            var assignmentOnBlock = docHost!.Document.FindByTaskId(taskId)?.Assignment;
            deleted = ScribeCompletion.ApplyDelete(docHost.Document, taskId, behavior);
            if (deleted.Count > 0) docHost.PersistFromReader();
            if (deleted.Contains(taskId)) NotifyAssignmentDiscardOnDelete(taskId, assignmentOnBlock, player.PlayerUID);
            Trace("  delete: removed {0} row(s) from source doc {1}", deleted.Count, docId);
        }
        else
        {
            Trace("  delete: source unresolvable for task {0} — pin removed only", taskId);
        }

        bool changed = false;
        foreach (var id in deleted)
            changed |= pinStore.RemovePin(player.PlayerUID, docId, id);
        if (changed) PushPinsTo(player);
    }

    /// <summary>
    /// Reorders the acting player's own pin list into a client-supplied order (unknown/duplicate ids
    /// ignored, omitted pins preserved). Never touches document block order or another player's list.
    /// Re-pushes when the order changed. Public for the integration suite.
    /// </summary>
    public void ReorderPinsForPlayer(IServerPlayer player, IReadOnlyList<(Guid DocId, Guid TaskId)> order)
    {
        if (sapi is null || pinStore is null) return;
        if (pinStore.ReorderPins(player.PlayerUID, order)) PushPinsTo(player);
    }

    /// <summary>
    /// Writes a Tracker's live <c>CurrentQuantity</c> by identity from a client count engine's
    /// <see cref="ScribeSetTrackerQuantityMessage"/>. Resolves the document (registry, or scanning the player's
    /// inventory) and writes the clamped count lock-free via
    /// <see cref="IScribeDocumentHost.SetTrackerCurrentQuantityFromReader"/>. Best-effort no-op when unresolvable,
    /// gone, or not a Tracker. Not a pin action. Public for the integration suite.
    /// </summary>
    public void SetTrackerQuantityForPlayer(IServerPlayer player, Guid docId, Guid taskId, int quantity)
    {
        if (sapi is null) return;
        if (TryResolveDocHost(docId, out var docHost, player))
            docHost!.SetTrackerCurrentQuantityFromReader(taskId, quantity);
    }

    /// <summary>
    /// Writes a QuestObjective's live <c>CurrentQuantity</c> by identity from the quest watcher's
    /// <see cref="ScribeSetQuestObjectiveProgressMessage"/>. Like <see cref="SetTrackerQuantityForPlayer"/> but
    /// through <see cref="IScribeDocumentHost.SetQuestObjectiveProgressFromReader"/> so it never shares a gate
    /// with the Tracker/Craft engine. Best-effort no-op; no pin involvement. Public for the integration suite.
    /// </summary>
    public void SetQuestObjectiveProgressForPlayer(IServerPlayer player, Guid docId, Guid taskId, int quantity)
    {
        if (sapi is null) return;
        if (TryResolveDocHost(docId, out var docHost, player))
            docHost!.SetQuestObjectiveProgressFromReader(taskId, quantity);
    }

    /// <summary>
    /// Writes a Read View filter pill + collapsed-group set from <see cref="ScribeSetReadViewStateMessage"/>,
    /// resolving the document like <see cref="SetTrackerQuantityForPlayer"/> and writing via
    /// <see cref="IScribeDocumentHost.SetReadViewStateFromReader"/>. Best-effort no-op when unresolvable.
    /// Public for the integration suite.
    /// </summary>
    public void SetReadViewStateForPlayer(IServerPlayer player, Guid docId, byte filterCategory, IReadOnlyCollection<Guid> collapsedGroupIds)
    {
        if (sapi is null) return;
        if (TryResolveDocHost(docId, out var docHost, player))
            docHost!.SetReadViewStateFromReader(filterCategory, collapsedGroupIds);
    }

    /// <summary>Collapses a completion policy to <see cref="ScribeCompletionPolicy.Unpin"/> when the target is a
    /// read-only (hard/fired) tablet, which can neither be reordered nor lose tasks and must not strand a pin on
    /// the HUD. <c>Keep</c> is left alone; an editable or unresolvable (null) source keeps its policy. Read-only-ness
    /// comes from the host's <see cref="ScribeDocumentPolicy.ReadOnly"/>.</summary>
    private static ScribeCompletionPolicy CollapsePolicyForReadOnlySource(
        ScribeCompletionPolicy policy, IScribeDocumentHost? host)
    {
        if (host is null || !host.Policy.ReadOnly) return policy;
        return policy == ScribeCompletionPolicy.Keep ? policy : ScribeCompletionPolicy.Unpin;
    }

    /// <summary>Resolves a docId to a host: registry first (Lecterns), else a transient host from a matching
    /// Notebook/Tablet in the acting player's inventory (those register only client-side).</summary>
    private bool TryResolveDocHost(Guid docId, out IScribeDocumentHost? host,
        IServerPlayer? player = null)
    {
        if (_hostRegistry.TryGetValue(docId, out host)) return true;

        if (player is null || sapi is null) return false;

        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            IEnumerable<ItemSlot>? slots;
            try { slots = new List<ItemSlot>(inv); }
            catch { continue; }

            foreach (var slot in slots)
            {
                if (slot is null) continue;
                if (slot.Itemstack?.Collectible is not IScribeDocumentItem) continue;
                if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) continue;
                if (doc.DocId != docId) continue;
                // Tier-correct host so the policy/title apply server-side; both derive from NotebookHost.
                var nbHost = slot.Itemstack.Collectible is ItemScribeTablet
                    ? new TabletHost(slot)
                    : new NotebookHost(slot);
                nbHost.AttachServerContext(sapi, player);
                host = nbHost;
                return true;
            }
        }
        return false;
    }

    /// <summary>Toggles an UNPINNED document task directly on the shared source. No store involvement, but the
    /// policy still applies on a transition INTO done: <c>Sink</c> moves it to the bottom, <c>Delete</c> removes it,
    /// <c>Keep</c>/<c>Unpin</c> just toggle. No-op when unresolvable.</summary>
    private void CompleteUnpinnedTaskAtSource(IServerPlayer player, Guid docId, Guid taskId,
        ScribeCompletionPolicy policy, ScribeSubtaskBehavior behavior)
    {
        if (!TryResolveDocHost(docId, out var docHost, player))
        {
            Trace("  complete(unpinned): doc {0} unresolvable — nothing to toggle", docId);
            return;
        }
        var block = docHost!.Document.FindByTaskId(taskId);
        if (block is null || !block.IsCompletable)
        {
            Trace("  complete(unpinned): task {0} not found in doc {1}", taskId, docId);
            return;
        }
        bool nowDone = !block.Done;

        // A read-only tablet collapses every mutating policy to Unpin, leaving a plain toggle.
        policy = CollapsePolicyForReadOnlySource(policy, docHost);

        var outcome = ScribeCompletion.ApplyGivenDone(docHost.Document, taskId, nowDone, policy, behavior);
        if (outcome.DocChanged) docHost.PersistFromReader();
        NotifyAssignmentDoneChanged(taskId, nowDone, block.Assignment);
        Trace("  complete(unpinned): task {0} toggled to done={1} (affected {2})", taskId, nowDone, outcome.AffectedTaskIds.Count);
    }

    /// <summary>Re-pushes a player their own full pin set (server → client); never anyone else's.</summary>
    public void PushPinsTo(IServerPlayer player)
    {
        if (sapi is null || pinStore is null) return;
        var bytes = ScribePinCodec.SerializeList(pinStore.Get(player.PlayerUID));
        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribePinnedSetMessage { PinnedRefBytes = bytes }, player);
    }

    /// <summary>Re-pushes each listed online player their own pin set; offline uids are skipped (synced on next join).</summary>
    public void PushPinsTo(IReadOnlyList<string> playerUids)
    {
        if (sapi is null) return;
        foreach (var uid in playerUids)
        {
            if (sapi.World.PlayerByUid(uid) is IServerPlayer player) PushPinsTo(player);
        }
    }

}
