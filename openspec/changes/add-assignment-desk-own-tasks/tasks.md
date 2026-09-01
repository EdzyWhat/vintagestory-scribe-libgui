## 1. Nav column: Read/Editor tabs, reorder, drop Pinned

- [x] 1.1 In `GuiDialogScribeAssignmentDesk.BuildRightColNav`, add `readBtn`/`editorBtn` `TitleButton`s
      wired to the base class's existing Read/Editor entry points (mirror how `assignmentBtn`/
      `sentHistoryBtn`/`inboxBtn` are already built and wired to their `OnClickSwitchTo*` handlers — add
      analogous `OnClickSwitchToRead`/`OnClickSwitchToEditor` handlers if the base doesn't already expose
      ones this dialog can call directly).
      Wired directly to the base's `EnterReadMode()` (already `public`) and `TryEnterEditor()` (changed
      from `private` to `protected` in `ScribeDialogBase.ViewSwitching.cs` so this subclass can call it) —
      no new handler methods needed. Added `IsReadView`/`IsEditorView`/`EditLockedByOther` protected
      accessors to `ScribeDialogBase.cs` so this dialog's nav button can match the base's own active-color/
      dimming styling without touching the private `viewMode` field directly.
- [x] 1.2 Reorder the returned `Column`'s `children` array to
      `{ assignmentBtn, sentHistoryBtn, inboxBtn, readBtn, editorBtn, settingsBtn }`.
- [x] 1.3 Confirm `DefaultToAssignmentView()` (ctor) and `EnterGrantedView()`'s override still land on
      Create Assignments by default and never force-switch away from Read/Editor/Sent-History/Inbox once
      one of them is active — this class already has the "no Read view to land on" comment that no longer
      applies; update it to reflect that Read now exists but Create Assignments stays the intentional
      default.
      Updated the class remarks and `EnterGrantedView()`'s doc-comment; behavior unchanged (`EnterGrantedView`
      still just tears down an editor session if active and rebuilds in place, leaving whichever tab —
      including the new Read/Editor — was already selected).
- [x] 1.4 Confirm `EditorAccessIsAsync => true` (already set) correctly gates the new Editor button through
      the same server-lock round-trip every other writing station uses — no new lock code expected, just
      verify the existing override is sufficient now that a button actually reaches it.
      Confirmed by inspection: `TryEnterEditor()` → `RequestEditorAccess()` → server round-trip →
      `EnterEditorMode(bytes)` is entirely base-class machinery already exercised by every other
      `EditorAccessIsAsync => true` surface (Lectern/Scriptorium/Chalkboard); this dialog's override was
      already set, just previously unreachable with no button wired to it.
- [x] 1.5 Do NOT add a Pinned nav button or wire `BuildPinnedContent`/Pin Tab plumbing for this dialog.
      Confirmed: no Pinned button added; `children` array has exactly the six tabs above.

## 2. Desk's own document as an alternate task source

- [x] 2.1 Add `private bool deskSourceActive;` to `GuiDialogScribeAssignmentDesk`, reset to `false` in
      `OnGuiClosed` (alongside the existing `stampActive = false;`/slot-controller teardown) — never
      persisted, matching `deleteFromSource`'s existing session-only lifecycle.
- [x] 2.2 In `BuildAssignmentContent`, extract the existing slot-item row-resolution block (the
      `stagedBlocksCache`/`stagedRowsCache` assignment from `slot.Itemstack`) into a resolvable "active
      source" decision: if the slot holds an item, resolve from it (unchanged); else if `deskSourceActive`
      and the Desk's own document (`host.Document`, same accessor `BuildReadContent` already uses) has
      ≥1 eligible row (same empty-Task-text filter as the existing `.Where(r => !r.IsTask || ...)`), resolve
      `stagedBlocksCache`/`stagedRowsCache` from it instead; else both stay empty.
      Added an `EligibleDeskBlocks()` helper (the same filter, applied to `host.Document.Blocks`) shared by
      the source resolution and the `canPullFromDesk` computation below.
- [x] 2.3 Compute `bool canPullFromDesk` each build: true only when the slot is empty, `deskSourceActive`
      is false, and the Desk's own document has ≥1 eligible row per the same filter.
