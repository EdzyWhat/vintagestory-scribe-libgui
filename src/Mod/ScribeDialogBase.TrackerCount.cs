using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog
using Vintagestory.API.Common;   // IInventory, CraftingRecipeIngredient
using Vintagestory.API.Config;   // GlobalConstants

namespace Scribe;

// Tracker count engine: while in the read view, keeps each Tracker's CurrentQuantity in sync with the VIEWING player's carried inventory (hotbar + backpack only),
// pushed through the server-authoritative ScribeSetTrackerQuantityMessage so other viewers converge. When a Tracker first reaches its target, the tracker-completion setting is applied.
// Read-view-only: the editor renders off `scratch`, so mutating the live document there would fight the editor's autosave flush.
public abstract partial class ScribeDialogBase
{
    /// <summary>Game-tick listener id for the ~1s backstop poll, registered for the dialog's lifetime. A cheap no-op unless the document has a Tracker and the read view is showing; it covers events <see cref="OnTrackerSlotModified"/> can miss (in-place stack merge, switching back into read).</summary>
    private long? trackerPollListenerId;

    /// <summary>Pending coalesced recompute callback id, so a burst of SlotModified events collapses into one recompute ~150ms later. Null when none is scheduled.</summary>
    private long? trackerRecomputeCallbackId;

    /// <summary>Carried inventories subscribed to SlotModified, so <see cref="TeardownTrackerEngine"/> unsubscribes exactly those.</summary>
    private readonly List<IInventory> trackerWatchedInventories = new();

    /// <summary>Resolved ingredient per target code, cached because resolving touches the registries. A null value caches "doesn't resolve" so the probe isn't retried each tick. Cleared on close.</summary>
    private readonly Dictionary<string, CraftingRecipeIngredient?> trackerIngredientCache = new();

    /// <summary>Starts the Tracker count engine: registers the poll, subscribes to carried-inventory changes, and recomputes immediately so an already-satisfied Tracker reads correctly on open.</summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        // Register the block inventory as OPEN server-side: LibGUI's OnGuiOpened opens it client-side only (its OnGuiClosed sends the paired 1001 close). Without the 1000 packet the server's HandleActivateInventorySlot finds no opened inventory and silently drops every slot move, so the item is lost on relog. BlockEntityScriptorium handles 1000 -> OpenInventory. Guarded so document-only surfaces (no inventory) are unaffected.
        if (Inventory != null)
        {
            SendBlockEntityPacket(1000);
        }

        trackerPollListenerId ??= capi.Event.RegisterGameTickListener(_ =>
        {
            RecomputeTrackers();
            RecomputeQuestObjectiveProgress();
        }, 1000);
        foreach (var inv in EnumerateTrackerWatchInventories())
        {
            inv.SlotModified += OnTrackerSlotModified;
            trackerWatchedInventories.Add(inv);
        }
        RecomputeTrackers();
        RecomputeQuestObjectiveProgress();
    }

    /// <summary>Tears down the count engine (from <see cref="OnGuiClosed"/>): unregisters the poll and pending recompute, unsubscribes inventories, drops the cache. Idempotent.</summary>
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

    /// <summary>The DocId this dialog has open (any view), or null when closed. The HUD's Tracker engine reads it across open dialogs and DEFERS for that doc (display-only: no count send, no rising-edge completion) for as long as the dialog is open:
    /// the read view's <see cref="RecomputeTrackers"/> already drives send + completion (no double-toggle), and the editor view is inert there, so a HUD count write would fight the editor's scratch autosave flush.</summary>
    internal Guid? OpenDocumentId => IsOpened() ? host.Document.DocId : (Guid?)null;

    /// <summary>The carried inventories a Tracker counts (hotbar + backpack), matching <see cref="ScribeModSystem.EnumerateCarriedSlots"/>'s scope so watch-set and count-set agree.</summary>
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

    /// <summary>A carried slot changed: schedules one coalesced recompute if none is pending.</summary>
    private void OnTrackerSlotModified(int slotId)
    {
        // While paused, delayed callbacks don't fire until unpause and the 2-arg RegisterCallback throws in developer mode, so recompute now without coalescing.
        if (capi.IsGamePaused)
        {
            RecomputeTrackers();
            return;
        }
        if (trackerRecomputeCallbackId is not null) return;
        // permittedWhilePaused: true because this only refreshes Tracker UI; the 2-arg overload throws in developer mode (ClientMain.RegisterCallback).
        trackerRecomputeCallbackId = capi.Event.RegisterCallback(_ =>
        {
            trackerRecomputeCallbackId = null;
            RecomputeTrackers();
        }, 150, permittedWhilePaused: true);
    }

    /// <summary>Recounts every Tracker from carried inventory: pushes a changed count through the server, refreshes the read view once, and applies the completion setting on the recompute a Tracker first crosses its target. No-op outside the read view or without Trackers.</summary>
    private void RecomputeTrackers()
    {
        if (isEditorMode || viewMode != ScribeLecternView.Read || !IsOpened()) return;
        if (capi.World.Player is not { } player) return;

        // Plain Trackers and Craft parents (counting their recipe OUTPUT) both count carried inventory; Craft ingredient children are plain Trackers and already included.
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

            // Optimistic local update so the counter updates immediately; the server echo supersedes it.
            block.CurrentQuantity = have;
            anyChange = true;

            SendTrackerQuantity(block.TaskId, have);

            // Rising edge only (unmet -> met): unchanged counts are skipped above, so a later shortfall neither re-fires nor un-completes, and a deleted Tracker doesn't resurrect.
            if (nowMet && !wasMet)
                ApplyTrackerCompletion(block);
        }

        if (anyChange) RefreshReadView();
    }

    /// <summary>Applies the tracker-completion setting when a Tracker fills: Complete issues the checkbox edit via <see cref="OnReadViewCompleteTask"/> (guarded by !Done because that path TOGGLES); Delete sends <see cref="ScribeDeleteTaskMessage"/>; Nothing leaves it.</summary>
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
                    SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
                });
                break;
            case ScribeTrackerCompletion.Nothing:
                break;
        }
    }

    /// <summary>Sends a Tracker's freshly counted quantity; the server clamps, writes it lock-free through the owning host, then resyncs viewers.</summary>
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
