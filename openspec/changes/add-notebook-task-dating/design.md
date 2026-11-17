## Context

Changes 1 and 2 of the calendar roadmap are done: a block can carry a `Date` and `RepeatYearly`
(`ScribeBlock`), `ScribeDateRules.Classify` returns the status, `ScribeWorldCalendar` turns the world's
calendar into dates and formats them through the player's Date order, and the right-click row menu
(`ScribeRowMenuEntries`, `ScribeRowMenuHost`, `ScribeDialogBase.RowMenu`) has entries for Edit, Pin and
Delete. Rows are three widgets: read rows, editor rows (which edit a private scratch copy under the editor
lock and save it) and Pin Tab rows (which show a pin snapshot with no date).

This change depends on `add-dated-task-core` and `add-task-row-context-menu`. Both must be archived before
this one, because this change modifies requirements that those changes introduce (see "Spec deltas").

Three facts shape the design:

- The read view cannot edit the document; its only lock-aware mutation today is Delete, which sends
  `ScribeDeleteTaskMessage` with `RespectEditorLock`, and the server refuses while another player holds the
  lock. Dates in the read view need an equivalent message.
- The menu host builds its entries once, when it opens. A toggle entry and the switch from Add date to
  Change date both need entries that follow the document after a change.
- The completion code (`ScribeCompletion`) decides from the player's policy alone. A task meant to repeat
  every year would be deleted by the Delete policy the moment it is checked.

See `proposal.md` for the motivation and `docs/calendar-roadmap.md` for the decisions this builds on.

## Goals / Non-Goals

**Goals:**
- Dating a plain task takes a right-click and a picker in both the read and editor views.
- Every viewer sees the same date and status, in their own Date order.
- Pure logic (entry gating, picker stepping, chip text, repeat-aware completion) lives in Core with tests.

**Non-Goals:**
- Reminders, HUD and Pin Tab badges (they need the date in the pin snapshot), rollover, and the calendar
  block.
- Left-clicking the chip to edit. The chip is display-only; the menu is the one way in, which keeps the
  rule that a right-click on any row control only opens the menu simple.
- Typed dates, quick choices (Tomorrow, +3 days) and a month dropdown, listed under Later in the roadmap.
- Dating tasks that came from an assignment (Assignment != null), and dating any kind other than a plain
  Task.

## Decisions

