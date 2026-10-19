using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

// Client-side entry point for the Handbook "Add to Scribe" flow. The Harmony postfix ScribeHandbookPatch injects links onto an
// item's Handbook page; clicking one calls AddFromHandbook here, which resolves which Scribe surface receives the new block
// (three tiers) and hands off to ScribeDialogBase.TryAddFromHandbook, reusing the dialog's save path (no dedicated packet).
public sealed partial class ScribeModSystem
{
    /// <summary>Harmony id for the client-side Handbook postfix; patched in <see cref="StartClientSide"/>, unpatched in <see cref="Dispose"/>.</summary>
    private const string HandbookHarmonyId = "scribe:handbook";

    /// <summary>The Harmony instance owning <see cref="ScribeHandbookPatch"/>; null on a pure server.</summary>
    private Harmony? handbookHarmony;

    /// <summary>DocId of the Scribe item whose dialog the player most recently opened, set via <see cref="NoteScribeItemDialogOpened"/>.
    /// A Handbook add with no Scribe dialog open prefers the carried item with this DocId, else the first carried Scribe item.
    /// Null until a Scribe item is opened this session.</summary>
    private Guid? lastOpenedScribeItemDocId;

    /// <summary>Read-only view of <see cref="lastOpenedScribeItemDocId"/>, so <see cref="ScribeDialogBase.ComputeAcceptCandidates"/>
    /// prefers the same book as the Handbook flow instead of listing every writeable item across all registered inventories.</summary>
    internal Guid? LastOpenedScribeItemDocId => lastOpenedScribeItemDocId;

    /// <summary>Record that the player just opened this item-hosted document, so a later Handbook add with no dialog open re-targets it.</summary>
    public void NoteScribeItemDialogOpened(Guid docId) => lastOpenedScribeItemDocId = docId;

    /// <summary>Install the client-side Handbook postfix (<see cref="ScribeHandbookPatch"/>); call once from <see cref="StartClientSide"/>.</summary>
    private void StartHandbookPatch()
    {
        handbookHarmony = new Harmony(HandbookHarmonyId);
        handbookHarmony.PatchAll(typeof(ScribeModSystem).Assembly);
    }

    /// <summary>Remove the Handbook postfix (<see cref="Dispose"/>); safe if never patched.</summary>
    private void DisposeHandbookPatch()
    {
        handbookHarmony?.UnpatchAll(HandbookHarmonyId);
        handbookHarmony = null;
    }

    /// <summary>Create a Tracker/Link on a Scribe surface from an <b>item</b> Handbook page's "Add to Scribe" click.
    /// <paramref name="itemCode"/> is the collectible code the link carried (e.g. <c>"game:ingot-copper"</c>).</summary>
    internal void AddFromHandbook(ScribeAddKind kind, string itemCode)
        => AddFromHandbookCore(dialog => dialog.TryAddFromHandbook(kind, itemCode));

    /// <summary>Create a <b>Crafting Task</b> from an item Handbook page's "Add Crafting Task" click, via
    /// <see cref="ScribeDialogBase.TryAddCraftFromHandbook"/> with the output <paramref name="itemCode"/> and the grid recipe's stable
    /// <paramref name="recipeSignature"/> (from <see cref="ScribeCraftRecipeProbe"/>).</summary>
    internal void AddCraftFromHandbook(string itemCode, string recipeSignature)
        => AddFromHandbookCore(dialog => dialog.TryAddCraftFromHandbook(itemCode, recipeSignature));

    /// <summary>Create a guide-page <b>Link</b> from a Handbook guide page's "Add Link" click. A guide page has no item to
    /// resolve a name from, so <paramref name="title"/> is captured at click time.</summary>
    internal void AddGuideLinkFromHandbook(string pageCode, string title)
        => AddFromHandbookCore(dialog => dialog.TryAddGuideLinkFromHandbook(pageCode, title));

