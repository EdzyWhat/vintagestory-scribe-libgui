All GUI work is in `src/Mod/GuiDialogScribeLecternLibGui.cs` (abbrev. **GuiDialog**) unless noted.
Line numbers are from the research pass and may drift — locate by symbol.

## 1. Divider above every view's scroll area

- [x] 1.1 Add `new Divider()` (`Gui.Widgets.Basic.Divider`, already imported) as the FIRST child of the
  read view's outer `Column` (before `Expanded(rowList)`), in `ScribeLecternReadContentState.Build`
  (~GuiDialog:1659).
- [x] 1.2 Same for the editor view's outer `Column` (before `Expanded(scrollBody)`), in the editor
  content Build (~GuiDialog:2062).
- [x] 1.3 Same for the pinned view — but coordinate with §3 (the picker also moves to the header): the
  pinned outer `Column` becomes `[ policyPicker, Divider, Expanded(scrollBody) ]` (~GuiDialog:2485).
- [x] 1.4 Confirm the divider inherits the theme border color in both Pixel-Art (light parchment) and
  global-theme modes and doesn't visually fight the notebook frame.

## 2. Pin toggle on read-view rows

- [x] 2.1 Thread an `Action<Guid> onTogglePinned` into `ScribeLecternReadContent` (ctor ~GuiDialog:1581)
  and `ScribeReadRow` (~GuiDialog:1690), alongside the existing `onToggleTask`.
- [x] 2.2 In `ScribeReadRowState.Build` (~GuiDialog:1706), render a `ScribeRowButton("scribepin")` for
  task rows only (guard on `Widget.Data.IsTask`), driven by `Widget.Data.Pinned` for its resting/active
  glyph state — mirroring the editor row's pin control. Text-section rows render no pin control.
- [x] 2.3 Wire the read row's pin toggle to a dialog handler that calls
  `SendSetPin(taskId, !IsPinnedForMe(taskId))` (reuse GuiDialog:729 / `ScribeSetPinMessage`); pass it in
  where `BuildReadContent` constructs `ScribeLecternReadContent` (~GuiDialog:1302).
- [x] 2.4 Confirm the read-view pin control's resting tint matches the editor's pinned-row indicator so a
  task reads as pinned identically across a read⇄editor switch.

## 3. Pinned view: policy picker above the list

- [x] 3.1 In the pinned content Build (~GuiDialog:2485), reorder the outer `Column` from
  `[ Expanded(scrollBody), policyPicker ]` to `[ policyPicker, <Divider from §1.3>, Expanded(scrollBody) ]`
  so the existing `policyPicker` (~GuiDialog:2465) is the header. Keep `Expanded` on the scroll body so it
  fills the remaining height.
- [x] 3.2 Confirm the picker still edits the same per-player preference via `onCompletionPolicyChanged`
  (wired at ~GuiDialog:1407 to `UpdateMySettings(s => s.CompletionPolicy = p)`) and that the settings
  window reflects the same value.

## 4. Real document "Sink" + uniform policy (scope expanded — see design.md Decision 1)

Implementation found that "Sink" never reordered the document (HUD-only display sort) and the policy
only applied to PINNED tasks. Author chose to make Sink a real, all-tasks document reorder.

- [x] 4.1 **Core:** add `ScribeDocument.MoveTaskToBottom(Guid taskId)` — moves the task with that id
  to the end of the block list, preserving the order of the rest; returns false for an unknown id or a
  non-task block. Add Core unit tests (moves to end; no-op-safe when already last; false on unknown id).
- [x] 4.2 **BlockEntity:** add a lock-free `MoveTaskToBottomFromReader(Guid taskId)` on
  `BlockEntityScribeLectern`, mirroring `SetTaskDoneFromReader`/`DeleteTaskFromReader` (calls the Core op,
  `MarkDirty(redrawOnClient:true)`, returns whether it moved).
- [x] 4.3 **Server op:** in `ScribeModSystem.CompleteTaskForPlayer`, on a transition into done under
  `Sink` policy, move the task to the document bottom at the source (best-effort if resolvable). Do the
  SAME in `CompleteUnpinnedTaskAtSource` so an UNPINNED task also sinks/deletes under the policy — the
  policy now applies to all tasks, not just pinned ones. Delete for an unpinned task: delete the source
  task. Unpin stays a no-op for an unpinned task (nothing to unpin).
- [x] 4.4 **Editor GUI:** change the editor row checkbox from the index-addressed `ToggleEditorTask`
  scratch toggle (GuiDialog:627) to the identity path — send `ScribeCompleteTaskMessage` with the
  player's policy by `TaskId` (as `OnReadViewCompleteTask` does), so the editor matches Read/Pinned/HUD.
- [x] 4.5 **Editor reconciliation — approach taken: enact-in-scratch (design Decision 2's blessed
  fallback), NOT a lock-free message + fold.** The editor holds the edit lock and autosaves the WHOLE
  scratch authoritatively, so a lock-free `ScribeCompleteTaskMessage` would be clobbered by the next
  whole-doc flush. Instead `ToggleEditorTask` enacts the policy directly in `scratch` by index (toggle
  done; Delete → `DeleteEditorBlock`; Sink → `ReorderEditorBlock(index, last)`), which reuses those
  paths' existing rebuild + focus/caret fix-up and preserves other rows' unsaved text. The flush's
  server-side `ReconcileActorPins` then carries the pin snapshot and drops a pin whose task was deleted
  for free; only `Unpin` (task survives) is sent explicitly via `SendSetPin(taskId, false)`. Result
  matches read/pinned/HUD end-state. (Editor isolation note: `add-pinned-task-hud 80777b7b`.)
- [x] 4.6 Run `dotnet test tests/Core.Tests/Core.Tests.csproj` (new MoveTaskToBottom tests green). 132/132.

## 5. Build, restage, verify

- [x] 5.1 `dotnet build src/Mod/Mod.csproj --nologo` clean; Core suite green. (0 warn/0 err; 132/132.)
- [x] 5.2 Restage (`bash build/restage.sh Debug`) — done (18 files staged). Fully relaunch the client
  before testing the in-game items below.
- [x] 5.3 In-game: a divider shows directly above the scroll list in all three views (read, editor,
  pinned), in both Pixel-Art and global-theme modes.
- [x] 5.4 In-game: pin/unpin a task from the READ view; it gets the pinned indicator in read AND editor,
  appears on the HUD, and persists across relog. Text sections show no pin control.
- [x] 5.5 In-game: the pinned view's policy picker sits ABOVE the list; changing it updates the settings
  window's completion policy too.
- [x] 5.6 In-game: set each policy (Keep/Sink/Unpin/Delete) and complete a task from EACH view (read,
  editor, pinned) — confirm the SAME outcome in every view (sink→bottom / unpin / delete), matching the
  HUD. Specifically confirm the editor case: ticking one row's box applies the policy AND leaves other
  rows' in-progress unsaved text + caret intact.
- [ ] 5.7 In-game: confirm Sink reordering from one view is reflected in the shared document for another
  viewer, and that Delete from the read/editor view removes the task for everyone (the accepted uniform
  behavior).
- [x] 5.8 Update `TESTING.md` with the new in-game items. (Added the `scribe-lectern-view-consistency`
  section: divider, pin-from-read, picker-above-list, uniform-policy, shared Sink/Delete.)
