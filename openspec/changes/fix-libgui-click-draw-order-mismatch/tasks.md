## 1. The guard check

- [x] 1.1 Add a small static helper (e.g. `ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen(ICoreClientAPI
      capi)`) in `src/Mod/`: returns true when `capi.Gui.OpenedGuis` contains any dialog where
      `DialogType != EnumDialogType.HUD` and the dialog is not a `Gui.GuiBase` instance. Verify
      `dotnet build src/Mod` succeeds.
- [x] 1.2 ~~Add unit/integration coverage (Atlas...)~~ **N/A — Atlas is a headless server-only
      harness with no `ICoreClientAPI`/`OpenedGuis`/rendering of any kind** (confirmed against
      `reference/atlas/` source and `reference/atlas-wiki/`: "No client, no window"; real network/
      rendering clients are explicitly out of Atlas's roadmap). `Core.Tests` can't cover it either —
      the helper needs the VS client API, which `src/Core/` must never reference. This check has no
      automatable coverage path in this project's current tooling; verification is the manual
      playtest in 2.3-2.5 only.

## 2. Verification

- [x] 2.1 `dotnet test` (Core) green — this change touches Mod-layer files only, confirm the suite is
      unaffected.
- [x] 2.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 2.3 Manual playtest: open the base-game Handbook, then call the guard from a debug command or
      temporary log line — confirm it reports true.
- [ ] 2.4 Manual playtest: with only a Scribe Notebook/Lectern dialog open (no vanilla dialog), confirm
      the guard reports false.
- [ ] 2.5 Manual playtest: with only the pinned-task HUD visible (no dialog open at all), confirm the
      guard reports false.
