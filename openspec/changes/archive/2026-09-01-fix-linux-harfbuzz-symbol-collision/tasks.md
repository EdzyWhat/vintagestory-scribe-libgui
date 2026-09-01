## 1. Implement the native-load isolation fix

- [x] 1.1 Added a standalone class implementing `ModSystem` (`ScribeHarfBuzzLoadFix`, its own file
      — NOT a `ScribeModSystem` partial, so its aggressive `ExecuteOrder()` only affects this one
      registration, not Scribe's whole startup sequence) that overrides `double ExecuteOrder()` to
      return `-1.0`, lower than the unmodified default `0.1` (decompiled `VintagestoryLib.dll`
      confirmed neither `ScribeModSystem` nor LibGUI's `GuiModSystem` override this).
- [x] 1.2 Guarded on `OperatingSystem.IsLinux()` — no-op on Windows/macOS.
- [x] 1.3 Guarded on `HarfBuzzSharp.Internals.PlatformConfiguration.IsGlibc` — no-op on non-glibc
      Linux (e.g. musl).
- [x] 1.4 **Corrected mid-implementation (caught before shipping, see design.md's Context
      correction): does NOT use `NativeLibrary.SetDllImportResolver`.** Decompiling `Gui.dll` (not
      just `HarfBuzzSharp.dll`) found `Gui.NativeLibraryLoader.Register()` — called at the first
      line of `GuiModSystem.StartClientSide` — already registers a resolver for the same
      `HarfBuzzSharp` assembly, unguarded. A second registration would throw
      `InvalidOperationException` inside `GuiModSystem.StartClientSide`; confirmed via
      `VintagestoryLib.dll`'s `TryRunModPhase` that this doesn't crash the client but does make
      `GuiModSystem` fail to start entirely (breaking every LibGUI dialog) — worse than the bug
      being fixed. Switched to a plain, direct `dlopen` pre-load instead (task 1.5) — no resolver
      registration at all, so nothing to collide with.
- [x] 1.5 On glibc Linux, locates the bundled `.so` relative to
      `typeof(HarfBuzzSharp.Face).Assembly.Location` (`native/<rid>/native/libHarfBuzzSharp.so`),
      then calls `[DllImport("libdl.so.2")] static extern IntPtr dlopen(string, int)` with
      `RTLD_NOW | RTLD_DEEPBIND` (`0x00002 | 0x00008`) once, in `StartClientSide`, before LibGUI's
      own loader runs. The OS dedupes `dlopen` by canonical path, so LibGUI's later, flag-less load
      of the same file reuses this already-mapped, already-deep-bound handle. The handle is
      deliberately never closed (kept resident for the process's lifetime).
- [x] 1.6 Every failure path (file not found, `dlopen` returns `IntPtr.Zero`, unexpected exception)
      logs a warning and simply returns without pre-loading — LibGUI's own loader then loads the
      library normally afterward. Never throws.
- [x] 1.7 Logs a `Notification` on success naming the resolved `.so` path.
      Required adding a compile-time reference to `HarfBuzzSharp.dll` in `Mod.csproj`
      (`Private=false`, same pattern as `Gui.dll` — the installed `gui` mod provides it at runtime;
      previously only `Gui.dll` was referenced since Scribe never needed HarfBuzzSharp's own types
      directly before this).

## 2. Verify locally (what's testable without a Linux machine)

- [x] 2.1 Ran the Core test suite (`dotnet test tests/Core.Tests`) — 613 passed, 0 failed, no
      change (this touches only `src/Mod/`; Core never references the VS API).
- [x] 2.2 Restaged a Debug build (`build/restage.sh Debug`, run from the worktree so the shared
      main working directory was never touched) and smoke-tested on macOS: mod loads cleanly, no
      new log warnings, no behavior change — expected, since the fix no-ops entirely on non-Linux.

## 3. Community verification (the only real test of the fix)

- [ ] 3.1 Publish a build with the fix and ask SnuwWulfie (who already has working `coredumpctl`
      tooling from the prior investigation) to test it on the same CachyOS/KDE Plasma system.
      **Backlogged 2026-08-31** — superseded by `broaden-linux-harfbuzz-fix`'s own task 3.3
      (publish a diagnostic build for glibc/Qt, glibc/GTK, and non-glibc community testing); this
      groups 1-2 implementation is what got ported forward into that change's `ScribeHarfBuzzLoadFix`
      (see its task 2.6), so any real-world verification now happens there, under a wider
      cross-desktop framing rather than KDE-only.
- [ ] 3.2 Confirm the new success/failure log line appears on world join, and note which it is.
      **Backlogged 2026-08-31** — same reasoning as 3.1; the log line itself shipped unchanged into
      `broaden-linux-harfbuzz-fix`'s `ScribeHarfBuzzLoadFix.cs`.
- [ ] 3.3 If it still crashes: capture a fresh `coredumpctl info <pid> > file.txt` backtrace (same
      method as before — redirect straight to a file, not console copy/paste) and check whether the
      `libharfbuzz.so.0` interposition frame (previously frame #4) is still present. If it's gone but
      a different crash appears, that's a new, different bug — do not assume this fix caused it
      without checking whether the new crash pre-existed. If it's still there unchanged, the
      `RTLD_DEEPBIND` registration did not take effect — check the log from 1.7 first to see whether
      registration even ran/succeeded. **Backlogged 2026-08-31** — folded into
      `broaden-linux-harfbuzz-fix`'s own task 3.4 (record each report's outcome/backtrace).
- [ ] 3.4 If it no longer crashes: confirmed fix. Close the loop on the ModDB threads and Discord
      where the investigation was posted, and update `assess-libgui-decoupling/design.md` §5 and
      `fix-linux-sans-serif-font-crash/`'s memory record with the outcome. **Backlogged 2026-08-31**
      — folded into `broaden-linux-harfbuzz-fix`'s own task 3.4/3.5.
- [ ] 3.5 Ask Jack_Frost independently too, since that report was never confirmed to be the same
      mechanism as SnuwWulfie's. **Backlogged 2026-08-31** — still a valid open item, now tracked
      under `broaden-linux-harfbuzz-fix`'s community-testing pass (task 3.3) rather than this change.

## 4. Upstream communication (tracked here, not blocking)

- [ ] 4.1 Once verified working, relay this exact pattern to ripls56 on the existing GitHub issue
      (ripls56/vslibgui#2) as a candidate *fix* (not just diagnosis) — a small, concrete patch
      LibGUI's own maintainer could adopt directly to fix this for every LibGUI-based mod, not just
      ones that adopt Scribe's own workaround. **Backlogged 2026-08-31** — superseded by
      `broaden-linux-harfbuzz-fix`'s own task 3.5 (same upstream relay, with the broader cross-desktop
      evidence once gathered).

## Disposition (2026-08-31)

This change's real implementation (tasks 1.1-2.2 above) was built and locally verified on its own
git branch (`fix-linux-harfbuzz-symbol-collision`, commits `71ba587`/`a16e7a6`), but that branch was
never merged into `main` and this file was never updated to reflect the work that happened there —
until this pass. Independently, `broaden-linux-harfbuzz-fix` branched from `main` before this
branch's `a16e7a6` correction landed, shipped its own (initially colliding) isolation approach, hit
the exact same collision this branch had already found and fixed, and — per its own task 2.6 — was
fixed by manually porting `a16e7a6`'s implementation forward into `main`. That port is now committed
on `main` (see `src/Mod/ScribeHarfBuzzLoadFix.cs`). This change's own remaining community-verification
and upstream-relay work (groups 3-4) is therefore superseded by `broaden-linux-harfbuzz-fix`'s own
Group 3, which covers the same ground under a broader (not KDE-only) framing. Archiving this change
as superseded rather than continuing it under its own name.
