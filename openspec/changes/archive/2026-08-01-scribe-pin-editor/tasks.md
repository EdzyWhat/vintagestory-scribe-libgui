## 1. Core: pure-data pin-edit convenience (no VS API)

- [x] 1.1 Add an optional `ScribeDocument.SetTaskText(Guid taskId, string text)` over the existing
      `FindByTaskId` + `SetBlockText`, honoring the blank/whitespace-only rejection invariant and
      reporting success/failure. Pure data — no VS API reference.
- [x] 1.2 (Optional) Add a Core reorder helper only if the store's permute cannot be expressed cleanly
      server-side; keep it pure-data if added. Skip if `ScribePinStore` handles reorder directly.
      (SKIPPED — `ScribePinStore.ReorderPins` handles the permute directly server-side.)
- [x] 1.3 Add `Core.Tests` coverage for `SetTaskText`: edits by present id, rejects blank/whitespace,
      no-op on absent id, leaves other blocks untouched. Run
      `dotnet test tests/Core.Tests/Core.Tests.csproj`.

## 2. BlockEntity: lock-free set-text-from-reader

- [x] 2.1 Add `SetTaskTextFromReader(Guid taskId, string text)` to `BlockEntityScribeLectern`, mirroring
      `SetTaskDoneFromReader` (BlockEntityScribeLectern.cs:268): lock-free, mutate the authoritative
      `Document` via the Core set-text path, `MarkDirty`. Return whether it wrote.
- [x] 2.2 Comment the same race caveat noted on the done-flag path (a concurrent whole-doc `ApplyEdit`
      under the edit lock can clobber this lock-free write; last write wins).

## 3. New messages + server handlers (mirror CompleteTaskForPlayer)

- [x] 3.1 Add `ScribeEditPinnedTaskMessage { DocId:byte[16], TaskId:byte[16], Text:string }` (C→S) next
      to the existing Scribe messages.
- [x] 3.2 Add `ScribeReorderPinsMessage` (C→S) carrying the new per-player pin order (ordered
      `(DocId, TaskId)` list, or a from/to permutation).
- [x] 3.3 (Optional) Add a standalone delete/unpin message, or extend the existing complete message with
      an action enum — whichever keeps the handler set smallest. (Added `ScribeDeleteTaskMessage`; unpin
      reuses the existing `ScribeSetPinMessage` with `Pinned=false`.)
- [x] 3.4 Register all new messages **appended** to the frozen registration order in
      `ScribeModSystem.Start` — never inserted mid-list.
- [x] 3.5 Edit-text handler: resolve via `TryResolveLectern(docId)`; if resolvable call
      `SetTaskTextFromReader`; always update the pin snapshot via `pinStore.SetPinText`
      and re-push. Degrade to snapshot-only when unresolvable (matches Delete-policy at
      ScribeModSystem.cs:476).
- [x] 3.6 Standalone delete handler: resolve; if resolvable call `DeleteTaskFromReader`; always
      `pinStore.RemovePin` and re-push. Safe no-op if the pin is already gone / source unloaded.
- [x] 3.7 Reorder handler: permute the actor's list in `ScribePinStore`, persist, re-push. Do NOT touch
      document block order.
- [x] 3.8 Validate all inbound payloads (bounded pin count, ids are 16 bytes, unknown ids ignored) so a
      malformed/hostile message cannot corrupt the store.

## 4. ScribePinStore: reorder + persist

- [x] 4.1 Add a reorder operation that permutes `_pins[uid]` into a client-supplied order, ignoring
      unknown/duplicate ids and preserving any pins the client omits.
- [x] 4.2 Persist the reordered list (already saved under `scribe:pins:v1` — no format change) and
      expose the op to the reorder handler.

## 5. Pin Tab view: rows sourced from MyPins (extend the editor rendering)

- [x] 5.1 Add a Pin Tab view mode to `GuiDialogScribeLecternLibGui`: promote the `bool isEditorMode` to a
      small view enum (or add a parallel flag) and add a `BuildPinnedContent()` branch in
      `BuildCentralRegion()`, a peer of the read/editor bodies. Reuse `sharedScrollController`.
- [x] 5.2 Build rows from `modSystem.MyPins` (all pins across documents, in pin-list order) by adapting the
      editor's `ScribeEditRow` rendering to a pin-sourced row-data record (from `ScribePinnedRef`), NOT the
      document's `ScribeEditRowData`. No max-row cap. Key each row `ValueKey<Guid>(TaskId)` so field State
      survives the `MyPinsChanged` rebuild (editor pattern).
