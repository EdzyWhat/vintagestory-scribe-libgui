using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

// Client-side entry point for the Handbook "Add to Scribe" flow (add-tracker-link-tasks Group 3). The
// Harmony postfix ScribeHandbookPatch injects "Add as Tracker"/"Add as Link" links onto an item's Handbook
// page; clicking one calls AddFromHandbook here with the chosen ScribeAddKind and the item's collectible
// code. AddFromHandbook resolves WHICH Scribe surface receives the new block (three tiers) and hands off to
// ScribeDialogBase.TryAddFromHandbook, which reuses the dialog's own save path — no new packet/server
// handler (the only persistence change is the v6 document bytes shipped in Group 2).
public sealed partial class ScribeModSystem
{
    /// <summary>Harmony id for the client-side Handbook postfix. Patched in <see cref="StartClientSide"/> and
    /// unpatched in <see cref="Dispose"/> so a mod reload/unload leaves the survival mod's Handbook clean.</summary>
    private const string HandbookHarmonyId = "scribe:handbook";

    /// <summary>The Harmony instance owning <see cref="ScribeHandbookPatch"/>. Client-only (created in
    /// <see cref="StartClientSide"/>); null on a pure server.</summary>
    private Harmony? handbookHarmony;

    /// <summary>DocId of the Scribe item whose dialog the player most recently opened (Notebook / Clockmaker /
    /// Tablet), set via <see cref="NoteScribeItemDialogOpened"/>. When a Handbook "Add to Scribe" click finds
    /// no Scribe dialog already open, <see cref="ResolveCarriedScribeItemSlot"/> prefers the carried item with
    /// this DocId so the task lands in the book the player was just using — falling back to the first carried
    /// Scribe item if that book isn't in inventory anymore. Null until the player opens a Scribe item this
    /// session.</summary>
    private Guid? lastOpenedScribeItemDocId;

    /// <summary>Record that the player just opened this item-hosted Scribe document, so a later Handbook
    /// "Add to Scribe" click with no dialog open re-targets the same book (add-tracker-link-tasks 3.2). Called
    /// from each Scribe item's dialog-open path. No-op semantics off the client (it only ever runs there).</summary>
    public void NoteScribeItemDialogOpened(Guid docId) => lastOpenedScribeItemDocId = docId;

    /// <summary>Install the client-side Handbook postfix (<see cref="ScribeHandbookPatch"/>). Separated from
    /// <see cref="StartClientSide"/> for readability; call once from there.</summary>
    private void StartHandbookPatch()
    {
        handbookHarmony = new Harmony(HandbookHarmonyId);
        handbookHarmony.PatchAll(typeof(ScribeModSystem).Assembly);
    }

    /// <summary>Remove the Handbook postfix. Called from <see cref="Dispose"/>; safe if never patched.</summary>
    private void DisposeHandbookPatch()
    {
        handbookHarmony?.UnpatchAll(HandbookHarmonyId);
        handbookHarmony = null;
    }

    /// <summary>Create a Tracker/Link on a Scribe surface from a Handbook "Add to Scribe" click
    /// (add-tracker-link-tasks 3.3). Resolves the target surface in three tiers and hands off to
    /// <see cref="ScribeDialogBase.TryAddFromHandbook"/> (which reuses the dialog's existing save path):
    /// <list type="number">
    /// <item>An <b>already-open</b> Scribe dialog (the player is looking at a book/lectern) — add to it.</item>
    /// <item>Else <b>open a carried Scribe item</b> (the last-opened one, or the first in inventory) and add
    /// to the freshly-opened dialog.</item>
    /// <item>Else <b>no Scribe item at all</b> — a transient error telling the player they need one.</item>
    /// </list>
    /// Client-only (the Handbook is a client GUI). <paramref name="itemCode"/> is the collectible code the
    /// injected link carried (e.g. <c>"game:ingot-copper"</c>).</summary>
    internal void AddFromHandbook(ScribeAddKind kind, string itemCode)
    {
        if (capi is null) return;

        // Tier 1: a Scribe dialog is already open — add straight to it.
        var openDialog = capi.Gui.OpenedGuis
            .OfType<ScribeDialogBase>()
            .FirstOrDefault(d => d.IsOpened());
        if (openDialog is not null)
        {
            openDialog.TryAddFromHandbook(kind, itemCode);
            return;
        }

        // Tier 2: no dialog open — open a carried Scribe item and add to it. OpenScribeDialog returns the
        // dialog it opened; item surfaces grant editor access synchronously, so TryAddFromHandbook appends
        // immediately (a read-only tablet would no-op, but ResolveCarriedScribeItemSlot can't tell wet from
        // fired without extra state, so an occasional no-op on a fired tablet is acceptable — the player
        // simply sees nothing added and can pick an editable book).
        if (ResolveCarriedScribeItemSlot() is { Itemstack.Collectible: IScribeDocumentItem item } slot)
        {
            var opened = item.OpenScribeDialog(slot, capi);
            opened?.TryAddFromHandbook(kind, itemCode);
            return;
        }

        // Tier 3: the player has no Scribe item — guide them to get one.
        capi.TriggerIngameError(this, "scribe-no-scribe-item", Lang.Get("scribe:scribe-gui-no-scribe-item"));
    }

    /// <summary>Find a carried Scribe item to receive a Handbook add, preferring the one whose document is the
    /// <see cref="lastOpenedScribeItemDocId"/> (the book the player was just using), else the first carried
    /// Scribe item. Scans only the player's own hotbar + backpack (not ground/creative). Returns null when the
    /// player carries no Scribe item. Reading each candidate's DocId deserializes its stored document, which is
    /// fine for this rare, one-shot click over a handful of slots.</summary>
    private ItemSlot? ResolveCarriedScribeItemSlot()
    {
        if (capi?.World.Player is not { } player) return null;

        ItemSlot? firstMatch = null;
        foreach (var slot in EnumerateCarriedSlots(player))
        {
            if (slot.Itemstack?.Collectible is not IScribeDocumentItem) continue;
            firstMatch ??= slot;

            // Exact last-opened match wins immediately — no need to scan the rest.
            if (lastOpenedScribeItemDocId is { } wanted
                && ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc)
                && doc is not null
                && doc.DocId == wanted)
            {
                return slot;
            }
        }
        return firstMatch;
    }

    /// <summary>Enumerate the player's own hotbar and backpack slots that hold a stack (the inventories a
    /// "carried" Scribe item can live in). Skips empty slots. Used by <see cref="ResolveCarriedScribeItemSlot"/>
    /// and by the Tracker count engine (<see cref="ScribeTrackerCounter"/>), which sums matching stacks over
    /// exactly these carried-only inventories (add-tracker-link-tasks D5 — chest/ground items are ignored).</summary>
    internal static IEnumerable<ItemSlot> EnumerateCarriedSlots(IClientPlayer player)
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
