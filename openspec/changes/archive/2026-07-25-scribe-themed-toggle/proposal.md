## Why

The Scribe Lectern renders functional widgets over a transparent window body under LibGUI's single
built-in theme — a dark parchment scheme (`ThemeData.Default`), the only preset that ships. The user
wants a themed, physical-object look for the Lectern (dark ink on light parchment), but that look must be
optional and must never depend on hand-authored art: the mod has to stay fully usable with zero PNGs.
This change adds the foundation the later art phases hang off — a persisted client toggle that flips the
Lectern between a net-new light theme and the player's own global GUI theme.

## What Changes

- Add a new client-local player preference `PixelArtDisplay` (default **on**) to
  `src/Core/ScribePlayerSettings.cs` as pure data (no VS API). Persistence and live propagation come
  for free via the existing `UpdateMySettings` → `scribe-hud-config.json` → `MyPinsChanged` path.
- Add a `PixelArtDisplay` checkbox ("Pixel-Art Display") to the Appearance section of
  `ScribeSettingsContent`, mirroring the existing `HudCollapsed` checkbox, plus `settings-pixelartdisplay`
  / `-help` localization keys.
- Add a net-new light `ThemeData` in a new `src/Mod/ScribeTheme.cs` (dark text on light parchment
  surfaces) and a `ScribeTheme.For(bool)` selector that returns the light theme when on, or the player's
  global theme (`ThemeData.Default`, loaded by LibGUI from the player's `libgui.json`) when off.
- Apply the theme to the **Lectern dialog only** by wrapping its `Build()` output in `new Theme(...)` —
  the supported per-dialog switch, since `GuiBase` exposes no theme override hook — and pass explicit
  `titleBarColor:` / `textColor:` to its `WindowFrame` (its title bar reads `ThemeData.Default` at
  construction, so it does not follow the wrap). The pinned-task HUD and the settings window are
  deliberately NOT wrapped: they always follow the player's global theme.
- Consolidate settings access to a single standalone window: remove the in-Lectern settings *tab* (the
  swap-in-place central-region view) and repoint the Lectern gear at the same standalone
  `ScribeSettingsDialog` the HUD gear opens, hoisted to `ScribeModSystem.OpenSettings()`.
- No **BREAKING** changes: the light theme is net-new behavior; the toggle defaults on but the Lectern
  falls back to the player's global theme (default dark) when it is off, depending on no art.

This change is Phase 1 (the "themed toggle" foundation). It deliberately excludes illustrated
backgrounds (follow-on change `scribe-gui-backdrops`), animated navigation tabs (`scribe-animated-tabs`),
and the slide-out pin editor (`scribe-pin-editor`). Those phases build on the theme split established
here; the toggle is intentionally the single "pixel-art mode" switch they will all read.

## Capabilities

### New Capabilities

- `client-theme-preference` (spec created at `specs/client-theme-preference/spec.md`): a persisted,
  client-local `PixelArtDisplay` preference that toggles the Lectern dialog between a net-new light theme
  and the player's global LibGUI theme, live and without a restart, and that guarantees the mod remains
  fully usable with zero art when the toggle is off. Covers the setting's existence / default /
  persistence / client-locality, the light-vs-global theming of the Lectern, the explicit title-bar
  handling that the `Theme` wrap does not reach, the deliberate exclusion of the HUD and settings window
  from the toggle (they always follow the global theme), the single settings window reachable from both
  the Lectern and HUD gears, and live propagation to the open Lectern.

### Modified Capabilities

- None. The light theme is net-new behavior layered onto existing surfaces; it does not change the
  normative requirements of `lectern-gui-shell`, `player-pins`, `settings-tab`, or any other existing
  spec. (Follow-on phases may modify those specs; this phase does not.)

## Impact

- **Core (`src/Core/`)**: `ScribePlayerSettings` gains one plain `bool PixelArtDisplay { get; set; } =
  true;` — pure data, no clamp, no VS API, so Core stays unit-testable.
- **Mod (`src/Mod/`)**: new `ScribeTheme.cs` (light `ThemeData` + `For(bool)` selector);
  `GuiDialogScribeLecternLibGui` wraps its `Build()` in `new Theme(...)` and computes explicit
  `WindowFrame` title-bar colors from the active scheme; its in-Lectern settings view is removed and its
  gear repointed at the shared standalone window; `ScribeModSystem` gains `OpenSettings()` owning the one
  `ScribeSettingsDialog`; `HudScribePins` drops its own settings-dialog ownership and calls the shared
  opener (it is NOT theme-wrapped); `ScribeSettingsContent` gains the checkbox.
- **Assets**: `assets/scribe/lang/en.json` gains `settings-pixelartdisplay` and
  `settings-pixelartdisplay-help`; the now-unused `settings-back` key is removed.
- **No new dependencies**: LibGUI (`gui`) is the existing hard dep; `Theme` / `ThemeData` / `ColorScheme`
  already ship in it. No art assets are required by this change.
- **Docs**: `VSAPI-NOTES.md` gains a LibGUI theming lesson (per-dialog `new Theme(...)` wrap; title bar
  and bare `Text` do not follow the wrap; `ThemeData.Default` is the player's global `libgui.json` theme).
- **Verification**: in-game only — the Core suite cannot reach `src/Mod` GUI code or the VS API.
  Confirmed by playtest 2026-07-25 (all in-game items pass).
