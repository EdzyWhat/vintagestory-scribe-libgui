## Context

The editor view renders from a private `scratch` `ScribeDocument` (`ScribeDialogBase.cs`), seeded once at
`EnterEditorMode` (`ScribeDialogBase.ViewSwitching.cs:52`) and treated as the source of truth for content
being edited. External changes to the live document arrive on the client through the block-entity resync
(`MarkDirty(redrawOnClient:true)` → `FromTreeAttributes` → `dialog.RefreshReadView()`).

Today `RefreshReadView`, in editor mode, deliberately does **not** merge external content into scratch —
it only *deletes* scratch tasks the server dropped, and even then skips the focused row and empty tasks
(`ScribeDialogBase.ViewSwitching.cs:341-401`). A done-state change or a `Sink` reorder is therefore never
propagated. Two consequences (confirmed by tracing the HUD-completion path):

1. The open editor's checkbox stays stale under `Keep`/`Sink` (the task survives, so the delete branch
   never runs).
2. The editor's next autosave flush (`FlushIfDirty` → server `ApplyEdit` whole-document replace,
   `BlockEntityScribeLectern.cs:353-358`) rewrites the live document from the stale scratch, silently
   reverting the external completion and any sink reorder — a last-write-wins data loss.

The completion itself is applied server-side lock-free via `SetTaskDoneFromReader` /
`MoveTaskToBottomFromReader` (`BlockEntityScribeLectern.cs:382-455`), regardless of who holds the edit
lock, so the live document is authoritative and correct; only the open editor's scratch is out of step.

The sibling surfaces are already correct: Read reads the live document; Pinned reads the pin cache; both
resync their reused rows through an `UpdateWidget` done-resync (`ScribeReadRowState`, `ScribePinRowState`).
The editor is the last surface, and the only one that also silently reverts.

## Goals / Non-Goals

**Goals:**
- An external completion (done-state) on a task that still exists in scratch is reflected in the open
  editor without reopening it.
- The `Sink`/`UnpinSink` move-to-bottom is applied live in the open editor, matching Read/Pinned.
- The subsequent autosave flush carries the external completion instead of reverting it (close the
  lost-update window for the completion case).
- All of the above without overwriting a row's in-progress unsaved text, and preserving the
  actively-edited row's caret and cross-row focus.

**Non-Goals:**
- No change to `src/Core/` policy semantics — the shared Core completion-policy function already defines
  what `Keep`/`Sink`/`Delete`/`Unpin`/`UnpinSink` do; this change applies that same result into scratch.
- No new packet, no new lock, no change to the server-authoritative flow.
- Not attempting to resolve the general concurrent whole-document text-edit race (two players editing the
  same document) — that remains the documented last-write-wins and is out of scope.
- Not merging external **text** edits into scratch mid-edit — scratch remains the source of truth for
  text being edited; only done-state and the completion-driven reorder are propagated.

## Decisions

**D1 — Propagate completion in `RefreshReadView`'s editor-mode branch, next to the existing delete pass.**
The handler already walks the authoritative server task list against scratch to delete dropped tasks. Extend
the same pass: for a task present in both, if `serverBlock.Done != scratchBlock.Done`, set the scratch
block's done-state to the server value; if the policy moved it to the bottom and its scratch index differs,
apply the move via the existing `ReorderEditorBlock` (the same call local sink uses), so the reconcile path
and its focus/caret/scroll guarantees are reused verbatim. *Alternative considered:* a dedicated new inbound
"completion" packet distinct from the block-entity resync — rejected as redundant; the resync already
carries the authoritative document and fires `RefreshReadView`.

**D2 — Reuse the reconcile-with-stable-identity machinery for the live reorder rather than a ForceRebuild.**
`ReorderEditorBlock` already preserves caret, in-progress text, focus, and scroll (spec: "The editor updates
structural mutations by reconcile with stable identity"). Driving the external reorder through it means the
external-sink path inherits those guarantees for free. *Alternative:* re-seed scratch wholesale from the
server document — rejected: it would clobber in-progress text and reset carets, the exact thing the current
guard protects against.

**D3 — Add the render-half `UpdateWidget` done-resync to `ScribeEditRowState`, mirroring the siblings.**
Once D1 flips `scratch`'s done-state, the reused (TaskId-keyed) editor row must repaint its checkbox. This is
the same one-line gated resync `ScribeReadRowState`/`ScribePinRowState` carry: on reconcile, if
`oldWidget.Data.Done != Widget.Data.Done` then re-seed the row's optimistic `done`. The gate ensures a pure
chrome reconcile never stomps an in-flight local optimistic tick. (This was prototyped and reverted earlier
in isolation because, without D1, scratch's done never changed and the resync was inert — it is correct only
as the render half of D1.)

**D4 — The lost-update fix is a consequence of D1, not separate work.** Once scratch's done-state and order
match the live document, `FlushIfDirty`'s whole-document `ApplyEdit` serializes the corrected scratch, so the
flush no longer reverts the completion. No lock or merge-on-server change is needed.

**D5 — Keep the existing in-flight/focused/empty-row guards intact.** The done-sync and reorder apply only to
tasks that exist in both scratch and the server snapshot; a legitimately-local not-yet-persisted row is still
never pruned, and the focused/empty-row delete guard is unchanged. Done-state is orthogonal to text, so
syncing done even on the focused row is safe (it does not touch that row's text or caret).

## Risks / Trade-offs

- **[A reorder moves a row under an active caret mid-edit]** → The user explicitly chose live reorder over
  deferring it. `ReorderEditorBlock` preserves the edited row's caret/text/focus by identity, so the moved
  row (or the edited row, if it is the one moved) keeps its editing state; only its on-screen position
  changes. Verify in-game that editing the very row being sunk keeps caret/text.
- **[Double-application: local optimistic completion + inbound resync of the same completion]** → The done
  sync is gated on an actual value difference and `ReorderEditorBlock` treats a move-to-same-index as a
  no-op, so re-applying an already-applied completion is inert. Confirm no visible jump when the editor's
  own completion round-trips back as a resync.
- **[Empty/focused-row skip in the delete branch interacting with the done branch]** → The done/reorder
  branch is for surviving tasks only and is independent of the delete-skip; ensure the two branches don't
  double-handle a task in one pass.
- **[Text-edit race still exists]** → Explicitly out of scope; behavior for concurrent text edits by two
  players is unchanged (documented last-write-wins).

## Migration Plan

Pure client-side view/merge behavior; no persistence format, packet, or Core change, so no data migration
and no save-compat concern. Rollback is reverting the `RefreshReadView` editor-branch extension and the
`ScribeEditRowState.UpdateWidget` resync. Ship behind the normal build; validated by the in-game parity
checklist against Read/Pinned.

## Open Questions

- None blocking. In-game verification will confirm the caret/focus behavior when the row being edited is the
  one an external `Sink` moves to the bottom.
