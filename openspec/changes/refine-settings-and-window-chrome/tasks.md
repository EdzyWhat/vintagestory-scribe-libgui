## 1. Title-bar drag-grip affordance

- [x] 1.1 In `src/Mod/GuiDialogScribeLecternLibGui.cs` `BuildTitleBar`, add a `scribegrip` `TitleButton`
  (passive affordance, `OnSurfaceVariant` tint) immediately to the LEFT of the existing `scribeclose`
  button in the trailing button group. No drag gesture on the grip — the band's `DragHandleHeight` still
  owns dragging.
- [x] 1.2 Add a localized "drag to move" tooltip key for the grip in `src/Mod/assets/scribe/lang/en.json`
  and wire it via the existing `TitleButton`/`WithTooltip` tooltip path.

## 2. Numeric-field clamp-on-unfocus + feedback (Mod UI)

- [x] 2.1 In `src/Mod/ScribeNumericField.cs`, add optional `Func<float,float> clamp` and
  `string? rangeText` inputs. Apply `clamp` when the field LOSES FOCUS (blur), not in `OnTextChanged`;
  during typing keep firing `onChanged` with the raw parseable value for live preview.
- [x] 2.2 On blur, if the clamped result differs from the entered value, rewrite the field's text to the
  clamped value, fire `onChanged` with it, and surface `rangeText` as helper/error text beneath the field.
- [x] 2.3 Clear the feedback text on the next in-range edit of that field.
- [x] 2.4 Confirm the change composes with the existing `ValueKey` remount + `ScribeNumericFocusRegistry`:
  because the clamp now fires on blur, the write-through rebuild remounts the field seeded from the clamped
  value with no focus to preserve; the +/- and arrow-key `onStepped`/arm-autofocus path is unchanged.

## 3. Wire the field callbacks in the settings form

- [x] 3.1 In `src/Mod/ScribeSettingsContent.cs`, pass each numeric field its Core clamp static as the
  `clamp` callback — `ClampHudMaxRows`, `ClampHudRowWidth`, `ClampHudOffset`, `ClampPixelArtSize`, and a
  percent-aware wrapper over `ClampFontScale` for the font-scale fields (no new Core code).
- [x] 3.2 Pass each field a localized `rangeText` built from that preference's `Min*/Max*` consts on
  `ScribePlayerSettings` (fed through a lang string, e.g. "Valid range: {0}–{1}").
- [x] 3.3 Stop clamping in the callers' `onChanged` (clamping now happens in the field on blur); keep
  `onMutate` writing through so `Normalized()` remains the persist-time backstop.

## 4. Three sections with dividers

- [x] 4.1 In `ScribeSettingsContent.Build`, restructure into three `SectionTitle` + body pairs separated by
  a horizontal divider widget: **Mod Behavior**, **Window Appearance**, **HUD Appearance**.
- [x] 4.2 Re-sort controls: Mod Behavior = completion policy + HUD-collapsed checkbox; Window Appearance =
  Pixel-Art Display + Pixel Art Size + window font scale; HUD Appearance = HUD anchor + max rows + row
  width + X/Y offsets + HUD font scale. Re-form the `PairedControls` groupings within their new sections.
- [x] 4.3 In `en.json`, replace `settings-section-behavior`/`settings-section-appearance` with the three new
  section titles (`settings-section-modbehavior`, `settings-section-windowappearance`,
  `settings-section-hudappearance`).

## 5. Settings default background

- [x] 5.1 In `src/Mod/ScribeSettingsDialog.cs` `Build`, wrap the `ScribeSettingsContent` in a `Container`
  whose `BoxStyle.Color` is the active theme's `ColorScheme.Surface` (read via `Theme.Of`/`ThemeData.Default`),
  so the form sits on an opaque panel while the window still follows the global theme.

## 6. Settings buttons toggle open AND closed

- [x] 6.1 In `src/Mod/ScribeModSystem.cs` `OpenSettings()`, toggle: if `settingsDialog.IsOpened()` call
  `TryClose()`, else `TryOpen()` (lazily building the dialog as today). Both the Lectern gear and the HUD
  gear route through this one method, so this covers both entry points.

## 7. Build, restage, verify

- [x] 7.1 `dotnet build src/Mod/Mod.csproj` clean; Core test suite green (no Core changes expected, but
  confirm the clamp statics are still the only clamp source).
- [x] 7.2 Restage Debug (`bash build/restage.sh Debug`) and fully relaunch the client.
- [ ] 7.3 In-game: the `scribegrip` icon shows left of the Lectern close button with a "drag to move"
  tooltip, and the whole title-bar band still drags the window.
  **FAILED playtest 2026-07-26 (submission …T22-24-24, TESTING `59d7ccbf`):** dragging ON the grip does not
  move the window. Fix in §8.1, then retest.
