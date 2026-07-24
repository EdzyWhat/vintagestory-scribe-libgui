## Status: SUPERSEDED by adopt-libgui-foundation (2026-07-23)

**Implemented, but retired; not carried into the LibGUI rebuild.** This first Notion-style affordance
pass redrew the per-row pin/delete/grip buttons as custom-drawn `ScribeHoverIconButton`/`ScribeRowElement`
controls — native `GuiComposer` machinery LibGUI replaces wholesale with flex `Row` + `IconButton` +
theme states. Superseded together with its follow-up `refine-row-affordance-visuals-2`. Archived
without syncing its `lectern-gui-shell` delta into `openspec/specs/`. Kept for the record only.

## Why

`restore-row-affordance-columns` brought the per-row pin/delete/grip affordances back and playtested
green, but the tester flagged six visual defects: the buttons wear Vintage Story's heavy brown chrome,
stretch to the full height of multi-line rows, sit in a reserved gutter that permanently narrows the
text, and their SVG icons render too small; the drag grip is on the far right rather than a natural
left-edge grab point; and the focused input's highlight touches the ruling with no bottom margin. The
target is a clean Notion-style affordance — thin-outline buttons that float over the text on hover.

## What Changes

- Redraw the per-row affordance buttons as minimal custom-drawn controls: a thin ink-tone outline, an
  opaque parchment background that occludes the text beneath on hover (Notion behavior), and a larger
  icon that fills most of the button — replacing VS's brown `GuiElementToggleButton` chrome.
- Size each button to a single text line's height (top-aligned on the row) rather than the full
  multi-line row height.
- Make pin/delete an **overlay**: the text runs full-width and the buttons float over its right end on
  hover, instead of a reserved gutter that permanently narrows the text column. **BREAKING** (spec):
  editor rows no longer reserve pin/delete gutter *width*.
- Move the drag grip to a dedicated far-left column (left of the checkbox); the read view reserves the
  same column width (drawing no grip) so the checkbox/text do not shift between views.
- Give the focused input a symmetric top/bottom margin against the ruling, and center single-line row
  content within its floored min-height.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: the pin/delete affordances become a hover overlay that does not reserve text
  gutter width (amends "Row icons are hover-conditional"); the drag-handle column moves to the row's
  far left and the read view reserves matching width (amends "Editor rows reserve a drag-handle
  affordance column").

## Impact

- Code: `src/Mod/ScribeBlockRowCell.cs` (`ScribeHoverIconButton` → self-drawing button; `RowHeight` as
  single-line source), `src/Mod/ScribeRowElement.cs` (draw template reuse, content centering, single-
  line height helper, yield boundary, checkbox/text X), `src/Mod/RowTextLayout.cs` (full-width text +
  overlay anchors + drag-column relocation), `src/Mod/GuiDialogScribeLectern.cs` (button/grip/input
  bounds), `src/Mod/ScribeClientConfig.cs` (new affordance-styling knobs).
- No Core change (the affordance visuals live entirely in the Mod GUI layer).
- No new dependencies; vanilla `VintagestoryAPI` only.
- The grip stays a visual no-op — actual drag-to-reorder feedback remains out of scope.
