## 1. Decouple assignment-completion derivation from document resolution (design.md D1)

- [x] 1.1 Re-gate `NotifyAssignmentDoneChanged` on `assignmentStore.TryGet(taskId)?.Assignment`
  being Accepted (using its `AssignerUid`/`TargetPlayerUid` for `PushAssignmentSyncToBothParties`)
  instead of gating on `assignmentOnBlock`. Keep `assignmentOnBlock` as an optional parameter:
  when non-null and itself Accepted, still mirror the completion onto that live object (unchanged
  behavior for the resolved case); when null or non-Accepted, skip the mirror but still complete
  the canonical record. Verify: `dotnet build src/Mod/Mod.csproj -c Debug` succeeds with 0
  warnings/errors.
- [x] 1.2 Update `CompleteTaskForPlayer` to call `NotifyAssignmentDoneChanged` unconditionally on
  every Done→true toggle, not only inside its `if (resolved)` branch — passing the resolved
  block's `Assignment` when `resolved` is true and `null` otherwise. Verify: read-through confirms
  the call now sits outside the resolved-only branch and the existing document-write-through logic
  still only runs when `resolved` is true.
- [x] 1.3 Confirm `CompleteUnpinnedTaskAtSource`'s existing call site (which always has a resolved
  block, since it returns early otherwise) still compiles and behaves unchanged against the new
  gate ordering. Verify: `dotnet build` succeeds.
- [ ] 1.4 Manually verify the fixed scenario: as Player A, send and have Player B accept an
  assignment; have B pin the resulting task; move B's Notebook out of B's inventory (e.g. into a
  chest); as B, complete the pinned task from the HUD or Pin Tab; confirm both B's Inbox and A's
  Sent Assignment History show Completed.

## 2. Add trace logging to previously-silent early-returns (design.md D2)

- [x] 2.1 Add a `Trace()` call to `NotifyAssignmentDoneChanged`'s two meaningfully-anomalous guard
  clauses (`sapi`/`assignmentStore` null; canonical store record isn't an Accepted assignment),
  following the existing `Trace("  complete: ...")` convention in this file. The ordinary
  `!nowDone` (uncheck) case is left silent — it fires on every uncheck of any task and is not part
  of the bug's failure path. Verify: `dotnet build` succeeds; completing a non-assignment task logs
  the "no Accepted canonical record" line.
- [x] 2.2 Add a `Trace()` call where `CompleteTaskForPlayer` previously took its unresolved branch
  silently, noting the derivation now still runs even though the document didn't resolve. Verify:
  completing a pinned assignment task with its Notebook absent from inventory logs both the
  existing "complete:" line and the new resolution-status line.

## 3. Regression coverage

- [ ] 3.1 Add or extend a Mod-layer/integration test (see `tests/Integration.Tests/`) covering:
  complete a pinned Accepted-assignment task whose Notebook is not in the completing player's
  inventory, and assert the canonical assignment record becomes Completed and both parties'
  synced views reflect it. Verify: the new/updated test passes.
- [ ] 3.2 Run the full Core suite as a regression sanity check (`src/Core/` is untouched by this
  change). Verify: `dotnet test tests/Core.Tests` passes.

## 4. Spec sync and verification

- [ ] 4.1 Confirm `specs/assignment-state-machine/spec.md`'s new scenario ("Completing a pinned
  assignment task whose document is not currently resolvable") matches the implemented behavior.
  Verify: `openspec validate fix-assignment-completion-doc-resolution --strict` passes.
