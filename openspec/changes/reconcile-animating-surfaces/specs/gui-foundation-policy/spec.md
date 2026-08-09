## ADDED Requirements

### Requirement: Reconcile is the default update path for animating surfaces
For a Scribe GUI surface that animates or that hosts interactions spanning an update (hover-gated
controls, an active caret, a press-then-release gesture), content changes SHALL be pushed by
reconciliation — a persistent content `StatefulWidget` updated via `SetState` — rather than by
`GuiBase.ForceRebuild()`. `ForceRebuild()` unmounts and recreates the entire widget tree, disposing
every `State`, `AnimationController`, and `RenderObject` and orphaning the pointer-capture the event
dispatcher holds as a concrete element reference; reconciliation preserves those matching elements
(and their identity) across the update. `ForceRebuild()` SHALL be reserved for genuinely-new trees —
switching between distinct views, seeding a fresh editor, lost-lock recovery — and for dev hot-reload.

#### Scenario: An animating surface updates by reconcile, not full rebuild
- **WHEN** a converted animating surface (e.g. the editor) changes its content in place (add, delete,
  reorder, toggle) while nothing about the surface's identity should reset
- **THEN** the surface updates via `SetState` on its persistent content, preserving hover, focus/caret,
  pointer-capture, and in-flight animation controllers, rather than calling `ForceRebuild()`

#### Scenario: ForceRebuild is retained for a genuinely-new tree
- **WHEN** the surface switches to a genuinely different tree (a different view, a fresh editor seed, or
  lost-lock recovery)
- **THEN** `ForceRebuild()` is still used, because there is no identity to preserve across that change

### Requirement: Reconcile-hosted rows are keyed by stable identity and never swap type at a slot
A surface converted to reconcile SHALL key its rows by stable logical identity (the row's TaskId, not
its array index), and SHALL keep the same widget type at a given slot across a row's state transitions
(for example, a departing/collapsing row is an internal state of one stable row widget, not a
different widget type spliced into that slot). This is required because LibGUI reconciliation reuses an
element only when its type and key match at its position; an index-based key that shifts, or a
type-swap at a slot, silently destroys that subtree's `State` (caret, focus, optimistic flags) exactly
as a full rebuild would.

#### Scenario: A row keeps its state across a list mutation because its key is stable
- **WHEN** rows are added, removed, or reordered on a reconcile-hosted surface
- **THEN** a surviving row that is being edited keeps its `State` (caret position, in-progress text,
  focus) because it is matched by its stable TaskId key rather than a shifting index

#### Scenario: A departing row does not change widget type at its slot
- **WHEN** a row transitions into its departing/collapsing animation
- **THEN** the slot keeps the same widget type (the transition is an internal state of the stable row
  widget), so reconciliation does not tear down and remount the subtree
