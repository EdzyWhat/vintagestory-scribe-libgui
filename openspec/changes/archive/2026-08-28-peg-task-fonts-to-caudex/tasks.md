## 1. Metrics chokepoint

- [x] 1.1 Extend `ScribeTaskFont` in `src/Mod/ScribeRowConstants.cs` with a per-family record (`SizeScale`, `OffsetEm`, `OpticalScale`, optional `SizeScaleOverride`), plus `EffectiveSize(family, nominalSize)`, `OffsetY(family, nominalSize)`, and `LineHeight(nominalSize)`. Caudex is identity (scale 1, optical 1, offset 0). `LineHeight` always returns Caudex's `"Ag"` Y at that nominal size. Keep Core free of Skia / VSAPI types.
- [x] 1.2 Add `ScribeTaskFont.BuildMetrics()` that, after typefaces are registered, measures each `KnownTaskFonts` family plus the empty default (`sans-serif`) against Caudex at a reference size and stores `SizeScale = caudexY / familyY` (guard `familyY <= 0`). If Caudex itself failed to register, log once and use `"sans-serif"` as the reference. Call it from `RegisterCustomFonts` at the end of that method.
- [x] 1.3 Seed `OffsetEm` at 0 and `OpticalScale` at 1 for every family (including Default). Leave `SizeScaleOverride` unset. Document that playtest fills OffsetEm later and OpticalScale from the HTML tuner.

## 2. Inheritance and row layout measure

- [x] 2.1 Change `ScribeTextDefaults.Style` to set `FontSize` to `ScribeTaskFont.EffectiveSize(taskFontFamily, baseFontSize)` while `FontFamily` stays `Resolve(...)`. Every task-text tab that already wraps with `ScribeTextDefaults` then inherits the pegged size.
- [x] 2.2 Change `ScribeRowControlNudge.TextLineHeight` to return `ScribeTaskFont.LineHeight(fontSize)` instead of measuring hardcoded `"sans-serif"`. Drop the now-misleading private `FontFamily` const (or point it at Caudex with a comment that it is the peg, not the row face).
- [x] 2.3 Add `ScribeTaskFont.OffsetWrap(family, nominalSize, child)` that returns `Transform.Translate(0, OffsetY(...), child)` when the offset is non-zero, else `child`. Use it on the Read-view task/note text child in `ScribeReadContent` (not the whole row, not checkbox/grip). Grep Read-view `Text` sites so Tracker/Link names on the non-cuneiform path get the same wrap.

## 3. Editor field draw and measure

- [x] 3.1 In `ScribeMultilineFieldRender`, keep taking the caller's *nominal* `fontSize` + resolved family. Use `ScribeTaskFont.LineHeight(nominal)` for `lineHeight` / field height / caret / selection. Draw (and wrap-width-measure) at `EffectiveSize`. Add `OffsetY` to glyph draw Y only. Do not apply offset to caret or selection boxes. Skip all of this when `useCuneiform` is true.
- [x] 3.2 Thread the same nominal-size contract through `ScribePinnedContent`'s editor field and `ScribeDialogBase.Guestbook`'s note field (they already pass `style.FontSize` / body size + `Resolve`). Confirm they do not pass a pre-scaled size after 2.1.
- [x] 3.3 Update `ScribeGlyphFallback.DrawLine` / `MeasureWidth` call sites in the field so they receive the effective size (same as the no-arrow `DrawText` path). Fallback family ("Cormorant Unicase") is only for missing arrows; do not run it through the task-font scale table.

## 4. Remaining task-font paint (HUD out)

- [x] 4.1 Leave the pinned HUD on its own face and sizing. Do **not** apply `EffectiveSize`, `OffsetWrap`, or Task Text Font to HUD rows. (First impl pegged the HUD; playtest reverted it.)
- [x] 4.2 Grep `src/Mod` for `TextLayoutHelper.MeasureText`, `style.FontSize` passed into custom text paint, and `ScribeTaskFont.Resolve` without `EffectiveSize`. Every remaining **task-TTF document** site must use the chokepoint; cuneiform, `ButtonFamily`, `TitleFontFamily`, HUD, and Settings chrome must not.

## 5. Tests and notes

- [x] 5.1 Core: extend `ScribePlayerSettingsTests` so `KnownTaskFonts` still contains Caudex, Scapholene, La Belle Aurore, Noto Sans, Noto Serif, Playfair Display, Cormorant Unicase — the set `BuildMetrics` must cover. No Skia in Core.
- [x] 5.2 `dotnet test tests/Core.Tests` passes. Mod builds with 0 new warnings.
- [x] 5.3 Append a `VSAPI-NOTES.md` LibGUI note: task-font line-box is pegged to Caudex; `TextLineHeight` must not measure the selected family; Default/`sans-serif` is included; cuneiform, HUD, and Settings chrome are excluded; OpticalScale sits on top of auto SizeScale.

## 6. In-game calibration (author)

- [x] 6.1 Height lock: on a Lectern with a mix of single-line tasks, cycle every font in Settings on Read and on Edit. Confirmed: single-line row height/position does not jump (within 1 px), including Default, Scapholene, and La Belle Aurore. `OffsetEm` filled from the tuner (task 7.4).
- [x] 6.2 HUD: leave it alone. Confirmed it uses a different font and must not pick up the peg. Cuneiform tablets unchanged.
- [x] 6.3 Confirm titles and in-dialog buttons stay unscaled Caudex while the task font is not Caudex. Confirm Read ↔ Edit on a non-Caudex font does not jump single-line rows (`lectern-gui-shell` parity).

## 7. Settings chrome, optical scale, tuner

- [x] 7.1 Settings form uses LibGUI default face (`sans-serif`) at 100% (`WrapSettingsChrome` / `scale = 1`). It does not inherit Task Text Font or Window Text Size. Window scale still live-previews Read/Edit.
- [x] 7.2 Revert HUD task-font / `EffectiveSize` / `OffsetWrap` wiring from the first impl.
- [x] 7.3 Add `OpticalScale` on `FamilyMetrics` (`EffectiveSize = nominal × SizeScale × OpticalScale`). Land every family at 1. Add `tools/task-font-optical-scale/index.html` (line-box lock + optical sliders + Copy C#).
- [x] 7.4 Confirm in-game that Read/Edit row height still does not jump after OpticalScale + OffsetEm (La Belle Aurore offset 0.18, Scapholene 0.05, Playfair −0.03, Cormorant −0.02). Values are in `OpticalScaleOf` / `OffsetEmOf`.