- [x] 2.4 Add `private void OnPullFromDesk() { deskSourceActive = true; RebuildBody(); }` (mirrors
      `OnToggleDeleteFromSource`'s shape).
- [x] 2.5 Verify `selectedTaskIds.IntersectWith(stagedRowsCache.Select(r => r.TaskId));` (existing line)
      still correctly prunes stale selections when the active source switches between slot-item and
      Desk-own — no change expected, just confirm by inspection/test.
      Confirmed by inspection: this line is source-agnostic (D5) — it prunes against whatever
      `stagedRowsCache` holds regardless of where those rows came from, so a source switch (slot ↔ Desk)
      is handled identically to the pre-existing slot-item-swap case with no changes needed.

## 3. Empty-state "pull from Desk" button

- [x] 3.1 Add `bool CanPullFromDesk` and `Action OnPullFromDesk` to `ScribeAssignmentFormContent`'s ctor/
      properties, passed through from `BuildAssignmentContent`'s new `canPullFromDesk`/`OnPullFromDesk`
      (task 2.3/2.4).
- [x] 3.2 Thread both further into `ScribeAssignmentStageContent` (currently receives `Rows`/
      `SelectedTaskIds`/etc.) as two more ctor params.
- [x] 3.3 In `ScribeAssignmentStageContent.Build`'s `Rows.Count == 0` branch, replace the bare
      `Center(Text(...))` with a `Center(Column(...))` containing the existing hint `Text` plus a `Button`
      (shown only when `CanPullFromDesk`) below it, wired to `OnPullFromDesk`.
- [x] 3.4 Add a new lang key for the button's label (e.g. `scribe-assignment-pull-from-desk`) to
      `assets/scribe/lang/en.json` — short, action-oriented copy consistent with `scribe-assignment-send`'s
      style.
      Added `"scribe-assignment-pull-from-desk": "Pull tasks from this Desk"`.

## 4. Wire protocol + server-side delete-from-source symmetry

- [x] 4.1 Add `public bool SourceIsDeskDocument { get; set; }` to `ScribeSendAssignmentBatchMessage`,
      documented alongside the existing `StagingSlot`/`DeleteFromSource` fields.
- [x] 4.2 In `GuiDialogScribeAssignmentDesk.OnSendAssignmentBatch`, set the new field from whichever source
      was active for this build (mirrors how `stagedBlocksCache` already reflects the active source per
      task 2.2 — no separate tracking needed if `OnSendAssignmentBatch` reads the same active-source state).
      Added a `sourceIsDeskDocument` field, set alongside `stagedBlocksCache` in `BuildAssignmentContent`
      (`!slotHasItem && deskSourceActive`), and read (not re-derived) by `OnSendAssignmentBatch`.
- [x] 4.3 In `ScribeModSystem.Assignment.cs`'s `OnServerReceivedSendAssignmentBatch`, branch on
      `message.SourceIsDeskDocument`: call the existing `TryRemoveStagedRows` (slot ItemStack path,
      unchanged) when false, or a new `TryRemoveDeskOwnRows` when true.
- [x] 4.4 Implement `TryRemoveDeskOwnRows(int x, int y, int z, List<Guid> sourceTaskIdsToRemove)`: resolve
      the `BlockEntityAssignmentDesk` at the position, read its own document via the same accessor the
      writing-station base's editor-save path uses, filter out matched TaskIds (mirror
      `TryRemoveStagedRows`'s `doc.Blocks.Where(...)`/`ReplaceBlocks`/persist-and-sync shape), and no-op
      silently if the block/document can no longer be resolved or nothing matched — same best-effort
      semantics as the existing method's doc-comment.
      Implemented by reusing `BlockEntityScribeWritingStation.DeleteTaskFromReader(Guid)` per TaskId — an
      existing lock-free mutate-and-persist-and-sync method (the same one the Delete completion policy
      already calls), rather than a new bespoke doc.Blocks/ReplaceBlocks path: it already no-ops per-id on
      an unknown TaskId, giving the same best-effort semantics as `TryRemoveStagedRows` for free.

## 5. Tests

- [ ] 5.1 Manual test: with nothing in the staging slot and no tasks on the Desk's own document, confirm
      the Create Assignments tab's empty state shows only the existing hint text, no button.
- [ ] 5.2 Manual test: author a task on the Desk's own document via the new Editor tab, switch to Create
      Assignments — confirm the pull-from-Desk button now appears below the hint; click it and confirm the
      task list populates with that task, selectable and sendable exactly like a staged item's task.
- [ ] 5.3 Manual test: with the Desk source active and populated, place an item in the staging slot —
      confirm the list immediately switches to the item's tasks. Remove the item — confirm the Desk's tasks
      reappear without re-clicking the button.
- [ ] 5.4 Manual test: with the Desk source active, switch to the Editor tab and add/delete a task, then
      switch back to Create Assignments — confirm the list reflects the edit without re-clicking the button.
- [ ] 5.5 Manual test: select a parent task (with subtasks) pulled from the Desk's own document — confirm
      the same parent-selects-subtasks-once cascade as the staged-item flow.
- [ ] 5.6 Manual test: with "Delete from source on send" enabled and the Desk source active, send a batch
      — confirm the sent rows are removed from the Desk's own document (visible on the Read/Editor tab)
      and the assignments arrive normally in the recipient's Inbox.
- [ ] 5.7 Manual test: confirm the Assignment Desk's Read tab shows checkbox/pin/delete/reorder/Tracker
      affordances identical to a Notebook's Read tab, and the Editor tab requires the same server-lock
      round-trip (e.g. observe the same brief "acquiring lock" behavior another shared block shows).
- [ ] 5.8 Manual test: confirm no Pinned nav button appears on the Assignment Desk.
- [ ] 5.9 Restage both client and server builds together before testing (wire-message shape change, design.md
      Migration Plan) — a mismatched pair will misread `ScribeSendAssignmentBatchMessage`.
