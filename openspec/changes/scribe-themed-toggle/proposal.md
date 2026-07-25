## Why

The Scribe dialogs render functional widgets over a transparent window body under LibGUI's single
built-in theme — a dark parchment scheme (`ThemeData.Default`), the only preset that ships. The user
wants a themed, physical-object look (dark ink on light parchment), but that look must be optional and
must never depend on hand-authored art: the mod has to stay fully usable with zero PNGs. This change
adds the foundation both goals hang off — a persisted client toggle that flips every Scribe surface
between a net-new light theme and the stock dark fallback.

## What Changes

- Add a new client-local player preference `ThemedBackgrounds` (default **on**) to
  `src/Core/ScribePlayerSettings.cs` as pure data (no VS API). Persistence and live propagation come
  for free via the existing `UpdateMySettings` → `scribe-hud-config.json` → `MyPinsChanged` path.
- Add a `ThemedBackgrounds` checkbox to the Appearance section of `ScribeSettingsContent`, mirroring the
  existing `HudCollapsed` checkbox, plus `settings-themedbackgrounds` / `-help` localization keys.
- Add a net-new light `ThemeData` in a new `src/Mod/ScribeTheme.cs` (dark text on light parchment
  surfaces) and a `ScribeTheme.For(bool)` selector that returns the light theme when themed, or the
  stock `ThemeData.Default` dark fallback when not.
- Apply the chosen theme per dialog by wrapping each `Build()` output in `new Theme(...)` in the Lectern
  dialog, the pinned-task HUD, and the standalone settings dialog — the supported per-dialog switch,
  since `GuiBase` exposes no theme override hook.
- Handle the surfaces that do NOT follow a `Theme` wrap: pass explicit `titleBarColor:` / `textColor:` to
  each `WindowFrame` (its title bar reads `ThemeData.Default` at construction), and make the HUD's
  hardcoded dark glow halo theme-conditional so HUD text stays legible in either mode.
- No **BREAKING** changes: the light theme is net-new behavior; the toggle defaults on but every surface
  falls back to the existing dark look when it is off.

This change is Phase 1 (the "themed toggle" foundation). It deliberately excludes illustrated
backgrounds (follow-on change `scribe-gui-backdrops`), animated navigation tabs (`scribe-animated-tabs`),
and the slide-out pin editor (`scribe-pin-editor`). Those phases build on the theme split established
here; the toggle is intentionally the single "themed mode" switch they will all read.

## Capabilities

### New Capabilities

- `client-theme-preference` (spec created at `specs/client-theme-preference/spec.md`): a persisted,
  client-local `ThemedBackgrounds` preference that toggles all Scribe GUI surfaces between a net-new
  light theme and the stock dark LibGUI fallback, live and without a restart, and that guarantees the
  mod remains fully usable with zero art when the fallback is active. Covers the setting's existence /
  default / persistence / client-locality, the light-vs-fallback theming of the Lectern + HUD + settings
  dialog, the explicit title-bar and HUD-halo handling that the `Theme` wrap does not reach, and live
  propagation across all open surfaces.

### Modified Capabilities

- None. The light theme is net-new behavior layered onto existing surfaces; it does not change the
  normative requirements of `lectern-gui-shell`, `player-pins`, `settings-tab`, or any other existing
  spec. (Follow-on phases may modify those specs; this phase does not.)

## Impact

- **Core (`src/Core/`)**: `ScribePlayerSettings` gains one plain `bool ThemedBackgrounds { get; set; } =
  true;` — pure data, no clamp, no VS API, so Core stays unit-testable.
- **Mod (`src/Mod/`)**: new `ScribeTheme.cs` (light `ThemeData` + `For(bool)` selector);
  `GuiDialogScribeLecternLibGui`, `HudScribePins`, and `ScribeSettingsDialog` each wrap `Build()` in
  `new Theme(...)` and compute explicit `WindowFrame` title-bar colors from the active scheme; the HUD
  glow halo becomes theme-conditional; `ScribeSettingsContent` gains the checkbox.
- **Assets**: `assets/scribe/lang/en.json` gains `settings-themedbackgrounds` and
  `settings-themedbackgrounds-help`.
- **No new dependencies**: LibGUI (`gui`) is the existing hard dep; `Theme` / `ThemeData` / `ColorScheme`
  already ship in it. No art assets are required by this change.
- **Docs**: `VSAPI-NOTES.md` gains a LibGUI theming lesson (per-dialog `new Theme(...)` wrap; title bar
  and bare `Text` and the HUD halo do not follow the wrap).
- **Verification**: in-game only — the Core suite cannot reach `src/Mod` GUI code or the VS API.
