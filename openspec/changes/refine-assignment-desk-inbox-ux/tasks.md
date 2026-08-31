## 1. Tab names, tooltips, and icons

- [x] 1.1 Change `scribe-tab-assignment` in `src/Mod/assets/scribe/lang/en.json` from "New Assignment"
      to "Create Assignments"; confirm the same lang key drives both the tab label and its hover
      tooltip (no separate tooltip string to update).
- [x] 1.2 Confirm `scribe-tab-inbox` already reads "Assignment Inbox" (it does) and its tooltip
      matches — no lang change needed there, just verify in-game.
- [x] 1.3 Author (or source) an inbox-with-down-arrow SVG and a plus SVG, matching the existing
      icon set's flat single-color style (`src/Mod/assets/scribe/textures/icons/`).
- [x] 1.4 Register the two new icons in `ScribeModSystem.Assets.cs` via `RegisterSvgIcon`
      (`scribeinboxarrow`, `scribeplus`), following the existing `scribegear`/`scribeassignment`
      pattern.
- [x] 1.5 Swap the Inbox nav button's icon name from `scribeinventory` to `scribeinboxarrow` in every
      call site (`GuiDialogScribeAssignmentDesk.cs`, `GuiDialogScribeInbox.cs`,
      `GuiDialogScribeLecternLibGui.cs`, `GuiDialogScribeChalkboard.cs`,
      `GuiDialogScribeScriptorium.cs`).
- [x] 1.6 Swap the Assignment Desk's Create Assignments nav button's icon name from
      `scribeassignment` to `scribeplus` in `GuiDialogScribeAssignmentDesk.cs`.

## 2. State chip colors

- [x] 2.1 Add five new named color constants to `ScribeRowConstants.cs`: `AssignmentChipNew`,
      `AssignmentChipAccepted`, `AssignmentChipRejected`, `AssignmentChipCancelled`,
      `AssignmentChipCompleted` (see design.md D1 for starting RGBA values).
- [x] 2.2 Update `ScribeAssignmentChip.For(...)` in `ScribeInboxContent.cs` to map Unaccepted →
      `AssignmentChipNew`, Accepted → `AssignmentChipAccepted`, Declined and Discarded (both) →
      `AssignmentChipRejected`, Cancelled → `AssignmentChipCancelled`, Completed →
      `AssignmentChipCompleted`.
- [ ] 2.3 In-game screenshot check: confirm all five colors read as distinct against the active
      theme's chip foreground (`NavActiveGlyph`) and against each other; tune the D1 starting values
      if any two read too similarly.

## 3. Inbox nav-button gating on assignment history

- [x] 3.1 In `GuiDialogScribeLecternLibGui.GetExtraNavButtons()`, wrap the existing Inbox
      `yield return` in `if (modSystem.MyReceivedAssignments.Count > 0)`.
- [x] 3.2 Apply the same gate in `GuiDialogScribeChalkboard`'s and `GuiDialogScribeScriptorium`'s
      equivalent nav-button methods.
- [x] 3.3 Confirm `GuiDialogScribeAssignmentDesk` and `GuiDialogScribeInbox` are untouched by this
      gate (their Inbox tab/view is unconditional per spec).
- [ ] 3.4 Manual test: on a fresh player with zero assignment history, confirm no Inbox button shows
      on Lectern/Scriptorium/Chalkboard; send that player an assignment and confirm the button
      appears on the next rebuild without reopening the dialog.

## 4. Particle seen-trigger fix

- [x] 4.1 Extract the `ScribeMarkAssignmentsSeenMessage` send (currently inline in
      `OnClickSwitchToInbox()`, `ScribeDialogBase.ViewSwitching.cs`) into a small
      `MarkInboxSeenIfNeeded()` helper.
- [x] 4.2 Call `MarkInboxSeenIfNeeded()` from `OnClickSwitchToInbox()` (replacing the inline send),
      from `GuiDialogScribeInbox`'s constructor (after `DefaultToInboxView()`), and from
      `GuiDialogScribeInbox.EnterGrantedView()`.
- [ ] 4.3 Manual test: send an assignment to a player, have them open the standalone Inbox block
      directly (not via another surface's nav button) and close it without acting — confirm the
      particle and nav shimmer both clear afterward.
- [ ] 4.4 Manual test: send an assignment, have the recipient Decline it after viewing it via any
      Inbox-capable surface — confirm the particle clears and does not resume.

## 5. Particle range, frequency, and silhouette

- [x] 5.1 In `ScribeAssignmentParticleEmitter.cs`, change `DetectionRadius` from 6.0 to 12.0.
- [x] 5.2 Change `CountMultiplier` from 1f to 0.6f.
- [x] 5.3 Move the spawn-origin Y band in `SpawnAt` from just-above-the-block
      (`pos.Y + 0.85` .. `pos.Y + 1.25`) to centered around the block's vertical midpoint.
- [x] 5.4 Reduce the upward `Velocity`/adjust `GravityEffect` magnitude in `BuildBatch` so the
      particle's total vertical travel shrinks to roughly two-thirds of its current distance while
      `LifeLengthAvg`/`LifeLengthVar` stay unchanged (particles rise more slowly, not for less time).
- [ ] 5.5 In-game check: stand at various distances up to 12 blocks from an Inbox-capable block with
      an unseen assignment and confirm the particle triggers throughout that range; visually compare
      the new spawn height/travel distance against the old behavior.

## 6. Scribe Settings: LibGUI theme-picker shortcut

- [x] 6.1 Determine the correct API call to programmatically trigger a client-registered chat command
      (`.ui settings`) from Scribe's own button `onTap` (see design.md D6 — likely
      `ICoreClientAPI.ChatCommands`-based; confirm exact method during implementation). Found:
      `capi.ChatCommands.ExecuteUnparsed(".ui settings", new TextCommandCallingArgs { Caller = ... })`.
