## 1. Shared helper

- [x] 1.1 Add `NotifyDoneAssignmentsInDocument(ScribeDocument doc)` to
      `src/Mod/ScribeModSystem.Assignment.cs`, next to `NotifyAssignmentDoneChanged`: iterate
      `doc.Blocks`, and for every block that is `IsCompletable`, `Done == true`, and has a
      non-null `Assignment`, call `NotifyAssignmentDoneChanged(block.TaskId, true, block
      .Assignment)`. Verify by inspection it makes no assumption about a prior/old document —
      it only reads the document passed in.
- [x] 1.2 Add a Core.Tests-adjacent (Mod-layer) unit test, or an Atlas integration test if the
      helper needs `assignmentStore`/`sapi` to exercise meaningfully, confirming
      `NotifyDoneAssignmentsInDocument` is a no-op (no store mutation, no sync push) when called
      twice in a row on a document whose completed task is already Completed in the store —
      this is the idempotency the design relies on instead of diffing old vs. new.

## 2. Wire into the Lectern's whole-document flush

- [x] 2.1 In `BlockEntityScribeWritingStation.ApplyEdit` (`src/Mod/BlockEntityScribeWritingStation.cs:543`),
      after `Document = doc;` and the existing `RegisterHost`/`MarkDirty`/`ReconcileActorPins`
      calls, call `ModSystem?.NotifyDoneAssignmentsInDocument(doc)`. Verify with an Atlas
      integration test: place a Lectern, send an assignment to a player, have them Accept it
      (placing the task onto the Lectern's document), then have them check the task off from the
      Lectern's Editor view (simulating its whole-document autosave, not the pinned-task
      completion message) — confirm the Assignee's Inbox and the Assigner's Sent Assignment
      History both show Completed afterward.

## 3. Wire into the Notebook/Tablet whole-document flush

- [x] 3.1 In `ScribeModSystem.OnServerReceivedNotebookSave` (`src/Mod/ScribeModSystem.Network.cs:434`),
      after the existing `ScribeDocumentAttributes.WriteTo`/`slot.MarkDirty()`/pin-reconcile
      block, call `NotifyDoneAssignmentsInDocument(doc)`. Verify with an Atlas integration test
      that reproduces the reported bug directly: send an assignment to a player, have them
      Accept it onto a Tablet (a surface whose dialog has only an Editor tab), complete the task
      via the same whole-document save path a Tablet's Editor uses, and confirm both the
      Assignee's Inbox and the Assigner's Sent Assignment History show Completed — without any
      `ScribeCompleteTaskMessage` ever being sent.
- [x] 3.2 Atlas integration test: repeat 3.1 but for a held Notebook (not a Tablet) completed via
      its Editor tab specifically (as opposed to the HUD, Pin Tab, or Read view) — confirm the
      same Completed propagation, establishing this isn't Tablet-specific.

## 4. Regression coverage

- [x] 4.1 Atlas integration test: completing an assigned task via the Editor's whole-document
      flush when the canonical store record is NOT Accepted (e.g. already Discarded via the
      Inbox) — confirm `NotifyDoneAssignmentsInDocument` leaves the store record unchanged (no
      resurrection of a terminal state), matching the existing no-op guard in
      `NotifyAssignmentDoneChanged`.
- [x] 4.2 Atlas integration test: a document containing more than one completed, assigned task in
      a single whole-document save (e.g. two tasks checked off before the next autosave tick)
      — confirm both transition to Completed and both parties' syncs reflect both.
- [x] 4.3 Atlas integration test: the existing HUD/Pin Tab/Read-view completion paths are
      unaffected — completing a pinned assignment task from the HUD still marks it Completed
      exactly as before (no double-push or behavior change from the new flush-time call).

## 5. Verification

- [x] 5.1 Run the full Atlas suite locally (per `build/install-hooks.sh`'s pre-push gate) with
      `VINTAGE_STORY` pointed at the local install, and confirm all new and existing tests pass.
- [x] 5.2 Manually play-test in-game: as the Assignee, accept an assignment onto a Tablet, check
      the task off from the Tablet's Editor view, and confirm — without closing or reopening
      anything — the Assigner's Sent Assignment History (on a second client or session) shows
      Completed, and the Assignee's own Inbox shows Completed too.
