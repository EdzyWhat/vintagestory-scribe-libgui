## 1. Core: the themed-mode setting

- [ ] 1.1 Add `public bool ThemedBackgrounds { get; set; } = true;` to
  `src/Core/ScribePlayerSettings.cs` as pure data (no clamp, no VS API); confirm `Normalized()` leaves it
  untouched (a bool needs no clamping).
- [ ] 1.2 Confirm the field round-trips through `scribe-hud-config.json` load/store with no code change
  (Newtonsoft serializes the new property; absent key defaults to `true`).
- [ ] 1.3 (If a `ScribePlayerSettings` serialization/default test exists) add a case asserting
  `ThemedBackgrounds` defaults to `true`; run the Core suite (`dotnet test tests/Core.Tests`).

## 2. Settings form control + localization

- [ ] 2.1 Add a `ThemedBackgrounds` `Checkbox` to the Appearance section of `ScribeSettingsContent`
  (mirror the `HudCollapsed` control at ~:98): `LabeledControl("settings-themedbackgrounds", colors,
  new Checkbox(value: settings.ThemedBackgrounds, onChanged: v => onMutate(s => s.ThemedBackgrounds = v),
  size: 22))`.
- [ ] 2.2 Add `settings-themedbackgrounds` and `settings-themedbackgrounds-help` keys to
  `assets/scribe/lang/en.json` (label + hover-help, matching the existing `settings-<key>` convention).

## 3. Mod: the light theme

- [ ] 3.1 Create `src/Mod/ScribeTheme.cs` defining `static readonly ThemeData Light` as a
  `new ThemeData(new ColorScheme { … })` populating all 17 roles for a light parchment scheme (light
  `Surface`/`SurfaceLow`/`SurfaceHigh`/`Background`, dark `OnSurface`/`OnBackground`, a warm `Primary`
  accent, readable `Border`/`StateHover`/`StateSelected`), letting the per-widget style structs cascade.
- [ ] 3.2 Add `public static ThemeData For(bool themed) => themed ? Light : ThemeData.Default;` as the
  single selector; the fallback is the untouched framework default.

## 4. Apply the theme wrap + explicit title-bar colors per dialog

- [ ] 4.1 In `GuiDialogScribeLecternLibGui`, read `modSystem.MySettings.ThemedBackgrounds` fresh in
  `Build()` and wrap the window output in `new Theme(ScribeTheme.For(themed), child: <window>)`.
- [ ] 4.2 In the Lectern `Build()`, compute explicit `titleBarColor:` / `textColor:` for the `WindowFrame`
  from the active scheme (it reads `ThemeData.Default` at construction, `WindowTitleBar.cs:56`, so it will
  not follow the wrap).
- [ ] 4.3 In `ScribeSettingsDialog` (the standalone HUD-gear settings window), wrap its `Build()` output
  in the same `new Theme(ScribeTheme.For(themed), …)` and set its `WindowFrame` title-bar colors from the
  active scheme.
- [ ] 4.4 In `HudScribePins`, wrap its `Build()` output in `new Theme(ScribeTheme.For(themed), …)` so HUD
  body content recolors with the mode.

## 5. Mod: HUD glow-halo inversion

- [ ] 5.1 Make the hardcoded dark glow halo (`HudScribePins.cs:503`, `new Vector4(0,0,0,0.9)`)
  theme-conditional: a light halo in themed (light) mode, the existing dark halo in fallback mode, so HUD
  text stays legible over both.

## 6. In-game verification

- [ ] 6.1 With the toggle ON, open the Lectern, the HUD, and the settings surface: all three render dark
  text on light surfaces; the title bar and HUD halo are light and text is legible everywhere.
- [ ] 6.2 With the toggle OFF, all three render the stock dark LibGUI theme with plain flat panels and no
  art; every surface stays fully legible and usable with zero art assets present.
- [ ] 6.3 Toggle the setting while the Lectern, HUD, and settings surface are all open: all three flip
  between light and dark live, with no reopen and no restart.
- [ ] 6.4 Confirm the setting persists across a relog (written to `scribe-hud-config.json`) and defaults
  to ON on a fresh profile.
- [ ] 6.5 Confirm no bare `new Text(...)` renders white-on-light (all text passes a theme color); check
  the title bar text and settings-form labels specifically.

## 7. Docs

- [ ] 7.1 Append a LibGUI theming lesson to `VSAPI-NOTES.md` (`## LibGUI`): per-dialog theme =
  wrap `Build()` in `new Theme(...)` (no `GuiBase` hook); `ColorScheme.Default()` is the only preset (a
  light theme is net-new); the `WindowFrame` title bar reads `ThemeData.Default` at construction and a
  bare `new Text(...)` defaults to white — both must be set explicitly; the HUD glow halo is a hardcoded
  constant that must be inverted for light mode. Record the in-game legibility verdict.