    /// <summary>Resolve which Scribe surface receives a Handbook-originated add and run <paramref name="apply"/> on it. Three tiers:
    /// an already-open Scribe dialog; else open a carried WRITEABLE Scribe item (last-opened, else first; read-only tablets are skipped
    /// so the append never silently no-ops); else a transient error ("only read-only items" vs "no Scribe item"). Client-only.</summary>
    private void AddFromHandbookCore(Action<ScribeDialogBase> apply)
    {
        if (capi is null) return;

        // Tier 1: a Scribe dialog is already open — add straight to it.
        var openDialog = capi.Gui.OpenedGuis
            .OfType<ScribeDialogBase>()
            .FirstOrDefault(d => d.IsOpened());
        if (openDialog is not null)
        {
            apply(openDialog);
            return;
        }

        // Tier 2: no dialog open. OpenScribeDialog returns the dialog it opened; item surfaces grant editor access synchronously,
        // so the deferred append runs immediately. Read-only tablets are skipped.
        var writeable = ResolveWriteableCarriedSlot(out bool anyScribeItem);
        if (writeable is { Itemstack.Collectible: IScribeDocumentItem item })
        {
            var opened = item.OpenScribeDialog(writeable, capi);
            if (opened is not null) apply(opened);
            return;
        }

        // Every carried Scribe item is read-only: one clear error rather than one per item.
        if (anyScribeItem)
        {
            capi.TriggerIngameError(this, "scribe-item-locked", Lang.Get("scribe:scribe-gui-all-locked"));
            return;
        }

        // Tier 3: the player has no Scribe item at all — guide them to get one.
        capi.TriggerIngameError(this, "scribe-no-scribe-item", Lang.Get("scribe:scribe-gui-no-scribe-item"));
    }

    /// <summary>Find a carried WRITEABLE Scribe item to receive a Handbook add: the one whose document is
    /// <see cref="lastOpenedScribeItemDocId"/> if writeable, else the first writeable one; read-only items are skipped. Scans only
    /// hotbar + backpack. <paramref name="anyScribeItem"/> reports whether ANY Scribe item was carried, so the caller can tell "none"
    /// from "only read-only". Null when nothing writeable. Reading a DocId deserializes the stored document, fine for a rare one-shot click.</summary>
    private ItemSlot? ResolveWriteableCarriedSlot(out bool anyScribeItem)
    {
        anyScribeItem = false;
        if (capi?.World.Player is not { } player) return null;

        ItemSlot? firstWriteable = null;
        foreach (var slot in EnumerateCarriedSlots(player))
        {
            if (slot.Itemstack?.Collectible is not IScribeDocumentItem item) continue;
            anyScribeItem = true;

            // Skip a read-only item (hardened/fired tablet).
            if (!item.IsSlotWriteable(slot)) continue;
            firstWriteable ??= slot;

            // The last-opened book wins only if it is the writeable candidate just vetted.
            if (lastOpenedScribeItemDocId is { } wanted
                && ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc)
                && doc is not null
                && doc.DocId == wanted)
            {
                return slot;
            }
        }
        return firstWriteable;
    }

    /// <summary>Enumerate the player's own hotbar and backpack slots that hold a stack. Shared by the Handbook flow, the Tracker count
    /// engine (<see cref="ScribeTrackerCounter"/>, carried-only inventories), and the server-side proximity heartbeat
    /// (<see cref="ScribeModSystem.OnTaskNoticeProximityTick"/>); typed to <see cref="IPlayer"/> so it works for <c>IServerPlayer</c> too.</summary>
    internal static IEnumerable<ItemSlot> EnumerateCarriedSlots(IPlayer player)
    {
        var invMgr = player.InventoryManager;
        foreach (var invName in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            var inv = invMgr.GetOwnInventory(invName);
            if (inv is null) continue;
            foreach (var slot in inv)
                if (slot?.Itemstack is not null)
                    yield return slot;
        }
    }
}
