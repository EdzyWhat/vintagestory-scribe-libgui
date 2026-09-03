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
- [x] 2.3 In-game screenshot check: confirm all five colors read as distinct against the active
      theme's chip foreground (`NavActiveGlyph`) and against each other; tune the D1 starting values
      if any two read too similarly. Playtest verdict (2026-08-31): the D1 custom palette read too
      saturated/"fancy" — rejected in favor of directly borrowing nav-icon colors; see 12.1's rework.

## 3. Inbox nav-button gating on assignment history

- [x] 3.1 In `GuiDialogScribeLecternLibGui.GetExtraNavButtons()`, wrap the existing Inbox
      `yield return` in `if (modSystem.MyReceivedAssignments.Count > 0)`.
- [x] 3.2 Apply the same gate in `GuiDialogScribeChalkboard`'s and `GuiDialogScribeScriptorium`'s
      equivalent nav-button methods.
- [x] 3.3 Confirm `GuiDialogScribeAssignmentDesk` and `GuiDialogScribeInbox` are untouched by this
      gate (their Inbox tab/view is unconditional per spec).
- [x] 3.4 Manual test: on a fresh player with zero assignment history, confirm no Inbox button shows
      on Lectern/Scriptorium/Chalkboard; send that player an assignment and confirm the button
      appears on the next rebuild without reopening the dialog. Playtest verdict (2026-08-31): confirmed
      working.

## 4. Particle seen-trigger fix

- [x] 4.1 Extract the `ScribeMarkAssignmentsSeenMessage` send (currently inline in
      `OnClickSwitchToInbox()`, `ScribeDialogBase.ViewSwitching.cs`) into a small
      `MarkInboxSeenIfNeeded()` helper.
- [x] 4.2 Call `MarkInboxSeenIfNeeded()` from `OnClickSwitchToInbox()` (replacing the inline send),
      from `GuiDialogScribeInbox`'s constructor (after `DefaultToInboxView()`), and from
      `GuiDialogScribeInbox.EnterGrantedView()`.
- [x] 4.3 Manual test: send an assignment to a player, have them open the standalone Inbox block
      directly (not via another surface's nav button) and close it without acting — confirm the
      particle and nav shimmer both clear afterward. Playtest verdict (2026-08-31): "It works for both
      effects and in all cases."
- [x] 4.4 Manual test: send an assignment, have the recipient Decline it after viewing it via any
      Inbox-capable surface — confirm the particle clears and does not resume. Playtest verdict
      (2026-08-31): "Works as well."

## 5. Particle range, frequency, and silhouette

- [x] 5.1 In `ScribeAssignmentParticleEmitter.cs`, change `DetectionRadius` from 6.0 to 12.0.
- [x] 5.2 Change `CountMultiplier` from 1f to 0.6f.
- [x] 5.3 Move the spawn-origin Y band in `SpawnAt` from just-above-the-block
      (`pos.Y + 0.85` .. `pos.Y + 1.25`) to centered around the block's vertical midpoint.
- [x] 5.4 Reduce the upward `Velocity`/adjust `GravityEffect` magnitude in `BuildBatch` so the
      particle's total vertical travel shrinks to roughly two-thirds of its current distance while
      `LifeLengthAvg`/`LifeLengthVar` stay unchanged (particles rise more slowly, not for less time).
- [x] 5.5 In-game check: stand at various distances up to 12 blocks from an Inbox-capable block with
      an unseen assignment and confirm the particle triggers throughout that range; visually compare
      the new spawn height/travel distance against the old behavior. Playtest verdict (2026-08-31): "The
      shape and range were successfully changed and look good."

## 6. Scribe Settings: LibGUI theme-picker shortcut

- [x] 6.1 Determine the correct API call to programmatically trigger a client-registered chat command
      (`.ui settings`) from Scribe's own button `onTap` (see design.md D6 — likely
      `ICoreClientAPI.ChatCommands`-based; confirm exact method during implementation). Found:
      `capi.ChatCommands.ExecuteUnparsed(".ui settings", new TextCommandCallingArgs { Caller = ... })`.
- [x] 6.2 Add a labeled button to the Window Appearance section of the Settings surface that invokes
      it, with localized label + helptext added to `en.json`.
- [x] 6.3 Manual test: click the new button and confirm LibGUI's theme-picker dialog opens, matching
      typing `.ui settings` directly. Playtest verdict (2026-08-31): "the button doesn't do anything";
      root-caused + fixed by 12.3 — retest via 12.11.
      - Confirmed 2026-09-01: real root cause was a privilege check, not dialog stacking (12.3's
        DrawOrder theory was disproven by a clean repro — failed even with zero other dialogs open).
        `ui`/`settings` never calls `RequiresPrivilege`, so `Caller.HasPrivilege(null)` fell through to
        `false`; a real typed command passes only because `ChatCommandApi`'s own local-input path grants
        `CallerPrivileges = new[] { "*" }`. Fix: grant the same wildcard on our synthetic `Caller` in
        `ScribeSettingsDialog.OpenLibGuiThemePicker`. Playtest-confirmed working after restage.

## 7. Create Assignments form layout

