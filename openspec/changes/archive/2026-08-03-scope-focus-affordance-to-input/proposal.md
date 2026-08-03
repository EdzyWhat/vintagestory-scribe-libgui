## Why

The `lectern-gui-shell` spec already requires that a text field's focus indicator be scoped
to "that field specifically, not on the row as a whole." The normal Lectern/Notebook editor
path honors this — the focus box is drawn by the field's own render widget. The tablet
(cuneiform) editor path does not: it draws the focus border + background on the whole-row
`Container`, the same container that carries the pinned-row tint. On a pinned row the two
affordances become the same shape, so the `Primary` focus border and the `Secondary` pinned
wash can no longer be told apart — the 2026-08-03 playtest fail `f640f9ab`
(`add-tablet-clay-type-themes` task 6.8).

## What Changes

- On the tablet cuneiform editor path, move the focus border / fill / corner-radius from the
  whole-row `Container` onto the cuneiform text field's own render-widget box, so focus is
  drawn around just the input element — matching the normal Lectern/Notebook path, which
  already does this.
- After the move, the editor row `Container` only ever carries the pinned-row tint and the
  drag-source/drop-target washes (never the focus chrome), so a focused input on a pinned row
  shows a small `Primary`-bordered input inside the row's `Secondary` pinned wash — two
  distinct shapes.
- No change to the normal (non-cuneiform) path, to the pinned tint, or to the drag washes.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: extends the existing "Focus ring is scoped to the active field"
  requirement so it explicitly holds on the tablet cuneiform path and remains distinguishable
  from the pinned-row wash when a pinned row's input is focused.

## Impact

- `src/Mod/ScribeEditorContent.cs` — the `focusedCuneiform` row-`Container` fill/border/corner
  logic (~lines 763–782) is removed; the row `Container` reverts to carrying only the pinned
  tint + drag washes.
- `src/Mod/ScribeMultilineField.cs` — the cuneiform render-widget box (currently hard-zeroed,
  ~lines 1140–1143) gains the focus border/fill/corner, mirroring the normal path's field box
  (~lines 1169–1172). `focusNode.HasFocus` and the cuneiform flag are already available there.
- No Core changes; no new dependencies. Visual-only; no persistence/network impact.
