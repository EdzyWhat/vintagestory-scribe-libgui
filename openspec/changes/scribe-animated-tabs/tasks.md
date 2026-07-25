## 1. ScribeTabBar.cs: animated, keyed tab-bar widget

- [ ] 1.1 Add `src/Mod/ScribeTabBar.cs` as a `StatefulWidget` cloning stock `TabView`'s `State` +
  `_activeIndex` + per-tab `GestureDetector(onTap: e => SetState(() => _activeIndex = index))` pattern
  (`reference/vslibgui/.../Widgets/Basic/TabView.cs`), but for the tab *strip* only — it does NOT own the
  view bodies.
- [ ] 1.2 Render each tab's chrome with an `AnimatedContainer` (color, ~150ms EaseOut — the primitive
  `Button`/`IconButton` use) and animate the active tab's highlight and/or an `AnimatedScale` (springy
  grow), instead of stock `TabView`'s instant `Color` swap.
- [ ] 1.3 Give `ScribeTabBar` a stable `Key` (e.g. `ValueKey`) so its animation `State` survives the
  dialog's `ForceRebuild` view swaps (mirroring how the dialog-owned `ScrollController`s survive
  rebuilds).
- [ ] 1.4 Sync `_activeIndex` from the current view mode on build (display mirror of
  `isEditorMode`/`isSettingsMode`), so a server-driven grant/deny or the settings return path is
  reflected even when the tab did not initiate it.
- [ ] 1.5 Dispose every animation controller in `State.Dispose()` (following the `AnimatedOpacity`
  precedent in `HudScribePins.cs:555`; ticked by `GuiBase.OnRenderGUI → _tickerScheduler.Update`).
- [ ] 1.6 Expose tab labels/selection via constructor `Action` callbacks (no direct view-mode field
  access) and render a flat animated-container placeholder chrome now, leaving room for an optional
  `NineSliceBox` (crisp) sprite chrome once art lands.

## 2. Restructure BuildCentralRegion into a unified shell

- [ ] 2.1 In `GuiDialogScribeLecternLibGui.BuildCentralRegion()` (`:825`), remove the `isSettingsMode`
  early-return so the settings view no longer short-circuits the shared shell.
- [ ] 2.2 Build one shell for all views: a single `ScribeTabBar` nav row over a single `Expanded` body,
  with the body chosen by the existing mode fields (`isSettingsMode` → `ScribeSettingsView`; else
  `isEditorMode` → editor content; else read content).
- [ ] 2.3 Remove `ScribeGearHeader` as the navigation affordance (delete the node / retire the class),
  replacing it with the `ScribeTabBar` in the shell.

## 3. Wire tab callbacks to the real lock-aware navigation methods

- [ ] 3.1 Route the read/edit tab to `OnClickSwitchToRead` (`:292`) / `RequestEditorAccess` (`:392`, the
  `ScribeRequestAccessMessage` round-trip landing in `EnterEditorMode` `:235`) — never a bool flip.
- [ ] 3.2 Route the settings tab to `OnClickOpenSettings` (`:306`) and leaving settings to
  `OnClickCloseSettings` (`:323`), preserving `wasEditorBeforeSettings` and the editor-lock re-grant.
- [ ] 3.3 Confirm `ScribeTabBar` never touches `isEditorMode` / `isSettingsMode` directly — all
  navigation stays owned by the four existing methods.

## 4. Per-mode title-bar color

- [ ] 4.1 In `Build()` (`:812`), compute a `titleBarColor`/`textColor` for the active view mode and pass
  them explicitly to `WindowFrame` (the title bar reads `ThemeData.Default` at construction and does not
  follow a `Theme` wrap — `WindowTitleBar.cs:56`).
- [ ] 4.2 Keep the values forward-compatible with the sibling `scribe-themed-toggle` (this change owns the
  per-mode *selection*; the toggle owns *what* each mode's colors are — use the current constants until it
  lands).

## 5. Lang keys

- [ ] 5.1 Add tab-label lang keys (read/edit tab and settings tab) to `assets/scribe/lang/en.json`.

## 6. In-game verification

- [ ] 6.1 Tab presence + animation: the Lectern shows a labeled tab bar (not a gear button); the active
  tab animates its highlight/scale; the tab bar is present on the settings page.
- [ ] 6.2 Lock semantics: selecting Edit acquires the editor lock via the server round-trip (a second
  player cannot simultaneously edit); Read / leaving releases it; a denied grant leaves the player in read.
- [ ] 6.3 Settings enter/return: enter Settings from the read view → leaving returns to read; enter from
  the editor view → leaving returns to the editor and re-acquires the lock (`wasEditorBeforeSettings`).
- [ ] 6.4 Rebuild survival: a rebuild mid-animation does not reset the tab bar's animation state (keyed
  `State`); closing the dialog leaks no animation controller.
- [ ] 6.5 Title bar: the `WindowFrame` title-bar color matches the active mode.

## 7. Documentation

- [ ] 7.1 Append a LibGUI animated-tabs lesson to `VSAPI-NOTES.md` (`## LibGUI`): clone `TabView`'s
  `GestureDetector`+`_activeIndex` strip and swap its `Container` for `AnimatedContainer`/`AnimatedScale`
  to animate (stock `TabView` instant-swaps and owns its content); key the `State` so animation survives
  `ForceRebuild`; dispose animation controllers in `State.Dispose()`; route tab callbacks to the real
  lock-aware nav methods, not a flag flip; `WindowFrame` title bar reads `ThemeData.Default` at
  construction (pass explicit `titleBarColor`/`textColor`).
