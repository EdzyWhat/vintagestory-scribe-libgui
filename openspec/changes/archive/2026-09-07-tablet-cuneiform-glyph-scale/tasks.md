> **Revised 2026-09-07** after in-game testing showed two earlier approaches didn't deliver what was
> asked (a paint-only ink shrink that left the row unchanged; then a per-character-pivot fix for a
> caret-drift bug that paint-only approach introduced). The final mechanism shrinks the cuneiform
> render widgets' own LAYOUT height (via `PerformLayout`), deliberately decoupled from the tablet's
> `CheckboxSize`/`ControlSize` (left untouched) — see `design.md`'s History section for the full story
> of what was tried and why each was superseded. Tasks below describe the FINAL implementation; where a
> task's original wording described a superseded approach, it's been rewritten to match what actually
> shipped, not left describing dead code.

## 1. Read-only render widget (`CuneiformText.cs`)

- [x] 1.1 Add `CuneiformMetrics.GlyphDrawScale = 0.85f` next to `LineHeightRatio` in the existing
      `CuneiformMetrics` static class, and verify `dotnet build src/Mod/Mod.csproj -c Debug` succeeds.
- [x] 1.2 Add a `GlyphDrawScale` float property to `CuneiformTextRender` (`CuneiformText.cs`), mirroring
      `StrokeWeightScale`'s guard (non-positive → `1`) but wired `relayout: true` (not `repaint: true` —
      it must affect layout, since it feeds `PerformLayout`'s line-height formula, not just paint).
- [x] 1.3 In `PerformLayout`, fold `GlyphDrawScale` into the existing D7 line-height formula:
      `renderedHeight = fontSizeEm * CuneiformMetrics.LineHeightRatio * glyphDrawScale`. `scale` then
      derives from that (now smaller) height exactly as before. `BuildStrokePath` needs NO changes — it
      already just does `corners[n] * scale`, and `scale` now carries the shrink. Verify `Size` shrinks
      proportionally at `GlyphDrawScale = 0.85f` vs. `1f` for the same text/`fontSizeEm` (a deliberate
      change from the original wording, which asked to verify `Size` was UNCHANGED — that requirement
      described the superseded paint-only approach; see design.md's History).
- [x] 1.4 Thread `GlyphDrawScale` through `CuneiformTextRenderWidget` (constructor param +
      `UpdateRenderObject`) and `CuneiformText` (constructor param, default `1f`), following the exact
      threading pattern already used for `strokeWeightScale`. Verify `dotnet build` succeeds.
- [x] 1.5 Confirm the glow passes in `PaintInternal` reuse the same `BuildStrokePath` output (no
      separate un-scaled path for the glow), so the halo automatically tracks the smaller ink. Verify by
      reading the two glow/ink loops in `PaintInternal` and confirming both call the same
      (now-scaled-via-`scale`) `BuildStrokePath`.

## 2. Editable field render (`ScribeCuneiformField.cs`)

- [x] 2.1 Add the same `GlyphDrawScale` float property to `ScribeCuneiformFieldRender`, mirroring its
      existing `StrokeWeightScale` property's guard, wired `relayout: true` (see 1.2's note on why not
      `repaint: true`).
- [x] 2.2 In `PerformLayout`, fold `GlyphDrawScale` into the existing formula:
      `lineHeightPx = fontSizeEm * CuneiformMetrics.LineHeightRatio * glyphDrawScale`; `scale` derives
      from that as before. `DrawStrokePass` needs NO changes — it already just does
      `corners[n] * scale`. Because caret position, the selection-highlight box, and hit-testing
      (`OffsetAtPosition`, `CaretOffsetVertical`) all already read this SAME `scale`/`lineHeightPx`,
      they automatically stay in lockstep with the shrunk ink at any buffer length — verify by
      confirming none of those four call sites needed a separate change.
- [x] 2.3 Thread `GlyphDrawScale` through `ScribeCuneiformFieldRenderWidget` (constructor param +
      `UpdateRenderObject`), following the same pattern as task 1.4. Verify `dotnet build` succeeds.

## 3. Wire the tablet's cuneiform call sites to the new constant

- [x] 3.1 Pass `CuneiformMetrics.GlyphDrawScale` into the `new CuneiformText(...)` call sites used for
      tablet display-only text: `ScribeRowWidgets.cs:565`, `ScribeEditorContent.cs:598`, and
      `ScribeAddKindPicker.cs:230`. **Resolved with the user (2026-09-07):** `Style.UseCuneiform` is set
      `true` only by the tablet (`GuiDialogScribeTablet.cs`), so all three call sites are equally
      tablet-cuneiform-only — wired all three, not just a subset scoped to "title/row text."
- [x] 3.2 Pass `CuneiformMetrics.GlyphDrawScale` into the `new ScribeCuneiformFieldRenderWidget(...)`
      call sites used for tablet rows and title: `GuiDialogScribeTablet.cs:288`,
      `ScribeCuneiformTitleField.cs:344`, `ScribeMultilineField.cs:1330`, `ScribeRowWidgets.cs:509`, and
      `ScribeReadContent.cs:664`.
- [x] 3.3 Verify `dotnet build src/Mod/Mod.csproj -c Debug` succeeds with 0 warnings/errors after wiring
      all call sites.
