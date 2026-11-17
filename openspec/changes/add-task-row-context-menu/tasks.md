## 1. Gating and menu widget

- [x] 1.1 Add `ScribeRowMenuEntries.For(view, readOnly, pinLive, isPinned)` in `src/Core/` (every block kind gets the same entries, so there is no kind parameter) returning the entry list per the design's gating table; unit-test the matrix (read editable, read-only Tablet, editor, Pin Tab, pinned and unpinned) in `tests/Core.Tests/`
- [x] 1.2 Add `ScribeRowContextMenu` on the `ScribeAddKindPicker` overlay pattern: text tiles with hover highlight, `CurrentShade` re-applied, cuneiform labels on the Tablet, left-click-only choose, barrier closing on any press, placement below the row at pointer x with flip-above and shift-left at the dialog edges; leave room for a future toggle entry kind that does not close on choose
- [ ] 1.3 Add the single per-dialog owner in `ScribeDialogBase` (`openRowMenu`): opening closes any open menu; close on view switch, dialog close, row scrolled out of view, and `TaskId` no longer shown; re-anchor across rebuilds that keep the row; verify in-game that an illumination change and an unrelated resync keep it open
- [ ] 1.4 Intercept Escape in `ScribeDialogBase.OnKeyDown` before the focused field and `base` when a menu is open; verify Escape closes only the menu, including with an editor field focused
- [x] 1.5 Add lang keys for Edit, Pin, Unpin and Delete entry labels to `en.json` and the 12 other locales; run `build/check-locales.py`

## 2. Right-click routing

- [ ] 2.1 Make every row child that handles presses act only on the left button: checkbox, link and tracker names, collapse caret, hover Pin/Unpin/Delete buttons, read-only tablet text detector; verify a right-click on each does nothing by itself
- [ ] 2.2 Make `ScribeMultilineField.OnFieldPress` return early on a right press (no caret move, no selection change, no drag) and exempt that blur from the empty-row purge; verify on a row with text selected and on an empty new row
- [x] 2.3 Add `onSecondaryPress` to read, editor and Pin Tab rows (a right-button-only detector on the row background plus the forwarded child presses); null when the row has no menu

## 3. Wire the rows

- [ ] 3.1 Read rows: thread `onSecondaryPress`, an `onDelete(TaskId)` and an `onEdit(TaskId)` through `ScribeReadContent` and `ScribeDialogBase.BuildReadContent`; add `enableRowMenu` (false from `GuiDialogTaskNotice`); verify on a Notebook, a hard or fired Tablet, and a Task Notice
- [x] 3.2 Read-view Delete: add `RespectEditorLock` (ProtoMember 4) to `ScribeDeleteTaskMessage`; the client checks `host.IsLockedByOther` first, and the server refuses in `DeleteTaskForPlayer` with `scribe:scribe-gui-locked` when set and locked; add an Atlas test for the refused and allowed cases, and confirm the Pin Tab delete is unchanged
- [ ] 3.3 Read-view Edit: add `pendingEditFocusTaskId` modelled on `pendingHandbookAppend`, consumed in `EnterEditorMode` (TaskId to scratch index, `autoFocusRowOnRebuild`) and cleared on refusal, close or missing task; item rows scroll into view without a caret; verify the locked feedback and that a later nav-button entry focuses nothing
- [ ] 3.4 Editor rows: Pin/Unpin and Delete resolve `TaskId` to the current index when chosen; Pin sets the explicit current state; verify no Edit entry appears
- [ ] 3.5 Pin Tab rows: Unpin and Delete wired to the existing identity-addressed callbacks; verify the server messages match the hover controls'
- [x] 3.6 Update the Editor Features handbook text (`craftinginfo-scribe-editor-reference-text`) to mention right-click

## 4. Verification

- [x] 4.1 Run `build/verify.sh` with the client closed and verify Core and Atlas are green
- [x] 4.2 Add playtest items to `TESTING.md`: the menu in each dialog and view from the proposal's table; a note row; a hard or fired Tablet (Pin only); read-view Delete with and without another player holding the lock; right-click on each row control doing nothing else; outside-click, Escape and view-switch closing; the menu near the bottom and right edges; hover controls still working; ctrl-click and two-finger tap on macOS