- [x] 5.3 Per row (editable by default): complete (checkbox), directly-editable text
      (`ScribeMultilineField`), delete, unpin, and a reorder grip. Each action sends its identity-addressed
      message; NO undo/sink timer (applies immediately).
- [x] 5.4 Render the "on completing a task" `ScribeCompletionPolicy` picker on the tab, reading/writing the
      shared `ScribePlayerSettings.CompletionPolicy` via `UpdateMySettings` (same control the Settings
      window uses; one shared value).

## 6. Wire the Pin Tab into the nav + senders

- [x] 6.1 Wire the `scribepin` nav-button `onTap` (currently the stub at
      `GuiDialogScribeLecternLibGui.cs:1123-1124`) to switch to the Pin Tab view via a real entry method
      (mirroring `OnClickSwitchToRead` / `RequestEditorAccess`), not an inline flag flip.
      (`OnClickSwitchToPinned`.)
- [x] 6.2 Add the edit/delete/unpin/reorder client senders that emit the new messages, addressed by
      `(DocId, TaskId)` / pin order; complete reuses the existing `ScribeCompleteTaskMessage`.
- [x] 6.3 Add Pin Tab / row-action / policy-picker labels to `assets/scribe/lang/en.json` (the
      `scribe-gui-nav-pinned` nav tooltip already exists). (Added `scribe-gui-pintab-empty`; the
      policy picker reuses the existing `settings-completionpolicy` + `scribe-completion-*` keys.)
- [x] 6.4 Confirm no change to `HudScribePins` behavior — both surfaces read the same `MyPins`.
      (HudScribePins.cs untouched.)
- [x] 6.5 Confirm the Pin Tab is governed by the Lectern-dialog settings (`PixelArtDisplay`,
      `WindowFontScale`, `PixelArtSize`), not the HUD-prefixed settings. (Uses `RowStyle` =
      `ScribeRowStyle.FromSettings` and the dialog's `LecternLayout`/`ScribeTheme.For`; no HUD-prefixed
      settings referenced.)

## 7. In-game verification (pending — requires a running game; not executable in this environment)

- [x] 7.1 Open the Lectern; the `scribepin` nav button switches the central region to the Pin Tab, which
      lists all pins across documents with no row cap, back-and-forth with read/editor.
- [x] 7.2 Rows are editable by default: the text field, checkbox, delete, unpin, and reorder grip are all
      present and act on the right pin.
- [x] 7.3 Complete a task from the Pin Tab → applies immediately with NO undo delay; confirm the HUD
      still applies its undo window when used from the HUD.
- [x] 7.4 Edit a pin whose source Lectern is **loaded** → source doc text updates + persists (reopen to
      confirm); pin snapshot updates.
- [x] 7.5 Edit/delete a pin whose source is **unloaded** → pin snapshot/removal updates, no crash, source
      unchanged until loaded (accepted best-effort, matches Delete-policy today).
- [x] 7.6 Unpin removes only the pin (task survives); delete removes the task.
- [x] 7.7 Reorder pins → order persists per-player across relog (saved under `scribe:pins:v1`); confirm
      document block order is unchanged.
- [x] 7.8 Cross-check the corner HUD updates in lockstep with every Pin Tab action (same `MyPins`).
- [x] 7.9 Confirm blank/whitespace-only inline edit is rejected and leaves the task unchanged.
- [x] 7.10 Change the completion policy from the Pin Tab picker → the Settings window reflects the same
      value, it persists across relog, and it governs a Pin Tab check-off (sink/keep/unpin/delete).
- [x] 7.11 Confirm the Pin Tab follows the Lectern-dialog theme/size (`PixelArtDisplay`, `WindowFontScale`,
      `PixelArtSize`) — not the HUD settings — and that editing-row focus/caret survives a background
      pin resync (the `MyPinsChanged` rebuild).

## 8. Docs

- [x] 8.1 Append a LibGUI lesson to `VSAPI-NOTES.md` (`## LibGUI`): adding a new central-region view to the
      Lectern dialog (a peer of read/editor via the `BuildCentralRegion` view-mode switch), reusing the
      editor `ScribeEditRow` rendering from an alternate row-data source, and keying rows by
      `ValueKey<Guid>(TaskId)` so field State survives the `MyPinsChanged` `ForceRebuild`; plus in-game
      legibility verdicts. (Corrected the keying claim: `ForceRebuild` fully unmounts, so a write-through
      `pinEditBuffer` re-seed is what actually preserves in-progress text; in-game verdicts pending playtest.)
