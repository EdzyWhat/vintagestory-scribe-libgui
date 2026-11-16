# Review: add-task-row-context-menu

Four fresh-context reviewers (comprehension, spec critic, design critic, readiness) read only this change,
the main specs it touches, the project context and the repo. `openspec validate --strict` passed before
review. Code claims below were spot-checked against the repo and `reference/vslibgui` (4.0.0); none were
false.

## Paraphrase (comprehension reviewer, verbatim)

**What this change proposes.** Right-clicking a task row in a Scribe dialog's read view, editor view or Pin
Tab opens a small floating menu next to the row, with up to three entries. **Edit** appears in the read view
only and opens the editor focused on that row. **Pin/Unpin** and **Delete** each appear only where that
action is already allowed. The existing hover buttons stay. The menu closes when an entry is chosen, on an
outside click, on Escape, or when its row goes away, and only one menu is open at a time. It never opens on
Text note rows or the HUD, and if no entry is allowed it does not open at all. Under the hood, one shared
`ScribeRowContextMenu` overlay widget, copied from the New Task picker's barrier-plus-follower pattern, is
wired into the three row widgets, and each entry is meant to call the callback the row's hover control
already uses. There are no save-format, network or Core changes. This change is the first step of the
calendar roadmap: a later change adds date entries to the same menu.

**How the existing system works, per these files.** There are three row widgets. Read rows (`ScribeReadRow`,
inside `ScribeReadContent`) toggle a task's done state and its pin. Editor rows (`ScribeEditRow`) delete and
pin by row index inside the locked scratch copy of the document. Pin Tab rows (`ScribePinnedContent`)
complete, edit, delete and unpin by `(DocId, TaskId)`, without taking the editor lock. The dialog decides
what is allowed: `ReadOnly` and `CompletionAndPinLive`, the editor lock on block surfaces, and the Notebook
having no lock at all. `TryEnterEditor` shows a "locked" error when someone else holds the lock, and the
Handbook-append path (`pendingHandbookAppend`) already shows how to carry an intent across the async lock
grant. The Lectern access mode exists, but `Private` cannot be set by players, so every Lectern behaves as
`Public`.

**Mismatch with intent:** the paraphrase is accurate except that "each entry calls the callback the row's
hover control already uses" is impossible for read-view Edit and Delete. Read rows have neither control
(finding 1).

## Findings

