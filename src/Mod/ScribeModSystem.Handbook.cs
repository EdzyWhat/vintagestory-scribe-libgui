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

    /// <summary>Public read-only view of <see cref="lastOpenedScribeItemDocId"/> — used by
    /// <see cref="ScribeDialogBase.ComputeAcceptCandidates"/> (refine-assignment-desk-inbox-ux triage
    /// 2026-08-31) to prefer the same "book the player was just using" target that the Handbook's "Add to
    /// Scribe" flow already does, instead of listing every writeable Scribe item across ALL registered
    /// inventories (which could include an open chest's).</summary>
    internal Guid? LastOpenedScribeItemDocId => lastOpenedScribeItemDocId;

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

    /// <summary>Create a Tracker/Link on a Scribe surface from an <b>item</b> Handbook page's "Add to Scribe"
    /// click (add-tracker-link-tasks 3.3). Resolves the target surface via <see cref="AddFromHandbookCore"/>
    /// and hands the chosen dialog to <see cref="ScribeDialogBase.TryAddFromHandbook"/>. <paramref name="itemCode"/>
    /// is the collectible code the injected link carried (e.g. <c>"game:ingot-copper"</c>).</summary>
    internal void AddFromHandbook(ScribeAddKind kind, string itemCode)
        => AddFromHandbookCore(dialog => dialog.TryAddFromHandbook(kind, itemCode));

    /// <summary>Create a <b>Crafting Task</b> on a Scribe surface from an item Handbook page's "Add Crafting
    /// Task" click (add-crafting-tasks D5). Same three-tier surface resolution as <see cref="AddFromHandbook"/>,
    /// but hands off to <see cref="ScribeDialogBase.TryAddCraftFromHandbook"/> with the output
    /// <paramref name="itemCode"/> and the chosen grid recipe's stable <paramref name="recipeSignature"/>
    /// (from <see cref="ScribeCraftRecipeProbe"/>), which creates the Craft parent and generates its ingredient
    /// subtasks.</summary>
    internal void AddCraftFromHandbook(string itemCode, string recipeSignature)
        => AddFromHandbookCore(dialog => dialog.TryAddCraftFromHandbook(itemCode, recipeSignature));

    /// <summary>Create a guide-page <b>Link</b> on a Scribe surface from a Handbook guide/explainer page's
    /// injected "Add Link" click (add-tracker-link-tasks 7.6). Same three-tier surface resolution as
    /// <see cref="AddFromHandbook"/>, but hands off to <see cref="ScribeDialogBase.TryAddGuideLinkFromHandbook"/>
    /// with the guide's <paramref name="pageCode"/> and display <paramref name="title"/> (a guide page has no
    /// item to resolve a name from, so the title is captured here at click time).</summary>
    internal void AddGuideLinkFromHandbook(string pageCode, string title)
        => AddFromHandbookCore(dialog => dialog.TryAddGuideLinkFromHandbook(pageCode, title));

    /// <summary>Resolve which Scribe surface receives a Handbook-originated add and run <paramref name="apply"/>
    /// against it (add-tracker-link-tasks 3.3 / 7.6). Both the item path (<see cref="AddFromHandbook"/>) and the
    /// guide-page path (<see cref="AddGuideLinkFromHandbook"/>) share this three-tier resolution, differing only
    /// in what they do with the chosen dialog:
    /// <list type="number">
    /// <item>An <b>already-open</b> Scribe dialog (the player is looking at a book/lectern) — apply to it.</item>
    /// <item>Else <b>open a carried WRITEABLE Scribe item</b> (the last-opened one, or the first in inventory)
    /// and apply to the freshly-opened dialog. Read-only tablets (hardened/fired) are skipped so the append
    /// never silently no-ops against one (feedback 6.2).</item>
    /// <item>Else a transient error: "only read-only Scribe items" and "no Scribe item at all" are distinct.</item>
    /// </list>
    /// Client-only (the Handbook is a client GUI).</summary>
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

        // Tier 2: no dialog open — open a carried WRITEABLE Scribe item and add to it. OpenScribeDialog
        // returns the dialog it opened; item surfaces grant editor access synchronously, so the deferred
        // append runs immediately. ResolveWriteableCarriedSlot skips read-only tablets (hardened/fired) so the
        // append never silently no-ops against one — it lands on the next editable book instead (feedback 6.2).
        var writeable = ResolveWriteableCarriedSlot(out bool anyScribeItem);
        if (writeable is { Itemstack.Collectible: IScribeDocumentItem item })
        {
            var opened = item.OpenScribeDialog(writeable, capi);
            if (opened is not null) apply(opened);
            return;
        }

        // The player carries Scribe items but every one is read-only (hardened or fired tablets): a single
        // clear error rather than one per item, per feedback 6.2.
        if (anyScribeItem)
        {
            capi.TriggerIngameError(this, "scribe-item-locked", Lang.Get("scribe:scribe-gui-all-locked"));
            return;
        }

        // Tier 3: the player has no Scribe item at all — guide them to get one.
        capi.TriggerIngameError(this, "scribe-no-scribe-item", Lang.Get("scribe:scribe-gui-no-scribe-item"));
    }

    /// <summary>Find a carried WRITEABLE Scribe item to receive a Handbook add, preferring the one whose
    /// document is the <see cref="lastOpenedScribeItemDocId"/> (the book the player was just using) IF it is
    /// writeable, else the first writeable carried Scribe item. A read-only item (a hardened/fired tablet —
    /// <see cref="IScribeDocumentItem.IsSlotWriteable"/> false) is skipped so the append never lands on a
    /// surface that would silently drop it (add-tracker-link-tasks feedback 6.2). Scans only the player's own
    /// hotbar + backpack (not ground/creative). <paramref name="anyScribeItem"/> reports whether ANY Scribe
    /// item was carried at all (writeable or not), so the caller can tell "no Scribe item" (guide the player)
    /// apart from "only read-only Scribe items" (a distinct locked error). Returns null when no writeable
    /// item is found. Reading each candidate's DocId deserializes its stored document, which is fine for this
    /// rare, one-shot click over a handful of slots.</summary>
    private ItemSlot? ResolveWriteableCarriedSlot(out bool anyScribeItem)
    {
        anyScribeItem = false;
        if (capi?.World.Player is not { } player) return null;

        ItemSlot? firstWriteable = null;
        foreach (var slot in EnumerateCarriedSlots(player))
        {
            if (slot.Itemstack?.Collectible is not IScribeDocumentItem item) continue;
            anyScribeItem = true;

            // Skip a read-only item (hardened/fired tablet); it can't take the append.
            if (!item.IsSlotWriteable(slot)) continue;
            firstWriteable ??= slot;

            // The last-opened book wins immediately — but only when it's the writeable candidate we just
            // vetted (a hardened last-opened tablet falls through to the next writeable item above).
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