- [x] 3.4 Deliberately leave `GuiDialogScribeTablet.DecorateRowStyle`'s `CheckboxSize`/`ControlSize`
      derivation (`style.FontSize * CuneiformMetrics.LineHeightRatio`, no `GlyphDrawScale` factor)
      UNCHANGED — confirmed no edit was made there. This is the decoupling the user asked for: checkbox/
      control size stays exactly as today while the cuneiform row/title band shrinks.

## 4. Verify in-game

- [x] 4.1 Restage per `build/restage.sh Debug` (client fully quit first — never restage while the game is
      running), then open a tablet and confirm: title bar and row text render visibly smaller (about
      85%) than before, AND the row/title band itself is physically shorter — NOT just smaller ink in an
      unchanged box. Checkbox/control/icon sizes should be pixel-identical to before (unchanged), which
      means the checkbox will now likely overflow above/below the shorter row — that mismatch is
      expected per design.md, not a bug.
- [x] 4.2 Confirm the editable field is unaffected functionally at any buffer length: type a full row's
      worth of text and confirm the caret position and any text selection track the actual glyphs
      exactly — no caret drift growing with character count, no mis-hit selection, and no growing/
      shrinking margin at the start of the line as you type (the two bugs found in the paint-only
      approach — see design.md's History — should not reproduce here).
- [x] 4.3 Confirm the per-material glow (`cuneiform-contrast-glow`) still tracks the ink correctly (no
      doubled/offset halo) on at least one wet and one fired tablet view.
- [x] 4.4 Confirm the `.cuneiform` dev harness and any non-tablet cuneiform surface are visually
      unchanged (still render at `GlyphDrawScale = 1f`).
- [x] 4.5 Look at the resulting checkbox/icon/counter alignment against the now-shorter row and decide
      whether `ScribeRowControlNudge.CheckboxAndGripTop`/`SingleLineInputHeight`'s nudge constants need
      retuning to look intentional rather than broken. This tuning pass is explicitly NOT part of this
      change's own scope (see design.md's Non-Goals) — flag findings for a follow-up rather than
      re-deriving those formulas here.

## 5. Caret height + field padding refinement (added 2026-09-07, mid-testing, before 4.x ever ran)

> Before section 4's in-game pass ran, the user revised the ask from a screenshot measurement:
> `GlyphDrawScale` 0.85 → 0.95 (0.85 read too aggressive once the row itself — not just the ink — was
> shrinking), plus two NEW independent knobs: the synthetic caret bar's drawn height, and the cuneiform
> field's top/bottom inner padding — both measured directly off a Photoshop screenshot (caret 48px → 44px
> against a 29px glyph; padding 9px → 6px top+bottom) and expressed as ratios (not absolute pixels) so
> they hold at any GUI scale. See `design.md`'s History for why these are separate paint-time knobs, not
> folded into `GlyphDrawScale`.

- [x] 5.1 Change `CuneiformMetrics.GlyphDrawScale` from `0.85f` to `0.95f`.
- [x] 5.2 Add `CuneiformMetrics.CaretHeightScale = 44f / 48f` and `CuneiformMetrics.FieldPadYScale =
      6f / 9f` next to `GlyphDrawScale`/`LineHeightRatio`.
- [x] 5.3 Add a `CaretHeightScale` float property to `ScribeCuneiformFieldRender` (guard + `repaint:
      true`, mirroring `StrokeWeightScale`). In `PaintInternal`'s synthetic-caret block, shrink the drawn
      bar to `lineHeightPx * CaretHeightScale`, vertically centered within its `lineHeightPx` band (not
      top-anchored) via `caretY = padY + lineIndex * lineHeightPx + (lineHeightPx - caretHeight) / 2f`.
      Update `TryGetCaretRect` to return the SAME shrunk/centered rect, since its doc comment promises it
      matches what `PaintInternal` draws. Thread the new property through
      `ScribeCuneiformFieldRenderWidget` (constructor param + `UpdateRenderObject`).
- [x] 5.4 Wire `caretHeightScale: CuneiformMetrics.CaretHeightScale` into all five
      `ScribeCuneiformFieldRenderWidget` call sites (same five as task 3.2) — harmless no-op on the three
      `caretVisible: false` sites (title display, item label, read row), since no caret is drawn there.
- [x] 5.5 At the TWO call sites where `padY` is non-zero (`ScribeMultilineField.cs`'s cuneiform branch —
      `Widget.PadY`; `ScribeReadContent.cs`'s cuneiform row branch — `style.FieldPadY`), multiply by
      `CuneiformMetrics.FieldPadYScale` before passing it in, so Read and Editor row boxes keep agreeing
      on height. Leave the three already-`0f` `padY` call sites (both title bands, the item label)
      untouched — scaling a literal `0f` is a meaningless no-op.
- [x] 5.6 Verify `dotnet build src/Mod/Mod.csproj -c Debug` succeeds with 0 warnings/errors.
- [x] 5.7 Verify in-game (folds into section 4's restage): caret renders visibly shorter than the full
      line band, centered rather than top/bottom-anchored; the cuneiform field's box is visibly tighter
      top-to-bottom around the text/caret in both Read and Editor, with Read and Editor row heights still
      matching each other; typing/caret/selection still behave correctly at any buffer length (no
      regression of the section-4 caret-drift fix).