- **Where dates are offered.** A virtual `SupportsRowDates` on `ScribeDialogBase`, true by default and
  false for the Tablet and the Assignment Desk. A row is **datable** when `block.Kind == Task` and
  `block.Assignment == null` (Core: `ScribeDateRules.IsDatable`). The menu offers date entries when all
  three hold: the dialog `SupportsRowDates`, the view is Read or Editor, and the row is datable. In the Read
  view the document must also be editable: `ScribeDocumentPolicy.ReadOnly` is false (this is the hard or
  fired Tablet's flag; every other surface here is editable, and every Lectern behaves as Public, so no
  ownership check is made). The Pin Tab is excluded because a pin snapshot carries no date until change 4.
  Depth-1 subtasks are datable. Rows in a collapsed group are not on screen, so they show no chip or menu
  until expanded; a filter pill hides a row exactly as it hides its chip. Other kinds keep a date they
  carry; the chip and entries never show for them.
  Alternative: allow dates on every completable kind. Rejected by the roadmap ("the UI only offers dates
  on plain tasks for now").

- **Entry gating stays one pure function.** `ScribeRowMenuEntries.For` gains `canDate` (the surface, the
  view and the block allow dating) and `hasDate`, and `ScribeRowMenuAction` gains `AddDate`, `ChangeDate`,
  `ClearDate` and `RepeatYearly`. Order: Edit, Pin or Unpin, then the date group, then Delete. The date group
  is separated from the entries around it (the host already supports a separator before an entry). The
  matrix test grows accordingly.

- **A toggle entry kind in the menu host.** `ScribeRowMenuItem` gains `Checked` (null for an action entry,
  true or false for a toggle). A toggle tile shows a small leading checkbox square, filled when on, and never
  closes the menu (`CloseOnChoose = false`). Alternative: a label that flips between "Repeat yearly" and
  "Don't repeat". Rejected because the roadmap specifies a checkbox and a flipping label reads as a command
  rather than a state.

- **The open menu follows the document.** The host gains `RefreshItems(IReadOnlyList<ScribeRowMenuItem>)`,
  which replaces the **Edit entry and the date group** of the shown menu, recomputes the placement (the
  entry count changes the menu height, so the flip above or below is decided again) and rebuilds the
  follower. Pin and Unpin are excluded from the refresh: that entry keeps the label the menu opened with
  and acts on the row's current state when chosen, as `add-task-row-context-menu` specifies. Delete does
  not change with the document either. `ScribeDialogBase.ServiceRowMenu`, the existing per-frame upkeep,
  resolves the open task's entries each frame through the function that built them and calls
  `RefreshItems` only when the Edit entry or date group differ from the shown ones, comparing
  `LabelLangKey`, `Checked`, `CloseOnChoose` and `SeparatorBefore` (never `OnChoose`, a new closure each
  time). Add date becomes Change date, a toggle shows the document's state whoever changed it, and a
  refused read-view toggle shows its true state. The host's `Tick` stays the single owner of closing the
  menu for a vanished row; `RefreshItems` does not close it. When the task still exists but resolves to no
  entries at all, the upkeep closes the menu through `Close()`. A toggle entry (`Checked` not null) never
  closes the menu when chosen; action entries do. These two rules (a menu with no entries closes; a toggle
  does not close) are stated in change 1's spec too.

- **The editor view edits the scratch document.** Set, Clear and Repeat yearly in the editor change the
  scratch block and set `isDirty` then `RebuildBody()`, the path the editor's other row edits use
  (`ToggleEditorTask`, `DeleteEditorBlock`), so the date is saved with the text on the existing save.
  Focus: the row's focused field index (`focusedEditIndex`) is re-homed through `pendingFocusRow`, as
  `OnMyPinsChanged` does, so closing the picker never drops the caret. Nothing new is sent. A date set in
  the editor and then discarded or closed without saving is lost, like any other editor edit.

- **The read view sends `ScribeSetTaskDateMessage`, one operation per message.** Fields: `DocId`, `TaskId`
  (raw bytes, like the delete message), `Operation` (`SetDate`, `ClearDate`, `SetRepeat`), `Year`, `Month`,
  `Day`, `Repeat`. Operations, not whole state, so a stale client cannot overwrite another viewer's change:
  - `SetDate(date)` sets the date and keeps the server's existing repeat flag;
  - `ClearDate` removes the date and the repeat flag;
  - `SetRepeat(bool)` changes only the flag and is ignored on an undated task.
  The handler follows the delete handler: `OnServerReceivedSetTaskDate` reads the ids and calls the public
  `SetTaskDateForPlayer(player, docId, taskId, operation, ...)` (public for the integration suite). The
  server resolves the document and **refuses** when the document is unresolvable, the task id is unknown,
  the block is not datable (not a Task, or `Assignment != null`), the document policy is `ReadOnly` (hard or
  fired Tablet), the date fails `ScribeDate.TryCreate`, or another player holds the editor lock
  (`docHost.IsEditorLockHeldByOther(player.PlayerUID)`). A lock refusal also tells the player with
  `player.SendIngameError("scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"))`, exactly as
  `DeleteTaskForPlayer` does; the other refusals say nothing. **On every refusal the server re-sends the
  authoritative document to the sender**, so the read view's optimistic copy reverts and the chip and menu
  show the true state. Task 4.3 finds the host's existing per-player document resend, or adds one to
  `IScribeDocumentHost` if there is none. An accepted change goes through `ScribeDocument.TrySetTaskDate`,
  `TryClearTaskDate` or `TrySetTaskRepeat` (Core, tested) and `PersistFromReader`, as a delete does. The
  server is authoritative; `SupportsRowDates` is a client-side hint and is never trusted.
  The client checks `IsLockedByOther` **when Add date, Change date or Repeat yearly is chosen**, so a locked
  document shows the standard locked message and the picker does not open; the server check covers the
  race. The read view applies the change **optimistically**, like `OnReadViewCompleteTask`: it applies the
  operation to a codec round-trip copy, calls `host.ApplyLocalOptimisticEdit(copy)` and refreshes, and the
  authoritative sync that follows (or the re-send after a refusal) replaces the copy. Two quick Repeat
  clicks therefore flip twice instead of sending the same state twice.
  Alternative: reuse `ScribeEditDocumentMessage`, which replaces the whole document under the lock.
  Rejected because it needs the lock to be free and would overwrite unsaved edits by another player.

- **Date picker.** `ScribeDatePicker` is an overlay (a barrier plus a follower linked to the row, the same
  pattern as the menu). It is **owned by the row-menu layer**: `ScribeRowMenuHost` exposes the live row's
  `LayerLink` and render object (`LinkOf(taskId)`, `RenderOf(taskId)`), creates and closes the picker, and
  `Tick` also ticks it, so the row-vanished and scrolled-out-of-view checks are one piece of code. The
  dialog holds at most one open menu or picker; choosing Add date or Change date closes the menu first, so
  a menu and a picker are never open together. It shows a month stepper (previous, the localized month
  name from `ScribeWorldCalendar.MonthName`, next), a day stepper and a year stepper (minus, value, plus
  buttons), Set and Cancel. A month dropdown was considered and moved to Later: it opens a second overlay
  whose clicks and Escape would have to be coordinated with the picker's barrier. The stepping and clamping
  rules are a pure Core model, `ScribeDatePickerModel`, built from the calendar shape and the starting date:
  - the day stepper stops at 1 and at the month's length, with no carry into the month;
  - the month stepper carries into the year (previous on month 1 is month 12 of the previous year) and is
    blocked at Year 1 month 1 and Year 9999 month 12;
  - the year is 1 to 9999 and does not wrap; a starting date above Year 9999 starts at Year 9999;
  - when the month or year changes, a day above the new month's length is clamped down, and it does not
    return to the larger value when stepping back. This rule is only reachable with an uneven calendar
    shape, since vanilla's months are all equal, so it is tested in Core with an explicit shape;
  - a starting day past the month's end is clamped for display, and Set stores the clamped day;
  - with no date it starts on today (`ScribeWorldCalendar.Today`).
  Dismissal: Set applies and closes; Cancel and Escape close without a change; a left-click outside closes
  the picker and is consumed by the barrier (it does not also act on the control beneath); a right-click on
  another row closes the picker and opens that row's menu, a right-click on the same row closes the picker
  and opens that row's menu again, and a right-click on empty space only closes it. The picker also closes
  on view switch, dialog close, the editor closing, its row going away or scrolling out of view (checked by
  the host's `Tick`) and another player taking the editor lock while a read-view picker is open (the Set
  would be refused). The barrier does not handle wheel scrolling; a scroll that moves the row out of view
  closes the picker through `Tick`.
  Escape: the two call sites in `ScribeDialogBase` (`OnKeyDown` and the key-down handler that call
  `TryCloseRowMenuOnEscape`) call one method that closes the open picker or the open menu, whichever is
  open.

- **The chip.** One line under the task text, inside the same column as the text so it lines up with it, in
  both the read and editor views (depth-1 subtasks show it too, indented with their text). The chip text is
  one lang template with the date from `ScribeWorldCalendar.Format(world, date, order)`. The status text
  comes from a Core mapping, `ScribeDateChipText.For(ScribeDateStatus)`, returning a record
  `(string? StatusLangKey, int Count, string TemplateLangKey)`: Due today (`Count` unused), upcoming one and
  other, overdue one and other, and for `Done` a null `StatusLangKey` with the date-only template. Colors are
  four constants in `ScribeRowConstants`: `DateChipDue` (a warm amber), `DateChipOverdue` (a warmer red),
  `DateChipUpcoming` (the row's muted text color) and `DateChipDone` (dimmed further). The testable color
  rule is that the four constants have four different values; the values are tuned in playtest. The chip data travels on the row snapshots: `ScribeReadRowData` and `ScribeEditRowData` gain the date, the
  repeat flag and the status, and every construction site (the read and editor builders, the Pin Tab, the
  Assignment Desk and the Task Notice, which pass no chip data) is updated. The row builds the chip from its
  props in `Build`, so a reused row (the `UpdateWidget` path after `OnMyPinsChanged`) refreshes, and the
  ghost snapshot of a row deleted with a collapse animation includes the chip's height. The chip is steady
  text, and it refreshes when the Date order setting changes: `UpdateMySettings` fires `MyPinsChanged`, and
  `ScribeDialogBase` subscribes (`OnMyPinsChanged`), which rebuilds the read, editor and Pin Tab bodies.
  The separator between status and date lives in the lang template, so a locale can change it; a task
  checks that the middle dot the English template uses exists in the task fonts.
  Lang keys (all in `en.json` and the 12 other locales, 16 in total):
  - menu: `scribe-gui-rowmenu-adddate`, `scribe-gui-rowmenu-changedate`, `scribe-gui-rowmenu-cleardate`,
    `scribe-gui-rowmenu-repeatyearly`;
  - picker: `scribe-datepicker-set`, `scribe-datepicker-cancel`, `scribe-datepicker-month`,
    `scribe-datepicker-day`, `scribe-datepicker-year`;
  - chip templates: `scribe-datechip` (`{0} · {1}`: status, date) and `scribe-datechip-done` (`{0}`: date);
  - chip status: `scribe-datestatus-today`, `scribe-datestatus-upcoming-one`,
    `scribe-datestatus-upcoming-other`, `scribe-datestatus-overdue-one`, `scribe-datestatus-overdue-other`.
  The chip and menu strings are localized; the English strings quoted in
  the spec are examples. Plural forms: two keys (one, other) per state. Locales with more plural forms use their closest "other"
  form; this is accepted for now (see Risks), and `build/check-locales.py` does not check plural agreement.

- **Status uses today at render.** Rows ask `ScribeWorldCalendar.Today(world)` when they build. A dialog
  left open across midnight keeps showing the old status until its next rebuild; this is accepted and stated
  in the spec. No timer is added; reminders and live badges are change 4. `ScribeDateRules.Classify` converts
  dates to day ordinals by looping years, which is cheap at normal years; caching the ordinal per build is a
  later option if a far-future date over many rows ever shows up in a profile.

- **Repeating tasks and completion.** `ScribeCompletion.EffectivePolicy(policy, blocks)` returns `Sink` when
  the policy is `Delete` and any block in `blocks` is a **repeating task** (`Kind == Task`, `Date != null`,
  `RepeatYearly`), and otherwise the policy unchanged. Only `Delete` is overridden: Keep, Sink, UnpinSink
  and Unpin act as the player chose, because none of them removes the task. The policy enum is exactly
  `Sink`, `Unpin`, `Delete`, `Keep` and `UnpinSink` (`ScribeCompletionPolicy`), and no separate "unpin on
  complete" setting exists: `player-pins`' "removes the pin unless the player opted out" is the policy
  itself (Keep and Sink keep the pin, Unpin and UnpinSink remove it). Pin results follow `Decide` for the
  effective policy: Delete as Sink sinks the task and keeps its pin; Keep leaves it in place with its pin;
  Unpin keeps it in place and removes the pin; UnpinSink sinks it and removes the pin; Sink sinks it and
  keeps the pin.
  It is applied inside `ApplyGivenDone`'s branches, where the blocks are in hand, after the server's
  `CollapsePolicyForReadOnlySource` (a read-only Tablet has already collapsed to Unpin or Keep, which are not
  overridden, so the order cannot disagree):
  - `ApplyLeaf` (a lone task, a depth-1 child completed alone, or any task under Independent): the acting
    block decides, so a repeating child completed alone under Delete sinks, and an Independent parent that
    does not repeat is deleted without touching its repeating child;
  - `ApplyBoundParent`: if any Task block in the owned run repeats, Delete is overridden to Sink for the
    whole run, so no child is deleted along with a non-repeating parent; under any other policy the run
    behaves as the policy says (the whole run is marked done, and a non-repeating task under a repeating
    parent follows its parent);
  - `ApplyDiscardParent` deletes the owned run's children under every policy today. Whenever the owned run
    contains a repeating task, the discard is **suppressed for every policy**: the parent is marked done,
    the children are kept as they are (not deleted, not marked done), and the effective policy applies to
    the parent. When that policy sinks, the parent moves to the bottom with its children
    (`MoveRangeToBottom` of the owned run), because a subtask must stay under its parent.
  `ScribeCompletion.ApplyDelete` is standalone trash, not a completion, and is unaffected: manually deleting
  a repeating task still deletes it. Other block kinds and the tracker-completion Delete path (which sends
  `ScribeDeleteTaskMessage` for a tracker) are unaffected, because the override is limited to plain tasks.
  Call sites: `ApplyLocal` (editor `ToggleEditorTask`, read view `OnReadViewCompleteTask`) and
  `ApplyGivenDone` (server `CompleteTaskForPlayer`, the Pin Tab by way of the server) reach it through
  `ApplyGivenDone`. The Pin Tab and the HUD send `ScribeCompleteTaskMessage` and have no local completion.
  The editor's `rangeOrMove` check (`ScribeDialogBase.Editor.cs`) is not changed: it already rebuilds for
  `Delete`, `Sink` and `UnpinSink`, so a Delete overridden to Sink rebuilds and shows the new order; an
  in-game check confirms it.
  Limits, accepted until change 4 puts a repeat flag into the pin snapshot (and stated in the
  `player-pins` and `pinned-task-tab` deltas): when the server cannot resolve the source document (a Notebook
  not in the player's inventory) it follows the player's policy, so the pin of a repeating task is removed
  under Delete or Unpin; and the HUD predicts the outcome from the policy alone, so under Delete a
  repeating task's HUD row can fade out and reappear when the authoritative result arrives. Without a
  rollover yet, a checked repeating task simply stays checked until change 4 rolls it over.

- **Copies.** The clone and clipboard paths already keep the date, as `dated-task` (change 2) requires, so
  this change adds no requirement for them; it adds tests for the clipboard and Scriptorium transfer paths.
  Assignment placement and the Assignment Desk capture keep dropping the date, as change 2 decided; a test
  here pins that behavior.

- **Spec deltas.** The repeating-task rule changes three completion requirements in the main specs, so this
  change carries MODIFIED deltas for them (`player-pins`: "Complete a pinned task by stable identity";
  `pinned-task-tab`: "The Pin Tab exposes the completion-policy control"; `scribe-dialog-base`: "Read-view
  completion applies the completion policy locally and immediately"). The row-menu specs of
  `add-task-row-context-menu` are not yet in `openspec/specs/`, so that change's "menu offers Pin or Unpin
  and Delete" wording is amended in place to allow the date entries; it must archive first.

## Assumptions (user to confirm)

The user was away when this change was planned. These were decided and recorded rather than asked:

1. Repeating tasks override only the Delete policy (to Sink); Keep, Sink, UnpinSink and Unpin are not
   changed. A Bound run is protected as a whole when any Task in it repeats.
2. Discard children is suppressed under every policy when the owned run contains a repeating task: the
   parent is marked done, children are kept as they are, and a sinking policy moves the parent with its
   children (so a subtask is never stranded).
3. The repeating-task exemption covers plain Tasks only. Other kinds and the tracker-completion Delete are
   unaffected.
4. The read view applies date changes optimistically, the message carries an operation, and every server
   refusal re-sends the authoritative document to the sender.
5. The first picker uses a month stepper that carries into the year; the month dropdown moves to Later.
   A menu and a picker are never open together, and Escape closes whichever is open.
6. The chip is display-only (no left-click to edit) and uses placeholder warm colors tuned in playtest.
7. A dialog left open across midnight does not change its chip until it rebuilds.
8. Slavic plural agreement ("in 2 days") is an accepted gap.
9. Assigned tasks (`Assignment != null`) get no date entries; there is no ownership check for dating.
10. A left-click outside the picker closes it and is consumed; the barrier does not handle wheel scrolling.
11. The unresolvable-source server branch and the HUD fade-then-reappear under Delete are accepted known
    limitations until change 4.
12. `RefreshItems` compares the resolved Edit entry and date group each frame (not a document version) and
    leaves Pin and Unpin as the menu opened.
13. Changes 1 and 2 archive before this one.
14. `SetRepeat` on an undated task is ignored by the server, and the client never offers it.

## Risks / Trade-offs

- [A refused read-view change looks applied until the server answers] → Every server refusal re-sends the
  authoritative document to the sender, so the optimistic copy reverts and the chip and the menu show the
  real state; a lock refusal also shows the in-game error.
- [Plural agreement in Slavic locales] → "in 2 days" uses the "other" form for every N other than 1, which
  is grammatically wrong for some counts in `ru`, `pl`, `cs` and `uk`. Accepted: the strings stay
  readable, and proper plural rules would be a cross-cutting change.
- [Chip color contrast on every backdrop] → The colors are constants chosen against the Notebook, Lectern
  and Chalkboard backdrops and tuned in playtest.
- [Overriding the Delete policy for repeating tasks] → A player who set Delete expects the checked task to
  go. Repeating tasks are the one exception, and the Repeat yearly entry is the only way to opt in.
- [HUD fade then reappear] → See the completion decision above; resolved by change 4.

## Open Questions

- Should the chip be clickable to open the picker? Deferred: the right-click menu is the single entry for
  now; adding a left-click target later does not change the specs.
