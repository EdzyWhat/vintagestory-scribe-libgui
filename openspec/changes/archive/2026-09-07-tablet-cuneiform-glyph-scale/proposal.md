## Why

Tablet cuneiform text currently reads a bit large relative to the checkbox and other row
controls, which sit on a different (non-cuneiform) sizing formula. On tablets,
`style.FontSize` is a single overloaded value that drives layout line-height, the tablet's
`CheckboxSize`/`ControlSize` (derived from `FontSize` only on the cuneiform branch), and the
cuneiform glyph's own paint size all at once — so naively shrinking `FontSize` to get smaller
glyph text would also shrink the checkbox and controls, which is not wanted. A precedent for a
paint-only size multiplier already exists for regular task fonts (`task-font-metrics`'s optical
scale, which explicitly excludes cuneiform), but no equivalent hook exists for cuneiform glyphs
today.

## What Changes

**Revised 2026-09-07 after in-game testing** — see `design.md`'s History section for the two
earlier approaches (paint-only ink shrink; layout shrink coupled to the checkbox) that were tried
and superseded before landing here.

- Add a fixed glyph draw-scale to the cuneiform render path (both the read-only `CuneiformText`
  widget and the editable `ScribeCuneiformField`) that shrinks the tablet's title/row/label
  cuneiform to 85% of its current *layout* height — the row/title band itself gets physically
  shorter, not just the drawn ink within an unchanged box.
- Leave `GuiDialogScribeTablet`'s `CheckboxSize`/`ControlSize` derivation completely untouched —
  the checkbox and controls stay exactly their current size. This deliberately breaks the
  today-only-by-construction equality between the cuneiform box's height and `CheckboxSize`; the
  checkbox will now sit taller than its row/title band, absorbed by
  `ScribeRowControlNudge.CheckboxAndGripTop`'s existing overflow-above/below support (no new
  mechanism needed there).
- No change to `CuneiformMetrics.LineHeightRatio`, `style.FontSize`, or any other
  `FontSize`-derived formula outside the two render classes.
- Explicitly defer re-tuning the checkbox/icon/counter alignment nudge constants to a follow-up,
  in-game pass — this change makes the row shorter and leaves the resulting mismatch visible for
  the user to tune, rather than guessing new constants blind.
- **Revised again 2026-09-07** from a direct pixel measurement, before any of the above was tested
  in-game: `GlyphDrawScale` 0.85 → 0.95, plus two new independent paint-time knobs —
  `CaretHeightScale` (shrinks the synthetic caret bar's drawn height, centered in its band) and
  `FieldPadYScale` (shrinks the cuneiform field's top/bottom inset at the two real-row call sites).
  Both are ratios of measured screenshot pixels (44/48 and 6/9), not absolute pixel counts. See
  `design.md`'s Round 4 for why these are separate from `GlyphDrawScale` rather than folded into it.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `cuneiform-glyph-font`: the render widget gains a glyph draw-scale that shrinks its OWN
  reserved layout height (and, following from that, its drawn ink) independently of the
  tablet's `CheckboxSize`/`ControlSize`, mirrored in the editable cuneiform field.

## Impact

- `src/Mod/CuneiformText.cs` (`CuneiformTextRender.PerformLayout`) — fold the new draw-scale into
  the existing D7 line-height formula that derives `renderedHeight`/`scale`.
- `src/Mod/ScribeCuneiformField.cs` (`ScribeCuneiformFieldRender.PerformLayout`) — same, for
  `lineHeightPx`/`scale`. No changes needed in either file's paint code (`BuildStrokePath`/
  `DrawStrokePass`) — they already just multiply by `scale`, which now carries the shrink.
- No change to `src/Mod/GuiDialogScribeTablet.cs`'s `CheckboxSize`/`ControlSize` derivation.
  `src/Mod/ScribeRowWidgets.cs`'s `CheckboxAndGripTop`/`SingleLineInputHeight` alignment constants
  will likely need re-tuning once this is visible in-game, but that tuning is a follow-up, not
  part of this change's own tasks.
- No change to `src/Core/`.
