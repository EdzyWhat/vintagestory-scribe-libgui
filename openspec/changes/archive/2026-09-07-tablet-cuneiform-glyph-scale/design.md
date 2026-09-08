## Context

`CuneiformTextRender.PerformLayout` (`src/Mod/CuneiformText.cs:176-197`) computes one value,
`scale = renderedHeight / gridHeight` (where `renderedHeight = fontSizeEm * CuneiformMetrics.LineHeightRatio`),
and uses it for two different jobs: it sets the widget's reserved layout `Size` (`width`/`renderedHeight`)
*and* it is the grid-units-to-pixels multiplier `BuildStrokePath` (`CuneiformText.cs:281-314`) applies to
every stroke's corner coordinates before filling the quad. `ScribeCuneiformField.cs:164-173` computes an
analogous `scale`/`lineHeightPx` pair from the same `fontSizeEm * CuneiformMetrics.LineHeightRatio`
formula, and that field's `scale` additionally drives caret-bar placement, selection-highlight boxes, and
hit-testing (per its own comment: "layout, paint, caret, and hit-testing all agree on the same wrapped
lines" — `ScribeCuneiformField.cs:103-104`). Separately, `GuiDialogScribeTablet.cs`'s `DecorateRowStyle`
derives the tablet's `CheckboxSize`/`ControlSize` from `style.FontSize * CuneiformMetrics.LineHeightRatio`
— the same `FontSize` value, but a completely separate computation outside these two render classes.

Both render classes already carry a precedent for a paint-time-only multiplier that never touches layout,
caret, selection, or hit-testing: `StrokeWeightScale` (thickens/thins the stroke fill only) and
`JitterStrength`/`RotationDegrees` (perturb the drawn quad only). See proposal.md for why a plain
`FontSize` reduction is not viable.

## Goals / Non-Goals

**Goals (revised 2026-09-07 after in-game testing — see History below):**
- Shrink the tablet's cuneiform title/row/label text — BOTH the read-only render widget and the
  editable field — to 85% of its current *layout* height (not just its drawn ink), so the row/title
  band itself sits physically shorter.
- Leave `GuiDialogScribeTablet.DecorateRowStyle`'s `CheckboxSize`/`ControlSize` derivation completely
  unchanged — the checkbox and controls stay exactly the size they are today. The cuneiform box and
  the checkbox are equal-by-construction today (both `style.FontSize * LineHeightRatio`); this change
  deliberately breaks that equality so the two can be tuned independently.
- Caret position, selection highlight, and hit-testing must stay exactly in sync with the (now
  smaller) drawn ink at every character count — no divergence as text is typed.

**Non-Goals:**
- Making the draw scale a per-view (material/state) or user-facing setting — this change ships one fixed
  constant, in the manner of the jitter/reveal-timing/`LineHeightRatio` constants, not a new preference.
- Touching `tablet-readability-style`'s per-view `stroke-weight scale` bundle — that mechanism is
  unrelated (it scales stroke *thickness*, authored per clay material/state) and is left as-is.
- Changing `GuiDialogScribeTablet.DecorateRowStyle`'s `CheckboxSize`/`ControlSize` derivation.
- Non-tablet cuneiform surfaces (the `.cuneiform` dev harness) — the new parameter defaults to `1.0`
  (no-op) so nothing changes for them unless a call site explicitly passes the tablet's value.
- Re-deriving `ScribeRowControlNudge.CheckboxAndGripTop`/`SingleLineInputHeight`'s exact alignment
  constants in THIS change — the mismatch this change introduces (checkbox now taller than its cell)
  is deliberately left for the user to tune in-game once they can see it live; those formulas already
  support a checkbox taller than its band (negative offset = overflow above/below), so no new
  mechanism is needed there, just new constant values.

## Decisions

**Add a `GlyphDrawScale` property, sibling to `StrokeWeightScale`, on both render classes — but as a
LAYOUT input, not a paint-time-only one.** `CuneiformTextRender`/`CuneiformTextRenderWidget`/
`CuneiformText` and `ScribeCuneiformFieldRender`/its widget gain a `GlyphDrawScale` float property,
defaulting to `1f`, guarded like `StrokeWeightScale` (a value `<= 0` becomes `1`), but wired with
`SetProperty(..., relayout: true)` — it must trigger `PerformLayout`, not just a repaint.

**Apply it inside `PerformLayout`'s existing D7 line-height formula, before it derives `scale`.**
`CuneiformTextRender.PerformLayout`: `renderedHeight = fontSizeEm * LineHeightRatio * GlyphDrawScale`.
`ScribeCuneiformFieldRender.PerformLayout`: `lineHeightPx = fontSizeEm * LineHeightRatio * GlyphDrawScale`.
Both then derive `scale` from that (now smaller) height exactly as before — `BuildStrokePath`/
`DrawStrokePass` need NO changes at all (they already just do `corners[n] * scale`), and every
caret/selection/hit-testing read site in `ScribeCuneiformFieldRender` already reads that same `scale`/
`lineHeightPx`, so it automatically stays in lockstep with the smaller drawn ink at every character
count — there is no separate "paint scale" vs. "layout scale" to keep synchronized, because there is
only one scale again, exactly like before `GlyphDrawScale` existed.

**Source the `0.85` value from one new constant next to `LineHeightRatio`.** Add
`CuneiformMetrics.GlyphDrawScale = 0.85f` (`CuneiformText.cs`'s existing `CuneiformMetrics` static
class), following the same pattern as `DefaultJitterStrength`/`DefaultRotationDegrees`. Tablet call
sites that construct `CuneiformText`/`ScribeCuneiformField` for title/row/label text pass this constant
through the new property; every other consumer (the `.cuneiform` dev harness, and the property's own
default) stays at `1.0` unless a future change opts it in too.

**Deliberately do NOT touch `CheckboxSize`/`ControlSize`.** These stay `style.FontSize *
LineHeightRatio` with no `GlyphDrawScale` factor, in `GuiDialogScribeTablet.DecorateRowStyle`. The
cuneiform box and the checkbox were equal by construction before this change; after it, the cuneiform
box is smaller. `ScribeRowControlNudge.CheckboxAndGripTop` already has a documented mechanism for a
checkbox taller than its row band (`centered = (band - style.CheckboxSize) / 2` goes negative — "control
overflows above/below the line rather than growing the row"), so this mismatch is absorbed by existing
code, not new code. The exact alignment constants (nudge offsets, icon/counter centering) are expected
to need re-tuning in-game once the row is visibly shorter — that tuning is explicitly deferred to the
user rather than guessed here.

## History (superseded approaches)

**Round 1 (rejected before implementation): shrink the layout `Size` too, by feeding a scaled-down
`FontSizeEm` into the two render classes.** This was the FIRST version of this exact idea, rejected in
the original design pass because — without ALSO decoupling `CheckboxSize`/`ControlSize` — the row would
not visibly shrink (the checkbox's still-full-height band would keep the row that tall) and would
silently reopen the checkbox/band-alignment formulas. That rejection was correct as far as it went, but
missed that the user's actual ask included accepting that reopening — see Round 3.

**Round 2 (implemented, shipped, then found broken in-game): paint-time-only `GlyphDrawScale`.**
Applied the scale only inside `BuildStrokePath`/`DrawStrokePass`'s corner-to-pixel conversion, leaving
`scale`/`lineHeightPx`/`Size` untouched, with a per-line pixel-space re-centering translate
(`offsetY = renderedHeight * (1 - GlyphDrawScale) / 2`, `offsetX = lineWidth * (1 - GlyphDrawScale) / 2`
from the line's CURRENT unscaled width). This shipped and broke live typing: multiplying every corner's
absolute X by `GlyphDrawScale` before translating compresses inter-character spacing (each character
sits closer to its neighbor the further it is from the line start) while caret/selection/hit-testing
read the un-scaled advance, so ink and caret diverged more with every character typed; the
`lineWidth`-based `offsetX` also grew with the buffer, showing up as an expanding left margin while
typing. A same-session fix replaced the whole-line offset with per-character pivot scaling (reusing
`GlyphRotationPivot`, the same pivot rotation already uses) — this fixed the typing bug, but the row
still didn't shrink, because `Size` was still deliberately untouched. When the user then said they
wanted the row itself shorter (not just the ink), THIS was the point Round 1's rejected alternative
should have been raised as a live option — it wasn't, so the paint-only path was implemented, shipped,
and had to be walked back.

**Round 3 (current): Round 1's alternative, but WITHOUT requiring `CheckboxSize`/`ControlSize` to
shrink in step.** The user confirmed directly: they want row height to change *relative to* the
checkbox (i.e., decoupled from it, not shrinking together), and they will tune the resulting
checkbox/icon/counter alignment themselves once they can see it live. That is exactly Round 1's
alternative with its original objection (checkbox drags the row back to full height) turned into an
accepted trade-off rather than a blocker — so Round 1's mechanism is now correct, and the
paint-time-only apparatus from Round 2 (including its pivot-scale fix) is fully removed as dead code.

**Round 4 (2026-09-07, before any in-game testing of Round 3 ran): `GlyphDrawScale` 0.85 → 0.95, plus
two NEW independent paint-time knobs — caret height and field padding.** The user revised the target
size from a Photoshop measurement of the (not-yet-tested) Round 3 build and asked for two additional,
narrower reductions: the synthetic caret bar's drawn height (48px → 44px against a 29px-tall glyph) and
the cuneiform field's top/bottom inner padding (9px → 6px each side). Both are expressed as RATIOS
(`CaretHeightScale = 44/48`, `FieldPadYScale = 6/9`) rather than absolute pixel counts, so they hold at
any GUI scale/`FontSize`, matching every other constant in this file.

These are deliberately SEPARATE knobs from `GlyphDrawScale`, not folded into it:
- `CaretHeightScale` shrinks ONLY the caret bar's drawn height (`ScribeCuneiformFieldRender.PaintInternal`'s
  synthetic-caret block), centered within its unchanged `lineHeightPx` band. It is paint-time only —
  `TryGetCaretRect` is updated to report the same shrunk/centered rect (its doc comment already promised
  parity with what's drawn), but nothing else (hit-testing, `CaretToLineLocal`, selection) changes.
- `FieldPadYScale` shrinks ONLY the top/bottom inset PASSED IN to the two real-row call sites
  (`ScribeMultilineField`'s cuneiform branch, `ScribeReadContent`'s cuneiform row branch) — applied at
  the call site, not inside `ScribeCuneiformFieldRender` itself, since `padY` is already a plain
  constructor parameter there. The two title-band call sites and the item-label call site already pass
  `padY: 0f` (their own wrapper supplies the inset) and are left untouched — multiplying a literal `0`
  by anything is a no-op, so there was nothing to change there.
- Both stack on top of `GlyphDrawScale`'s already-shrunk `lineHeightPx`, further tightening the
  editable field's visual footprint without touching the glyph ink's own size a second time.

## Risks / Trade-offs

- **The checkbox/control column no longer matches the cuneiform row's height** → intentional per Round
  3 above; the row is now visibly shorter than the checkbox, which overflows above/below it via
  `CheckboxAndGripTop`'s existing negative-offset support. Expect a visual tuning pass (nudge
  constants) once this is seen in-game — that pass is explicitly out of scope for this change's own
  tasks and left to the user.
- **A future glyph-draw-scale tweak must go through `PerformLayout`, not `BuildStrokePath`/
  `DrawStrokePass`** → those two methods are now back to their pre-this-change form (plain
  `corners[n] * scale`) and should stay that way; re-adding a second scale factor there would
  reintroduce Round 2's caret-drift bug. If ink needs to shrink WITHOUT the row shrinking again in some
  future surface, that is a materially different requirement and needs its own re-design, not a revival
  of Round 2's code.