- [x] 7.1 In `ScribeAssignmentFormContent.cs`, replace the "Send to" label + player-picker rows with
      a single `Row` containing: fixed-width `Text("Send to")`, `Expanded(flex: 1, child:
      playerPicker)`, and the Send button.
- [x] 7.2 Confirm the task-text label/field and the rest of the form (Sent-history section below the
      divider) are unaffected by this layout change.
- [x] 7.3 In-game check: confirm the row no longer reads as oversized/spread out, the player picker
      resizes correctly with the dialog's width, and the Send button stays reachable at every Pixel
      Art Size setting. Playtest verdict (2026-08-31): "size is fine"; a screenshot comparison flagged the
      caret looking differently placed than the Font picker's — investigated + explained as expected
      content-length variance, not a bug (12.4).

## 8. Wrap-up

- [x] 8.1a Build + restage (`build/restage.sh Debug`) succeeded; Core test suite (614 tests) still
      green, unaffected as expected since this change touches only `src/Mod/`.
- [x] 8.1b In-game playtest of the full batch end-to-end per `TESTING.md`/`what-to-test` conventions
      before considering this change done (covers 2.3, 3.4, 4.3, 4.4, 5.5, 6.3, 7.3 above).
      - Confirmed 2026-09-02: each covered task has its own terminal TESTING.md verdict —
        `00000044` (Obsolete, superseded), `00000045`-`0000004a` (all Confirmed).
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
- [x] 9.3 Design + implement the batch-send network message (N selected rows, one recipient UID, one
      delete-from-source-on-send flag) and its server-side handling (one independent `ScribeAssignment`
      per row, all addressed to that recipient). Implemented as `ScribeSendAssignmentBatchMessage`/
      `ScribeAssignmentBatchRow` (`src/Mod/ScribeSendAssignmentBatchMessage.cs`), addressed by the Desk's
      block position + staging slot index (mirrors `ScribeTranscribeCopyMessage`, not a DocId). Server
      handling is `OnServerReceivedSendAssignmentBatch` (`ScribeModSystem.Assignment.cs`): loops the rows,
      calls the D12-broadened `ScribeAssignmentStore.TryCreate` per row (a rejected row is skipped, not
      fatal to the batch), then `TryRemoveStagedRows` when `DeleteFromSource` is set. `ScribeAssignmentStore`
      was versioned (`Version` 1→2, `MinVersion` 1) to additively persist `RecipeSignature`, which the
      original wire format lacked. The dialog side (`GuiDialogScribeAssignmentDesk.BuildAssignmentContent`)
      mints a fresh `AssignmentId` per row client-side and carries `SourceTaskId` separately (a row sent
      twice must never reuse the same assignment id). Client build + Core test suite (619 tests) green.
- [x] 9.4 Implement the "Delete from source on send" checkbox: default unchecked, resets every time the
      Create Assignments tab is (re)opened — not a saved `ScribePlayerSettings` preference. `deleteFromSource`
      is a plain dialog field (`GuiDialogScribeAssignmentDesk`), reset to `false` after every successful send
      and never read from/written to `ScribePlayerSettings`; the checkbox itself renders in
      `ScribeAssignmentFormContent` via the same Checkbox+Text composition `ScribeSettingsContent` uses.
- [x] 9.5 Remove the freeform-text field + its Send button (added in task group 7) once the new flow is
      live and confirmed working end-to-end — this group fully replaces that one, per the proposal note.
      `ScribeAssignmentFormContent` rewritten: dropped the old `TextEditingController`/text field and its
      Send handler entirely, added the staging slot + `ScribeAssignmentStageContent` row list + the
      delete-checkbox + the new batch-Send button (still gated on a resolved target player, now also on
      `SelectedTaskIds.Count > 0` instead of non-blank text). The now-orphaned single-freeform-task
      `ScribeSendAssignmentMessage`/`OnServerReceivedSendAssignment` were deleted outright (no remaining
      caller after this rewrite) rather than left as dead code.
- [x] 9.6 Manual test: stage a document containing at least one of each kind (Task, Tracker, Craft, Link,
  - Confirmed 2026-08-31: TESTING.md `0000004b` "The batching/order still needs looking at (that's a different task), but the rest (multi-select auto-subtask-select + each row landing independently) are all good. Done." (submission 2026-08-31T22-17-42)
      Text, a parent with subtasks); multi-select a mix; send to one recipient; confirm each selected row
      arrives as its own independent assignment in that recipient's Inbox. Playtest verdict (2026-08-31):
      Trackers/Links/Crafting showed blank text in the Inbox (subtask indenting was correct); accepting
      created blank versions that also lost subtask indenting. Root-caused + fixed by 12.5/12.6 — retest
      via 12.9.
- [x] 9.7 Manual test: repeat with "Delete from source on send" checked — confirm the selected rows are
  - Confirmed 2026-08-31: TESTING.md `0000004c` "(no note)" (submission 2026-08-31T22-17-42)
      gone from the staged document once the send completes, and unchecked (default) leaves it untouched.
      Playtest verdict (2026-08-31): couldn't Accept these — the two-step Accept picker appeared but the
      second (confirm) tap did nothing. Investigated (12.7): no Kind/deletion-based rejection path found;
      leading theory is the same 12.5/12.6 blank-row bug made a successful Accept look like a no-op —
      retest via 12.9, and if it recurs check the server log for the `Trace(...)` line that fired.

