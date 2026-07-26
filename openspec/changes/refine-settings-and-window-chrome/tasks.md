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
- [ ] 7.2 Restage Debug (`bash build/restage.sh Debug`) and fully relaunch the client.
- [ ] 7.3 In-game: the `scribegrip` icon shows left of the Lectern close button with a "drag to move"
  tooltip, and the whole title-bar band still drags the window.
- [ ] 7.4 In-game: select-all + retype a numeric field (incl. Pixel Art Size) without a mid-edit snap;
  entering an out-of-range value clamps on blur and shows the range feedback; feedback clears on a valid
  edit; values persist across a relog.
- [ ] 7.5 In-game: Scribe Settings shows three dividers-separated sections (Mod Behavior / Window
  Appearance / HUD Appearance) with controls under the right section; the form sits on an opaque panel.
- [ ] 7.6 In-game: the Lectern gear and the HUD gear each toggle the settings window open AND closed.
- [x] 7.7 Update `TESTING.md` with the new in-game items and record any follow-ups.
