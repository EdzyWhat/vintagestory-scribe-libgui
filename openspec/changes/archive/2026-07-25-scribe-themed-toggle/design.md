## Context

Scribe's GUI runs on LibGUI (modid `gui`, a hard dep). LibGUI's theming lives in
`reference/vslibgui/.../Widgets/Framework/Theme.cs`: a `ColorScheme` is a `readonly struct` of 17
`Vector4` roles (`Surface`/`OnSurface`/`Background`/`OnBackground`/`Primary`/`Border`/`SurfaceLow`/
`SurfaceHigh`/`StateHover`/`StateSelected`/…). `ColorScheme.Default()` is the **only** preset that ships,
and it is already a **dark parchment** palette (dark `Surface` ≈ 0.16/0.12/0.07, light `OnSurface` text).
There is no light preset — a light theme is net-new.

`Theme` is an `InheritedWidget`; `Theme.Of(context)` returns the nearest ancestor's `ThemeData` (else
`ThemeData.Default`). `GuiBase` gives no override hook — `BuildRootTree` (`GuiBase.cs:1426`) always wraps
content in the global `Theme(ThemeData.Default…)`. The supported per-dialog switch is to wrap a dialog's
own `Build()` output in `new Theme(chosenThemeData, child)`: descendants recolor because
`UpdateShouldNotify` compares `ThemeData` by reference, so passing a different instance plus a rebuild
recolors with no teardown. Grepping `src/` for `new Theme(` / `ThemeData` returns zero hits today — this
change introduces the first per-dialog theme in the mod.

Most body content auto-recolors from `Theme.Of(context)`: rows, `ScribeMultilineField`
(`textColor:colors.OnSurface`, caret/selection from `Primary`), `ScribeRowButton`, and
every existing `new Text(...)` (each already passes an explicit theme color). Two things do **not**
follow the wrap on the surface that IS wrapped (the Lectern): (1) `WindowFrame`/`WindowTitleBar` read
`ThemeData.Default` at *construction*, not from context (`WindowTitleBar.cs:231`), so the title bar must
get explicit `titleBarColor:` / `textColor:` (`Vector4?` params on `WindowFrame`); (2) a bare
`new Text(...)` defaults to white (`TextStyle` default `Color = Vector4.One`) — but every existing Scribe text widget already passes a theme
color, so only *new* bare text would be at risk. Note `ThemeData.Default` is not a hardcoded dark
constant: LibGUI's `GuiModSystem.LoadThemeConfig` sets it from the player's `libgui.json`
(`reference/vslibgui/.../GuiModSystem.cs:277`), so it IS the player's global theme — which is why "off"
means "follow my global theme," not "force stock dark." The HUD and the settings window are deliberately
NOT wrapped, so they read this global default unchanged.

The setting plumbing is already in place. `ScribePlayerSettings` (`src/Core/`) is a plain mutable class of
client-local display prefs, never server-synced (`CompletionPolicy`, `HudCollapsed`, `HudMaxRows`,
`HudAnchor`, `HudOffset*`, `HudRowWidth`, `HudFontScale`, `WindowFontScale`). Adding a bool is one line and
needs no clamp. Persistence and propagation are free: `UpdateMySettings(mutate)`
(`ScribeModSystem.cs:150`) mutates the single live instance, calls `Normalized()`, `StoreModConfig(...,
"scribe-hud-config.json")`, then fires `MyPinsChanged` — the universal rebuild event the Lectern, HUD, and
settings dialog all subscribe to (each `ForceRebuild`s). Every build-time read of `MySettings` re-runs. No
new event and no network. The `WindowFontScale` precedent (`GuiDialogScribeLecternLibGui.cs:845`, read
fresh each `Build()` via `ScribeRowStyle.FromSettings(MySettings)`) is exactly the "read fresh each build"
path the theme bool follows; only its consumption site (the `new Theme(...)` wrap) is new.

This is Phase 1 of a larger themed-GUI effort (illustrated backgrounds, animated tabs, slide-out pin
editor). Phase 1 establishes the themed/fallback split alone so both states are exercisable in-game from
day one; the later phases (`scribe-gui-backdrops`, `scribe-animated-tabs`, `scribe-pin-editor`) hang off
this toggle.

## Goals / Non-Goals

**Goals:**
- Add a persisted, client-local `PixelArtDisplay` preference (default on) as pure Core data.
- Ship a net-new light `ThemeData` (dark text on light parchment) and a `ScribeTheme.For(bool)` selector
  returning it or the player's global `ThemeData.Default` theme.
