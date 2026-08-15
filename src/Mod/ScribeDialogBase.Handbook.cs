using System;                    // Action
using System.Linq;
using Gui;                       // GuiDialog
using Scribe.Core;
using Vintagestory.API.Client;   // ICoreClientAPI, GuiDialog, LinkTextComponent
using Vintagestory.API.Config;   // Lang

namespace Scribe;

// Handbook "Add to Scribe" entry point (add-tracker-link-tasks Group 3). A Handbook page's injected
// "Add to Scribe" link (ScribeHandbookPatch) carries the collectible code; ScribeModSystem.AddFromHandbook
// resolves a live/openable Scribe surface and calls TryAddFromHandbook here. The whole flow REUSES the
// dialog's existing per-surface save path (scratch → FlushIfDirty → SendFlushPacket) rather than adding a
// dedicated handbook packet/server handler — the only persistence change is the v6 document bytes shipped in
// Group 2, so this is backwards compatible (a ScribeAddKind is never serialized).
public abstract partial class ScribeDialogBase
{
    /// <summary>A Handbook-originated append waiting for this dialog to reach editor mode (Case B). Stashed
    /// as the deferred apply action when a "Add to Scribe"/"Add Link" click lands while the dialog is NOT
    /// editing, then invoked by <see cref="FlushPendingHandbookAppend"/> at the end of
    /// <see cref="EnterEditorMode"/> once editor access is granted (immediately for item surfaces, or on the
    /// async server grant for block surfaces). An <c>Action</c> (rather than a specific tuple) so both the
    /// item-page path (<see cref="TryAddFromHandbook"/>) and the guide-page path
    /// (<see cref="TryAddGuideLinkFromHandbook"/>) share one deferral mechanism. Null when nothing is
    /// pending.</summary>
    private Action? pendingHandbookAppend;

    /// <summary>True when this surface's editor access requires a server round-trip (a block surface —
    /// Lectern/Scriptorium — whose <see cref="RequestEditorAccess"/> sends a lock request and lands the grant
    /// asynchronously in <see cref="EnterEditorMode"/>). Item surfaces (Notebook/Clockmaker/Tablet) override
    /// <see cref="RequestEditorAccess"/> to enter the editor synchronously, so they leave this false. Used by
    /// <see cref="TryAddFromHandbook"/> to decide whether a stashed append should be kept for the pending
    /// grant or discarded because access was refused outright.</summary>
    protected virtual bool EditorAccessIsAsync => false;

    /// <summary>Whether a Handbook-originated append can ever drive THIS open surface to an editable state.
    /// True for every normally-editable surface (wet tablet, notebook, lectern, scriptorium — a block that's
    /// merely locked by another player still counts, because it becomes editable once the lock frees). False
    /// ONLY for a permanently read-only surface (a fired/hardened tablet), where <see cref="TryHandbookAppend"/>
    /// surfaces <see cref="NotifyHandbookAppendReadOnly"/> instead of stashing an append that could never apply
    /// — the fix for the silent no-feedback drop when a read-only item is open (feedback 7.13).</summary>
    protected virtual bool CanEditFromHandbook => true;

    /// <summary>Surface the "this document can't be written in" notice when a Handbook append targets a
    /// permanently read-only OPEN surface (<see cref="CanEditFromHandbook"/> is false). The base uses the
    /// generic locked-Scribe-items message; the tablet overrides it with its material-specific fired/hardened
    /// wording, reusing the same keys as its row-text-edit refusal (feedback 7.13).</summary>
    protected virtual void NotifyHandbookAppendReadOnly()
        => capi.TriggerIngameError(this, "scribe-gui-all-locked", Lang.Get("scribe:scribe-gui-all-locked"));

