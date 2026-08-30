## Why

LibGUI shows a first-run "pick a theme" dialog the first time any LibGUI-based mod runs on a client
install. New Scribe players find this confusing — it isn't Scribe's own UI, nothing in Scribe prompted
it, and most players never touch theme settings anyway. Since Scribe hard-depends on LibGUI, this is
squarely the mod's own new-player experience even though it isn't Scribe's code, and it's worth fixing
in-mod rather than accepting a confusing first impression.

## What Changes

- Add a small, standalone `ModSystem` that pre-seeds LibGUI's shared `ModConfig/libgui.json` with
  `Onboarded: true` (and no theme override, i.e. LibGUI's own default theme) before LibGUI's own
  `GuiModSystem.StartClientSide` checks that file — the same low-`ExecuteOrder()` race-winning pattern
  already used by `ScribeHarfBuzzLoadFix` (`fix-linux-harfbuzz-symbol-collision`).
- Only acts when the file doesn't exist yet (a genuinely fresh client install). If it already exists —
  including with `Onboarded: false`, which only happens if someone deliberately edited the file to
  re-trigger the dialog — this leaves it alone.
- No UI, no Harmony patch, no fork of LibGUI: this only ever writes a config file LibGUI itself already
  reads, using LibGUI's own public `GuiConfig` type and the standard `ICoreAPI.StoreModConfig` VS API.

## Capabilities

### New Capabilities
- `libgui-first-run-suppression`: Scribe suppresses LibGUI's first-run theme-picker dialog for new
  players by pre-seeding the shared onboarding config with LibGUI's own default theme.

### Modified Capabilities
(none — this is new mod behavior, not a change to any existing Scribe capability's requirements)

## Impact

- **Affected code:** one new file in `src/Mod/` (a standalone `ModSystem`, no changes to
  `ScribeModSystem` itself). No `Core` or network/save-format changes.
- **Affected systems:** `ModConfig/libgui.json` is a file shared by every LibGUI-based mod on a given
  client install, not something Scribe owns exclusively — pre-seeding it also skips the dialog for any
  *other* LibGUI-based mod the same player has installed, not just Scribe. This is treated as the
  intended, accepted side effect (see design.md), not an unintended one.
- **No new dependencies.** `Gui.dll` is already a required, referenced dependency of Scribe.
