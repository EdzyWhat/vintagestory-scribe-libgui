## Why

Dragging a row to reorder it currently lets a depth-1 subtask land anywhere in the list, including
interleaved among depth-0 rows — and lets a depth-0 parent be dropped between an unrelated parent
and its subtasks. Because a row's "parent" is inferred purely from position (adjacent depth-0 row
above a run of depth-1 rows), a cross-depth drop silently reshapes relationships the player never
intended to touch. The Pin Tab has it worse: its drag-reorder is a flat list splice with no depth or
clustering awareness at all, so dragging a pinned parent past a sibling can strand its already-pinned
children behind it. The grip-handle's drop-target arrow (a right-facing triangle, added by
`replace-drag-wash-with-grip-arrows`) currently lights up on every hovered row during a drag, giving
the player no visual signal about which drops are actually safe.

## What Changes

- Reordering (both the document editor and the Pin Tab) is restricted to same-depth targets: a
  depth-0 row may only be dropped on another depth-0 row, and a depth-1 row only on another depth-1
  row. A cross-depth drop is rejected as a no-op, the same way an in-place drop already is.
- The grip-handle's drop-target arrow (▶) now renders ONLY on rows that are a valid same-depth drop
  target for the row currently being dragged — driven by the exact same validity check the commit
  logic uses, not a separately hand-maintained visual condition, so the arrow can never promise a
  drop the commit then refuses.
- The Pin Tab's reorder gains the editor's existing cluster-move behavior: dragging a depth-0 pin
  moves it together with its already-pinned depth-1 children (the contiguous run immediately
  following it in the pin list) as one unit, so a parent pin's reorder can never strand its pinned
  children. The document editor already does this for depth-0 blocks; this closes the gap on the
  Pin Tab side.
- New shared Core primitive (`ScribeReorderValidity`): given a list of row depths and a dragged
  row's cluster bounds, computes the cluster and answers "is this drop target valid" once, so both
  surfaces' visual affordance and commit logic call one tested function instead of duplicating the
  same-depth rule by hand.

## Capabilities

### New Capabilities
(none — this refines existing reorder behavior, no new player-facing capability)

### Modified Capabilities
- `task-subtasks`: the existing drag-reorder requirement gains an explicit same-depth restriction
  and a requirement that the drop-target arrow only appears on valid (same-depth) targets.
- `player-pins`: the existing "Reorder the per-player pin list" requirement gains the same
  same-depth restriction, cluster-preserving behavior for a dragged pinned parent, and the
  arrow-only-on-valid-targets requirement.

## Impact

- `src/Core/`: new `ScribeReorderValidity.cs` (pure, depth-list-based cluster + validity helper, no
  VS API); `ScribePinOrdering.cs` gains a pin-list reorder method mirroring `ScribeDocument`'s
  existing `OwnedRun`/`MoveRange` clustering.
- `src/Mod/ScribeDialogBase.Editor.cs` (`ReorderEditorBlock`): gate the move on the new validity
  check instead of only checking `from == to`/drop-on-own-cluster.
- `src/Mod/ScribeDialogBase.PinTab.cs` (`OnPinReorder`): route the permutation through the new Core
  pin-reorder method instead of a flat list splice.
- `src/Mod/ScribeEditorContent.cs` and `src/Mod/ScribePinnedContent.cs`: `isDropTarget` for each row
  gains the same-depth check (sourced from the shared Core predicate) so the arrow only lights up on
  valid targets.
- `tests/Core.Tests/`: new tests for `ScribeReorderValidity` and the new `ScribePinOrdering` reorder
  method; no game install required.
- No network message shape changes — the Pin Tab still sends a full permuted pin-identity list via
  the existing `ScribeReorderPinsMessage`; the permutation it sends is just computed more carefully.
