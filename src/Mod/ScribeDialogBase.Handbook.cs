using System;                    // Action
using System.Linq;
using Gui;                       // GuiDialog
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog, LinkTextComponent
using Vintagestory.API.Config;   // Lang

namespace Scribe;

// Handbook "Add to Scribe" entry point: ScribeModSystem.AddFromHandbook resolves a surface and calls
// TryAddFromHandbook here. Reuses the dialog's save path (scratch -> FlushIfDirty -> SendFlushPacket).
public abstract partial class ScribeDialogBase
{
    /// <summary>A Handbook append waiting for this dialog to reach editor mode; invoked by
    /// <see cref="FlushPendingHandbookAppend"/> at the end of <see cref="EnterEditorMode"/>. Null when nothing is pending.</summary>
    private Action? pendingHandbookAppend;

    /// <summary>True between a singleplayer-optimistic editor entry (Handbook append on a block surface while the
    /// open Handbook has paused the integrated server) and the eventual server lock grant. Tells
    /// <see cref="EnterEditorMode"/> that the delayed grant, which carries the pre-flush document, must keep the
    /// optimistic scratch and re-flush it. Pure singleplayer only.</summary>
    private bool optimisticEditorEntry;

    /// <summary>True when editor access needs a server round-trip (block surfaces); item surfaces enter the
    /// editor synchronously. Decides whether a stashed append awaits a pending grant or is discarded.</summary>
    protected virtual bool EditorAccessIsAsync => false;

    /// <summary>Whether a Handbook append can ever make this open surface editable. False only for a permanently
    /// read-only surface (fired tablet); a block locked by another player still counts.</summary>
    protected virtual bool CanEditFromHandbook => true;

    /// <summary>Shows the "can't be written in" notice for a Handbook append on a permanently read-only surface.
    /// The tablet overrides it with material-specific wording.</summary>
    protected virtual void NotifyHandbookAppendReadOnly()
        => capi.TriggerIngameError(this, "scribe-gui-all-locked", Lang.Get("scribe:scribe-gui-all-locked"));

    /// <summary>Create a Tracker/Link task from a Handbook "Add to Scribe" click via the dialog's own save path.
    /// Editing: append and flush now. Not editing: stash the append and request editor access (item surfaces
    /// consume it synchronously, block surfaces in <see cref="EnterEditorMode"/> on the async grant). A
    /// permanently read-only surface reports instead (<see cref="CanEditFromHandbook"/>); a refused request
    /// clears the stash.</summary>
    internal void TryAddFromHandbook(ScribeAddKind kind, string itemCode)
        => TryHandbookAppend(() => ApplyHandbookAppend(kind, itemCode));

    /// <summary>Create a guide-page Link task from a Handbook guide page's "Add Link" click. The target is a
    /// <c>"page:"</c>-prefixed code, so the title is captured at click time. Tracker doesn't apply to guide pages.</summary>
    internal void TryAddGuideLinkFromHandbook(string pageCode, string title)
        => TryHandbookAppend(() => ApplyGuideLinkAppend(pageCode, title));

    /// <summary>Create a Crafting Task from a Handbook item page click, carrying the output item code and the
    /// recipe's stable signature (<see cref="ScribeCraftRecipeProbe"/>).</summary>
    internal void TryAddCraftFromHandbook(string itemCode, string signature)
        => TryHandbookAppend(() => ApplyCraftHandbookAppend(itemCode, signature));

