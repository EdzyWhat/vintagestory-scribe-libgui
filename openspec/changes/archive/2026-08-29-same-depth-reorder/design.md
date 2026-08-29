## Context

Two independent drag-reorder implementations exist today, both built on the same
grip-press/move-threshold/drop pattern (`task-subtasks` D11, `replace-drag-wash-with-grip-arrows`):

- **Editor** (`ScribeEditorContentState` + `ScribeDialogBase.Editor.cs`'s `ReorderEditorBlock`)
  already clusters a dragged depth-0 block with its owned run (`ScribeDocument.OwnedRun` +
  `MoveRange`) and no-ops a drop onto that cluster's own children. It has **no** depth-match check:
  a depth-1 leaf can be dropped at any index, including inside a different parent's owned run
  (silent re-parenting by position, since a row's parent is inferred from adjacency, not stored).
- **Pin Tab** (`ScribePinnedContentState` + `ScribeDialogBase.PinTab.cs`'s `OnPinReorder`) is a bare
  flat-list splice over `(OwnerDocId, TaskId)` pairs — no depth awareness, no clustering. Dragging a
  pinned parent past a sibling can strand its already-pinned children.

Both surfaces drive the grip→▶-arrow visual from identical widget-level booleans
(`IsDragSource`/`IsDropTarget`/`DragActive`), computed in `ScribeEditorContent.cs:981-985` and
`ScribePinnedContent.cs:540-544` from `dragFromIndex`/`dragOverIndex` state. `IsDropTarget` today is
purely "is the pointer over this row during a drag" — no depth involved.

`Depth` is a stored `[0,1]` int on both `ScribeBlock` and `ScribePinnedRef` (a snapshot for pins).
Parent/child adjacency is always *positional*: the contiguous run of depth-1 rows immediately
following a depth-0 row, computed on demand (`ScribeDocument.OwnedRun`/`FindParentIndex`). The Pin
Tab has no document-order equivalent today — `ScribePinOrdering.PlaceNewPin` derives clustering from
the *source document's* owned run at insertion time, but nothing recomputes clustering from the pin
list's own `Depth` values at drag time.

The Pin Tab's `OnPinReorder` builds the entire new permutation client-side and sends it wholesale via
`ScribeReorderPinsMessage`; the server (`OnServerReceivedReorderPins`) only validates payload shape
(well-formed, equal-length, bounded id lists) and otherwise fully trusts the client's chosen order —
it has no depth or clustering awareness. This change does not add server-side semantic validation; it
makes the client-side permutation the reorder is built from same-depth-safe and cluster-preserving,
consistent with that existing trust boundary.

## Goals / Non-Goals

**Goals:**
- A drag reorder in either surface can only land on a same-depth target; a cross-depth drop is a
  no-op, exactly like an in-place drop is today.
- The ▶ drop-target arrow is a direct rendering of the same validity check the commit uses — it
  cannot show a target that then refuses the drop, and cannot hide a target that would actually
  work.
- Dragging a depth-0 pinned parent moves its already-pinned depth-1 children with it, matching the
  editor's existing depth-0 cluster-move behavior.
- One shared, Core-level, game-agnostic, unit-testable predicate backs both surfaces so the rule is
  defined once.

**Non-Goals:**
- Locking a depth-1 row to its *current* parent's cluster. A depth-1 row remains a leaf that can move
  to sit under a *different* same-depth run elsewhere in the list (existing, accepted behavior per
  `task-subtasks`: "siblings do not follow"). This change only blocks *cross-depth* drops, not
  cross-parent depth-1 moves.
- Server-side validation of the Pin Tab's permutation. The server already trusts the client's
  self-permutation wholesale; this change only makes the client compute a same-depth-safe,
  cluster-preserving permutation before sending it.
- Any change to how `Depth` itself is toggled (the tap gesture) or to the one-level indentation cap.

## Decisions

### One shared Core predicate over `IReadOnlyList<int>` depths, not two hand-copies

`ScribeReorderValidity` (new, `src/Core/`) takes a plain `IReadOnlyList<int>` of row depths — not a
`ScribeDocument` or a pin list — with two static methods:

```csharp
public static (int Start, int End) Cluster(IReadOnlyList<int> depths, int fromIndex);
public static bool IsValidDropTarget(IReadOnlyList<int> depths, int clusterStart, int clusterEnd, int toIndex);
```

`Cluster` mirrors `ScribeDocument.OwnedRun`'s exact algorithm (depth-0 row: itself plus the
contiguous depth-1 run after it; depth-1 row: itself alone) but works over any depths list.
`IsValidDropTarget` returns true when `toIndex` sits inside the dragged row's own cluster (a
harmless no-op the caller already handles separately) or when `depths[toIndex] == depths[clusterStart]`
(same depth as the dragged row) — false otherwise.

Both surfaces derive a `depths` list from their own row data (`Widget.Blocks[i].Depth` /
`pins[i].Depth`) and call these two functions identically at both the render site (drop-target arrow)
and the commit site (whether to actually move anything). This is the one piece that must not drift
between "arrow says yes" and "commit says no" — everything else (the editor's existing
`ScribeDocument.OwnedRun`/`MoveRange`, the new pin-list cluster move) can stay surface-specific
because it's pre-existing, already-tested logic, not the new rule being introduced.

*Alternative considered*: a `ScribeDocument`-only helper, with the Pin Tab duplicating the same-depth
check by hand against its own `Depth` field. Rejected — that's exactly the "hand-maintained visual
condition that can drift from the commit logic" the proposal calls out as the current problem; a
depths-list-based helper costs nothing extra (both call sites already have `Depth` values readily
available) and removes the duplication entirely.

### Editor: gate the existing cluster-move on the new validity check, don't replace it

`ReorderEditorBlock` keeps using `scratch.OwnedRun(from)` to compute the cluster it will `MoveRange`
(unchanged — it's the document's real index space, needed for the actual mutation). It adds one
check before acting: if `!ScribeReorderValidity.IsValidDropTarget(depths, start, end, to)`, take the
existing no-op branch (same as today's `from == to || dropOnCluster`) instead of moving anything.
`depths` is built once per call as `scratch.Blocks.Select(b => b.Depth).ToList()` — cheap at the list
sizes this mod handles (dozens of rows), and only computed on an actual drop, not every hover frame.

The render-time arrow gate (`ScribeEditorContentState.Build`) computes the same `depths` list from
`Widget.Blocks`, derives the dragged row's cluster via `ScribeReorderValidity.Cluster`, and ANDs the
existing `isDropTarget` expression with `IsValidDropTarget(...)`.

### Pin Tab: new `ScribePinOrdering.Reorder`, mirroring the editor's shape

Add to `ScribePinOrdering`:

```csharp
public static bool Reorder(List<ScribePinnedRef> pins, int from, int to);
```

Internally: build `depths = pins.Select(p => p.Depth).ToList()`; compute `(start, end) =
ScribeReorderValidity.Cluster(depths, from)`; if `from == to` or
`!ScribeReorderValidity.IsValidDropTarget(depths, start, end, to)`, return `false` (no-op); otherwise
move the `[start, end)` slice to land at `to` (same slice-remove-and-reinsert shape as
`ScribeDocument.MoveRange`, adapted to `List<ScribePinnedRef>`) and return `true`.

`ScribeDialogBase.PinTab.cs`'s `OnPinReorder` calls this against a **copy** of `modSystem.MyPins`
instead of the current bare splice, and only builds/sends `ScribeReorderPinsMessage` when it returns
`true` — matching the editor's "no edit sent on a no-op" behavior exactly.

The render-time arrow gate in `ScribePinnedContentState.Build` mirrors the editor: derive `depths`
from the pin list, compute the dragged pin's cluster, AND the existing `isDropTarget` expression with
the validity check.

*Alternative considered*: keep clustering keyed off the source document's owned run (like
`PlaceNewPin` does for initial insertion) rather than the pin list's own positional `Depth` run.
Rejected — a manual drag-reorder must work even when a pin's source document is unloaded (an existing
hard constraint: "Mutating an unloaded document's source is best-effort"), and `PlaceNewPin`'s own
`GatherOwnedRunChildren` already guarantees pinned children sit contiguously right after their parent
in steady state, so the pin list's own positional adjacency is already the correct proxy — no
document resolution needed at drag time.

