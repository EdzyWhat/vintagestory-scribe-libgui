## 1. Core: log entry type and assignment field

- [ ] 1.1 Add `ScribeAssignmentLogKind` enum (`Accepted, Completed, Declined, Cancelled,
      Discarded`) and `ScribeAssignmentLogEntry` (`Kind`, `Date` (string), `Detail`
      (nullable string, Accepted-only)) to `src/Core/ScribeAssignment.cs` (or a new sibling
      file if that file is getting crowded) — game-agnostic, no Vintage Story API
      reference (design.md Decision 1).
- [ ] 1.2 Add `LogEntries` (`IReadOnlyList<ScribeAssignmentLogEntry>`) to `ScribeAssignment`,
      backed by an internal mutable list and an internal `AppendLogEntry(...)` method used
      only by the Mod-layer choke points (task group 3). Update the constructor to accept an
      optional initial list (default empty) and `Clone()` to copy the list (entries are
      immutable, so a shallow copy is sufficient).
- [ ] 1.3 Unit-test (`tests/Core.Tests`): `Clone()` copies log entries independently of the
      source list (mutating one doesn't affect the other); a freshly constructed assignment
      has an empty log.

## 2. Persistence: both binary codecs

- [ ] 2.1 Bump `ScribeAssignmentStore`'s blob version and extend
      `WriteRecordList`/`TryReadRecordList` (`src/Core/ScribeAssignmentStore.cs:208-278`) to
      write/read each assignment's `LogEntries` (count, then per-entry
      kind/date/nullable-detail), appended strictly after the existing `TargetPlayerUid`
      field, per `docs/CODEC-MIGRATION.md`'s append-only convention. Reading an
      older-version blob yields an empty log per assignment (no crash).
- [ ] 2.2 Bump `ScribeDocumentCodec`'s version past 11 and extend the assignment block
      section (`src/Core/ScribeDocumentCodec.cs:135-145`) the same way, appended strictly
      after the existing `TargetPlayerUid` write.
- [ ] 2.3 Unit-test (`tests/Core.Tests`) a round-trip through each codec for an assignment
      with: zero log entries, one Accepted entry (with detail), and a full five-entry
      lifecycle (Accepted → Completed) — confirm entries and order survive serialize/
      deserialize. Also test that a blob written at the *old* version deserializes
      successfully with an empty log (backward-compat scenario from the spec).

## 3. Mod layer: append a log entry at every transition choke point

- [ ] 3.1 In `TryPlaceAcceptedAssignment` (`src/Mod/ScribeModSystem.Assignment.cs:115-147`):
      confirm the exact call order relative to `TryApplyAction`'s `Accept` transition
      (design.md's Open Question) — read `OnServerReceivedAssignmentAction`'s lines 58-90
      to settle whether `State` is already committed before this method runs. Append an
      Accepted `ScribeAssignmentLogEntry` (detail = `<item type name> "<doc title>"`,
      reusing the `scribe:scribe-assignment-candidate-label` lang key server-side, omitting
      the title suffix for an untitled document, mirroring `FormatCandidateLabel`) to the
      canonical record immediately after the slot/doc/placement checks succeed and
      immediately before the clone is built (so the clone in `AppendAssignedBlock` carries
      the new entry too) — do NOT append if any early-return (unresolved slot, not
      writeable, doc full) is hit.
- [ ] 3.2 In `OnServerReceivedAssignmentAction` (`src/Mod/ScribeModSystem.Assignment.cs:58-
      90`): after a successful `TryApplyAction` call for `Decline`/`Cancel`/`Discard`
      (Accept is handled by 3.1), append a log entry of the matching kind with
      `NotebookHost.FormatDate(sapi)` as the date.
- [ ] 3.3 In `NotifyAssignmentDoneChanged` (`src/Mod/ScribeModSystem.Assignment.cs:158-
      168`): after each successful `TryMarkCompleted` call (both the canonical store record
      and the placed clone), append a Completed log entry with the current in-game date to
      that same record.
- [ ] 3.4 In `NotifyAssignmentDiscardOnDelete` (`src/Mod/ScribeModSystem.Assignment.cs:174-
      181`): after its `TryApplyAction`-equivalent Discard transition succeeds, append a
      Discarded log entry — this is a separate call site from 3.2's Discard action and is
      easy to miss (flagged explicitly in design.md's risks).
- [ ] 3.5 Manually trace (or add a targeted integration test if the Atlas suite already has
      assignment-lifecycle coverage to extend) each of the five terminal outcomes once end
      to end: Accept-with-placement, Decline, Cancel, Discard-via-Inbox-button,
      Discard-via-block-delete, and Complete-via-task-checkbox — confirming exactly one log
      entry is appended per event and no path is silently skipped.

## 4. Rendering

- [ ] 4.1 Add `LogEntries` (`IReadOnlyList<ScribeAssignmentLogEntry>`) to
      `ScribeInboxRowData` (`src/Mod/ScribeInboxContent.cs:25-27`).
- [ ] 4.2 Populate it at both existing construction sites in
      `src/Mod/ScribeDialogBase.ViewSwitching.cs` (`BuildInboxContent`,
      `BuildAssignmentContent`) from `b.Assignment.LogEntries`.
- [ ] 4.3 Add lang keys to `src/Mod/assets/scribe/lang/en.json` for each log kind, following
      the existing `"scribe-assignment-assigned-by": "Assigned by {0} — {1}"` em-dash
      convention: an Accepted template taking the detail + date
      (`"Accepted onto {0} — {1}"`), and one each for Completed/Declined/Cancelled/
      Discarded taking just the date (`"Completed — {0}"`, etc.).
- [ ] 4.4 In `ScribeInboxContent.BuildExpandedDetail` (`ScribeInboxContent.cs:294-299` and
      the `rowChildren` list at line ~322): render one additional `Text` widget per log
      entry, in list order, directly beneath the existing `meta` ("Assigned by") line and
      above the action-button row, using the new lang keys keyed by each entry's `Kind`.
- [ ] 4.5 Confirm both the Assignment Desk's Sent view and the standalone Inbox's Received
      view show the same log for the same assignment (both read through
      `ScribeInboxRowData`/`BuildExpandedDetail`, so this should hold by construction —
      verify in-game, not just by inspection).

## 5. Playtest verification

- [ ] 5.1 In-game: send an assignment, accept it onto a titled Notebook, verify the expanded
      row on both the assigner's Assignment Desk (Sent) and the assignee's Inbox (Received)
      shows the "Accepted onto Notebook "<title>"" line with the correct date.
- [ ] 5.2 In-game: complete the accepted task via its checkbox; verify a "Completed" line
      appears on both views.
- [ ] 5.3 In-game: for a separate assignment, decline it from the Inbox; verify a "Declined"
      line appears.
- [ ] 5.4 In-game: for a separate assignment, cancel it from the Assignment Desk; verify a
      "Cancelled" line appears.
- [ ] 5.5 In-game: for a separate accepted assignment, delete the document block it was
      placed on (not the Inbox Discard button); verify a "Discarded" line appears — this is
      the path most likely to have been missed (design.md risk).
- [ ] 5.6 In-game: load a save created before this change shipped (or a document saved
      before applying this change, if one is available) and confirm its pre-existing
      assignments open without error and simply show no log lines.
