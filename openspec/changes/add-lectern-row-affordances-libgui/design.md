## Context

The LibGUI lectern editor (`src/Mod/GuiDialogScribeLecternLibGui.cs`) renders each editor row as
a `ScribeEditRow` = a `Checkbox` (task rows) + an `Expanded(ScribeMultilineField)`, inside a
non-virtualized `SingleChildScrollView` + `Column` of `ValueKey<int>`-keyed rows. The read view is
a virtualized `ListView` of `ScribeReadRow`. The document model (`src/Core/ScribeDocument`) already
implements and unit-tests `DeleteBlock(int)`, `MoveBlock(int,int)`, `TogglePinned(int)` and carries
`ScribeBlock.Pinned` — none are called from `src/Mod`. Editor mutations flow through the dialog's
lock-gated autosave (`FlushIfDirty` → `ScribeEditDocumentMessage`), and structural changes rebuild
the editor tree via `ForceRebuild()` (which preserves keyboard focus through the dialog-owned
`editorFocusNodes` + `FocusManager`, but re-seeds each field's caret to end-of-text).

LibGUI research (against `reference/vslibgui`) confirmed the primitives needed:
- **Pointer capture is automatic.** `EventDispatcher` captures the hit element on pointer-down and
  routes every subsequent `OnPointerMove` and the terminal `OnPointerUp` to it regardless of what
  the cursor is over — the same mechanism `Scrollbar`'s thumb drag relies on. No manual capture call.
- **`GestureDetector`** (`Widgets/Input/GestureDetector.cs`) exposes `onPress`/`onMove`/`onRelease`
  (plus `onDragEnter`/`onDragExit` for drag-aware hover on sibling elements). `PointerEvent` carries
  global `X`/`Y`; `RenderObject.GlobalToLocal` converts to local space; `Scrollable.ComputeContentSpaceY`
  is the worked pattern for summing child offsets to a content-space Y.
- **`VsIcon(iconName, size, color)`** (`Widgets/Basic/VsIcon.cs`) renders a VS icon *by code*, and
  `DrawIconInt` resolves `CustomIcons` first — so the mod's already-registered `scribepin`/`scribegrip`/
  `scribeclose` glyphs render inside LibGUI widgets with **zero new rendering code**.
- **`IconButton(Icon, color, onTap)`** gives a clickable icon with built-in hover feedback;
  **`Divider`** and **`Container`+`BoxStyle`** (tint/border) provide drop-position feedback.
- There is **no** built-in reorderable list in LibGUI — it must be built from these primitives.

## Goals / Non-Goals

**Goals:**
- Wire per-row **delete**, **pin/unpin**, and **mouse-drag reorder** to the existing Core mutations
  in the LibGUI editor view, routed through the existing lock-gated autosave.
- Drag reorder shows a **drop-position indicator** and moves the row on release; release-in-place is
  a no-op.
- Pinned tasks show a **resting indicator** in both views; per-row controls stay hover-conditional
  and scale with the text-size preference (consistent with `lectern-gui-shell`).
- No new network protocol, no Core changes, no new mod dependency.

**Non-Goals:**
- HUD rendering of pinned tasks (a later tier).
- Text-section creation control (removed earlier by design).
- Multi-select, cross-document drag, or drag between read and editor views.
- Keyboard reorder/delete (mouse-drag is the chosen reorder input; delete/pin are click controls).
- Pixel-faithful reproduction of the native affordance visuals — honor their layout intent, rebuilt
  idiomatically on LibGUI.

## Decisions

### D1. Reorder via a grip `GestureDetector`, using automatic pointer capture
Attach a `GestureDetector(onPress, onMove, onRelease)` to each editor row's grip control (the
reserved drag-handle column). `onPress` records the dragged block index and marks a drag active;
because the dispatcher auto-captures the grip element, `onMove`/`onRelease` keep arriving even as the
cursor moves over sibling rows (exactly as `Scrollbar`'s thumb works). On `onMove`, convert
`PointerEvent.(X,Y)` into the editor `Column`'s local space via `GlobalToLocal` and walk the mounted
children summing `Size.Y` to find the hover/drop index (the non-virtualized editor Column keeps every
row mounted, so this is stable and matches `ComputeContentSpaceY`). On `onRelease`, if the drop index
differs from the start index, call a new dialog method wrapping `scratch.MoveBlock(from, to)` →
`SyncFocusNodesToScratch()` → mark dirty → `ForceRebuild()`; otherwise no-op.

*Alternative considered:* per-row `RenderObject` handles via `onLayout` callbacks and hit-testing each
row's `GlobalToLocal`. Rejected as more bookkeeping than the single-Column walk, which the
non-virtualized editor layout already makes cheap.

*Why the grip, not the whole row:* dragging the whole row would conflict with click-to-edit and text
selection in `ScribeMultilineField`. Confining the drag start to the grip column keeps the field's
pointer interactions intact.

### D2. Drop feedback via an inserted `Divider` (and/or a tinted target `Container`)
During an active drag, `SetState` to render a thin `Divider` at the computed drop index in the editor
`Column` (and optionally tint the dragged row's `Container`). This is in-list, clips with the scroll
viewport, and needs no overlay. A floating drag-ghost via `Stack`/global overlay is possible but
deferred — the insertion line is the minimum that satisfies the spec's "indicate the prospective drop
position."

### D3. Delete and pin as `IconButton(VsIcon(...))`, reusing registered glyphs
Delete = `IconButton(new VsIcon("scribeclose", ...), onTap: …)`; pin = `IconButton(new VsIcon("scribepin", ...))`.
These call new dialog methods wrapping `scratch.DeleteBlock(index)` / `scratch.TogglePinned(index)`,
mirroring the existing `OnClickAddTask` (mutate scratch → mark dirty → `SyncFocusNodesToScratch` →
`ForceRebuild`). Using `VsIcon` by code reuses the already-registered `CustomIcons` with no new
rendering code (research §6). The grip is a non-button `VsIcon("scribegrip", ...)` wrapped in the D1
`GestureDetector`.

*Delete + focus safety:* deleting a row shrinks the focus-node list; `SyncFocusNodesToScratch()`
disposes the trailing node and the subsequent `ForceRebuild()` rebinds the rest. Clear/relocate
`focusedEditIndex` if it pointed at or past the deleted row so no path focuses a removed row (the spec's
"deleting the focused row does not break focus").

### D4. Pin toggle is editor-only over autosave; read view shows a resting indicator but no toggle
Pinning goes through the editor's lock-gated autosave (`ScribeEditDocumentMessage`), like every other
editor mutation — no new packet. The read view does **not** get a pin *toggle*: unlike the task-done
toggle (a deliberately lock-free, always-allowed viewer action with its own `ScribeToggleTaskMessage`),
pinning is an authoring action that belongs behind the editor lock. The read view only *reflects* pinned
state via the resting indicator (D5).

*Alternative considered:* a lock-free read-view pin toggle paralleling `ScribeToggleTaskMessage`.
Rejected for v1 — it widens the always-allowed action surface and adds a packet type for a
non-essential convenience; revisit if players ask for it.

### D5. Resting pin indicator: a subtle row tint in both views
Render a subtle background tint on a pinned task row (both read and editor), drawn under the row
content, so a pinned task reads as pinned without hovering — matching the native
`PinnedIndicatorMode.RowTint` intent (its config knobs still exist in `ScribeClientConfig`). Applied in
`ScribeReadRow`/`ScribeEditRow` build from the row's `Pinned` snapshot.

### D6. Hover-conditional controls and text-size scaling
Per-row delete/pin/grip controls are hidden unless the row is hovered (per the existing
`lectern-gui-shell` "Row icons are hover-conditional" requirement), tracked with `onEnter`/`onExit` on
the row. Control sizes derive from the existing `ScribeRowStyle`/text-size scale so they grow and
shrink with the row, consistent with the checkbox.

## Risks / Trade-offs

- **Drag index math vs. scroll offset** → The editor `Column` sits inside a `SingleChildScrollView`;
  the hover-index walk must use content-space Y (via `GlobalToLocal` into the Column, which already
  accounts for the viewport translation), not raw screen Y. Mitigation: reuse the `ComputeContentSpaceY`
  pattern verbatim; test with the list scrolled.
- **Rebuild-on-drop caret reset** → `ForceRebuild()` re-seeds the focused field's caret to end-of-text
  (the residual half of task 8.5). A reorder/delete generally isn't mid-typing, so impact is low; if it
  grates, capture/restore the caret as `EnterEditorMode` already tracks the focused index. Out of scope
  to fully fix here.
- **Hover flends with drag capture** → While a drag holds capture, sibling `onEnter`/`onExit` behave
  differently (the dispatcher fires drag-hover instead). Mitigation: drive the drop indicator from the
  drag's own `onMove` index computation, not from sibling hover callbacks.
- **Deleting the focused/last row** → covered by D3's focus-index cleanup and the empty-state hint the
  editor already renders for a zero-block document.
- **`VsIcon` caching by (name, size)** → sizes come from the scaled row style; a continuous text-size
  range could thrash the texture cache. Low concern (text size changes rarely, per-open), but worth a
  glance if a perf issue appears.

## Open Questions

- Exact visual grammar for the drop indicator (insertion line only vs. line + dragged-row tint vs. a
  floating ghost) — settle during implementation against a quick in-game look; the spec only requires
  that the prospective drop position be indicated.
- Final resting-tint alpha/hue (the native value was left "loud" for visibility testing) — tune in-game.
- Whether delete needs a confirm step for a non-empty row — default to no confirm (fast, reversible by
  undo-less re-add); revisit only if playtest finds accidental deletes common.