### Destination resolution: forward cluster-onto-cluster drops need their own adjustment

Playtest surfaced a second bug beyond plain validity: gating *whether* a drop is allowed (same-depth,
not-your-own-cluster) is not the same as computing *where* the moved rows actually land. Both
`ScribeDocument.MoveRange` and the new `ScribePinOrdering.Reorder` use the standard
remove-then-insert-at-index shape, where `destIndex` is a **pre-move** row index: after removing the
dragged slice, the remaining rows shift, and the slice re-inserts so its first row lands exactly where
`destIndex` used to point. For a *single-row* target with no children this is unambiguous. But when
the target is itself a depth-0 parent with its own owned run, "land where the target's row used to be"
means landing **immediately after the target's single row** — which, moving forward, wedges the
dragged cluster between the target and the target's own first child, splitting the very cluster this
change is supposed to protect. Moving backward has no such problem: inserting at the target's
untouched original index carries the target and everything after it (its whole cluster included)
forward together, so the dragged cluster lands cleanly before it.

Fix: `ScribeReorderValidity.ResolveDestination(depths, clusterStart, toIndex)`. When `toIndex` is
before the dragged cluster (backward move), it's a passthrough — already correct. When `toIndex` is
after (forward move), it re-targets to `Cluster(depths, toIndex).End - 1` — the **last row of the
target's own cluster** — so the existing remove-then-insert math (unchanged) lands the dragged rows
immediately after the target's entire cluster instead of after just its first row. Both
`ReorderEditorBlock` and `ScribePinOrdering.Reorder` call this once, right before the
`MoveBlock`/`MoveRange`/slice-move call, and `ReorderEditorBlock`'s `newStart` (used to re-target
keyboard focus after the move) is recomputed from the *resolved* destination, not the raw `to`, so
focus tracking stays correct too.

