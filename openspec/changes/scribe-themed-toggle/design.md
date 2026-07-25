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
(`textColor:colors.OnSurface`, caret/selection from `Primary`), `ScribeRowButton`, the settings form, and
every existing `new Text(...)` (each already passes an explicit theme color). Three things do **not**
follow the wrap: (1) `WindowFrame`/`WindowTitleBar` read `ThemeData.Default` at *construction*, not from
context (`WindowTitleBar.cs:56`), so the title bar must get explicit `titleBarColor:` / `textColor:`
(`Vector4?` params on `WindowFrame`); (2) the HUD glow halo is a hardcoded dark constant
(`HudScribePins.cs:503`, `new Vector4(0,0,0,0.9)`); (3) a bare `new Text(...)` defaults to white
(`TextStyle` default `Color = Vector4.One`) — but every existing Scribe text widget already passes a theme
color, so only *new* bare text would be at risk.

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
- Add a persisted, client-local `ThemedBackgrounds` preference (default on) as pure Core data.
- Ship a net-new light `ThemeData` (dark text on light parchment) and a `ScribeTheme.For(bool)` selector
  returning it or the stock `ThemeData.Default` fallback.
- Apply the chosen theme per dialog (Lectern, HUD, standalone settings dialog) via a `new Theme(...)`
  wrap, and handle the surfaces the wrap does not reach (title bar, HUD halo) explicitly.
- Guarantee a mandatory zero-art fallback: with the toggle off, every surface renders the stock dark
  LibGUI look and depends on no asset.

**Non-Goals:**
- Illustrated per-item / per-view backgrounds (`scribe-gui-backdrops`).
- Animated navigation tabs replacing the gear header (`scribe-animated-tabs`).
- The slide-out pin-editor pagelet and its cross-document sync (`scribe-pin-editor`).
- Any server-side, per-world, or cross-player theming — the preference is strictly client-local.
- A per-surface theme control — one toggle governs all Scribe surfaces.

## Decisions

### D1: The `ThemedBackgrounds` bool lives in Core as pure data

Add `public bool ThemedBackgrounds { get; set; } = true;` to `src/Core/ScribePlayerSettings.cs`. It is a
plain scalar with no clamp and no VS API reference, so Core stays unit-testable and the architectural rule
(Core never references the game API) holds. Persistence, normalization, and live propagation are inherited
free from `UpdateMySettings` → `Normalized()` → `StoreModConfig("scribe-hud-config.json")` →
`MyPinsChanged` (`ScribeModSystem.cs:150`) — no new field-specific plumbing.
- *Rationale:* mirrors every existing display pref; the free propagation event is exactly what makes the
  live cross-surface toggle (D5) cost nothing.

### D2: Per-dialog `new Theme(...)` wrap, not a global `ThemeData.Default` swap

