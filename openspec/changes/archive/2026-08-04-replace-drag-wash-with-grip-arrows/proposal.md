## Why

Drag-reorder feedback and the pinned-row highlight are both whole-row background tints
derived from theme colors: the drop-target row paints a darkened `Primary` wash, the
source row a brightened `Primary` wash, and a pinned row paints a `Secondary` wash. That
was tolerable while the pinned wash was faint (alpha 0.33), but the pinned wash was just
strengthened for readability (alpha → 0.55 plus a saturation boost), and now the darkened-
`Primary` drop-target wash and the pinned `Secondary` wash read as nearly the same block of
color in some theme/row combinations. During a drag over a pinned row the player can no
longer tell "this row is pinned" from "the task will drop here."

Rather than keep hunting for a wash hue that stays distinct from every theme's `Secondary`
(fragile), this change moves drag feedback **off the row background entirely** and onto the
grip glyphs — the one element already guaranteed to be mounted throughout a drag (it holds
the pointer capture). Nothing tinted competes with the pinned wash anymore.

## What Changes

- While a grip-drag is in progress, editor and pinned-view rows SHALL stop painting the
  source/drop-target background washes. The pinned-row wash still paints (pinned state is
  independent of dragging), so a pinned row stays visibly pinned even mid-drag.
- The **source** row (the one grabbed) shows its grip glyph as a left-pointing triangle
  (◀) and dims its whole row to ~50% opacity, reading as "lifted / in hand."
- **Every other** row hides its grip glyph while the drag is active, decluttering the list
  down to just the two rows that matter.
- The row the cursor is currently drag-hovering (the prospective drop) shows its grip glyph
  as a right-pointing triangle (▶), marking where the task lands.
- The reorder model is unchanged: it remains a move (extract + reinsert via
  `ScribeDocument.MoveBlock(from, to)`, list reflows — not a swap), commit still happens on
  release, and a release on the origin is still a no-op.
- Applies identically to the editor view (`ScribeEditorContent`) and the Pin Tab view
  (`ScribePinnedContent`), which share the same drag state machinery and grip rendering.
  The read view is non-reorderable and unaffected.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: the "Editor rows reserve a drag-handle affordance column"
  requirement's drop-feedback behavior changes — the prospective drop position and the
  drag source are now indicated by grip-glyph arrows (▶ target, ◀ source) plus a dimmed
  source row and hidden non-participant grips, instead of by highlighted/washed row
  backgrounds, so drag feedback never collides with the pinned-row wash.

## Impact

- `src/Mod/ScribeEditorContent.cs` — remove the `dragShift` source/target wash branches
  from the row `Container` fill/border (~lines 752–782); drive the grip glyph and the
  row-dim from `IsDragSource` / `IsDropTarget` / a new "drag active" signal instead.
- `src/Mod/ScribePinnedContent.cs` — mirror the same change (~lines 348–401).
- `src/Mod/ScribeModSystem.Assets.cs` — register two new SVG glyphs (left/right triangle);
  the icons dir has no arrow glyph today.
- `src/Mod/assets/scribe/textures/icons/` — add `triangle-left.svg` and
  `triangle-right.svg` (or a single triangle reused at two rotations).
- Rows need to know a drag is active even when they are neither source nor target (to hide
  their grip); this is a new per-row input threaded from the same parent drag state that
  already produces `IsDragSource`/`IsDropTarget`.
- No Core change (reorder logic untouched), no new dependencies, no persistence/network
  impact — purely the drag-feedback presentation.
