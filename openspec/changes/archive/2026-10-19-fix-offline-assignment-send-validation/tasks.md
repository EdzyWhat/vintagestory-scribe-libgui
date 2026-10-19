## 1. Core: known-players membership check

- [x] 1.1 Add `Contains(string uid)` to `ScribeKnownPlayersStore` (`src/Core/ScribeKnownPlayersStore.cs`),
      an O(1) lookup against the store's existing private backing dictionary, and verify with a new
      test in `tests/Core.Tests/ScribeKnownPlayersStoreTests.cs`: `Contains` returns `true` for a uid
      after `Upsert`, and `false` for a uid that was never upserted.

## 2. Mod: accept offline-known targets in the send path

- [x] 2.1 Update the target-validation guard in `SendAssignmentBatch`
      (`src/Mod/ScribeModSystem.Assignment.cs:51`) to accept a target uid when EITHER
      `sapi.World.PlayerByUid(targetUid)` is non-null OR `knownPlayersStore` (guarded for null)
      `.Contains(targetUid)` is true, rejecting (unchanged `Trace` + early return) only when neither
      holds. Verify by re-reading the guard: an unknown/garbage uid is still rejected exactly as
      before, and the rest of `SendAssignmentBatch` (both `SendBatchViaNotice` and
      `SendBatchViaLocalInboxes`) is untouched.

## 3. Integration tests

- [x] 3.1 Add an Atlas scenario (new or alongside `NoticeLifecycleScenarios`) that seeds
      `Mod.KnownPlayersStore!.Upsert(fakeUid, "SomeOfflinePlayer")` directly — without ever joining
      that uid as a live player in the scenario, so it never enters the engine's online-player table
      and reproduces the exact "offline but previously known" condition — loads a blank notice into
      an Assignment Desk, calls `Mod.SendAssignmentBatch` targeting `fakeUid` with
      `DeliveryChoice = SendNotice`, and asserts the Desk's output slot now holds a sealed Task
      Notice (non-null `Itemstack` carrying the sent row).
- [x] 3.2 Add a second scenario, same setup, with `DeliveryChoice = LocalInboxes`, asserting
      `Mod.AssignmentStore!.Received(fakeUid)` contains the newly-created record.
- [x] 3.3 Add a regression scenario confirming a send to a uid that is neither joined nor upserted
      into `KnownPlayersStore` is still silently rejected: no assignment record created for that uid,
      no notice placed in the output slot — locks in the pre-existing "reject truly unknown targets"
      behavior.
- [x] 3.4 Run the full Atlas integration suite locally (per `build/install-hooks.sh`'s pre-push gate)
      and confirm it passes, including the three new scenarios above.

## 4. Manual verification

- [ ] 4.1 Restage (`build/restage.sh Debug`, client not running), then manually verify in-game: host
      a world open to LAN (or a dedicated server), have a second player join once and disconnect,
      restart the server process, open the Assignment Desk, select the now-offline player as the
      target with "Send a Notice" delivery mode, send a batch, and confirm a Sealed Task Notice
      appears in the Desk's output slot (previously: nothing was produced).
- [ ] 4.2 Repeat 4.1 with "Local Inboxes" delivery mode and confirm the recipient sees the new
      assignment in their Inbox once they reconnect.

## 5. Core: unseen-and-undelivered predicate

- [x] 5.1 Add a small computed predicate to `ScribeAssignment` (`src/Core/ScribeAssignment.cs`) —
      e.g. `NeedsAmbientParticle => Seen == false && ReceivedDate is null` (name at implementer's
      discretion) — and cover it with new tests in `tests/Core.Tests/ScribeAssignmentTests.cs` (or
      equivalent): true when `Seen` is false and `ReceivedDate` is null (Local-Inboxes, never
      received-as-notice); false when `Seen` is true regardless of `ReceivedDate`; false when
      `ReceivedDate` is non-null regardless of `Seen` (notice already delivered).

## 6. Mod: stop the block particle once a notice is in inventory

- [x] 6.1 Add a new gate to `ScribeModSystem` (`src/Mod/ScribeModSystem.cs`), alongside
      `HasUnseenAssignment` (line 176) — e.g. `HasUnseenUndeliveredAssignment` —
      delegating to the §5 Core predicate over `myReceivedAssignments`. `HasUnseenAssignment`
      itself is untouched.
- [x] 6.2 Update `OnAssignmentParticleTick` (`src/Mod/BlockEntityScribeWritingStation.cs:284-305`)
      to check the new §6.1 gate instead of `HasUnseenAssignment`. Verify by re-reading:
      `ScribeDialogBase.ShowInboxShimmer` (`src/Mod/ScribeDialogBase.cs:184`) still reads the
      original, unchanged `HasUnseenAssignment` — the nav-button shimmer's behavior is untouched.

## 7. Integration test + manual verification

- [x] 7.1 Add an Atlas scenario asserting the server-side precondition for §5/§6: send a batch via
      "Send a Notice", drive the notice into the recipient's carried inventory (as
      `MarkReceivedForCarriedNotices` already does), and assert the resulting
      `AssignmentStore`/`ScribeAssignment` record has `ReceivedDate` non-null while `Seen` is still
      false — locking in the exact record shape §5's predicate depends on. (Actual particle
      rendering is client-only and out of reach of a headless Atlas scenario; this scenario proves
      the data precondition instead.)
- [ ] 7.2 Restage (`build/restage.sh Debug`, client not running), then manually verify in-game:
      send yourself (or a second player) a Task Notice via "Send a Notice" delivery, stand within
      the particle detection radius of a Desk/Lectern/Scriptorium/Chalkboard/Inbox the whole time,
      and confirm once the sealed notice lands in inventory, that block's ambient particle does
      NOT start (previously: it did). Then, without opening an Inbox, walk near a block while a
      separate Local-Inboxes-delivered assignment is still unseen, and confirm that block's ambient
      particle STILL fires as before — the fix is scoped to notice-delivered assignments only.
