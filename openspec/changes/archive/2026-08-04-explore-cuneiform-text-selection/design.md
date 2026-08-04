## Context

Live cuneiform input ships with caret-only editing. The SELECTION MODEL is already fully live on the
cuneiform path — `ScribeMultilineFieldState` owns `text`/`caret`/`anchor` and drives shift-arrow
extension, drag-select, and double/triple-click word/line select for BOTH render objects, resolving
click positions through the shared `IScribeEditableTextRender.OffsetAtPosition` contract that
`ScribeCuneiformFieldRender` already implements. The ONLY gap is on the render side: the cuneiform
render object never paints a highlight and `ScribeMultilineFieldState.Build()` doesn't even hand it the
`anchor`/selection color (it passes those only to the normal render widget).

## Goals / Non-Goals

**Goals:** Paint a selection highlight behind the cuneiform strokes for the current selection range,
reusing the existing State selection model unchanged. Cover wrapped multi-line rows and the single-line
title band.

**Non-Goals:** No new selection state model. No new Core layout API (the char→X map already covers it —
see Decisions). No sync/persistence changes (selection is client-only). No change to gesture handling
(already live). Text selection stays a tablet-only affordance (the cuneiform path is tablet-only).

## Decisions

- **Reuse `ScribeMultilineFieldState` selection range** (`[min(anchor,caret), max(anchor,caret)]`); do
  not invent parallel state. The gesture layer (`OnFieldPress`/`OnFieldMove`, word/line boundaries) is
  already shared across both render paths — nothing to add there.
- **No new Core range→X map.** The exploration's original premise (add a range→pixel-X map to
  `CuneiformLineLayout`) is unnecessary: the caret's per-character `CuneiformLine.CharBoundaries` +
  `CaretXAt(localIndex)` (shipped by add-tablet-cuneiform-chrome) already yields the pixel-X of any
  boundary, and a selection is just two boundaries. Selection reuses that map verbatim.
- **Highlight is a per-line port of the normal field.** In `ScribeCuneiformFieldRender.PaintInternal`,
  before drawing strokes, loop the cached `lines`; for line *i* with `lineStart = SourceStart` and
  `count = CharBoundaries.Count - 1`, clamp the selection to `[lineStart, lineStart+count]`, then draw
  one `DrawBox` at `x = padX + CaretXAt(a-lineStart)·scale` … `padX + CaretXAt(b-lineStart)·scale`,
  `y = padY + i·lineHeightPx`, height `lineHeightPx`. This is the exact structure of
  `ScribeMultilineFieldRender.PaintInternal`'s selection loop, with `CaretXAt` replacing `MeasureWidth`.
- **Thread `SelectionAnchor` + `selectionColor` into the cuneiform path.** Add a `SelectionAnchor`
  property to `ScribeCuneiformFieldRender`/`…Widget` (mirroring the normal render), and in
  `ScribeMultilineFieldState.Build()` pass `selectionAnchor: anchor` and
  `selectionColor: colors.Primary with { W = 0.35f }` to the cuneiform widget too (currently only the
  normal widget receives them).
- **Single-line title band:** the same loop runs with one line; the enclosing `Clip` already hard-clips
  overflow, so a selection extending past the visible band is simply clipped — no special case.
- **Clipboard = underlying plain text**, automatically: `CopySelection`/`Paste` operate on the shared
  `text` buffer, so Ctrl+C over a cuneiform selection already copies the plain characters. Nothing to add.

## Resolved Open Questions

- **Multi-line highlight over wrapped cuneiform?** Per-line `DrawBox` rects, one per wrapped line,
  clamping the selection to each line's `[SourceStart, SourceStart+count]` and using `CaretXAt` for the
  span endpoints — a direct port of the normal field's existing selection loop. A selection crossing a
  soft-wrap boundary naturally runs to the end of the earlier line's glyphs and from the start of the
  next, exactly as the normal field renders it (the dropped wrap-separator space carries no glyph, so
  nothing highlights in the gap).
- **Clipboard copy = underlying plain text?** Confirmed — the shared State's `CopySelection` copies
  `text.Substring(...)`, so the cuneiform path yields plain text with no extra work.

## Risks / Trade-offs

- **Low risk, render-only.** No State, Core, gesture, or persistence changes — the diff is confined to
  `ScribeCuneiformField.cs` (new `SelectionAnchor`/`SelectionColor` + the paint loop) and two threaded
  args in `ScribeMultilineField.cs::Build`. The normal field path is untouched.
- **Highlight color contrast** over the placeholder earthen tablet palette is unverified in-game; the
  selection alpha may need a tug once the real clay/wax backdrops land (tracked separately in
  add-tablet-clay-type-backdrops). Not a blocker — start from the normal field's `Primary @ 0.35` and
  confirm at playtest.