- [ ] 7.4 In-game: select-all + retype a numeric field (incl. Pixel Art Size) without a mid-edit snap;
  entering an out-of-range value clamps on blur; values persist across a relog. (Range-feedback line
  requirement dropped — see §8.2.)
  **FAILED playtest 2026-07-26 (submission …T22-24-24, TESTING `bb25e8d3`):** retype + clamp-on-blur work,
  but every numeric field unfocuses after each +/- step-button click. Fix in §8.2, then retest.
- [x] 7.5 In-game: Scribe Settings shows three dividers-separated sections (Mod Behavior / Window
  Appearance / HUD Appearance) with controls under the right section; the form sits on an opaque panel.
  **PASSED playtest 2026-07-26 (TESTING `52f2e92e`).** (Polish requests captured in §9.)
- [x] 7.6 In-game: the Lectern gear and the HUD gear each toggle the settings window open AND closed.
  **PASSED playtest 2026-07-26 (TESTING `c2c153d1`).**
- [x] 7.7 Update `TESTING.md` with the new in-game items and record any follow-ups.

## 8. Playtest fixes (2026-07-26 submission …T22-24-24)

- [x] 8.1 **Grip drag-through (7.3 fix).** The `scribegrip` in `BuildTitleBar` is a passive
  `ScribeVsIconGlyph` with no gesture handler, but pressing it still swallows the drag instead of the
  title-bar band handling it (a press on a non-`IFocusable`/non-drag child doesn't propagate to the band's
  `DragHandleHeight`). Make the grip truly click-through so a press on it falls through to the band's drag
  (preferred), or failing that wire the grip's own press to drag the window like the band. Verify dragging
  works both ON the grip and anywhere else in the band. Restage + retest 7.3.
- [x] 8.2 **Numeric +/- unfocus (7.4 fix) + drop range-feedback.** In `ScribeNumericField` /
  `ScribeSettingsContent`, each +/- step-button click blurs the field (so repeated stepping needs a
  re-click). Keep focus on the field across a step press (the step buttons must not steal/clear focus — see
  the `ScribeNumericFocusRegistry` arm-autofocus path, which was meant to cover this). ALSO remove the
  clamp range-feedback line entirely (spec amended — the tester judged it unwanted): drop the `rangeText`
  wiring in `ScribeSettingsContent`, the feedback rendering in `ScribeNumericField`, and the
  `settings-range-*` lang keys. Restage + retest 7.4.
- [x] 8.3 Amend the `settings-tab` spec: the "A clamped numeric field surfaces its valid range as feedback"
  ADDED requirement is removed (done — see the dropped-note in `specs/settings-tab/spec.md`); clamp-on-blur
  itself is retained.

## 9. Settings-surface polish (2026-07-26 submission …T22-24-24 notes)

These are tuning tweaks to THIS change's settings form (passed 7.5, refinements requested):

- [x] 9.1 Section-header font is too large: make `SectionTitle` only ~8% larger than the window text
  (currently `BaseSettingsFontSize * scale + 2f` — change to ~`* scale * 1.08`).
- [x] 9.2 Put "Pixel Art Size" and "Window text size (%)" side by side as two columns (reuse
  `PairedControls`), in Window Appearance.
- [x] 9.3 Rename the "Pixel Art Size" label → "Pixel Art Size (px)" (`settings-pixelartsize` lang value).

## 10. Cross-cutting v1 grab-bag (2026-07-26 submission …T22-24-24 general notes)

Pulled into this change as the v1 catch-all pass (not scoped out to another change).

- [x] 10.1 **Settings gear last in the nav group.** In `BuildRightColNav` reorder the right-column nav so
  the `scribegear` (Settings) button is the LAST item, after Read / Edit / Pinned (currently it's first).
- [x] 10.2 **Pin icon +10% in all its buttons.** Enlarge the `scribepin` glyph by ~10% wherever it renders
  as a button — the right-column nav Pinned button, the editor row's hover pin (`ScribeEditRowState`), and
  the Pin Tab row's unpin (`ScribePinRowState`) — keeping it centered in its button box (scale the glyph,
  not the box, mirroring `ScribeRowButton`'s BoxShrink split so only the SVG grows).
- [x] 10.3 **Max HUD Rows clamp 20 → 10.** In `src/Core/ScribePlayerSettings.cs` set
  `MaxHudMaxRows = 10` (Core-only const; `ClampHudMaxRows`/`Normalized` and the HUD's `MaxRenderedRows`
  already derive from it, so a saved 11–20 re-clamps to 10 on next load). Update the `settings-hudmaxrows`
  help text "(1–20)" → "(1–10)" in `en.json`. Add/adjust the `ClampHudMaxRows` Core test bound. Run
  `dotnet test tests/Core.Tests/Core.Tests.csproj`.
- [x] 10.4 **Edit/Pin-view grip margins → 0.** The drag-grip column takes too much width in the editor and
  Pin Tab rows. Reduce the grip's left/right margins to 0 (the `Padding`/spacing around the
  `ScribeVsIconGlyph("scribegrip", …)` in `ScribeEditRowState` and `ScribePinRowState`) so the text column
  gets that space back; keep the vertical centering nudge.
