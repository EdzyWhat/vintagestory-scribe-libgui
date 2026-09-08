## 1. Implement the onboarding pre-seed

- [x] 1.1 Added a standalone class implementing `ModSystem` (`ScribeSkipLibGuiOnboarding`, its own
      file — not a `ScribeModSystem` partial, for the same reason as `ScribeHarfBuzzLoadFix`) that
      overrides `double ExecuteOrder()` to return `-1.0`, guaranteeing it runs before LibGUI's own
      `GuiModSystem.StartClientSide`.
- [x] 1.2 In `StartClientSide`, calls `api.LoadModConfig<GuiConfig>("libgui.json")`; if it returns
      non-null (a decision already exists, in either direction), returns without changing anything.
- [x] 1.3 If it returns null (no file yet — a genuinely fresh client install), calls
      `api.StoreModConfig(new GuiConfig { Onboarded = true }, "libgui.json")` — no `Theme` override,
      so LibGUI resolves its own default theme (`ThemeData.Default = new ThemeData()`, identical to
      LibGUI's own "no config yet" branch).
- [x] 1.4 Logs a `Notification` on success, and wraps the whole body in a try/catch that logs a
      `Warning` and returns on any failure — never throws, never blocks startup.

## 2. Verify locally

- [x] 2.1 Built clean (`dotnet build src/Mod/Mod.csproj --configuration Debug`), 0 warnings/errors.
- [x] 2.2 Reproduced the "before" state on macOS: deleted the real
      `~/Library/Application Support/VintagestoryData/ModConfig/libgui.json` and confirmed (from
      earlier investigation) that this is what makes LibGUI treat the next launch as first-run.
- [x] 2.3 Restaged Debug with the fix, deleted `libgui.json` again, relaunched: confirmed no dialog
      appeared and the theme resolved to LibGUI's default — verified directly by the user, 2026-08-30.

## 3. Merge

- [x] 3.1 Merge the `skip-libgui-first-run-onboarding` branch into `main` once the other terminal's
      concurrent `add-assignment-and-quest-support` work is in a safe state to merge alongside (avoid
      disrupting its in-progress uncommitted changes) — coordinate before merging, don't just push.
      Landed on `main` as commit `3745fba`, 2026-08-30.
- [x] 3.2 Remove the worktree at `/tmp/scribe-onboarding-worktree` after a successful merge.
