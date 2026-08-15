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
    /// when a "Add to Scribe" click lands while the dialog is NOT editing, then consumed by
    /// <see cref="FlushPendingHandbookAppend"/> at the end of <see cref="EnterEditorMode"/> once editor
    /// access is granted (immediately for item surfaces, or on the async server grant for block surfaces).
    /// Null when nothing is pending.</summary>
    private (ScribeAddKind Kind, string ItemCode)? pendingHandbookAppend;

    /// <summary>True when this surface's editor access requires a server round-trip (a block surface —
    /// Lectern/Scriptorium — whose <see cref="RequestEditorAccess"/> sends a lock request and lands the grant
    /// asynchronously in <see cref="EnterEditorMode"/>). Item surfaces (Notebook/Clockmaker/Tablet) override
    /// <see cref="RequestEditorAccess"/> to enter the editor synchronously, so they leave this false. Used by
    /// <see cref="TryAddFromHandbook"/> to decide whether a stashed append should be kept for the pending
    /// grant or discarded because access was refused outright.</summary>
    protected virtual bool EditorAccessIsAsync => false;

    /// <summary>Create a Tracker/Link task on THIS dialog from a Handbook "Add to Scribe" click, reusing the
    /// dialog's own save path (add-tracker-link-tasks 3.4). Two cases:
    /// <list type="bullet">
    /// <item><b>Already editing</b> — the scratch document is live, so append + flush immediately
    /// (<see cref="ApplyHandbookAppend"/>).</item>
    /// <item><b>Not editing</b> — stash the append and request editor access via <see cref="TryEnterEditor"/>.
    /// Item surfaces enter synchronously (the stash is consumed before this returns); block surfaces get an
    /// async grant, so the stash is kept for <see cref="EnterEditorMode"/> to consume. If access is refused
    /// (locked by another player, or the surface can't edit) the stale stash is cleared so a later editor
    /// entry doesn't silently apply it.</item>
    /// </list>
    /// The kind is one of the item-bound kinds (<see cref="ScribeAddKinds.Tracker"/> /
    /// <see cref="ScribeAddKinds.Link"/>); <paramref name="itemCode"/> is the collectible code the Handbook
    /// link supplied.</summary>
    internal void TryAddFromHandbook(ScribeAddKind kind, string itemCode)
    {
        if (isEditorMode)
        {
            ApplyHandbookAppend(kind, itemCode);
            return;
        }

        pendingHandbookAppend = (kind, itemCode);

        // A block surface will get its grant asynchronously (server lock round-trip) UNLESS the lock is held
        // by someone else — in which case TryEnterEditor surfaces the generic lock error and never requests.
        bool grantPending = EditorAccessIsAsync && !host.IsLockedByOther(capi.World.Player.PlayerUID);
        TryEnterEditor();

        // If we didn't synchronously enter editor mode AND no async grant is coming, the request was refused
        // (locked-by-other, or a read-only/non-editable surface): drop the stash so it can't be applied later.
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
        // Land the caret in the new row so the player can type right away (feedback 6.4). The block is
        // appended last, so target that index; the row MOUNTS on the rebuild below, firing its mount-only
        // autoFocus. Only a Tracker has a focusable stepper — a Link is just an icon + name, nothing to type.
        if (kind == ScribeAddKinds.Tracker) autoFocusRowOnRebuild = scratch.Blocks.Count - 1;
        // Persist immediately (Case A appends + flushes at once); the autosave tick would otherwise carry it
        // within ~1s, but the player clicked in the Handbook and expects the task to exist right away.
        FlushIfDirty();
        // Reconcile the editor list so the new row appears. A no-op if the body hasn't mounted yet (the
        // deferred-append path runs right after EnterEditorMode's ForceRebuild, which rebuilds from the now
        // mutated scratch on its own next frame regardless).
        RebuildBody();
    }

    /// <summary>Consume a stashed Handbook append once editor access has landed (called at the end of
    /// <see cref="EnterEditorMode"/>'s normal path). One-shot: the stash is cleared before applying so a
    /// failed append can't re-fire.</summary>
    private void FlushPendingHandbookAppend()
    {
        if (pendingHandbookAppend is not { } pending) return;
        pendingHandbookAppend = null;
        ApplyHandbookAppend(pending.Kind, pending.ItemCode);
    }

    /// <summary>The footer add-picker guide action for an item-bound kind (add-tracker-link-tasks 3.7). A
    /// Tracker/Link can't be created from a bare footer click — it needs a target item code that only a
    /// Handbook page's "Add to Scribe" link supplies — so instead of adding a row we GUIDE the player there:
    /// <list type="bullet">
    /// <item>Handbook <b>closed</b> → open the task-types explainer entry, which describes Tracker/Link and
    /// points at the per-item link.</item>
    /// <item>Handbook <b>open</b> (already on some item's page) → a transient error telling them to scroll to
    /// the bottom of the current entry and click "Add to Scribe".</item>
    /// </list>
    /// Reuses the reflection-free handbook discovery/open pattern from
    /// <see cref="ToggleEditorReferenceHandbook"/> (scan <c>OpenedGuis</c> by <c>ToggleKeyCombinationCode</c>,
    /// open via the registered <c>"handbook"</c> link protocol); both paths degrade to a safe no-op when the
    /// survival mod's handbook isn't loaded.</summary>
    private void DispatchItemKindGuide(ScribeAddKind kind)
    {
        GuiDialog? openHandbook = capi.Gui.OpenedGuis
            .FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook");

        if (openHandbook != null)
        {
            capi.TriggerIngameError(this, "scribe-additem-guide", Lang.Get("scribe:scribe-gui-additem-guide"));
            return;
        }

        if (capi.LinkProtocols.TryGetValue("handbook", out var open))
            open(new LinkTextComponent("handbook://craftinginfo-scribe-task-types"));
    }
}
