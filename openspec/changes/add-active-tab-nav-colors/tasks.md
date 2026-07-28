## 1. Color constants

- [ ] 1.1 In `src/Mod/ScribeRowConstants.cs`, add the four thematic nav colors as `Vector4`
  constants/statics (Read `#465481`, Edit `#9d4b44`, Pinned `#6b8257`, Settings `#746f66`) plus the
  active-glyph cream `#eae6dd`. Document each hex in a comment.

## 2. Button widget: optional active color

- [ ] 2.1 Add an optional `Vector4? activeColor = null` parameter to `ScribeRowButton` (and its
  `ActiveColor` property) and thread it through `TitleButton`.
- [ ] 2.2 In `ScribeRowButtonState.Build`, when `ActiveColor` is set: use it as the resting box fill,
  force the glyph color to the cream constant, and set the hover fill to
  `ScribeRowConstants.ShiftBrightness(ActiveColor, +10f)` (leave press behavior sensible). When null,
  keep the exact current `SurfaceHigh`-based behavior and the passed `IconColor` glyph.

## 3. Wire active state in the nav

- [ ] 3.1 In `BuildRightColNav`, pass `activeColor` to each of Read/Edit/Pinned based on `viewMode`
  (`ScribeLecternView.Read`/`.Editor`/`.Pinned`), else null.
- [ ] 3.2 Pass `activeColor: <settings>` to the gear button when the standalone settings window is
  open, else null (read via a `modSystem.IsSettingsOpen` passthrough over `settingsDialog?.IsOpened()`).

## 4. Settings-open live repaint

- [ ] 4.1 In `ScribeModSystem`, add `bool IsSettingsOpen` and an `event Action? SettingsVisibilityChanged`;
  invoke it from `OpenSettings` (after the toggle) and from the settings dialog's `OnGuiClosed` so all
  close routes (gear toggle, X, Escape) notify.
- [ ] 4.2 In the lectern dialog, subscribe to `SettingsVisibilityChanged` on open and call
  `ForceRebuild` (mirror the `MyPinsChanged` handling); unsubscribe on close/dispose.

## 5. Build, stage, verify

- [ ] 5.1 `bash build/restage.sh Debug` clean (0 warn/0 err); relaunch client.
- [ ] 5.2 Manually verify: each of Read/Edit/Pinned highlights in its color with a cream glyph when
  active and reverts when not; hovering the active button brightens its fill (+10 V); opening the
  settings window (from lectern gear AND HUD gear) colors the gear live and closing it reverts —
  check against both pixel-art and dark global themes for cream-glyph contrast.