- [x] 6.2 Add a labeled button to the Window Appearance section of the Settings surface that invokes
      it, with localized label + helptext added to `en.json`.
- [ ] 6.3 Manual test: click the new button and confirm LibGUI's theme-picker dialog opens, matching
      typing `.ui settings` directly.

## 7. Create Assignments form layout

- [x] 7.1 In `ScribeAssignmentFormContent.cs`, replace the "Send to" label + player-picker rows with
      a single `Row` containing: fixed-width `Text("Send to")`, `Expanded(flex: 1, child:
      playerPicker)`, and the Send button.
- [x] 7.2 Confirm the task-text label/field and the rest of the form (Sent-history section below the
      divider) are unaffected by this layout change.
- [ ] 7.3 In-game check: confirm the row no longer reads as oversized/spread out, the player picker
      resizes correctly with the dialog's width, and the Send button stays reachable at every Pixel
      Art Size setting.

## 8. Wrap-up

- [x] 8.1a Build + restage (`build/restage.sh Debug`) succeeded; Core test suite (614 tests) still
      green, unaffected as expected since this change touches only `src/Mod/`.
- [ ] 8.1b In-game playtest of the full batch end-to-end per `TESTING.md`/`what-to-test` conventions
      before considering this change done (covers 2.3, 3.4, 4.3, 4.4, 5.5, 6.3, 7.3 above).
- [x] 8.2 Update `TESTING.md` with a checklist entry for this change via the `what-to-test` skill:
      added 7 in-game items (codes `00000044`-`0000004a`), retired the fully-Confirmed
      `add-assignment-and-quest-support` group to `playtest-history/TESTING-archive.md`.

## 9. Multi-item assignment creation (design settled via D8-D13 — spec deltas (9.2) still needed before implementation)

- [x] 9.1 Run a design pass (design.md decisions) for `assignment-multi-item-creation`: how the staged-item
      slot works (placement, locking while a source document is open elsewhere), whether a selected
      parent row's subtasks auto-include, and how much of `ScribeReadContent`/`ScribeInboxContent` the
      Read-view-style multi-select list can reuse vs. needs new widgetry for. Resolved as design.md
      D8-D13: staging slot reuses `ItemSlotScribeDocument` verbatim on a new 1-slot `BlockEntityAssignmentDesk`
      inventory (mirroring `BlockEntityScriptorium`); no new lock concept needed; a new
      `ScribeAssignmentStageRow` widget reuses `ScribeReadRowData` but not `ScribeReadRow`'s checkbox;
      parent-checks-subtasks cascades once but every row stays independently overridable; `TryCreate`
      gains the full block shape; a new batch-send message carries N rows + 1 recipient + 1 delete flag.
- [x] 9.2 Write spec deltas for the new `assignment-multi-item-creation` capability and the
      `assignment-desk-block` modification (Create Assignments tab's content swaps from freeform text to
      this flow) once 9.1's decisions are settled. Written as
      `specs/assignment-multi-item-creation/spec.md` (5 ADDED requirements: staging slot,
      Read-view-style selectable rows, parent-cascades-to-subtasks selection, one independent
      assignment per selected row, "Delete from source on send") and a MODIFIED requirement on
      `assignment-desk-block` clarifying creation is staging-and-select, not freeform text entry.
- [ ] 9.3 Design + implement the batch-send network message (N selected rows, one recipient UID, one
      delete-from-source-on-send flag) and its server-side handling (one independent `ScribeAssignment`
      per row, all addressed to that recipient).
- [ ] 9.4 Implement the "Delete from source on send" checkbox: default unchecked, resets every time the
      Create Assignments tab is (re)opened — not a saved `ScribePlayerSettings` preference.
- [ ] 9.5 Remove the freeform-text field + its Send button (added in task group 7) once the new flow is
      live and confirmed working end-to-end — this group fully replaces that one, per the proposal note.
- [ ] 9.6 Manual test: stage a document containing at least one of each kind (Task, Tracker, Craft, Link,
      Text, a parent with subtasks); multi-select a mix; send to one recipient; confirm each selected row
      arrives as its own independent assignment in that recipient's Inbox.
- [ ] 9.7 Manual test: repeat with "Delete from source on send" checked — confirm the selected rows are
      gone from the staged document once the send completes, and unchecked (default) leaves it untouched.

<!-- More refinement items may be appended below as additional numbered sections as implementation
     proceeds — this change is an open batch, not a fixed one-shot list. -->
