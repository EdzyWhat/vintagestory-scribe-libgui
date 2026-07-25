## Context

The Scribe Lectern dialog (`GuiDialogScribeLecternLibGui`, `src/Mod/`) has three mutually-exclusive
views selected by two bool fields — `isEditorMode` (`:53`) and `isSettingsMode` (`:59`) — with no
controller: a switch mutates a field and calls `ForceRebuild()`. `BuildCentralRegion()` (`:825`)
**early-returns the settings view** and otherwise composes a `Column` of a `ScribeGearHeader`
(`:1707`) over the read/editor body. `ScribeGearHeader` is a single right-aligned `ScribeRowButton`
firing `OnClickOpenSettings`.

This change is Phase 3 (workstream B of the approved plan): replace the gear header with an animated
navigation tab bar and fold settings into one shared shell. It composes with the sibling
`scribe-themed-toggle` (per-mode title-bar color) and `scribe-gui-backdrops` (the read/editor and
settings pages the tabs select), but its own scope is strictly the tabs and the navigation restructure —
not the toggle, the light theme, the backdrops, or the pin editor.

Navigation facts from exploration (ground truth):
- read↔editor is **lock-gated**: `RequestEditorAccess` (`:392`) sends `ScribeRequestAccessMessage`, and
  the server grant lands in `EnterEditorMode` (`:235`) which sets `isEditorMode` and rebuilds into the
  editor; `OnClickSwitchToRead` (`:292`) leaves the editor and releases the lock. Settings is a pure
  local toggle: `OnClickOpenSettings` (`:306`) records `wasEditorBeforeSettings`, commits + releases the
  lock if it was in the editor, then shows settings; `OnClickCloseSettings` (`:323`) returns to the prior
  view, calling `RequestEditorAccess` again to re-grant the lock when the editor was active beforehand.
- Two dialog-owned `ScrollController`s (`sharedScrollController`, `settingsScrollController`) survive
  rebuilds and are disposed once in `OnGuiClosed`; `ForceRebuild` tears down view `State`. So any new
  animation controller must live on a `State` whose element identity is stabilized by a `Key`.

Animation facts from exploration (ground truth):
- LibGUI's `TabView` (`reference/vslibgui/.../Widgets/Basic/TabView.cs`) is **unanimated** — a `State`
  with `_activeIndex`, a `GestureDetector(onTap: e => SetState(() => _activeIndex = index))` per tab, and
  a stock `Container` that swaps its `BoxStyle.Color` **instantly** between `Primary@0.15` (active) and
  `Vector4.Zero`.
- `AnimatedContainer` (color/size, ~150ms EaseOut) is already how `Button`/`IconButton` animate hover;
  `AnimatedScale` gives a springy grow. `AnimatedOpacity` is an existing proven precedent in
  `HudScribePins.cs:555`. Frame pump: `GuiBase.OnRenderGUI` → `_tickerScheduler.Update` each frame;
  animation controllers rebuild via `OnValueChanged → SetState` and must be `Dispose()`d in
  `State.Dispose()`.
- `NineSliceBox` is LibGUI's only nearest-neighbor (crisp) path — reserved for pixel-art tab chrome once
  sprites exist; a flat `AnimatedContainer` stands in until then.

## Goals / Non-Goals

**Goals:**
- Replace `ScribeGearHeader` with an animated navigation tab bar (`ScribeTabBar`).
- Animate the active tab (highlight color and/or scale) rather than the instant swap stock `TabView`
  does.
- Fold the settings view into one shared dialog shell: one nav row over one view-switched body, with no
  settings early-return.
- Route tab callbacks to the **real** lock-aware navigation methods, preserving the editor lock
  round-trip and the `wasEditorBeforeSettings` return path exactly — only the affordance changes.
- Survive `ForceRebuild`: the tab bar's animation `State` is keyed; its controllers are disposed on
  `State.Dispose()`.
- Set the `WindowFrame` title-bar color per mode (composes with the themed toggle).
- Degrade gracefully: a flat animated-container placeholder now, optional `NineSliceBox` pixel-art chrome
  later — an art-only swap.

**Non-Goals:**
- The `ThemedBackgrounds` toggle and light theme (sibling `scribe-themed-toggle`).
- The per-view backdrops (sibling `scribe-gui-backdrops`) — this change selects the pages; it does not
  draw their art.
- The slide-out pin editor and its sync extension (sibling `scribe-pin-editor`).
- Shipping final tab-chrome art. Only the flat placeholder is in scope; `NineSliceBox` sprites are a
  later, non-blocking deliverable.
- Any `src/Core/` change — all widget/animation/VS code stays in `src/Mod/`.
- Changing the lock protocol, the network messages, or the editor-access flow in any way.

