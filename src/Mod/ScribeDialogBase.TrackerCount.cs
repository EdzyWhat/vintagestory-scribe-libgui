using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog
using Vintagestory.API.Common;   // IInventory, CraftingRecipeIngredient
using Vintagestory.API.Config;   // GlobalConstants

namespace Scribe;

// Tracker count engine (add-tracker-link-tasks Group 4). While a Scribe surface is open in the read view,
// this keeps every Tracker task's live CurrentQuantity in sync with the VIEWING player's carried inventory
// (hotbar + backpack only — never block-stored or world items, D5). The count is derived client-side and
// pushed through the server-authoritative path (ScribeSetTrackerQuantityMessage), so it persists and other
// viewers converge, exactly like the Done flag. When a Tracker first reaches its target, the player's
// tracker-completion setting is applied (complete / delete / nothing, D6/4.4).
//
// Deliberately read-view-only: the editor renders off `scratch`, so mutating the live document there would
// fight the editor's autosave flush (the scratch-vs-external-edit hazard). In the editor the player is
// setting the target quantity, not watching progress — the read view is where the counter lives.
public abstract partial class ScribeDialogBase
{
    /// <summary>Game-tick listener id for the ~1s edge-case poll (D5/4.2), registered for the dialog's
    /// whole lifetime. The poll is a cheap guarded no-op unless the open document has a Tracker AND the
    /// read view is showing, so it costs nothing for note/task-only documents. Backstops the
    /// <see cref="OnTrackerSlotModified"/> event path (which can miss e.g. an in-place stack merge or a
    /// view-switch back into read).</summary>
    private long? trackerPollListenerId;

    /// <summary>A pending coalesced recompute callback id, so a burst of <c>SlotModified</c> events (picking
    /// up a stack touches several slots at once) collapses into a single recompute ~150ms later instead of
    /// one per slot. Null when no recompute is scheduled.</summary>
    private long? trackerRecomputeCallbackId;

    /// <summary>The carried inventories this dialog subscribed to <c>SlotModified</c> on, kept so
    /// <see cref="TeardownTrackerEngine"/> can unsubscribe exactly what it subscribed.</summary>
    private readonly List<IInventory> trackerWatchedInventories = new();

    /// <summary>Resolved <see cref="CraftingRecipeIngredient"/> per target code, cached because resolving
    /// touches the item/block registries; the frequent recompute then re-runs only the cheap carried-stack
    /// sum. A null value caches "this code doesn't resolve" so we don't retry the probe every tick. Cleared
    /// on close.</summary>
    private readonly Dictionary<string, CraftingRecipeIngredient?> trackerIngredientCache = new();

    /// <summary>Start the Tracker count engine when the dialog opens: register the poll backstop, subscribe
    /// to carried-inventory changes, and do an immediate recompute so an already-satisfied Tracker reads
    /// correctly the moment the surface opens (D5 — recompute on open).</summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        // Register the block inventory as OPEN *server-side* (add-scriptorium-inventory). LibGUI's
        // GuiDialogBlockEntityBase.OnGuiOpened opens the inventory only CLIENT-side (so move prediction
        // works) and never tells the server; its OnGuiClosed does send the paired 1001 close. Vanilla
        // containers open with a matching 1000 packet (BlockEntityOpenableContainer.toggleInventoryDialogClient)
        // — without it the server's HandleActivateInventorySlot resolves the target inventory id ONLY from the
        // player's OPENED inventories, finds nothing ("no such inventory currently opened?"), and silently
        // drops every slot move. The item then lives only in the client's optimism: it's absent server-side, so
        // it's gone on relog and isn't there for DropAll to spill on break. BlockEntityScriptorium handles
        // 1000 → OpenInventory. Guarded by Inventory != null so the document-only surfaces (Lectern / Notebook /
        // Tablet, which pass no inventory) are byte-for-byte unaffected.
        if (Inventory != null)
        {
            SendBlockEntityPacket(1000);
        }