| # | Severity | Where | Finding | Raised by |
|---|----------|-------|---------|-----------|
| 1 | Blocker | proposal What Changes; spec "offers Edit, Pin or Unpin, and Delete"; design Decisions | Read rows have no delete control or callback (`ScribeReadContent.cs:758-760`: "The read view exposes no edit/delete/drag"). Read-view Delete needs a new path. The lock-free `ScribeDeleteTaskMessage` would let any player delete from a shared block while another player holds its editor lock, which is a new permission and contradicts "no new permission logic". | all 4 |
| 2 | Blocker | design "Right-click is detected with the row's existing pointer handling"; Risks bullet 1 | No row has press handling. LibGUI's `GestureDetector` fires `OnTap`/`OnPress` for any button. `EventDispatcher.DispatchPointerDown` marks the event handled, moves focus to the innermost focusable element, and dispatches with `stopOnHandled`, so ancestors never see presses a child takes. As a result, right-clicking a checkbox toggles it, right-clicking a link opens the Handbook, and right-clicking over editor/Pin Tab text moves the caret and opens no menu. The Risks claim ("a press that starts a text selection is a left-click") is false. | design, readiness, spec, comprehension |
| 3 | Major | spec "closes predictably" (Escape) | Nothing intercepts Escape. The picker has no Escape handling, and Escape bubbles to the dialog and closes it (`ScribeMultilineField.cs:9`). | all 4 |
| 4 | Major | spec scenario "Read-only document hides editing entries"; tasks 2.1, 3.2 | A read-only Lectern can't be reached: lectern-access-mode says every lectern behaves as `Public`. The real read-only surface is the hard/fired Tablet (Pin/Unpin only). The lock-held Lectern case has no scenario. | all 4 |
| 5 | Major | spec scenario "Opening a second menu closes the first" | The picker's full-screen barrier is `GestureDetector(onTap: Close)` (`ScribeAddKindPicker.cs:153-157`). A second right-click hits the barrier, closes the first menu and never reaches the second row. | design, readiness |
| 6 | Major | spec "removed or rebuilt" vs design "rebuild that removes its row" | The two artifacts disagree. Rebuilds are frequent (illumination buckets, resyncs, ForceRebuild), so "rebuilt" closes the menu on its own, while "removed" alone leaves stale index-addressed editor callbacks (`OnDelete(index)`) and a stale Pin/Unpin label. | all 4 |
| 7 | Major | design "focuses that row once the editor is built"; task 2.2 | The block-surface grant is async. `EnterEditorMode` clears index-based focus hooks, read-view indices differ from editor indices (filters, collapse), and item rows have no text field. The design needs a pending TaskId stash modelled on `pendingHandbookAppend` that is cleared on refusal. | comprehension, design, readiness |
| 8 | Major | spec Req 1 note exclusion; task 2.5 | "Task row" is undefined for Tracker/Link/Craft/QuestObjective. Notes are excluded without a reason, although they have pin/delete in the editor and unpin/delete on the Pin Tab (pinned-task-tab), so right-click works on one row and not the next. | all 4 |
| 9 | Major | design "added only when its callback is non-null" | The gating is not decided in one place. Task Notice passes non-null no-op lambdas (`GuiDialogTaskNotice.cs:128-133`), so it would get dead entries. `TogglesLive` is true on a fired tablet, where Delete must be hidden. The change needs an explicit predicate per entry per row type, and Task Notice/Inbox must be listed as in or out. | comprehension, spec, design, readiness |
| 10 | Major | spec "stay inside the dialog's bounds"; design | `CompositedTransformFollower` only offsets. It does not clamp or flip, and the picker is deliberately unclipped. The design gives no placement rule and no pointer-to-local conversion. | comprehension, design, readiness, spec |
| 11 | Major | design Decisions (shared widget) | Overlay content sits outside `ScribeGlobalTint` and needs `CurrentShade` re-applied, and labels need the cuneiform branch on the Tablet. The picker does both (`ScribeAddKindPicker.cs:69-71, 190-203`). | design, readiness |
| 12 | Major | tasks.md (missing steps) | There is no task for the entry-label lang keys (none exist; 13 locales), for removing task 1.1's dev hook, or for updating the Editor Features handbook text (`en.json:403`). | design, readiness, comprehension |
| 13 | Major | tasks.md Tests | The gating matrix (row type x ReadOnly x TogglesLive x kind) is only checked by hand. A pure entry builder with unit tests is cheap, and there is precedent in `tests/Integration.Tests/ScribeReadViewFilterTests.cs`. | readiness, design |
| 14 | Major | spec "closes predictably" | The spec has no rule for dialog close, view switch (Read to Editor, Pin Tab, Settings) or scroll. The overlay is screen-level and can outlive its view. | spec |
| 15 | Major | spec Edit gating vs "Edit while locked" scenario | The spec doesn't say whether "may edit" includes the lock state. If Edit is hidden while locked, the locked-feedback scenario can't happen. The Edit nav button is dimmed rather than hidden (`ScribeDialogBase.Layout.cs:425`). | spec, design |
| 16 | Nit | proposal Impact / design Context | Names are wrong or missing: the row widget is `ScribeReadRow` (not `ScribeReadContent`), and the Pin Tab row state in `ScribePinnedContent.cs` isn't named. | comprehension, readiness |
| 17 | Nit | proposal Impact / task 3.2 | Not every dialog has every view: Assignment Desk has no Read/Pin Tab, and a wet Tablet is editor-only. A dialog-by-view table would make the playtest list concrete. | comprehension |
| 18 | Nit | spec "one at a time" vs task 1.2 "per dialog" | The spec doesn't say whether the one-menu limit is per dialog or global, or where the owner lives (`ScribeDialogBase`). | comprehension, readiness |
| 19 | Nit | spec "where pinning is live", "may edit" | These user-facing terms are undefined. They map to `TogglesLive`/`CompletionAndPinLive`, `ReadViewIsReadOnly` and `scribe-gui-locked`. | comprehension |
| 20 | Nit | spec Req 1, Req 2 | Requirements bundle unrelated behaviors (open, bounds, exclusions; entries plus the Edit flow). Split Edit and the exclusions out. | spec |
| 21 | Nit | design Goals vs roadmap | Change 3 needs a stateful "Repeat yearly" checkbox entry. The action-only, close-on-choose model doesn't cover it. | spec |
| 22 | Nit | design Risks bullet 3 | Entries as plain gesture detectors also fire on right-click and have no hover or press styling, unlike the picker's `Button` tiles. | design |
| 23 | Nit | proposal (unsure) | It is unverified whether macOS ctrl-click or a two-finger tap reaches VS as a right button. The author plays on Mac. | design |