- Apply the chosen theme to the Lectern via a `new Theme(...)` wrap, and handle the surface the wrap does
  not reach (the title bar) explicitly.
- Leave the pinned-task HUD and the standalone settings window on the player's global theme (not governed
  by the toggle).
- Consolidate settings to one standalone window reachable from both the Lectern and HUD gears.
- Guarantee a mandatory zero-art fallback: with the toggle off, the Lectern renders the player's global
  LibGUI look and depends on no asset.

**Non-Goals:**
- Illustrated per-item / per-view backgrounds (`scribe-gui-backdrops`).
- Animated navigation tabs replacing the gear header (`scribe-animated-tabs`).
- The slide-out pin-editor pagelet and its cross-document sync (`scribe-pin-editor`).
- Any server-side, per-world, or cross-player theming — the preference is strictly client-local.
- Theming the HUD or settings window — those always follow the player's global theme.
- A per-surface theme control — one toggle governs the Lectern's theme.

## Decisions

### D1: The `PixelArtDisplay` bool lives in Core as pure data

Add `public bool PixelArtDisplay { get; set; } = true;` to `src/Core/ScribePlayerSettings.cs`. It is a
plain scalar with no clamp and no VS API reference, so Core stays unit-testable and the architectural rule
(Core never references the game API) holds. Persistence, normalization, and live propagation are inherited
free from `UpdateMySettings` → `Normalized()` → `StoreModConfig("scribe-hud-config.json")` →
`MyPinsChanged` (`ScribeModSystem.cs:150`) — no new field-specific plumbing.
- *Rationale:* mirrors every existing display pref; the free propagation event is exactly what makes the
  live cross-surface toggle (D5) cost nothing.

### D2: Per-dialog `new Theme(...)` wrap on the Lectern only, not a global `ThemeData.Default` swap

Wrap the **Lectern** dialog's `Build()` output in
`new Theme(ScribeTheme.For(modSystem.MySettings.PixelArtDisplay), child: <window>)` rather than mutating
the global `ThemeData.Default` (which `GuiBase.BuildRootTree` bakes in at `GuiBase.cs:1426`). The HUD and
the settings window are NOT wrapped — they render on the global default (see D4/D6).
- *Rationale:* `GuiBase` exposes no theme override hook, and the wrap is the framework-supported switch —
  `UpdateShouldNotify` compares `ThemeData` by reference, so a different instance plus a rebuild recolors
  every descendant that reads `Theme.Of(context)` with no teardown.
