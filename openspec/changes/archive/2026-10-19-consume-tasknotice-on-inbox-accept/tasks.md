## 1. Generalize the notice search/traversal

- [x] 1.1 In `src/Mod/ScribeModSystem.Delivery.cs`, generalize `NoticeAddressedTo` into a predicate
      parameter (`Func<ScribeDocument, bool>` or similar) so callers can match "any row addressed to
      this player" (today's ping use) or "carries this exact `TaskId`" (new consume use) through the
      same traversal. Verify `dotnet build` succeeds and the existing ping call site compiles
      unchanged in behavior.
- [x] 1.2 Extend the traversal (carried slots via `EnumerateCarriedSlots`, dropped `EntityItem`s via
      `GetEntitiesAround`, block-entity containers via chunk `BlockEntities`) so it can optionally
      consume the first match instead of only returning its position: clear a container/carried slot
      via `slot.Itemstack = null; slot.MarkDirty()`, or despawn a dropped `EntityItem` via
      `Die(EnumDespawnReason.Removed)`. Verify a unit-level dry run (temporary console trace or
      debugger) removes the correct stack without throwing when no match exists.
- [x] 1.3 Return whether anything was actually removed, so the caller can gate
      `outstandingNoticeCountByTargetUid` bookkeeping. Verify the return value is `false` when the
      scan finds nothing.

## 2. Multi-row gating

- [x] 2.1 Before removing a matched notice, check every row on its document: only proceed with
      removal if every row's own `ScribeAssignmentStore` record is no longer in a state where the
      notice's own dialog would still offer Accept/Decline for it (i.e., none remain `Sent` or
      `Unaccepted` from that notice's perspective, other than the row that just resolved). Verify with
      a new integration test: a two-row notice where only one row resolves via the Inbox tab is NOT
      consumed; once the second row also resolves (via either path), it is.

## 3. Wire consumption into the generic action path

- [x] 3.1 In `src/Mod/ScribeModSystem.Assignment.cs`'s `OnServerReceivedAssignmentAction`, after a
      successful `TryApplyAction` for Accept, Decline, Cancel, or Discard, call the new consume-search
      anchored on the resolved record's `TargetPlayerUid`, using the exact-`TaskId` predicate from
      Task 1. Skip the search entirely if that player is offline (no live position to scan around),
      matching the existing proximity signal's online-only bound. Verify via a new integration test:
      accepting an assignment through the generic action path (not `ApplyTaskNoticeAction`) while
      carrying its sealed notice consumes that notice.
- [x] 3.2 On a successful removal, decrement `outstandingNoticeCountByTargetUid` for that recipient
      the same way `ApplyTaskNoticeAction` already does. Verify via a test that the heartbeat's
      outstanding-count gate reaches zero (and stops scanning) after a proactive consume, not just
      after a direct notice Accept/Decline.

## 4. Regression + manual verification

- [x] 4.1 Add integration coverage (extending `tests/Integration.Tests/NoticeLifecycleScenarios.cs`)
      for: Accept-via-Inbox consumes a carried notice; Decline-via-Inbox consumes a nearby-container
      notice; Cancel-via-Inbox (Assigner-initiated) consumes the Assignee's carried notice; a notice
      left out of scan range is untouched and still self-consumes on next direct interaction. Verify
      `dotnet test` (Atlas suite) passes for all four.
- [ ] 4.2 Manual playtest: seal a notice, accept the same assignment via the Inbox tab while still
      holding the notice — confirm it visibly disappears from inventory immediately, with no further
      interaction needed. Verify by direct observation in-game.
