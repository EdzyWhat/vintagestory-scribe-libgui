## 1. Core: shared validity predicate

- [x] 1.1 Add `src/Core/ScribeReorderValidity.cs` with `Cluster(IReadOnlyList<int> depths, int fromIndex)`
      (mirrors `ScribeDocument.OwnedRun`'s algorithm over a plain depths list) and
      `IsValidDropTarget(IReadOnlyList<int> depths, int clusterStart, int clusterEnd, int toIndex)`.
- [x] 1.2 `tests/Core.Tests/ScribeReorderValidityTests.cs`: cover a depth-0 cluster's bounds
      (with/without owned-run children), a depth-1 leaf's trivial single-row cluster, a valid
      same-depth target, an invalid cross-depth target, a drop-on-own-cluster no-op, and out-of-range
      `toIndex`.
- [x] 1.3 (Found during in-session review, not in the original plan) Add
      `ScribeReorderValidity.ResolveDestination(depths, clusterStart, toIndex)`: dropping a cluster
      *forward* onto a parent that itself owns children must land after that parent's whole cluster,
      not merely after its own row (which would wedge the dragged cluster between the target and its
      first child — reparenting it by position). Dropping *backward* needs no adjustment: inserting
      at the target's original index already carries the target's whole cluster forward together.
      Wired into both `ReorderEditorBlock` and `ScribePinOrdering.Reorder`'s destination math. See
      design.md's "Destination resolution" decision.
- [x] 1.4 Tests for `ResolveDestination` (forward onto a parent-with-children, backward, childless
      target, depth-1 leaf target) plus an end-to-end `ScribePinOrdering.Reorder` regression test
      dragging an earlier pinned parent-with-child forward past a later pinned parent-with-child.
- [x] 1.5 (Found during in-session review, not in the original plan) Fix
      `ScribeDialogBase.PinTab.cs`'s `OnPinReorder`: it built its working copy from
      `modSystem.MyPins` (raw storage order), but the `from`/`to` indices it receives are ON-SCREEN
      row indices — which under the default Sink/UnpinSink completion policy is
      `OrderedPinsForDisplay()`'s done-sunk order, a different permutation whenever a done pin is
      mixed with not-done ones. The drag would silently apply to the wrong rows. Fixed by building
      the working copy from `OrderedPinsForDisplay()` (the same list `BuildPinnedContent` renders
      rows from), matching the editor (whose `scratch.Blocks` has no such display-vs-storage split).

## 2. Core: pin-list cluster-aware reorder

- [x] 2.1 Add `ScribePinOrdering.Reorder(List<ScribePinnedRef> pins, int from, int to)`: builds the
      pin list's own depths, computes the dragged pin's cluster and validity via
      `ScribeReorderValidity`, and — when valid and `from != to` — moves the `[start, end)` slice to
      land at `to` (slice-remove-and-reinsert, same shape as `ScribeDocument.MoveRange`). Returns
      `false` on any no-op (invalid target, in-place, or out-of-range) without mutating `pins`.
- [x] 2.2 `tests/Core.Tests/ScribePinOrderingTests.cs`: add cases for `Reorder` — same-depth pin move,
      cross-depth rejection (both directions), and a depth-0 pin with pinned depth-1 children moving
      together as one cluster without stranding any child.

## 3. Editor: gate reorder + arrow on validity

- [x] 3.1 In `ScribeDialogBase.Editor.cs`'s `ReorderEditorBlock`, build `depths` from
      `scratch.Blocks`, and extend the existing `from == to || dropOnCluster` no-op branch to also
      cover `!ScribeReorderValidity.IsValidDropTarget(depths, start, end, to)` (a cross-depth drop is
      rejected exactly like an in-place drop — no `MoveBlock`/`MoveRange`, no `isDirty`, no focus
      re-target beyond the existing re-home-caret behavior).
- [x] 3.2 In `ScribeEditorContentState.Build`, derive `depths` from `Widget.Blocks`, compute the
      dragged row's cluster via `ScribeReorderValidity.Cluster`, and AND the existing `isDropTarget`
      expression passed to `ScribeEditRow` with `IsValidDropTarget(...)` so the ▶ arrow only renders
      on a valid same-depth target.

## 4. Pin Tab: gate reorder + arrow on validity

- [x] 4.1 In `ScribeDialogBase.PinTab.cs`'s `OnPinReorder`, call the new `ScribePinOrdering.Reorder`
      against a copy of `modSystem.MyPins` instead of the current flat splice; only build and send
      `ScribeReorderPinsMessage` when it returns `true`.
- [x] 4.2 In `ScribePinnedContentState.Build`, derive `depths` from the pin list, compute the dragged
      pin's cluster via `ScribeReorderValidity.Cluster`, and AND the existing `isDropTarget`
      expression passed to the pin row with `IsValidDropTarget(...)`.

## 5. Specs and docs

- [x] 5.1 Sync the `task-subtasks` and `player-pins` delta specs in this change into
      `openspec/specs/` (via `openspec-sync-specs` or at archive time).
- [x] 5.2 Add a `CHANGELOG.md` entry once this ships (same-depth drag restriction + arrow fix +
      Pin Tab cluster-move).

## 6. Verification

- [x] 6.1 `dotnet test` on `Core.Tests` — new `ScribeReorderValidity`/`ScribePinOrdering.Reorder`
      tests pass alongside the existing suite. (559/559 pass, including the 1.4 regression test.)
- [x] 6.2 In-game (via `build/restage.sh Debug` + manual test, added to `TESTING.md`): drag a depth-1
      subtask over a depth-0 row in the editor (no arrow, no reorder); drag a depth-0 parent with
      subtasks over an unrelated depth-1 row (no arrow, no reorder); drag a pinned parent with
      pinned children past a sibling pin on the Pin Tab (children move with it); confirm same-depth
      drags still work normally in both surfaces. Confirmed 2026-08-29.