        trackerPollListenerId ??= capi.Event.RegisterGameTickListener(_ => RecomputeTrackers(), 1000);
        foreach (var inv in EnumerateTrackerWatchInventories())
        {
            inv.SlotModified += OnTrackerSlotModified;
            trackerWatchedInventories.Add(inv);
        }
        RecomputeTrackers();
    }

    /// <summary>Tear down the count engine (called from <see cref="OnGuiClosed"/>): unregister the poll +
    /// any pending coalesced recompute, unsubscribe every watched inventory, and drop the ingredient cache.
    /// Idempotent.</summary>
    private void TeardownTrackerEngine()
    {
        if (trackerPollListenerId is { } pollId)
        {
            capi.Event.UnregisterGameTickListener(pollId);
            trackerPollListenerId = null;
        }
        if (trackerRecomputeCallbackId is { } cbId)
        {
            capi.Event.UnregisterCallback(cbId);
            trackerRecomputeCallbackId = null;
        }
        foreach (var inv in trackerWatchedInventories)
            inv.SlotModified -= OnTrackerSlotModified;
        trackerWatchedInventories.Clear();
        trackerIngredientCache.Clear();
    }

    /// <summary>The DocId of the document this dialog currently has open (any view), or <c>null</c> when the
    /// dialog is closed. The HUD's own live Tracker count engine reads this across every open Scribe dialog so
    /// it can DEFER for a doc a dialog holds — it treats such a doc as display-only and does NOT send a count
    /// or fire the rising-edge completion for its pinned Trackers (add-tracker-link-tasks 7.10). Deferring for
    /// the WHOLE time the dialog is open (not just its read view) is deliberate and covers two hazards at once:
    /// <list type="bullet">
    /// <item><b>Read view</b> — the dialog's own <see cref="RecomputeTrackers"/> is already driving the send +
    /// completion, so the HUD must not double-send a quantity or double-fire the completion (a double-toggle).</item>
    /// <item><b>Editor view</b> — <see cref="RecomputeTrackers"/> is intentionally inert there because the
    /// editor renders off <c>scratch</c>; a HUD-driven external count write would fight the editor's autosave
    /// flush (the scratch-vs-external-edit hazard). Deferring keeps the HUD out of that document while it's
    /// being edited.</item>
    /// </list>
    /// A pinned Tracker whose document is NOT open in any dialog is the HUD's to drive.</summary>
    internal Guid? OpenDocumentId => IsOpened() ? host.Document.DocId : (Guid?)null;

    /// <summary>The carried inventories a Tracker counts across: the player's own hotbar + backpack (D5).
    /// Matches <see cref="ScribeModSystem.EnumerateCarriedSlots"/>'s scope so watch-set and count-set agree.</summary>
    private IEnumerable<IInventory> EnumerateTrackerWatchInventories()
    {
        var invMgr = capi.World.Player?.InventoryManager;
        if (invMgr is null) yield break;
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            var inv = invMgr.GetOwnInventory(name);
            if (inv is not null) yield return inv;
        }
    }

    /// <summary>A carried slot changed — schedule a single coalesced recompute if one isn't already pending
    /// (see <see cref="trackerRecomputeCallbackId"/>).</summary>
    private void OnTrackerSlotModified(int slotId)
    {
        if (trackerRecomputeCallbackId is not null) return;
        trackerRecomputeCallbackId = capi.Event.RegisterCallback(_ =>
        {
            trackerRecomputeCallbackId = null;
            RecomputeTrackers();
        }, 150);
    }

    /// <summary>Recount every Tracker from carried inventory and reconcile: push a changed count through the
    /// server (persist + converge), refresh the read view once, and apply the completion setting on the
    /// exact recompute a Tracker first crosses its target. No-op outside the read view or when the document
    /// has no Tracker.</summary>
    private void RecomputeTrackers()
    {
        if (isEditorMode || viewMode != ScribeLecternView.Read || !IsOpened()) return;
        if (capi.World.Player is not { } player) return;

        // Both plain Trackers and Craft parents count the viewer's carried inventory against their target
        // (add-crafting-tasks 9.2 — a Craft parent counts its recipe OUTPUT), so recount every count-tracked
        // block. Craft ingredient CHILDREN are themselves plain Trackers (depth 1), so they're already
        // included; the Craft parent is the only kind this predicate widens over IsTracker.
        var trackers = host.Document.Blocks.Where(b => b.IsCarriedCountTracked).ToList();
        if (trackers.Count == 0) return;

        bool anyChange = false;
        foreach (var block in trackers)
        {
            string codeKey = block.TargetItemCode ?? "";
            if (!trackerIngredientCache.TryGetValue(codeKey, out var ingredient))
            {
                ScribeTrackerCounter.TryResolveIngredient(capi.World, block.TargetItemCode, out ingredient);
                trackerIngredientCache[codeKey] = ingredient; // may cache null ("unresolvable → counts as 0")
            }

            int counted = ingredient is null ? 0 : ScribeTrackerCounter.CountCarried(player, ingredient);
            int have = Math.Max(0, counted); // raw carried count; NOT capped at the target (overflow shows, 7.14)
            int oldCurrent = block.CurrentQuantity;
            if (have == oldCurrent) continue; // unchanged — nothing to send, and no rising edge to act on

            bool wasMet = oldCurrent >= block.TargetQuantity;
            bool nowMet = have >= block.TargetQuantity;

            // Optimistic local update so the read-view counter reflects the new count immediately; the
            // server echo (MarkDirty → FromTreeAttributes → RefreshReadView) supersedes it shortly.
            block.CurrentQuantity = have;
            anyChange = true;

            // Persist + converge through the server-authoritative path (like Done, D5/4.3).
            SendTrackerQuantity(block.TaskId, have);

            // Rising edge only (unmet → met): apply the completion setting exactly once (4.4). Because we
            // skip unchanged counts above and only fire on the rising edge, a later shortfall can neither
            // re-fire the action nor un-complete a task, and a deleted Tracker simply vanishes from the next
            // recompute (no resurrection).
            if (nowMet && !wasMet)
                ApplyTrackerCompletion(block);
        }

        if (anyChange) RefreshReadView();
    }

    /// <summary>Apply the player's tracker-completion setting when a Tracker fills up (D6/4.4):
    /// <list type="bullet">
    /// <item><b>Complete</b> — issue the same edit as ticking the checkbox (<see cref="OnReadViewCompleteTask"/>),
    /// so it honors the player's own completion policy. Guarded by <c>!Done</c> because that path TOGGLES,
    /// and re-collecting after a drop must never un-complete an already-done Tracker.</item>
    /// <item><b>Delete</b> — remove the task from its document via the standalone identity-addressed
    /// <see cref="ScribeDeleteTaskMessage"/>.</item>
    /// <item><b>Nothing</b> — leave it; the row just reads as satisfied.</item>
    /// </list></summary>
    private void ApplyTrackerCompletion(ScribeBlock block)
    {
        switch (modSystem.MySettings.TrackerCompletion)
        {
            case ScribeTrackerCompletion.Complete:
                if (!block.Done) OnReadViewCompleteTask(block.TaskId);
                break;
            case ScribeTrackerCompletion.Delete:
                capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
                {
                    DocId = host.Document.DocId.ToByteArray(),
                    TaskId = block.TaskId.ToByteArray(),
                });
                break;
            case ScribeTrackerCompletion.Nothing:
                break;
        }
    }

    /// <summary>Send a Tracker's freshly-counted quantity to the server (add-tracker-link-tasks 4.3). The
    /// server clamps and writes it lock-free through the owning host, then resyncs viewers.</summary>
    private void SendTrackerQuantity(Guid taskId, int quantity)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetTrackerQuantityMessage
        {
            DocId = host.Document.DocId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Quantity = quantity,
        });
    }
}