    /// <summary>Create a Tracker/Link task on THIS dialog from a Handbook "Add to Scribe" click, reusing the
    /// dialog's own save path (add-tracker-link-tasks 3.4). Two cases:
    /// <list type="bullet">
    /// <item><b>Already editing</b> — the scratch document is live, so append + flush immediately
    /// (<see cref="ApplyHandbookAppend"/>).</item>
    /// <item><b>Not editing</b> — stash the append and request editor access via <see cref="TryEnterEditor"/>.
    /// Item surfaces enter synchronously (the stash is consumed before this returns); block surfaces get an
    /// async grant, so the stash is kept for <see cref="EnterEditorMode"/> to consume — landing the player in a
    /// live editor view so they can immediately set the new Tracker's count. A permanently read-only surface
    /// reports instead of stashing (<see cref="CanEditFromHandbook"/>); a surface locked by another player
    /// surfaces the generic lock error and the stale stash is cleared so a later editor entry doesn't silently
    /// apply it.</item>
    /// </list>
    /// The kind is one of the item-bound kinds (<see cref="ScribeAddKinds.Tracker"/> /
    /// <see cref="ScribeAddKinds.Link"/>); <paramref name="itemCode"/> is the collectible code the Handbook
    /// link supplied.</summary>
    internal void TryAddFromHandbook(ScribeAddKind kind, string itemCode)
        => TryHandbookAppend(() => ApplyHandbookAppend(kind, itemCode));

    /// <summary>Create a guide-page <b>Link</b> task on THIS dialog from a Handbook guide/explainer page's
    /// injected "Add Link" click (add-tracker-link-tasks 7.6). Same two-case deferral as
    /// <see cref="TryAddFromHandbook"/>, but the target is a <c>"page:"</c>-prefixed guide code rather than an
    /// item — so it carries the guide's <paramref name="pageCode"/> and its display <paramref name="title"/>
    /// (captured at click time because a guide page has no item to resolve a name from). Tracker does not
    /// apply to guide pages (nothing to count).</summary>
    internal void TryAddGuideLinkFromHandbook(string pageCode, string title)
        => TryHandbookAppend(() => ApplyGuideLinkAppend(pageCode, title));

    /// <summary>Shared deferral for a Handbook-originated append (item or guide-page). If already editing, run
    /// <paramref name="apply"/> now; otherwise stash it and request editor access. Item surfaces enter
    /// synchronously (the stash is consumed before this returns); block surfaces get an async grant, so the
    /// stash is kept for <see cref="EnterEditorMode"/> to consume. If access is refused (locked by another
    /// player, or the surface can't edit) the stale stash is cleared so a later editor entry doesn't silently
    /// apply it.</summary>
    private void TryHandbookAppend(Action apply)
    {
        if (isEditorMode)
        {
            apply();
            return;
        }

        // The open surface is permanently read-only (a fired/hardened tablet, or any future "uneditable"
        // document): it can never reach editor mode, so surface the surface-specific read-only notice rather
        // than silently dropping the append — the bug where an open locked item gave NO feedback (feedback 7.13).
        if (!CanEditFromHandbook)
        {
            NotifyHandbookAppendReadOnly();
            return;
        }

        pendingHandbookAppend = apply;

        // A block surface will get its grant asynchronously (server lock round-trip) UNLESS the lock is held
        // by someone else — in which case TryEnterEditor surfaces the generic lock error and never requests.
        bool grantPending = EditorAccessIsAsync && !host.IsLockedByOther(capi.World.Player.PlayerUID);
        TryEnterEditor();

        // If we didn't synchronously enter editor mode AND no async grant is coming, the request was refused
        // (locked-by-other): drop the stash so it can't be applied later.
        if (!isEditorMode && !grantPending) pendingHandbookAppend = null;
    }

    /// <summary>Append the Handbook-originated Tracker/Link block to the live scratch document and flush it
    /// through the dialog's existing save path (add-tracker-link-tasks 3.4/3.5). Enforces the task-cap gate
    /// for cap-counting kinds (Tracker counts; Link does not — see <see cref="ScribeAddKind.CountsAgainstTaskCap"/>)
    /// exactly as the footer add does, so a full tablet refuses with the same notice. No-op unless the editor
    /// is live.</summary>
    private void ApplyHandbookAppend(ScribeAddKind kind, string itemCode)
    {
        if (scratch is null || !isEditorMode) return;
        if (kind.CountsAgainstTaskCap && !CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }
        if (!kind.Add(scratch, itemCode)) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // NOTE (feedback 6.4, round 2): we do NOT auto-focus the new Tracker's stepper. The add originates
        // from a click inside the Handbook window, and VS keeps real keyboard focus on the window last
        // clicked — so arming the stepper's caret only PAINTED a caret here while typing still went to the
        // Handbook, misleading the player. Cross-window focus hand-off isn't worth forcing; the player taps
        // the stepper themselves. (The ScribeNumericField autoFocus seam remains for in-dialog use.)
        // Persist immediately (Case A appends + flushes at once); the autosave tick would otherwise carry it
        // within ~1s, but the player clicked in the Handbook and expects the task to exist right away.
        FlushIfDirty();
        // Reconcile the editor list so the new row appears. This is the live path when the append arrives
        // while ALREADY editing (Case A): the body is mounted, so the in-place reconcile shows the new row at
        // once. In the deferred Case B (append stashed, then applied from EnterEditorMode) this is a harmless
        // no-op — that path applies the append BEFORE its ForceRebuild, which then builds the whole tree from
        // the now-mutated scratch, so the row is present regardless of whether this reconcile resolved.
        RebuildBody();
    }