This adjustment is a no-op whenever the target has no children (single-row cluster, the common case
today), so it doesn't change any previously-correct behavior — it only fixes the specific
forward-onto-a-parent-with-children case. Validity (`IsValidDropTarget`) is still evaluated against
the raw hovered `toIndex`, not the resolved destination — the arrow and no-op detection describe "is
this row a legal target," which doesn't change; only the actual insertion math needed the fix.

### Pin Tab: `OnPinReorder` must build its working copy from the ON-SCREEN order, not raw storage

Playtest surfaced a third bug, Pin-Tab-only: `OnPinReorder(from, to)` receives row indices from the
Pin Tab's rendered rows (`ScribePinnedContentState.Build`'s `items`, built from `Widget.Rows` — i.e.
`BuildPinnedContent`'s `orderedPins`). But it built its working copy from `modSystem.MyPins` — the
per-player list's raw storage order. Those two orders are only the same list when the completion
policy doesn't sink done pins; under the DEFAULT policy (`Sink`), `OrderedPinsForDisplay()` returns
`ScribePinOrdering.ForDisplay(MyPins)`, which sinks done pins below not-done ones. Whenever a done pin
is mixed in with not-done ones, the two orders diverge, so `from`/`to` (on-screen indices) landed on
the wrong pins in the raw-order working copy — the drag silently moved/validated against a different
row than the one under the cursor. The editor has no equivalent split (`scratch.Blocks` IS the render
order, always), so this bug was Pin-Tab-only and invisible in a test session with no completed pins
mixed into the pinned set.

Fix: `OnPinReorder` now builds its working copy via `OrderedPinsForDisplay().ToList()` — the exact
same list `BuildPinnedContent` renders rows from — so the indices it receives always index the list it
operates on. `ToList()` is required either way: the non-sunk branch of `OrderedPinsForDisplay` returns
`modSystem.MyPins` by reference, and `ScribePinOrdering.Reorder` mutates its list argument in place.

## Risks / Trade-offs

- [A depth-1 pin can still be dragged to sit under a *different* pinned parent's cluster, since it's
  a leaf and both positions are depth 1] → Explicitly a non-goal (matches the editor's existing
  accepted behavior for document subtasks); not a regression, and not what the player asked to
  restrict.
- [Building a `depths` list on every render-time `Build` call during a drag is extra allocation per
  frame] → Bounded by realistic row counts (dozens); the editor and Pin Tab already rebuild their
  full row-widget list every `Build` call for other reasons, so this is a marginal addition, not a
  new class of cost.
- [Pin Tab's permutation is still entirely client-computed and client-trusted, same as today] →
  Unchanged trust boundary; a malicious client could still send an invalid permutation directly via
  the network message, bypassing `Reorder` entirely — but that's a pre-existing gap in
  `ScribeReorderPinsMessage` handling, out of scope for this change (server-side validation is a
  Non-Goal above).

## Open Questions

None — behavior, ownership, and the shared-predicate boundary are all resolved above.