Wrap each dialog's `Build()` output in `new Theme(ScribeTheme.For(modSystem.MySettings.ThemedBackgrounds),
child: <window>)` rather than mutating the global `ThemeData.Default` (which `GuiBase.BuildRootTree` bakes
in at `GuiBase.cs:1426`).
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
`ScribeTheme.For(bool themed) => themed ? Light : ThemeData.Default` is the single selector every dialog
calls. Per-widget style structs (`ButtonStyle`, `CheckboxStyle`, …) cascade automatically from the scheme
when omitted, so only the 17 `ColorScheme` roles need authoring.
- *Rationale:* one selector keeps the light/fallback choice in exactly one place; using the untouched
  `ThemeData.Default` as the fallback is what makes the zero-art fallback free and mandatory.
- *Alternatives rejected:* authoring a second dark theme for the fallback (pointless — the shipped default
  already is the intended fallback); overriding every per-widget style struct by hand (unnecessary given
  the automatic cascade).

### D4: The toggle bundles theme + (future) backgrounds as one "themed mode"

`ThemedBackgrounds` is intentionally the single switch for both the light theme (this phase) and the
illustrated backgrounds (the follow-on `scribe-gui-backdrops` phase). On = light theme (+ later, art
backgrounds); off = stock dark theme with plain flat panels and no art.
- *Rationale:* the user specified these are one feature — the toggle *is* the dark/light switch, and the
  art always rides with the light theme. A single preference avoids an incoherent "light theme but dark
  fallback backgrounds" state and gives the later backgrounds phase a switch that already exists.
- *Alternatives rejected:* two separate settings (theme vs backgrounds) — permits nonsensical
  combinations and doubles the settings surface for no user benefit.

### D5: Live cross-surface propagation reuses `MyPinsChanged`; the setting is read fresh each build

The Lectern, HUD, and settings dialog already subscribe to `MyPinsChanged` and `ForceRebuild()`; each
reads `modSystem.MySettings.ThemedBackgrounds` fresh inside `Build()` and re-wraps in the current theme.
Because `UpdateMySettings` fires `MyPinsChanged`, toggling the checkbox relights every open surface with
no restart and no reopen — following the `WindowFontScale` "read fresh each build" precedent
(`GuiDialogScribeLecternLibGui.cs:845`).
- *Rationale:* zero new machinery; the same event already fans layout changes out live.

### D6: Explicit `WindowFrame` title-bar colors; the settings surface via its host

Each `WindowFrame` gets explicit `titleBarColor:` / `textColor:` (`Vector4?` params) computed in `Build()`
from `ScribeTheme.For(...)`'s scheme, because `WindowTitleBar` reads `ThemeData.Default` at construction
(`WindowTitleBar.cs:56`) and will not follow the wrap. The in-Lectern settings *view* recolors with the
Lectern (it is central-region content under the Lectern's `Theme` wrap); the standalone HUD-gear settings
dialog (`ScribeSettingsDialog`) wraps its own `Build()` and sets its own title-bar colors.
- *Rationale:* the title bar is the one framed element guaranteed not to follow the wrap; computing its
  colors from the active scheme keeps it consistent with the body in both modes.

## Risks / Trade-offs

- **[Risk] The `WindowFrame` title bar does not follow the `Theme` wrap** (reads `ThemeData.Default` at
  construction, `WindowTitleBar.cs:56`) → **Mitigation:** pass explicit `titleBarColor:` / `textColor:` to
  every themed `WindowFrame`, computed from the active scheme each `Build()`; verify the title stays
  legible in both modes in-game.
- **[Risk] The HUD glow halo is a hardcoded dark constant** (`HudScribePins.cs:503`,
  `new Vector4(0,0,0,0.9)`) and will not invert with the theme → **Mitigation:** make the halo color
  theme-conditional (light halo in light mode, dark in fallback); verify HUD text legibility over both.
- **[Risk] A bare `new Text(...)` defaults to white** (`TextStyle` default `Color = Vector4.One`) and
  would vanish on a light surface → **Mitigation:** all existing Scribe text already passes a theme color;
  any *new* text widget introduced with the wrap must pass `Color = colors.OnSurface/OnBackground`. Watch
  for this when touching the settings form and title.
- **[Risk] Light-theme contrast is unverifiable outside the game** (the Core suite cannot reach `src/Mod`
  GUI code or the VS API) → **Mitigation:** verification is in-game only — confirm dark-on-light contrast
  for rows, fields, buttons, the title bar, and the HUD halo across the Lectern, HUD, and settings dialog;
  record the legibility verdict in `VSAPI-NOTES.md`.
- **[Trade-off] Bundling theme + backgrounds under one toggle** means the two cannot be exercised
  independently → accepted per the user's "one setting" decision (D4); the light theme alone is fully
  testable now, and the backgrounds phase adds art behind the same switch.
- **[Constraint] Core purity:** the `ThemedBackgrounds` bool must stay in `src/Core/ScribePlayerSettings.cs`
  as pure data; all theme/GUI code (`ScribeTheme.cs`, the wraps, the halo) lives in `src/Mod`. `src/Core`
  must never reference the VS API.