## 10. Submission stamp + send-button lock (Create Assignments tab)

- [x] 10.1 Add lang key `scribe-assignment-stamp-imprint`: `"Submitted to\nPlayer"` (explicit 2-line break,
      matching the existing `\n`-in-Lang-value convention already used by `scribe-transcribe-io-slot` etc.).
- [x] 10.2 Promote the Scriptorium's private `ImprintInk` `Vector4` (`GuiDialogScribeScriptorium.cs`) to a
      shared `ScribeRowConstants.StampImprintInk`, and repoint the Scriptorium at it, so the Assignment
      Desk's stamp (10.3) can't drift from the tuned color. Also extracted the Scriptorium's stamp-sound
      loading boilerplate to a shared `ScribeStampSound.Play(capi)` while here, so both dialogs' stamp cues
      share one implementation instead of duplicating the `LoadSound`/`SoundParams` setup.
- [x] 10.3 Wire the same `ScribeStamp` flourish (`ScribeStamp.cs`) onto the Assignment Desk's staging slot
      in `GuiDialogScribeAssignmentDesk`: mirror the Scriptorium's `stampRegistry`/`stampGeneration`/
      `BuildStampOverlay`/`PlayStamp`/`OnStampEnded` trio (simplified — one slot, one label, no
      `stampTargetSlot`/`stampLabel` variability needed), firing on a successful `OnSendAssignmentBatch`
      and reading `scribe-assignment-stamp-imprint`.
