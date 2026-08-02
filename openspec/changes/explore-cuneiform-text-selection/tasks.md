## 1. Exploration

- [x] 1.1 Flesh out this exploration (resolve design.md Open Questions) before implementing. — done
      2026-08-02: both Open Questions resolved (per-line `DrawBox` rects for wrapped highlight; clipboard
      copy already yields plain text via the shared buffer). Key finding: no new Core range→X map is
      needed (the shipped `CuneiformLine.CharBoundaries`/`CaretXAt` already covers it) and the selection
      model + gestures are already live on the cuneiform path — the only gap is rendering the highlight +
      threading the anchor/color. proposal.md + design.md updated to match; implementation tasks below.

## 2. Render the selection highlight

- [x] 2.1 Add a `SelectionAnchor` (int) and `SelectionColor` (Vector4) property to
      `ScribeCuneiformFieldRender` and `ScribeCuneiformFieldRenderWidget`, mirroring the normal
      `ScribeMultilineFieldRender` (repaint on change; no relayout). — done.
- [x] 2.2 In `ScribeCuneiformFieldRender.PaintInternal`, before the stroke pass, draw the selection: when
      focused and `selEnd > selStart`, loop the cached `lines`; for each line clamp the selection to
      `[SourceStart, SourceStart + (CharBoundaries.Count - 1)]`, and if non-empty `DrawBox` from
      `padX + CaretXAt(a-lineStart)·scale` to `padX + CaretXAt(b-lineStart)·scale` at
      `y = padY + i·lineHeightPx`, height `lineHeightPx`, in `SelectionColor`. Mirror the normal field's
      loop structure exactly (`CaretXAt` replaces `MeasureWidth`). — done.
- [x] 2.3 In `ScribeMultilineFieldState.Build()`, thread `selectionAnchor: anchor` and
      `selectionColor: colors.Primary with { W = 0.35f }` into the `ScribeCuneiformFieldRenderWidget`
      branch (currently passed only to the normal widget). — done. Also threaded the title field
      (`ScribeCuneiformTitleField`, which builds the same widget): `selectionAnchor` = its controller's
      `Selection.BaseOffset`, same selection color.

## 3. Validation

- [x] 3.1 Verify the single-line title band highlights correctly and overflow past the band is clipped by
      the existing enclosing `Clip` (no new clip logic). — done in code: the title field's single-line
      widget now receives the anchor/color and runs the same one-line highlight loop; the band's enclosing
      `Clip` (unchanged) clips overflow. Live confirmation folded into 3.3.
- [x] 3.2 Build (`0 errors`) and run the Core suite (no Core changes expected to break; confirm still
      green). — done: Mod builds 0 errors; Core 250 pass / 0 fail (no Core changes, as designed).
- [ ] 3.3 Manual in-game check (tablet): shift-arrow, drag-select, double-click word, triple-click line all
      show a highlight behind the cuneiform strokes on both a single-line row and a wrapped multi-line row;
      the title band highlights and clips; Ctrl+C copies the underlying plain text. Record in TESTING.md.
- [x] 3.4 Update the caret-only note in `ScribeCuneiformField.cs`'s header comment (it currently says
      "SelectionAnchor is accepted but not highlighted here") once the highlight lands. — done.
- [x] 3.5 Run `openspec validate explore-cuneiform-text-selection --strict` and reconcile. — valid (2026-08-02).