## Triage

| # | Verdict | Reason |
|---|---------|--------|
| 1 | Ask → **Decided** | The user chose to add read-view Delete by identity, refused (server-enforced) while another player holds the editor lock. Applied via `RespectEditorLock` on `ScribeDeleteTaskMessage`. |
| 2 | Fix | Design a real interception: a right-button branch in `ScribeMultilineField`'s press handler, plus right-button filtering in every row child with `onTap`/`onPress` (checkbox, link name, caret, hover buttons, tablet refused detector). Each child forwards the right press to a row-level `onSecondaryPress(TaskId, pointer)`. Add a requirement that a right-click anywhere on a row runs no left-click action and does not move the caret. State that focus still moves (LibGUI does this before dispatch) and that the empty-row purge must not fire on that blur. |
| 3 | Fix | `ScribeDialogBase.OnKeyDown` consumes Escape when a row menu is open, before the field and `base`. Add a scenario: "Escape closes only the menu". |
| 4 | Fix | Replace with a hard/fired Tablet scenario (Pin/Unpin only) and a lock-held Lectern scenario. Drop "each access mode" from 3.2. |
| 5 | Fix | Keep the barrier simple. A right-click on the barrier closes the menu, and the next right-click opens the new one. Rewrite the scenario to match. |
| 6 | Fix | Use one rule in spec, design and tasks: entries capture TaskId and resolve the target when chosen (editor: TaskId to current index). The menu closes when its TaskId is gone. The Pin entry acts on the current pin state, not a toggle captured at open. Add a scenario where an unrelated resync keeps the menu open. |
| 7 | Fix | Add a `pendingEditFocusTaskId` stash modelled on `pendingHandbookAppend`, consumed in `EnterEditorMode` and cleared on refusal or close. Item rows get no caret; the row is scrolled into view. |
| 8 | Ask → **Decided** | The user chose to give every block kind the menu, notes included. Applied. |
| 9 | Fix | Use an explicit per-entry predicate table in the design, and an `enableRowMenu` flag on `ScribeReadContent` (false for Task Notice). Inbox previews are out of scope. |
| 10 | Fix | Placement rule: below the row at pointer x; flip above when there is no room below; clamp x to the dialog's right edge. Compute from the dialog bounds the dialog already has. |
| 11 | Fix | Require shade re-application and cuneiform labels, as the picker does. |
| 12 | Fix | Add tasks for en and 12 locale keys, removal of the dev hook before 3.1, and the handbook editor text. |
| 13 | Fix | Extract a `ScribeRowMenuEntries` pure helper and unit-test the matrix (including fired tablet, Task Notice, note and item kinds). |
| 14 | Fix | Close on dialog close and view switch. On scroll, the follower tracks the row, and the menu closes if the row leaves the viewport. |
| 15 | Fix | Edit shows whenever the document type and state allow editing, regardless of who holds the lock. The lock is checked on choose, with the existing feedback. |
| 16-20, 22 | Fix | Wording, naming, a dialog-by-view table, a per-dialog single menu, defined terms, a requirement split, and entry hover styling with left-click-only choose. |
| 21 | Fix | Note in the design that toggle entries come later. The close-on-choose rule applies to action entries only. |
| 23 | Fix | Add a Mac ctrl-click / two-finger-tap item to the playtest task. |

## Applied

All Fix items and both decisions were applied to proposal, design, spec and tasks. `openspec validate --strict` passes.
