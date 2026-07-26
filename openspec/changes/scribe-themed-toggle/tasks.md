> Reconciled 2026-07-25 to the shipped design after the pivot: the toggle governs the **Lectern only**
> (not the HUD or settings window); it is named `PixelArtDisplay`; the in-Lectern settings tab was removed
> and both gears open one standalone settings window. All in-game items confirmed by playtest 2026-07-25.

## 1. Core: the pixel-art-display setting

- [x] 1.1 Add `public bool PixelArtDisplay { get; set; } = true;` to
  `src/Core/ScribePlayerSettings.cs` as pure data (no clamp, no VS API); `Normalized()` leaves it
  untouched (a bool needs no clamping).
- [x] 1.2 Confirm the field round-trips through `scribe-hud-config.json` load/store with no code change
  (Newtonsoft serializes the new property; absent key defaults to `true`).
- [x] 1.3 Add a `ScribePlayerSettings` test asserting `PixelArtDisplay` defaults to `true` and survives
  `Normalized()` both ways; run the Core suite (`dotnet test tests/Core.Tests`) — 103/103 pass.

## 2. Settings form control + localization

- [x] 2.1 Add a `PixelArtDisplay` label-hugging `Checkbox` to the Appearance section of
  `ScribeSettingsContent` (mirroring the `HudCollapsed` control).
- [x] 2.2 Add `settings-pixelartdisplay` ("Pixel-Art Display") and `settings-pixelartdisplay-help` keys to
  `assets/scribe/lang/en.json`; the help text notes the HUD and settings window are not affected. Remove
  the now-unused `settings-back` key (the in-Lectern settings view is gone).

## 3. Mod: the light theme

- [x] 3.1 Create `src/Mod/ScribeTheme.cs` defining `static readonly ThemeData Light` — a
  `new ThemeData(new ColorScheme { … })` populating all 17 roles for a light parchment scheme (light
  `Surface`/`SurfaceLow`/`SurfaceHigh`/`Background`, dark `OnSurface`/`OnBackground`, a warm `Primary`
  accent, readable `Border`/`StateHover`/`StateSelected`), letting the per-widget style structs cascade.
- [x] 3.2 Add `public static ThemeData For(bool pixelArt) => pixelArt ? Light : ThemeData.Default;` as the
  single selector; the off path is the player's global theme (LibGUI loads `ThemeData.Default` from their
  `libgui.json`), not a forced dark preset.

## 4. Apply the theme wrap to the Lectern + consolidate settings access

- [x] 4.1 In `GuiDialogScribeLecternLibGui`, read `modSystem.MySettings.PixelArtDisplay` fresh in
  `Build()` and wrap the window output in `new Theme(ScribeTheme.For(pixelArt), child: <window>)`.
- [x] 4.2 In the Lectern `Build()`, compute explicit `titleBarColor:` / `textColor:` for the `WindowFrame`
  from the active scheme (it reads `ThemeData.Default` at construction, `WindowTitleBar.cs:231`, so it will
  not follow the wrap).
- [x] 4.3 Hoist the single `ScribeSettingsDialog` into `ScribeModSystem.OpenSettings()`; remove the
  in-Lectern settings *view* (`ScribeSettingsView`, `isSettingsMode`, its open/close/back plumbing and
  controllers) and repoint the Lectern gear at `OpenSettings()`.
- [x] 4.4 Point `HudScribePins`' gear at the shared `OpenSettings()` and drop its own settings-dialog
  ownership. The standalone `ScribeSettingsDialog` is NOT theme-wrapped and sets no explicit title-bar
  colors, so it follows the player's global theme.

## 5. Mod: the HUD is not governed by the toggle

- [x] 5.1 (Revised) The HUD is NOT wrapped in Scribe's theme and does NOT toggle: `HudScribePins.Build()`
  returns its content with no `Theme` wrap, so it reads the player's global theme via `Theme.Of(context)`.
  The glow halo stays the original dark constant (`new Vector4(0,0,0,0.9)`) — no inversion, since the HUD
  always renders on the (light-text) global theme. *(An early build wrapped the HUD and inverted the halo;
  removed per user feedback 2026-07-25.)*

## 6. In-game verification (all confirmed 2026-07-25)

- [x] 6.1 Pixel-Art ON: the Lectern (read + editor) renders dark text on light parchment; the title bar is
  light and text is legible everywhere.
- [x] 6.2 Pixel-Art OFF: the Lectern follows the player's global game theme (stock dark by default), plain,
  legible, depending on no art.
- [x] 6.3 Toggle while the Lectern is open: it flips between the light theme and the global theme live, no
  reopen and no restart.
- [x] 6.4 The setting persists across a relog (written to `scribe-hud-config.json`) and defaults to ON on a
  fresh profile.
- [x] 6.5 No bare `new Text(...)` renders white-on-light — the title-bar text and settings-form labels are
  legible.
- [x] 6.6 One settings window from two gears: the Lectern gear and the HUD gear both open the same
  standalone window; opening from the Lectern does not disturb the editor lock / in-progress edit.
- [x] 6.7 The HUD and the settings window do NOT change theme when the toggle flips — both stay on the
  player's global theme.

## 7. Docs

- [x] 7.1 Append a LibGUI theming lesson to `VSAPI-NOTES.md` (`## LibGUI`): per-dialog theme = wrap
  `Build()` in `new Theme(...)` (no `GuiBase` hook); `ColorScheme.Default()` is the only built-in preset
  and `ThemeData.Default` is the player's global `libgui.json` theme (a light theme is net-new); the
  `WindowFrame` title bar reads `ThemeData.Default` at construction and a bare `new Text(...)` defaults to
  white — both must be set explicitly. Record the in-game legibility verdict (confirmed 2026-07-25).