- [x] 10.4 Disable the Create Assignments tab's Send button for the duration of the stamp animation: threaded
      a `sending` flag (backed by the dialog's `stampActive`) into `ScribeAssignmentFormContent`'s `canSend`
      gate (alongside the existing target-player/selection checks).
- [x] 10.5 Manual test: send a batch and confirm the stamp plays over the staging slot, and the Send button
      is unclickable until the animation finishes. Playtest verdict (2026-08-31): "It stamps and sounds
      good" — but asked for the imprint to read one word, "Submitted" (was 2 lines, "Submitted to
      Player"); a separate rendering bug makes the imprint box 3 lines tall regardless of text, explicitly
      accepted as out of scope for now. Text changed per 12.8; confirmed good by the same verdict.

## 11. Inbox / Create Assignments tab layout parity (divider-width bug)

- [x] 11.1 Root cause found: `ScribeReadContent`, `ScribePinnedContent`, the Guestbook, and the Clockmaker's
      Timer tab all wrap their root `Column` in `ScribeTextDefaults.Wrap(family, size, new
      Padding(EdgeInsets.All(10), ...))` before their `Divider()`. `ScribeInboxContent.Build()` and
      `ScribeAssignmentFormContentState.Build()` return a bare, unwrapped `Column` — no outer inset, no
      font-default ancestor — so their `Divider()` (and everything else) spans edge-to-edge instead of
      sitting inset like every other tab, which is what reads as "much wider."
- [x] 11.2 Wrap `ScribeInboxContent.Build()`'s returned `Column` in the same
      `ScribeTextDefaults.Wrap(Widget.Style.TaskFontFamily, Widget.Style.FontSize, new
      Padding(EdgeInsets.All(10), ...))` pattern.
- [x] 11.3 Apply the same wrap to `ScribeAssignmentFormContentState.Build()`'s returned `Column`.
- [x] 11.4 Manual test: open the Inbox tab on Lectern/Chalkboard/Scriptorium and the Create Assignments tab
      on the Assignment Desk; confirm the divider width and content inset now match Read/Edit/Pinned/Guest
      Book/Timer. Playtest verdict (2026-08-31): "Layout is good!"

## 12. Playtest round 2 (2026-08-31 verdicts): color rework, ordering, settings-button fix, Inbox display
    bugs, Accept-placement content-loss bug, one-word stamp text

- [x] 12.1 Chip colors: playtest feedback found the D1 custom palette (2.1/2.2) read as too saturated next
      to the rest of the GUI's muted nav-icon backgrounds. Reversed to directly ALIAS specific nav colors
      instead of independently-tuned values: New → `NavActiveGuestbook` (purple), Accepted →
      `NavActiveRead` (blue), Declined/Discarded → `NavActiveEdit` (red), Cancelled → `NavActiveTranscribe`
      (orange/gold), Completed → `NavActivePinned` (green). `ScribeRowConstants.AssignmentChip*` are now
      aliases, not independent constants — a nav color retune flows through automatically.
- [x] 12.2 Inbox ordering: playtest found newest-received assignments belonged at the top, not the bottom.
      `MyReceivedAssignments` is in creation order (oldest first); `AssignedDate` is a human-readable
      calendar string with no finer-than-in-game-day resolution, so it can't be sorted on directly and
      multiple sends on the same day would collide. Fix (`BuildInboxContent`): group into contiguous
      same-`AssignedDate` runs (one run = one batch/single-send, created together) and reverse the RUN
      order — a batch's own parent-then-subtask row order stays intact, only which batch is on top flips.
- [x] 12.3 Settings theme-picker button: playtest confirmed the button did nothing. Decompiling the shipped
      `Gui.dll`'s `.ui settings` subcommand handler showed it unconditionally builds+opens LibGUI's own
      `SettingsDialog` — no privilege/caller-shape gate that our `Caller{Player=...}` construction could be
      failing. Leading theory: that dialog was opening BEHIND the still-open Scribe Settings window (two
      separate top-level windows, nothing coordinates z-order) — invisible, not "not working". Fix:
      `OpenLibGuiThemePicker` now closes the Scribe Settings window first, and logs the command's
      `TextCommandResult` to the client log (not chat — a retest diagnostic, not a player-facing notice) so
      a further recurrence pinpoints the exact failure instead of another silent "nothing happened".
      Also moved the button off its own oversized full-width row onto a `PairedControls` row alongside the
      Font selector (arbitrary pairing — no thematic link, just two controls that fit one row).
- [x] 12.4 Dropdown caret placement (Font picker vs Send-to player picker) investigated via a side-by-side
      screenshot: NOT a bug. Both use the identical `Dropdown<T>`/`DropdownStyle`; decompiling the shipped
      `Gui.dll`'s `Dropdown<T>.Build` confirmed the label (in an `Expanded`) and the caret share one
      `Row(SpaceBetween)` in both cases. The narrower Send-to row (shared with a fixed label + Send button)
      simply leaves less spare width once the (possibly longer) player name fills its `Expanded` slot,
      pushing the caret proportionally closer to the border — expected content-length-vs-box-width
      variance, not a positioning defect. No code change.
- [x] 12.5 Inbox blank-text bug (Tracker/Link/Craft rows): a Tracker/Link/Craft block's `Text` is blank BY
      CONVENTION (its label lives on `TargetItemCode`/`LinkTarget` instead — see
      `ScribeAssignmentStore.TryCreate`'s remarks); `ScribeInboxRowData`/`ScribeInboxContent` rendered
      `Text` unconditionally, unlike the read view's `ResolveRowItem`-based resolution, so those kinds
      showed blank in both the Inbox and Sent-history lists. Fix: `ScribeInboxRowData` gained a
      `DisplayName` field + a `Label` property (`DisplayName ?? Text`); both `BuildInboxContent` and
      `ComputeSentAssignmentRows` now resolve it via `ResolveRowItem`, and the row widget renders `Label`.
- [x] 12.6 Accept-placement content-loss bug: `TryPlaceAcceptedAssignment` built the placed block as
      `new ScribeBlock(record.Kind, record.Text, ...)` — ONLY Kind/Text/TaskId/Assignment, silently
      dropping `TargetItemCode`/`TargetQuantity`/`CurrentQuantity`/`LinkTarget`/`LinkLabel`/
      `LinkDescription`/`RecipeSignature`/`Depth`. A placed Tracker/Link/Craft therefore landed blank
      (12.5 is why it displayed blank; this is why it WAS blank) and lost its subtask indent. Fixed to
      carry every field through.
- [x] 12.7 "Can't accept tasks deleted from source" (second Accept tap does nothing): investigated
      `ScribeAssignmentTransitions`/`TryApplyAction`/`TryPlaceAcceptedAssignment` — found no Kind-based or
      source-deletion-based rejection path; every branch that can silently no-op already calls `Trace(...)`
      to the server log. Leading theory: this was the SAME root cause as 12.5/12.6 — a blank/malformed
      placed row made a successful Accept look like nothing happened. No separate code change beyond
      12.5/12.6; retest and check the server log (`[scribe] assignment-action ...`) if it recurs.
- [x] 12.8 Stamp text: playtest asked for one word, "Submitted" (accepting the known 3-line-tall visual bug
      as out of scope). Changed `scribe-assignment-stamp-imprint` from the 2-line `"Submitted to\nPlayer"`
      to `"Submitted"`.
- [x] 12.9 Manual test: repeat the 9.6/9.7 multi-item batch-send check (Task/Tracker/Craft/Link/Text +
      subtasks) — confirm every kind now shows its real name/icon in the Inbox AND after Accept, subtasks
      keep their indent after Accept, and a second Accept (on a different assignment) succeeds normally.
      - Confirmed 2026-09-02: retest of 9.6/9.7 verified via TESTING.md `0000004b`/`0000004c`
        (both Confirmed).
- [x] 12.10 Manual test: send a couple of batches on different in-game days (or fake it by sending one now,
      confirming top-of-list, then sending another) — confirm the Inbox shows the newest batch at the top
      with its own rows still in original (parent-before-subtask) order.
      - Confirmed 2026-09-02: retest of 12.2 verified via TESTING.md `00000056` (Confirmed;
        `00000050` Obsolete/superseded by it).
- [x] 12.11 Manual test: click the (now paired) theme-picker button — confirm LibGUI's theme picker opens
      and is visible (not hidden behind the Scribe Settings window); check the client log for the
      `[scribe] .ui settings -> ...` line either way.
      - Confirmed 2026-09-01: client log showed `noprivilege`, not a stacking issue (see 6.3) — real fix
        was `CallerPrivileges = new[] { "*" }`. Playtest-confirmed working after restage.

## 13. Create Assignments tab split, staged-list styling, Accept-placement insert policy

- [x] 13.1 Accept-placement now follows the accepting player's own New Task Insert preference
      (`ScribePlayerSettings.NewTaskInsert`) instead of always appending to the bottom. The preference is
      client-local (never server state), so `ScribeAssignmentActionMessage` gained a `NewTaskInsert` byte
      sent alongside an Accept request; `ScribeDocument` gained `InsertAssignedBlock(index, block)`
      (`AppendAssignedBlock`'s sibling, at an explicit clamped index) and
      `TryPlaceAcceptedAssignment` now calls `doc.InsertAssignedBlock(doc.InsertIndex((ScribeNewTaskInsert)
      message.NewTaskInsert), placed)`.
- [x] 13.2 Split the Create Assignments tab into two: a new "Sent Assignment History" tab
      (`ScribeLecternView.SentHistory`, `BuildSentAssignmentHistoryContent`, `OnClickSwitchToSentHistory`,
      `IsSentHistoryView`) hosts the pills + historical Sent list this player's own Assigner-role
      `ScribeInboxContent` view used to render below a divider on the Create tab. New nav button on the
      Assignment Desk reuses the "scribeassignment" (scroll) icon code freed up by 1.6's swap onto the
      plus glyph, colored `NavActiveHistory` when active. Lang key `scribe-tab-senthistory`.
- [x] 13.3 `ScribeAssignmentFormContent`/`ScribeAssignmentFormContentState` narrowed to staging-and-select
      only: dropped `SentRows`/`ResolvePlayerName`/`OnAction` and the divider+heading+history section
      entirely. The Create Assignments tab is now just: heading, staging slot + hint, the boxed staged-row
      list (13.4), the delete-from-source checkbox, and the Send-to row.
- [x] 13.4 The staged-row list (`ScribeAssignmentStageContent`) is now inscribed in a rounded box with a
      slight inset `BoxShadow` (LibGUI's `BoxShadow.Inset` — paints INSIDE the box, reading as a shadow
      cast into a recessed tray rather than one cast onto the page) instead of sitting on the bare tab
      background.
- [x] 13.5 Manual test: open the Assignment Desk — confirm Create Assignments now shows ONLY the staging
  - Confirmed 2026-08-31: TESTING.md `00000051` "(no note)" (submission 2026-08-31T20-35-02)
      slot/list/player-picker (no Sent history below it), the new Sent Assignment History tab shows the
      pills + historical list that used to live there, and the nav column reads
      Create/History/Inbox/Settings left to right.
- [x] 13.6 Manual test: confirm the staged-row list reads as a rounded, subtly-recessed box distinct from
  - Confirmed 2026-08-31: TESTING.md `00000052` "(no note)" (submission 2026-08-31T20-35-02)
      the plain tab background, at a couple of different Pixel Art Size settings.
- [x] 13.7 Manual test: as the ASSIGNEE, set New Task Insert to Top in Settings, accept an assignment into a
  - Confirmed 2026-08-31: TESTING.md `00000053` "Works." (submission 2026-08-31T22-17-42)
      Notebook that already has tasks — confirm it lands at index 0. Switch to Bottom, accept another —
      confirm it lands at the end.

## 14. Playtest round 3 (2026-08-31 triage): tooltip title noise, Accept-candidate scope, batch-ordering
     root-cause fix, filter-pill rework

- [x] 14.1 Removed the confusing permanent "Title: (untitled)" line from the Inbox and Assignment Desk
      blocks' placed/held tooltips (triage: "we should remove the following since they can't be renamed").
      `BlockScribeWritingStation` gained `protected virtual bool ShowsDocumentTitleInTooltip => true`
      gating the title-line append in both `GetPlacedBlockInfo`/`GetHeldItemInfo`; `BlockInbox`/
      `BlockAssignmentDesk` override it `false` — they're thin subclasses of the Lectern/Scriptorium base
      for placement/interaction plumbing only and have no document title a player can ever set.
- [x] 14.2 Narrowed the Accept-placement candidate list (triage: "too exhaustive ... should work like the
      links from Handbook ... last opened Scribe item first, then Scribe items in the player's appropriate
      inventories, notably not ground placement, or in chests or other inventory types"). Rewrote
      `ScribeDialogBase.ComputeAcceptCandidates` to scan `ScribeModSystem.EnumerateCarriedSlots` (hotbar +
      backpack ONLY — the same scope the Handbook's "Add to Scribe" flow already used) instead of
      `InventoryManager.InventoriesOrdered` (which could include e.g. an open chest's inventory), and to
      prefer the book matching the newly-exposed `ScribeModSystem.LastOpenedScribeItemDocId` when it's
      among the eligible carried items — mirroring `ResolveWriteableCarriedSlot`'s own priority — before
      falling back to listing every eligible carried item. This drops the previous "currently-held wins
      outright regardless of last-opened" rule (design.md Decision 7) in favor of the Handbook's
      established precedent.
- [x] 14.3 Root-caused the Inbox batch-ordering bug (00000050: "subtasks are getting created and inserted
      at the top of the list inappropriately"). The previous fix (12.2) grouped by `AssignedDate`, a
      coarse human-readable in-game-day string — TWO SEPARATE batches sent on the same calendar day
      collide on it and silently merge into one run, losing the newest-batch boundary. Fix: added a real
      `ScribeAssignment.BatchId` (Guid), minted once per send call
      (`OnServerReceivedSendAssignmentBatch`) and stamped on every row that call creates; store/save format
      bumped to v3 (a pre-v3 blob synthesizes a deterministic legacy id via `DeriveLegacyBatchId` so an
      existing multi-item batch keeps grouping after upgrade). `BuildInboxContent`/`ComputeSentAssignmentRows`
      now share one `NewestBatchFirst` helper: `blocks.GroupBy(b => b.Assignment!.BatchId).Reverse().SelectMany(g => g)`
      — `GroupBy` preserves both needed orderings by contract (group-of-first-occurrence order; source
      order within each group), so no manual contiguous-run bookkeeping is needed anymore.
- [x] 14.4 Sent Assignment History now uses the same newest-batch-first ordering as the Inbox (triage:
      "Both the Sent Assignment History and the Assignment Inbox should have the same ordering: Newest at
      the top" — clarified 2026-08-31 follow-up as the intent of the earlier "also ... the History tab"
      aside, i.e. UX parity between the two pill-bearing tabs, NOT the Accept-candidate item-picker) —
      previously unordered (raw creation order). Same `NewestBatchFirst` helper as 14.3.
- [x] 14.5 Reworked the filter-chip row on both the Inbox and Sent Assignment History tabs (triage): the
      three terminal-rejection states (Declined/Cancelled/Discarded) now share ONE combined
      "Declined/Cancelled/Discarded" pill instead of three separate ones; a new "All" pill was added; the
      whole row now acts as radio buttons (`ScribeInboxContentState.activeFilterGroup`, a single
      `ScribeAssignmentFilterGroup` replacing the old independently-toggleable `HashSet<ScribeAssignmentState>`)
      — exactly one group is visible at a time, defaulting to All. Discarded's PER-ROW state chip no longer
      shares Declined's red (`ScribeRowConstants.AssignmentChipRejected`) — it's now
      `AssignmentChipDiscarded`, the component-wise midpoint between Declined's red and Cancelled's gold
      (triage: "halfway between the colors for Declined and Cancelled"), also reused as the combined pill's
      own representative swatch. New lang keys `scribe-assignment-filter-all` / `scribe-assignment-filter-rejected-group`.
- [x] 14.6 Manual test: look at (and pick up) a placed/carried Inbox block and Assignment Desk — confirm
  - Confirmed 2026-08-31: TESTING.md `00000054` "(no note)" (submission 2026-08-31T22-17-42)
      neither tooltip shows a "Title:" line at all; a Lectern/Scriptorium/Notebook nearby still shows its
      title line as before (regression check).
- [x] 14.7 Manual test: with several writeable Scribe items scattered across hotbar, backpack, AND an open
  - Confirmed 2026-08-31: TESTING.md `00000055` "(no note)" (submission 2026-08-31T22-17-42)
      chest, open an Inbox with an unaccepted assignment — confirm the Accept candidate picker lists only
      the hotbar/backpack items (never the chest's), and that whichever Scribe item you most recently
      opened this session is the one Accept defaults/narrows to when it's still carried.
- [x] 14.8 Manual test: send two SEPARATE multi-item batches to the same recipient on the same in-game day
  - Confirmed 2026-09-01: TESTING.md `00000056` "(no note)" (submission 2026-09-01T13-31-56)
      (batch A, then later batch B) — confirm the Inbox shows batch B's rows as one intact group above
      batch A's, with each batch's own parent-before-subtask order preserved (the 00000050 repro).
      Playtest verdict (2026-08-31, null): raised a DIFFERENT bug on the ACCEPT side — accepting a batch's
      rows one at a time was scattering subtasks away from their parent, not just a display-order issue.
      Root-caused + fixed by 15.1 — retest via 15.11.
- [x] 14.9 Manual test: open Sent Assignment History after sending a couple of batches at different times —
  - Confirmed 2026-08-31: TESTING.md `00000057` "(no note)" (submission 2026-08-31T22-17-42)
      confirm the most-recently-sent batch shows at the top, matching the Inbox's ordering.
- [x] 14.10 Manual test: open the Inbox or Sent Assignment History filter-chip row — confirm it now reads
  - Confirmed 2026-08-31: TESTING.md `00000058` "Mark this complete, but update color of the the combined "Declined-Cancelled-Discarded" pill to the color of Declined for both Inbox and Sent Assignment History tabs." (submission 2026-08-31T22-17-42)
      All / New / Accepted / Declined-Cancelled-Discarded / Completed, tapping one shows ONLY that group
      (previous selection deselects automatically), and a Discarded row's own chip color is visibly
      distinct from a Declined row's (a blend of red+gold, not plain red).

## 15. Playtest round 4 triage (2026-08-31): batch-accept ordering, disabled-button styling, history icon,
     per-transition history stubs, copy pass, staging-slot styling, Sent History stale-render

- [x] 15.1 Root-caused the 14.8 Accept-side batch-ordering bug: subtask nesting is pure ADJACENCY (no
      parent-id anywhere — `ScribeDocument.OwnedRun`/`FindParentIndex` both walk the flat block list;
      `Depth` is just an indent level), and `TryPlaceAcceptedAssignment` inserted each Accept independently
      at Top/Bottom with zero awareness of its batch siblings — so anything else accepted between a
      parent's and a subtask's separate Accept round-trips split them apart. Fixed via a new
      `ScribeDocument.InsertIndexForBatch(batchId, fallback)`: if a sibling from the same `BatchId` is
      already placed in the doc, insert right after its cluster (its owned run, if it's a depth-0 parent
      with children already placed) instead of the raw Top/Bottom preference; falls back to
      `InsertIndex(fallback)` when no sibling is placed yet or `batchId` is the shared `Guid.Empty` default
      (never glue together unrelated callers that don't set one). Known limitation: a subtask Accepted
      BEFORE its parent still falls back and doesn't self-heal once the parent lands — fully closing that
      needs deferring placement until a whole batch resolves, which this doesn't attempt. 4 new
      `ScribeDocumentTests` cases.
- [x] 15.2 Disabled Accept button: swapped the 0-eligible-candidates branch's `ButtonVariant.Primary` to
      `ButtonVariant.Secondary` (triage: "the current approach of grey text on amber primary color
      background isn't working" — LibGUI's `Button`/`ButtonVariantStyle` has no disabled-background variant
      at all, `enabled: false` only dims the label 45%, confirmed via decompiling the shipped 3.1.0 `Gui.dll`
      — this is the mod's existing convention everywhere, not a one-off bug). `Secondary`'s transparent +
      bordered fill reads clearly as inert without any new styling code.
- [x] 15.3 Sent Assignment History nav icon: swapped from the reused `scribeassignment` (scroll) code to
      the already-registered-but-unused `scribehistory` code (aliased to `guestbook.svg`) — triage: "I have
      no idea what the current icon is."
- [x] 15.4 Per-transition history stubs (triage: "we should also see similar stubs for when it was
      accepted, discarded, etc. — even if they happened on the same day"). `ScribeAssignment` gained five
      nullable date fields (`AcceptedDate`/`DeclinedDate`/`CancelledDate`/`DiscardedDate`/`CompletedDate`);
      `ScribeAssignmentStore` bumped to v4 to persist them additively (`WriteOptionalString`/
      `ReadOptionalString` helpers, defaulting to null on a pre-v4 blob). Core stays calendar-agnostic — the
      Mod layer's new `StampTransitionDate` helper (`ScribeModSystem.Assignment.cs`) stamps the right field
      immediately after a transition actually succeeds (`OnServerReceivedAssignmentAction`,
      `NotifyAssignmentDoneChanged`, `NotifyAssignmentDiscardOnDelete`) using `NotebookHost.FormatDate`.
      `ScribeInboxRowData` carries the five dates through; `BuildExpandedDetail` renders one line per
      reached transition below "Assigned by X — date" (`scribe-assignment-accepted-on` /
      `-declined-on` / `-cancelled-on` / `-discarded-on` / `-completed-on`). 2 new `ScribeAssignmentStoreTests`
      cases (v4 round-trip + pre-v4 blob defaulting to null).
- [x] 15.5 Combined "Declined/Cancelled/Discarded" filter pill's swatch color: changed from the blended
      Discarded color to Declined's red (glance feedback on 14.10: "update color of the combined pill to
      Declined") — a per-row Discarded chip still keeps its own blended color; only the GROUPED pill's
      representative swatch changed.
- [x] 15.6 Create Assignments tab's staging slot now matches the Scriptorium's inventory-slot adornments
      (triage: "style it with the same adornments... sizing, background image, border, etc."): added the
      same semi-opaque parchment veil background (`colors.Surface` at 66% alpha) and the "scribebook"
      watermark glyph underneath, mirroring `GuiDialogScribeScriptorium.BuildWatermarkedSlot`. Size/border
      already matched (shared `ScribeDocumentSlot`/theme fallback) — only the veil + watermark were missing.
- [x] 15.7 Copy pass on the Create Assignments tab (triage: "help me refine the various language for
      readability"): `scribe-assignment-form-heading` "Stage & Select" → "Assign Tasks"; `-stage-hint`
      trimmed to one line and de-enumerated ("Place a Scribe item here to select tasks from it below."),
      matching the Scriptorium Transcribe tab's minimal single-line style; `-stage-empty` rewritten as a
      3-step numbered walkthrough ("1. Create a task on a Scribe item\n2. Place it in the slot above\n3.
      Select tasks to send"); `-delete-from-source` reworded from a settings-toggle phrasing to a sentence
      ("Remove these tasks from the source when sent").
- [x] 15.8 Fixed the Completed/Discarded stale-render bug (triage: "we aren't updating Completed or
      Discarded properties properly"): `ScribeDialogBase.OnMyAssignmentsChanged` only rebuilt on
      `ScribeLecternView.Inbox`/`.Assignment` — task 13.2 split the Sent Assignment History tab out into its
      own `ScribeLecternView.SentHistory` afterward and this check was never updated, so a sync arriving
      while that tab was open updated the underlying data but never repainted it; the stale chip only
      refreshed on an unrelated rebuild (navigating away and back). Added `SentHistory` to the check.
- [x] 15.9 Manual test: repeat 9.6/9.7/12.9's multi-item batch-send check — confirm the disabled Accept
      button now reads clearly inert (bordered/transparent, not solid amber) when no eligible target exists.
      - Confirmed 2026-09-02: retest of 15.2 verified via TESTING.md `00000059` (Confirmed).
- [x] 15.10 Manual test: open the Sent Assignment History nav button — confirm it now shows a
      guestbook/journal-style icon instead of the scroll.
      - Confirmed 2026-09-02: retest of 15.3 verified via TESTING.md `0000005a` (Confirmed) —
        note the final icon shipped as the open-book/Link glyph, not guestbook/journal as this
        task's text originally assumed; `0000005a`'s wording reflects the actual shipped result.
- [x] 15.11 Manual test: as the recipient, Accept a batch's parent task, then (without leaving the Inbox)
      Accept one of its subtasks — confirm the subtask lands directly under its parent in the document, not
      detached elsewhere (the 14.8 repro). Then expand an Accepted/Completed/Discarded assignment in the
      Inbox and Sent Assignment History — confirm an "Accepted — date" line (and a Completed/Discarded one
      once it reaches that state) shows below "Assigned by".
      - Confirmed 2026-09-02: retest of 15.1/15.4 verified via TESTING.md `0000005d` and
        `0000005b` (both Confirmed).
- [x] 15.12 Manual test: open the Create Assignments tab and stage a Scribe item — confirm the staging slot
      now shows the same parchment veil + book watermark as the Scriptorium's inventory slots; with nothing
      staged, confirm the empty-list hint now reads as a 3-step numbered list.
      - Confirmed 2026-09-02: retest of 15.6 verified via TESTING.md `0000005c` (Confirmed).
- [x] 15.13 Manual test: as the ASSIGNER, open Sent Assignment History and leave it open while the recipient
      Accepts, then Completes or Discards the assignment — confirm the row's state chip updates live without
      navigating away and back (the 15.8 repro).
      - Confirmed 2026-09-02: retest of 15.8 verified via TESTING.md `0000005e` (Confirmed).

## 16. Assigned-task stamp icon (author-supplied art)

- [x] 16.1 Replaced the placeholder `scribeassignment` (rolled-scroll) SVG glyph with the author's
      full-color `scribe-assigned-stamp.png` art (100×100), rendered as a genuine raster image rather than
      flattened through the SVG-icon tint pipeline (which single-color-blends via `SKColorFilter.SrcIn` and
      would have destroyed the art). New leaf widget `ScribeRasterIcon` (mirrors LibGUI's own
      `VsIcon`/`RenderVsIcon` shape — a `RenderObjectWidget` with no child) paints the bitmap with LINEAR
      sampling (the opposite tradeoff from `ScribePixelArtBackdrop`'s nearest-neighbour pixel-art path),
      untinted, at `style.ControlSize` — a drop-in replacement, same footprint as the old glyph. The bitmap
      is resolved once per rebuild via `modSystem.GetGuiTextureBitmap` (self-caching) by whichever context
      owns `modSystem` (`ScribeDialogBase.Layout.cs`/`.PinTab.cs`) and threaded down as a plain `SKBitmap?`
      alongside `Style` — mirroring the existing "resolved by the dialog, row widgets stay API-free"
      convention — through `ScribeReadContent`/`ScribeReadRow`, `ScribeEditorContent`/`ScribeEditRow`/
      `ScribeFrozenEditorRow`, and `ScribePinnedContent`/`ScribePinRow`. `ScribeAssignedTaskIcon.Build` falls
      back to the old SVG glyph if the bitmap is null (pure-server context). No Core changes; `Mod` builds
      clean and all 625 Core tests still pass (unaffected).
- [x] 16.2 Manual test: open the Inbox, Editor, and Pin Tab — confirm an accepted assignment's leading icon
      now renders the full-color stamp art (not the old scroll glyph), at the same size/position as before,
      and reads smoothly (no pixelation) at typical window sizes.
      - Confirmed 2026-09-02: retest of 16.1 verified via TESTING.md `00000060` (Confirmed).

## 17. Accept picker: never auto-narrow to a single choice when 2+ items are carried

- [x] 17.1 `ComputeAcceptCandidates` (`ScribeDialogBase.ViewSwitching.cs`) previously let the item matching
      `ScribeModSystem.LastOpenedScribeItemDocId` win outright and return alone whenever it was among the
      eligible carried items — even with a second eligible item also carried, silently skipping the picker
      (triage 2026-09-01, playtest repro: 2 Scribe items carried, Accept immediately landed on the
      last-opened one with no picker shown). Changed to always list every eligible carried item, ordering
      the last-opened match FIRST as a convenience default (so the picker's initial selection is usually
      right) without ever bypassing the picker. Exactly one eligible item is unaffected (still a plain
      Accept button — nothing to choose between).
- [x] 17.2 Manual test: carry two (or more) eligible Scribe items where one matches your most-recently-
  - Confirmed 2026-09-01: TESTING.md `00000072` "Works, but it looks like on the Assignment Inbox we are missing what Scribe item the task was accepted onto. I thought we'd implemented that update already..." (submission 2026-09-01T17-35-07)
      opened — open an Inbox with an unaccepted task, tap Accept, confirm the picker now appears (not an
      immediate accept) with the last-opened item pre-selected, and confirm choosing a different candidate
      and confirming lands the task on that one instead.
