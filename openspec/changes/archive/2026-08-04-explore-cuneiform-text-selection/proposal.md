## Why

Live cuneiform input shipped (add-tablet-cuneiform-chrome) with caret-only editing. The selection
MODEL is already live on the cuneiform path — `ScribeMultilineFieldState` owns the `anchor`/`caret`
range and drives shift-arrow extension, drag-select, and double/triple-click word/line select for both
render objects through the shared `IScribeEditableTextRender` contract. **Confirmed in the 2026-08-02
playtest:** shift-arrow already *extends* the selection in a cuneiform row, but there is no visual
feedback — the selected range is invisible over the strokes. That render-side gap is why selection was
scoped out of add-tablet-cuneiform-chrome rather than shipped half-visible; this change closes it.

The exploration (task 1.1) is now resolved (see design.md): the work is smaller than first assumed —
no new Core layout API is needed (the caret's per-character `CharBoundaries`/`CaretXAt` map already
yields any boundary's pixel-X, and a selection is just two boundaries), and clipboard copy already
returns plain text via the shared buffer. The remaining work is purely rendering the highlight and
threading the selection anchor/color into the cuneiform render object.

## What Changes

- Paint a selection-highlight box behind the cuneiform strokes for the current selection range,
  reusing `ScribeMultilineFieldState`'s existing `[min(anchor,caret), max(anchor,caret)]` range (no new
  selection state, no gesture changes — those are already shared and live).
- Add `SelectionAnchor` + `SelectionColor` to `ScribeCuneiformFieldRender`/`…Widget` (mirroring the
  normal render object) and thread `anchor` + `colors.Primary @ 0.35` into the cuneiform widget in
  `ScribeMultilineFieldState.Build()` (currently passed only to the normal widget).
- Highlight rendering is a per-line port of the normal field's existing selection loop: clamp the
  selection to each wrapped line's `[SourceStart, SourceStart+count]` and draw one `DrawBox` spanning
  `CaretXAt(a)…CaretXAt(b)` per line. Covers wrapped rows and the single-line title band (clipped by
  the band's enclosing `Clip`).
- No Core changes: the range→X mapping reuses the shipped `CuneiformLine.CharBoundaries`/`CaretXAt`.

## Capabilities

### New Capabilities
- `cuneiform-text-selection`: selecting a text range highlights the corresponding cuneiform strokes.

### Modified Capabilities
<!-- none -->

## Impact

- `src/Mod/ScribeCuneiformField.cs` — new `SelectionAnchor`/`SelectionColor` properties + the
  per-line highlight paint loop (before the stroke pass).
- `src/Mod/ScribeMultilineField.cs` — thread `selectionAnchor`/`selectionColor` into the cuneiform
  render widget in `Build()`.
- No Core changes, no new dependencies, no persisted-state or sync changes (selection is client-only).
- Highlight color contrast over the tablet palette is verified at playtest (may re-tune once the real
  clay/wax backdrops land — add-tablet-clay-type-backdrops).