## Decisions

### D1: Clone `TabView`'s pattern into `ScribeTabBar`, animating the active tab
`ScribeTabBar` is a new `StatefulWidget` in `src/Mod/ScribeTabBar.cs` that clones stock `TabView`'s
`State` + `_activeIndex` + per-tab `GestureDetector(onTap: … SetState(…))` pattern, but swaps the stock
instant-swap `Container` for an `AnimatedContainer` (color, ~150ms EaseOut) plus an optional
`AnimatedScale` (springy grow) on the active tab.
- *Why clone rather than use stock `TabView`:* stock `TabView` swaps the active color **in a single
  frame** (`Color = isActive ? Primary@0.15 : Vector4.Zero`) with no animation, and it *owns its own
  content* (`Widget.Tabs[_activeIndex].Content`) — it would host the read/editor/settings bodies itself,
  which conflicts with the dialog's lock-gated navigation (the body must be chosen by `isEditorMode` /
  `isSettingsMode` after a server round-trip, not by an internal `_activeIndex`). We want only the *tab
  strip* + animation, with navigation delegated out. Cloning the small `GestureDetector`+`_activeIndex`
  strip and driving it with `AnimatedContainer`/`AnimatedScale` (the exact primitives `Button`/
  `IconButton`/`HudScribePins` already use) gives animated tabs without adopting `TabView`'s
  content-ownership model.
- *Alternative rejected:* extending stock `TabView` to animate — it still owns content and instant-swaps;
  reworking it is more invasive than cloning the ~40-line strip.

### D2: Route tab callbacks to the real navigation methods, never a bool flip
Tab selections invoke the dialog's existing methods: Read → `OnClickSwitchToRead` (`:292`), Edit →
`RequestEditorAccess` (`:392`, the `ScribeRequestAccessMessage` round-trip that lands in
`EnterEditorMode` `:235`), Settings → `OnClickOpenSettings` (`:306`), leaving Settings →
`OnClickCloseSettings` (`:323`). `ScribeTabBar` takes these as `Action` callbacks and never touches
`isEditorMode` / `isSettingsMode`.
- *Why:* read↔editor is lock-gated and settings has a `wasEditorBeforeSettings` return path — those
  semantics live entirely in the four methods. A tab that flipped a flag directly would grant "editor"
  without the server lock (breaking single-editor exclusivity and desyncing edits) and would skip the
  commit/release + return-path bookkeeping. Only the affordance changes; the navigation contract is
  untouched.
- *Alternative rejected:* giving the tab bar the mode fields and calling `ForceRebuild` — this is exactly
  the bug the requirement forbids (a local flip bypassing the lock round-trip).

### D3: Restructure `BuildCentralRegion` into one shared shell (no settings early-return)
`BuildCentralRegion()` (`:825`) currently early-returns the settings view before ever reaching the
`ScribeGearHeader` + body `Column`. This change removes the early-return: the method always builds one
shell — a single `ScribeTabBar` nav row over a single `Expanded` body — and the body is chosen by the
existing mode fields (`isSettingsMode ? settings body : isEditorMode ? editor : read`). The settings body
becomes the existing `ScribeSettingsView` placed inside the shared shell rather than short-circuiting it.
- *Why:* the tab bar must be present on **every** page (including settings) so the player can navigate
  back to read/edit from settings via a tab. The old early-return meant settings had no shared chrome.
  Unifying to one shell is also what lets the sibling backdrops change wrap "the body" per view uniformly.
- *Trade-off:* `ScribeSettingsView`'s own Back header (`onBack: OnClickCloseSettings`) now coexists with
  the tab bar's Settings→read/edit route; both call `OnClickCloseSettings`, so they stay consistent (the
  Back affordance is redundant with the tab but harmless — either path returns to the prior view).

### D4: Key the tab bar's `State` so animation survives `ForceRebuild`
`ScribeTabBar` carries a stable `Key` (e.g. a `ValueKey`) so its element identity is preserved across the
dialog's `ForceRebuild` view swaps, keeping its `_activeIndex` and its `AnimatedContainer`/`AnimatedScale`
controllers alive rather than reconstructing them each rebuild.
- *Why:* `ForceRebuild` tears down view `State` (established fact). The two dialog-owned `ScrollController`s
  already survive rebuilds by living on the dialog and being disposed in `OnGuiClosed`; a keyed `State` is
  the analogous mechanism for a widget that owns animation controllers. Without the key, a mid-flight tab
  animation would reset on every rebuild (and every navigation causes a rebuild).