    /// <summary>Insert a Crafting Task (Craft parent + generated Tracker ingredient children) at the New Task
    /// Insert edge and flush it. Counts against the task cap. No-op unless the editor is live.</summary>
    private void ApplyCraftHandbookAppend(string itemCode, string signature)
    {
        if (scratch is null || !isEditorMode) return;
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; } // Craft counts against the cap
        var craftId = scratch.InsertCraft(NewTaskInsertIndex(), itemCode, 1, signature);
        if (craftId == Guid.Empty) return;
        var craftBlock = scratch.FindByTaskId(craftId);
        if (craftBlock is not null) ReconcileCraftFromSignature(scratch, craftBlock);
        isDirty = true;
        SyncFocusNodesToScratch();
        // Persist immediately; the player expects the task to exist right away.
        FlushIfDirty();
        RebuildBody();
    }

    /// <summary>Re-resolve a Craft parent's bound recipe by persisted signature and reconcile its depth-1
    /// ingredient run to the current batch size (inserting missing children). An unresolvable signature leaves
    /// the parent and its children untouched. Returns whether the block count changed.</summary>
    private bool ReconcileCraftFromSignature(ScribeDocument doc, ScribeBlock craftBlock)
    {
        if (!craftBlock.IsCraft) return false;
        var probe = ScribeCraftRecipeProbe.ResolveBySignature(capi, craftBlock.RecipeSignature);
        if (probe is not { } p) return false;

        int before = doc.Blocks.Count;
        int craftsNeeded = ScribeCraftMath.CraftsNeeded(craftBlock.TargetQuantity, p.OutputPerCraft);
        doc.ReconcileCraftIngredients(craftBlock.TaskId, p.Ingredients, p.Notes, craftsNeeded);
        return doc.Blocks.Count != before;
    }

    /// <summary>Stepper-only: rescale matched ingredient children; never insert.</summary>
    private bool RescaleCraftFromSignature(ScribeDocument doc, ScribeBlock craftBlock)
    {
        if (!craftBlock.IsCraft) return false;
        var probe = ScribeCraftRecipeProbe.ResolveBySignature(capi, craftBlock.RecipeSignature);
        if (probe is not { } p) return false;

        int craftsNeeded = ScribeCraftMath.CraftsNeeded(craftBlock.TargetQuantity, p.OutputPerCraft);
        return doc.RescaleCraftIngredients(craftBlock.TaskId, p.Ingredients, p.Notes, craftsNeeded);
    }

    /// <summary>Shared deferral for a Handbook append. Editing: run <paramref name="apply"/> now. Otherwise stash
    /// it and request editor access; item surfaces consume the stash synchronously, block surfaces in
    /// <see cref="EnterEditorMode"/> on the async grant. A refused request clears the stash.</summary>
    private void TryHandbookAppend(Action apply)
    {
        if (isEditorMode)
        {
            apply();
            return;
        }

        // Permanently read-only surface: show the read-only notice instead of silently dropping the append.
        if (!CanEditFromHandbook)
        {
            NotifyHandbookAppendReadOnly();
            return;
        }

        pendingHandbookAppend = apply;

        // Pure-singleplayer block surface: the open Handbook paused the server, so the lock round-trip can't finish.
        // Enter the editor locally now, still send the lock request and then the flush (in that order) so the server
        // catches up on unpause; optimisticEditorEntry makes the delayed grant keep this scratch. Excluded in
        // multiplayer, where the lock can genuinely be refused.
        if (EditorAccessIsAsync && IsPureSingleplayer && !host.IsLockedByOther(capi.World.Player.PlayerUID))
        {
            optimisticEditorEntry = true;
            RequestEditorAccess();                                              // lock request — queued first
            EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));      // local entry: seed + apply + flush
            return;
        }

        // Block surfaces get an async grant unless another player holds the lock (TryEnterEditor then shows the lock error).
        bool grantPending = EditorAccessIsAsync && !host.IsLockedByOther(capi.World.Player.PlayerUID);
        TryEnterEditor();

        // Refused (locked by other): drop the stash so it can't apply later.
        if (!isEditorMode && !grantPending) pendingHandbookAppend = null;
    }

    /// <summary>True in a pure-singleplayer session (not LAN-hosted or dedicated). Only here does the open Handbook
    /// pause the integrated server and only here is optimistic local editor entry safe (no other client can
    /// contend the lock).</summary>
    private bool IsPureSingleplayer => capi.IsSinglePlayer && !capi.OpenedToLan;

    /// <summary>Insert the Handbook-originated Tracker/Link block at the New Task Insert edge and flush it.
    /// Applies the same task-cap gate as the footer add. No-op unless the editor is live.</summary>
    private void ApplyHandbookAppend(ScribeAddKind kind, string itemCode)
    {
        if (scratch is null || !isEditorMode) return;
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (!kind.Add(scratch, itemCode, NewTaskInsertIndex())) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // No auto-focus on the new stepper: VS keeps keyboard focus on the Handbook window, so arming a caret
        // here would paint it while typing still goes to the Handbook.
        // Persist immediately; the player expects the task to exist right away.
        FlushIfDirty();
        // Live path when already editing. In the deferred path this is a harmless no-op: the append is applied
        // before EnterEditorMode's ForceRebuild, which builds the tree from the mutated scratch.
        RebuildBody();
    }

    /// <summary>Insert a guide-page Link block at the New Task Insert edge and flush it. Counts against the
    /// task cap like any block. No-op unless the editor is live.</summary>
    private void ApplyGuideLinkAppend(string pageCode, string title)
    {
        if (scratch is null || !isEditorMode) return;
        if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (!scratch.InsertGuideLink(NewTaskInsertIndex(), pageCode, title)) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // Persist immediately; the player expects the task to exist right away.
        FlushIfDirty();
        RebuildBody();
    }

    /// <summary>Consume a stashed Handbook append once editor access has landed. One-shot: the stash is
    /// cleared before invoking so a failed append can't re-fire.</summary>
    private void FlushPendingHandbookAppend()
    {
        if (pendingHandbookAppend is not { } pending) return;
        pendingHandbookAppend = null;
        pending();
    }

    /// <summary>Footer add-picker action for an item-bound kind (Tracker/Link), which needs an item code only a
    /// Handbook page supplies. Handbook closed: opens the overview with the search box focused via the
    /// <c>"handbooksearch"</c> link protocol. Handbook open: shows an error telling the player to use "Add to Scribe"
    /// at the bottom of the entry. No-op when the survival mod's handbook isn't loaded.</summary>
    private void DispatchItemKindGuide(ScribeAddKind kind)
    {
        GuiDialog? openHandbook = capi.Gui.OpenedGuis
            .FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook");

        if (openHandbook != null)
        {
            capi.TriggerIngameError(this, "scribe-additem-guide", Lang.Get("scribe:scribe-gui-additem-guide"));
            return;
        }

        // "handbooksearch://" opens the overview with the search field focused (initOverviewGui focuses it on build).
        if (capi.LinkProtocols.TryGetValue("handbooksearch", out var search))
            search(new LinkTextComponent("handbooksearch://"));
    }
}