    /// <summary>Append a guide-page Link block to the live scratch document and flush it through the dialog's
    /// existing save path (add-tracker-link-tasks 7.6) — the guide-page sibling of
    /// <see cref="ApplyHandbookAppend"/>. A Link never counts against the task cap
    /// (<see cref="ScribeAddKind.CountsAgainstTaskCap"/> is false for <see cref="ScribeAddKinds.Link"/>), so
    /// unlike the item path there is no cap gate here. No-op unless the editor is live.</summary>
    private void ApplyGuideLinkAppend(string pageCode, string title)
    {
        if (scratch is null || !isEditorMode) return;
        if (!scratch.AddGuideLink(pageCode, title)) return;
        isDirty = true;
        SyncFocusNodesToScratch();
        // Persist immediately (the player clicked in the Handbook and expects the task to exist right away);
        // the autosave tick would otherwise carry it within ~1s.
        FlushIfDirty();
        RebuildBody();
    }

    /// <summary>Consume a stashed Handbook append once editor access has landed (called at the end of
    /// <see cref="EnterEditorMode"/>'s normal path). One-shot: the stash is cleared before invoking so a
    /// failed append can't re-fire.</summary>
    private void FlushPendingHandbookAppend()
    {
        if (pendingHandbookAppend is not { } pending) return;
        pendingHandbookAppend = null;
        pending();
    }

    /// <summary>The footer add-picker guide action for an item-bound kind (add-tracker-link-tasks 3.7). A
    /// Tracker/Link can't be created from a bare footer click — it needs a target item code that only a
    /// Handbook page's "Add to Scribe" link supplies — so instead of adding a row we GUIDE the player there:
    /// <list type="bullet">
    /// <item>Handbook <b>closed</b> → open the Handbook overview with the search box already focused (via the
    /// survival mod's <c>"handbooksearch"</c> link protocol), so the player can immediately type the item
    /// they want to track/link. Speed-to-entry is the priority (2026-08-15 feedback): opening our explainer
    /// entry instead dead-ended the player, who then had to navigate back to search anyway. The explainer
    /// stays discoverable via cross-links from the other top-level Scribe guides, not on this add path.</item>
    /// <item>Handbook <b>open</b> (already on some item's page) → a transient error telling them to scroll to
    /// the bottom of the current entry and click "Add to Scribe".</item>
    /// </list>
    /// Reuses the reflection-free handbook discovery/open pattern from
    /// <see cref="ToggleEditorReferenceHandbook"/> (scan <c>OpenedGuis</c> by <c>ToggleKeyCombinationCode</c>,
    /// open via a registered link protocol); both paths degrade to a safe no-op when the survival mod's
    /// handbook isn't loaded.</summary>
    private void DispatchItemKindGuide(ScribeAddKind kind)
    {
        GuiDialog? openHandbook = capi.Gui.OpenedGuis
            .FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook");

        if (openHandbook != null)
        {
            capi.TriggerIngameError(this, "scribe-additem-guide", Lang.Get("scribe:scribe-gui-additem-guide"));
            return;
        }

        // "handbooksearch://<text>" opens the overview and focuses the search field (empty text = ready to
        // type). initOverviewGui focuses "searchField" on build, so the caret lands in the box on open.
        if (capi.LinkProtocols.TryGetValue("handbooksearch", out var search))
            search(new LinkTextComponent("handbooksearch://"));
    }
}