- *Note:* the tab bar's `_activeIndex` is a **display** mirror of the active mode; the authoritative view
  is still `isEditorMode`/`isSettingsMode` on the dialog. The bar syncs its index from the current mode on
  build so a server-driven `EnterEditorMode` (or a lock-denied stay) is reflected without the tab having
  initiated it.

### D5: Dispose animation controllers in `State.Dispose()`
Every animation controller `ScribeTabBar`'s `State` creates is disposed in its `State.Dispose()`,
mirroring the LibGUI animation contract (`GuiBase.OnRenderGUI` → `_tickerScheduler.Update` ticks them;
`OnValueChanged → SetState` rebuilds them) and the `AnimatedOpacity` precedent in `HudScribePins.cs:555`.
- *Why:* controllers registered with the ticker leak if not disposed; the keyed `State` is disposed when
  the dialog closes, which is the correct point to release them.

### D6: Per-mode title-bar color passed explicitly to `WindowFrame`
`Build()` (`:812`) computes a `titleBarColor`/`textColor` for the active mode and passes them to
`WindowFrame`, rather than relying on theme inheritance for the title band.
- *Why:* `WindowFrame`/`WindowTitleBar` read `ThemeData.Default` **at construction, not from context**
  (`WindowTitleBar.cs:56`), so a `Theme` wrap does not recolor the title bar — the sibling toggle change
  documents this. Passing explicit `Vector4?` colors is the supported per-dialog title-bar switch. This
  change owns the *per-mode* selection of those colors (the themed toggle owns *what* the mode's colors
  are); when the toggle change is not yet landed, the values are the current constants, so this is
  forward-compatible.

## Risks / Trade-offs

- **[Risk] Restructuring `BuildCentralRegion` regresses the lock-gated read↔editor flow.** → Mitigation:
  keep all navigation in the four existing methods (D2); the restructure only changes *how the body is
  selected and what chrome sits above it*, never *who acquires the lock*. In-game verification explicitly
  checks that Edit acquires and Read/leaving releases the server lock (not a bool flip), including that a
  denied grant leaves the player in read.
- **[Risk] The `wasEditorBeforeSettings` return path breaks when settings stops early-returning.** →
  Mitigation: route the settings tab and any Back affordance through the unchanged `OnClickOpenSettings` /
  `OnClickCloseSettings`, which own `wasEditorBeforeSettings` and the re-grant `RequestEditorAccess`.
  Verification enters settings from *both* read and editor and confirms the correct return view (and lock
  re-acquisition when returning to the editor).
- **[Risk] Animation controller lifecycle — leaks or resets across `ForceRebuild`.** → Mitigation: key
  the `State` (D4) so it survives rebuilds, and dispose controllers in `State.Dispose()` (D5), following
  the `HudScribePins.cs:555` `AnimatedOpacity` precedent and the `ScrollController`-survives-rebuild
  pattern.
- **[Risk] Tab `_activeIndex` drifts from the authoritative view mode.** → Mitigation: treat
  `_activeIndex` as a display mirror synced from `isEditorMode`/`isSettingsMode` on build (D4), so a
  server-driven mode change (grant/deny) or the settings return path is reflected even when the tab did
  not initiate it.
- **[Trade-off] Redundant Back affordance in `ScribeSettingsView`.** Accepted: both it and the tab route
  to `OnClickCloseSettings`, so they never disagree; removing the Back header is out of scope.
- **[Trade-off] Tab chrome is a flat placeholder until pixel-art sprites land.** Accepted per the
  flat-color-first build strategy: navigation and animation are fully testable before art, and the
  `NineSliceBox` upgrade is an art-only swap that does not touch behavior.

## Verification (in-game only)

The Core suite cannot reach `src/Mod/` GUI or the VS API. Verify in-game:
1. **Tab presence + animation:** open the Lectern → a labeled tab bar (not a gear button) is shown; the
   active tab animates its highlight/scale on selection; the tab bar is present on the settings page too.
2. **Lock semantics preserved:** selecting Edit acquires the editor lock via the server round-trip (a
   second player cannot simultaneously edit); selecting Read / leaving the editor releases it; a denied
   grant leaves the player in read — confirming tabs route to the real methods, not a flag flip.
3. **Settings enter/return path:** enter Settings from the **read** view and leave → returns to read;
   enter Settings from the **editor** view and leave → returns to the editor and re-acquires the lock
   (the `wasEditorBeforeSettings` path).
4. **Rebuild survival:** trigger a rebuild (view swap / settings-driven repaint) mid-animation → the tab
   bar's animation state is not reset (keyed `State`); closing the dialog leaks no controller.
5. **Title bar per mode:** the `WindowFrame` title-bar color matches the active mode.
6. Record the LibGUI animated-tabs lesson in `VSAPI-NOTES.md`.
