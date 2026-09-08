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
- [x] 2.3 Manual playtest: open the base-game Handbook, then call the guard from a debug command or
      temporary log line — confirm it reports true.
- [x] 2.4 Manual playtest: with only a Scribe Notebook/Lectern dialog open (no vanilla dialog), confirm
      the guard reports false.
- [x] 2.5 Manual playtest: with only the pinned-task HUD visible (no dialog open at all), confirm the
      guard reports false.

## 3. Second consumer: `ScribeDialogBase.OnMouseDown` (added 2026-09-07, revised same day)

- [x] 3.1 ~~Override `ShouldReceiveMouseEvents()`~~ **Superseded same day** — that method has no click
      position, so the check was necessarily global (any vanilla dialog open anywhere on screen, not
      just one covering the click) and made Scribe's own window impossible to click back into focus. See
      design.md's "Correction (2026-09-07)". Replaced by 3.1b below.
- [x] 3.1b Add `ScribeVanillaDialogGuard.IsVanillaDialogAt(capi, x, y)`: true when the point falls inside
      any open vanilla (non-`GuiBase`, non-HUD) dialog's own composer `Bounds`. Override
      `ScribeDialogBase.OnMouseDown(MouseEvent args)` to return early (leaving `args.Handled` false) when
      `IsVanillaDialogAt(capi, args.X, args.Y)` is true, before calling `base.OnMouseDown(args)`. Verify
      `dotnet build src/Mod/Mod.csproj -c Debug` succeeds with 0 warnings/errors.
- [x] 3.2 Manual playtest: open a Notebook/Lectern/Tablet dialog, click a Handbook link so the base-game
      Handbook opens on top of it, then click somewhere inside the Handbook window that visually
      overlaps where the Scribe dialog sits underneath — confirm the click reaches the Handbook (e.g.
      navigates/selects there) and does NOT also toggle/edit anything in the Scribe row underneath.
- [x] 3.3 Manual playtest: same as 3.2 but for a Quest Link opening Progression Framework's Ledger
      (Quest Log tab) instead of the Handbook.
- [x] 3.4 Manual playtest: with the Handbook/Ledger STILL OPEN on top, click directly on the Scribe
      dialog itself (an area outside the vanilla window's bounds) — confirm Scribe responds immediately
      (checkbox toggle, row click, etc.) and regains dialog focus, without needing to close the vanilla
      dialog first. This is the corrected requirement (design.md's 2026-09-07 correction) — the original
      `ShouldReceiveMouseEvents` version failed this exact case.
- [x] 3.5 Manual playtest: with a Scribe dialog open and NO vanilla dialog open, confirm normal Scribe
      interaction (checkbox toggle, row click, typing) still works exactly as before — this task's
      change must not regress the no-vanilla-dialog case.
