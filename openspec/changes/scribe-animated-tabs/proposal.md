> **SUPERSEDED (2026-07-26).** This change's horizontal tab-bar (`ScribeTabBar`) nav model was superseded
> by `scribe-notebook-frame` (shipped), which replaced the gear header with a **vertical right-column icon
> nav** and dropped `WindowFrame`. The settings page it references was also pulled into a standalone window
> (2026-07-25 pivot). The Pin Tab was consolidated into the retargeted `scribe-pin-editor` change using the
> shipped vertical nav, not this tab bar. Retained for its "nav routes through a real method, not an inline
> flag flip" discipline (adopted by `scribe-pin-editor`); do not implement as-is without reconciling
> against the shipped notebook-frame nav.

## Why

The Scribe Lectern dialog switches between its read/edit page and its settings page through a single
right-aligned gear button (`ScribeGearHeader`, `GuiDialogScribeLecternLibGui.cs:1707`) drawn just under
the title bar. The gear is an unlabeled, unanimated affordance, and it only appears on the read/editor
views: `BuildCentralRegion()` (`:825`) **early-returns the settings view**, so settings is composed on a
different code path with no shared chrome. As the mod grows a per-view visual system (distinct
backgrounds per page, a themed mode), the navigation between pages needs to become a first-class,
labeled, animated affordance that is present on every page and reads as part of the themed object.

This change replaces the gear header with an **animated pixel-art navigation tab bar** and folds the
settings view into one shared dialog shell so every view (read, editor, settings) flows through a single
nav row + single body. Crucially, read↔editor is **lock-gated** (a server `ScribeRequestAccessMessage`
round-trip via `EnterEditorMode`), while settings is a pure local toggle with a `wasEditorBeforeSettings`
return path. The tabs MUST route to the real lock-aware navigation methods — only the affordance
changes; the lock semantics and the return path are preserved exactly.

## What Changes

- Add `src/Mod/ScribeTabBar.cs`: an animated tab bar cloning LibGUI's `TabView` pattern
  (`GestureDetector` + `_activeIndex` + `SetState`) but swapping the stock `Container` for
  `AnimatedContainer` (color, ~150ms EaseOut — the same primitive `Button`/`IconButton` use for hover)
  plus an optional `AnimatedScale` (springy grow) on the active tab. It is a `StatefulWidget` carrying a
  `Key` so its animation `State` survives the dialog's `ForceRebuild` view swaps; its animation
  controllers are disposed in `State.Dispose()`. Tab chrome degrades gracefully: a flat animated-container
  placeholder now, optional `NineSliceBox` (crisp nearest-neighbor) chrome once pixel-art sprites land.
- Restructure `BuildCentralRegion()` (`:825`) so **all** views share one shell: the settings view stops
  early-returning; a single `ScribeTabBar` nav row sits above a single view-switched body. Tabs: a
  read/edit tab (the same page in two modes, labeled per mode) and a Settings tab.
- Route tab callbacks to the **real** lock-aware methods, never a bool flip: Read → `OnClickSwitchToRead`,
  Edit → `RequestEditorAccess` (server round-trip landing in `EnterEditorMode`), Settings →
  `OnClickOpenSettings` (which commits + releases the lock and records `wasEditorBeforeSettings`), leaving
  Settings → `OnClickCloseSettings` (which returns to the prior read/editor view, re-acquiring the lock
  via `RequestEditorAccess` when it left the editor).
- Set the title-bar color per mode in `Build()` (composes with `scribe-themed-toggle`): the `WindowFrame`
  title bar reads `ThemeData.Default` at construction and does not follow a `Theme` wrap, so pass an
  explicit `titleBarColor`/`textColor` computed from the active mode.
- Add lang keys for the tab labels; append a LibGUI animated-tabs lesson to `VSAPI-NOTES.md`.

## Capabilities

### New Capabilities
- `gui-navigation-tabs`: an animated tab bar that navigates a Scribe dialog between its read/edit page
  and its settings page. The active tab is visually animated (highlight and/or scale), the tab bar's
  animation state survives dialog rebuilds (keyed `State`, disposed controllers), and the chrome degrades
  from a flat animated placeholder to optional crisp pixel-art (`NineSliceBox`) once art exists.

### Modified Capabilities
- `lectern-gui-shell`: the gear-button navigation is replaced by the tab bar, and the settings view is
  folded into the single shared shell instead of early-returning. The read→editor and editor→read
  requirements are restated to be tab-driven while keeping the single-editor lock round-trip identical;
  a new requirement covers the settings tab's enter/return-to-prior-view (`wasEditorBeforeSettings`) path.

## Impact

- **Composes with `scribe-themed-toggle`** (per-mode title-bar color: the active theme decides the
  `WindowFrame` `titleBarColor`/`textColor`, since the title bar doesn't follow a `Theme` wrap) and with
  `scribe-gui-backdrops` (the read/editor and settings pages the tabs select are the views those
  backdrops wrap). This change keeps its scope to the tabs and the navigation restructure — it does NOT
  introduce the toggle, the light theme, the backdrops, or the pin editor.
- **Mod (`src/Mod/`)**: new `ScribeTabBar.cs`; `GuiDialogScribeLecternLibGui` loses `ScribeGearHeader` as
  the nav affordance, `BuildCentralRegion` is restructured into one shell (no settings early-return, one
  nav row + one body), and `Build()` sets a per-mode title-bar color.
- **Core (`src/Core/`)**: none. All widget/animation/VS code stays in `src/Mod/`; `src/Core/` never
  references the VS API.
- **Assets**: adds tab-label lang keys to `assets/scribe/lang/en.json`; pixel-art tab sprites are a later,
  non-blocking art deliverable — the flat animated-container placeholder stands in until then.
- **No new dependencies**: LibGUI (`gui`, the existing hard dep) already ships `GestureDetector`,
  `AnimatedContainer`, `AnimatedScale`, and `NineSliceBox`.
- **Verification is in-game only** (the Core suite cannot reach `src/Mod/` GUI or the VS API): confirm the
  active-tab animation, that Read↔Edit still acquires/releases the editor lock (not a bool flip), and that
  entering/leaving Settings returns to the correct prior view.