- *Alternatives rejected:* reassigning the global `ThemeData.Default` would recolor **all** GUIs
  (including vanilla and other mods' dialogs that rely on it), is process-global mutable state, and would
  fight anything else reading the default. A subclass hook into `GuiBase` doesn't exist and would be
  upstream surgery on the dependency.

### D3: `ScribeTheme.For(bool)` selector; a net-new light theme, stock dark as fallback

New `src/Mod/ScribeTheme.cs` defines `static readonly ThemeData Light` = `new ThemeData(new ColorScheme {
Surface=<light>, OnSurface=<dark>, Background=<lighter>, OnBackground=<dark>, Primary=<warm accent>, … all
17 roles })`, and exposes the fallback simply as the framework's `ThemeData.Default`. A helper
`ScribeTheme.For(bool pixelArt) => pixelArt ? Light : ThemeData.Default` is the single selector the
Lectern calls; the `ThemeData.Default` branch is the player's global theme, not a forced dark preset. Per-widget style structs (`ButtonStyle`, `CheckboxStyle`, …) cascade automatically from the scheme
when omitted, so only the 17 `ColorScheme` roles need authoring.
- *Rationale:* one selector keeps the light/global choice in exactly one place; using the untouched
  `ThemeData.Default` (the player's global theme) as the off-state is what makes the zero-art fallback
  free and mandatory.
- *Alternatives rejected:* authoring a second dark theme for the off-state (pointless — the player's
  global theme already is the intended fallback); overriding every per-widget style struct by hand
  (unnecessary given the automatic cascade).

### D4: The toggle governs the Lectern only — the HUD and settings window follow the global theme

`PixelArtDisplay` is intentionally the single switch for the Lectern's light theme (this phase) and its
illustrated backgrounds (the follow-on `scribe-gui-backdrops` phase). On = Lectern light theme (+ later,
art backgrounds); off = the Lectern follows the player's global theme, plain, no art. The pinned-task HUD
and the standalone settings window are NOT governed by this toggle — they always render on the player's
global theme (clarification 2026-07-25: the earlier plan had the HUD and settings track the toggle too;
the user scoped it to the Lectern, since the HUD sits over the world and the settings window is "the
remainder" the player controls via their own `libgui.json`).
- *Rationale:* the user specified the pixel-art look is a Lectern (document-surface) concept; the HUD and
  settings are chrome that should honor the player's own global theme choice. A single preference still
  gives the later backgrounds phase a switch that already exists.
- *Alternatives rejected:* wrapping every Scribe surface (the HUD toggled with the setting in an early
  build — rejected on the user's feedback); two separate settings (theme vs backgrounds) — permits
  nonsensical combinations for no user benefit.

### D5: Live propagation to the open Lectern reuses `MyPinsChanged`; the setting is read fresh each build

The Lectern already subscribes to `MyPinsChanged` and `ForceRebuild()`; it reads
`modSystem.MySettings.PixelArtDisplay` fresh inside `Build()` and re-wraps in the current theme. Because
`UpdateMySettings` fires `MyPinsChanged`, toggling the checkbox (in the settings window, itself rebuilt
by the same event) relights the open Lectern with no restart and no reopen — following the
`WindowFontScale` "read fresh each build" precedent (`GuiDialogScribeLecternLibGui.cs:845`).
- *Rationale:* zero new machinery; the same event already fans layout changes out live.

### D6: Explicit Lectern `WindowFrame` title-bar colors; one settings window from two gears

The Lectern's `WindowFrame` gets explicit `titleBarColor:` / `textColor:` (`Vector4?` params) computed in
`Build()` from `ScribeTheme.For(...)`'s scheme, because `WindowTitleBar` reads `ThemeData.Default` at
construction (`WindowTitleBar.cs:231`) and will not follow the wrap. There is ONE settings surface — a
standalone `ScribeSettingsDialog` owned by `ScribeModSystem.OpenSettings()` — opened by BOTH the Lectern
gear and the HUD gear; the former in-Lectern settings *view* (a third central-region state) was removed.
The settings window is not theme-wrapped and sets no explicit title-bar colors, so it follows the global
theme end to end. Opening it from the Lectern floats a separate window over the editor, so the Lectern
keeps its lock and in-progress edit (no commit/release needed, unlike the old in-place swap).
- *Rationale:* the title bar is the one framed element guaranteed not to follow the wrap; a single
  settings window avoids duplicating the form and keeps settings reachable from either surface.

## Risks / Trade-offs

- **[Risk] The `WindowFrame` title bar does not follow the `Theme` wrap** (reads `ThemeData.Default` at
  construction, `WindowTitleBar.cs:231`) → **Mitigation:** pass explicit `titleBarColor:` / `textColor:`
  to the Lectern's `WindowFrame`, computed from the active scheme each `Build()`; verify the title stays
  legible in both modes in-game. *(Confirmed 2026-07-25.)*
- **[Risk] A bare `new Text(...)` defaults to white** (`TextStyle` default `Color = Vector4.One`) and
  would vanish on a light surface → **Mitigation:** all existing Scribe text already passes a theme color;
  any *new* text widget introduced with the wrap must pass `Color = colors.OnSurface/OnBackground`. Watch
  for this when touching the Lectern title. *(Confirmed 2026-07-25: no white-on-light text.)*
- **[Risk] Scoping creep — an early build wrapped the HUD too**, so it toggled with the setting when it
  should always follow the global theme → **Resolved 2026-07-25:** removed the HUD's `Theme` wrap (and the
  halo inversion that came with it); only the Lectern is wrapped. Confirmed in playtest.
- **[Risk] Light-theme contrast is unverifiable outside the game** (the Core suite cannot reach `src/Mod`
  GUI code or the VS API) → **Mitigation:** verification is in-game only — confirm dark-on-light contrast
  for rows, fields, buttons, and the title bar on the Lectern; record the legibility verdict in
  `VSAPI-NOTES.md`. *(All in-game items confirmed 2026-07-25.)*
- **[Trade-off] Bundling theme + backgrounds under one toggle** means the two cannot be exercised
  independently → accepted per the user's "one setting" decision (D4); the light theme alone is fully
  testable now, and the backgrounds phase adds art behind the same switch.
- **[Constraint] Core purity:** the `PixelArtDisplay` bool must stay in `src/Core/ScribePlayerSettings.cs`
  as pure data; all theme/GUI code (`ScribeTheme.cs`, the Lectern wrap) lives in `src/Mod`. `src/Core`
  must never reference the VS API.
