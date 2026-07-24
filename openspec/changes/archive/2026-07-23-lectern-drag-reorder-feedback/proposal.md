## Status: SUPERSEDED by adopt-libgui-foundation (2026-07-23)

**Never implemented (0/18); the ON-HOLD note below is now resolved as superseded.** The drag-reorder
lift-ghost / insertion-indicator / drop-settle feedback here was to be built on the native
custom-drawn `ScribeRowElement` row infrastructure — the exact machinery LibGUI replaces. Drag-reorder
returns under LibGUI via `ValueKey` row identity as part of the future "affordance columns +
drag-reorder" change (migration guide step 3). Archived without syncing its `lectern-gui-shell` delta
into `openspec/specs/`. Kept for the record only.

> **ON HOLD (2026-07-20):** folded into the `lectern-row-list-rework` exploration — the
> lift-ghost/insertion-indicator/drop-settle here will build on the unified custom-drawn row
> infrastructure that exploration is scoping, rather than on the current rows. Do not
> implement standalone; revisit after that exploration resolves.

## Why

The lectern editor already supports mouse-drag reordering of task/note rows, but it gives
**no visual feedback during the drag**: the row list stays completely static while the
mouse moves, and the reorder only happens — invisibly — on release. The player has no way
to see which row they're holding or where it will land until they let go and the list
recomposes. Playtesting (2026-07-20) flagged this as a high-priority gap: drag-to-reorder in
familiar apps shows the grabbed item lifting and a clear indicator of the drop target.

## What Changes

- While a row is being dragged (via its existing drag handle), a **lift-ghost** appears: a
  semi-transparent, non-interactive copy of the dragged row's content that follows the
  cursor. The original row stays in place but is visually **dimmed** to read as "picked up."
- A **live insertion indicator** (a horizontal marker/arrow drawn in the gap between two
  rows) shows exactly where the dragged row will drop if released now, and **updates
  continuously** as the cursor moves over different rows — driven by the `hoverTargetIndex`
  the dialog already tracks on every mouse-move.
- On release, the ghost **eases** (a short tween) into the resolved drop slot, then the
  actual `MoveBlock` + recompose swaps the real rows into their new order.
- Non-goals for this change (deliberately, to keep scope tight): the non-dragged rows do
  **not** shift/spread to open a physical gap (the "just the row lifts" model chosen in
  scoping); no drag-reorder in the read view (editor-only, as today); no change to the
  underlying `ScribeDocument.MoveBlock` model or its persistence/networking.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: the row-list drag-reorder interaction gains required live visual
  feedback (lift-ghost, insertion indicator, eased drop-settle). Today the spec's row-list
  requirements cover scrolling, hover icons, and the pin affordance but say nothing about
  reorder feedback; this adds that behavior as a requirement.

## Impact

- **Code:** `src/Mod/GuiDialogScribeLectern.cs` (drag lifecycle: `OnRowDragMouseDown`,
  `OnMouseMove`, `OnMouseUp`, plus a new per-frame render hook for the ghost/indicator/tween
  via the existing `OnRenderGUI` override). Likely one or two new small custom
  interactive-pass GUI elements (a ghost element and an insertion-indicator element),
  mirroring the existing `ScribeDragHandleElement`/`ScribeHoverIconButton` pattern in
  `src/Mod/ScribeBlockRowCell.cs`.
- **Rendering constraint:** the ghost and indicator must be drawn in the **interactive
  render pass** (redrawn per frame), not baked static content — the same static-vs-
  interactive split documented in `VSAPI-NOTES.md` and the archived `skeuomorphic-lectern-gui`
  design. Static-baked content cannot move per-frame, so the moving pieces are necessarily
  custom per-frame elements.
- **No Core changes:** `src/Core/` is untouched; this is purely a client-side GUI feedback
  layer over the existing reorder operation. No new dependencies. No persistence/network
  changes, so no codec version bump and no Atlas integration-test surface (client-visual
  only; covered by manual playtest).
