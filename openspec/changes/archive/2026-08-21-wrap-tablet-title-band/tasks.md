## 1. Wrap the resting title (BuildTitleDisplay)

- [x] 1.1 In `GuiDialogScribeTablet.BuildTitleDisplay`, replace the single-line `CuneiformText` with the
      wrapping `ScribeCuneiformFieldRenderWidget` (display-only: `caret/selectionAnchor: 0`,
      `hasFocus/caretVisible: false`, `caretColor/selectionColor: Vector4.Zero`, transparent box/border,
      `singleLine: false`), matching the item-label wrapping the sibling `wrap-tablet-item-titles` set up.
      Keep the per-material `glow`, the resolved `inkColor`, and `fontSizeEm: titleStyle.FontSize`. Use a
      fixed jitter seed (reuse `TitleJitterSeed`) so the wobble is stable across rebuilds.
- [x] 1.2 Keep the enclosing `Clip` and give it a height budget of exactly TWO line-heights so a title
      longer than two lines clips at the end of line 2 (no partial third line). Bottom-align the wrapped
      title within the slot so line 1 grows upward into the band's existing headroom. (The `contentBoxH`
      grows to `TitleBtnsH + (2-1)·titleLineH` and `titleCrossAlign` bottom-anchors the row, so line 1
      grows up into the band's slack; the enclosing `Clip` caps at two line-heights.)

## 2. Wrap the editing title (BuildTitleField)

- [x] 2.1 In `ScribeCuneiformTitleField` (`ScribeCuneiformField.cs:347` sets `singleLine: true`), allow the
      title field to wrap to two lines — flip `singleLine` to false for the tablet title path and cap the
      rendered height to two line-heights (same clip budget as §1.2). Confirm the maxlength gate and the
      `OnTitleFieldKeyDown` Enter/Escape commit are unchanged (Enter commits, does not insert a newline).
      (Added a `bool singleLine = true` ctor param → `Widget.SingleLine` → render-widget `singleLine:`;
      the tablet's `BuildTitleField` passes `singleLine: false`. Key handling is untouched, so the
      maxlength gate + Enter/Escape commit are byte-identical.)
- [x] 2.2 Confirm the fixed `jitterSeed`/`TitleJitterSeed` and progression behavior are unchanged so the
      title does not re-wobble on every keystroke while wrapped. (`TitleJitterSeed` and the reveal-schedule
      wiring were not touched — only the wrap flag was added.)

## 3. Give the band room for two lines (tablet-scoped)

- [x] 3.1 In `ScribeDialogBase.Layout.cs` `BuildTitleBar`, size the title slot to host up to two
      line-heights WITHOUT changing the shared `ScribeLayout.TitleBarH`/`TitleBtnsH` metrics (do not
      enlarge the Lectern/Notebook bands). Prefer growing the tablet title slot into the band's existing
      vertical slack (the band is `0.13·H`, the bottom-anchored content row `0.065·H`); keep the trailing
      pencil/grip/close group anchored so it does not drift when the title is two lines. (New
      `private protected virtual int TitleMaxLines => 1` seam; base computes `contentBoxH` = `TitleBtnsH`
      when 1, else `min(TitleBarH, TitleBtnsH + (n-1)·titleLineH)`, and `titleCrossAlign` = Center when 1
      else End. The tablet overrides `TitleMaxLines => ActiveCuneiformBundle is not null ? 2 : 1`. When
      `TitleMaxLines == 1` every value is identical to before, so Lectern/Notebook are unchanged.)
- [x] 3.2 If §3.1's slack proves insufficient in-game (§4.1), add a tablet-only band-height override (e.g.
      a virtual accessor the tablet increases) rather than editing the shared `ScribeLayout` metric.
      Confirm base (Lectern/Notebook) title bands are byte-identical afterward. (Not needed at
      implementation time — the two-line box is capped at `TitleBarH` (`0.13·H`), which already exceeds the
      one content row (`0.065·H`), so the second line fits inside the existing band without a metric bump.
      Revisit only if §4.1 shows clipping.)

## 4. In-game verification (playtest gate)

- [x] 4.1 In-game: on a wet tablet, set a very long title (e.g. a long item name pasted in) → the resting
      title wraps to two lines within the band, is fully readable up to two lines, and the drag/pencil/close
      chrome stays clear and vertically sensible. Calibrate the exact two-line height here (measure, don't
      theorize) and decide whether §3.2's fallback is needed.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 4.2 In-game: click the pencil and type a long title past one line → the editing field wraps to a
      second line (not clipped off the right), and Enter commits it, showing the same two-line resting title.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 4.3 In-game: confirm a SHORT tablet title looks identical to before (single line, no band growth), and
      that Lectern/Notebook/Scriptorium titles are visually unchanged (still single-line with ellipsis).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 4.4 In-game: confirm the disable-cuneiform fallback still renders a readable single-line title on the
      tablet (base path), with no crash when the glyph bundle is absent.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).

## 5. Build + tests

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Debug` — 0 warnings / 0 errors.
- [x] 5.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — green (no Core change expected; this is a
      Mod-layer render/layout change). (463 pass; the 7 `ScribeBrightnessCurveTests` failures are the
      pre-existing illumination-floor drift that fails on clean `main` too — Core untouched here.)
- [x] 5.3 `bash build/restage.sh Debug` before handing off to in-game testing (client not running).
      (Restaged 137 files; client confirmed not running first.)

## 6. Docs

- [x] 6.1 If the two-line clip budget or the tablet-only band-height override lands as a non-obvious layout
      fact, add a one-line note to `VSAPI-NOTES.md` (`## LibGUI` section) so it is not re-derived.
  - Done 2026-08-21: the bottom-anchor + two-line clip-budget technique (grow line 1 up into the band's
    existing `TitleBarH` slack, no shared-metric bump; the §3.2 override proved unnecessary) is a reusable
    LibGUI fact — added it to the `## LibGUI` section of `VSAPI-NOTES.md`.
