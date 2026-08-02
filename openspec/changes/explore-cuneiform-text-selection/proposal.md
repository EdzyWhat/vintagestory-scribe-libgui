## Why

> **EXPLORE STUB — breadcrumb only, not ready to implement.** Captures a deferred
> capability so it isn't lost; flesh out before building.

Live cuneiform input is shipping with caret-only editing (type, backspace, arrow-nav,
click-to-place caret) but **no text selection**. The underlying `ScribeMultilineFieldState`
already tracks a selection anchor/range (it drives the normal field); the gap is purely on
the cuneiform RENDER side, which has no way to paint a highlight over the glyph strokes.

## What Changes

- Add text-selection over cuneiform in the tablet's editable rows/title, reusing the
  existing `ScribeMultilineFieldState` selection model (no new selection state).
- Selection gestures: shift-arrow extension, drag-to-select, double-click word, triple-click line.
- Render a selection-highlight box behind the cuneiform strokes for the selected range.
- Needs a range→pixel-X map in Core cuneiform layout (`src/Core/Cuneiform/CuneiformLineLayout.cs`),
  analogous to the caret's char→X map added now.

## Capabilities

### New Capabilities
- `cuneiform-text-selection`: selecting a text range highlights the corresponding cuneiform strokes.

### Modified Capabilities
<!-- none -->

## Impact

- `src/Core/Cuneiform/CuneiformLineLayout.cs` (new range→X map).
- Cuneiform render path in the tablet dialog (highlight box).
- No new dependencies; no persisted-state or sync changes (selection is client-only).
