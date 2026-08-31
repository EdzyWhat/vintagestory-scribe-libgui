## 1. Implement the native-load isolation fix

- [ ] 1.1 Add a small class implementing `ModSystem` (or extend `ScribeModSystem`) that overrides
      `double ExecuteOrder()` to return a value lower than the unmodified default `0.1`, so its
      `StartClientSide` runs before every other mod's (decompiled `VintagestoryLib.dll` confirms
      neither `ScribeModSystem` nor LibGUI's `GuiModSystem` currently override this).
- [ ] 1.2 In that `StartClientSide`, guard on `OperatingSystem.IsLinux()` — do nothing at all on
      Windows/macOS.
- [ ] 1.3 On Linux, further guard on `HarfBuzzSharp.Internals.PlatformConfiguration.IsGlibc` — do
      nothing on non-glibc systems (e.g. musl).
- [ ] 1.4 On glibc Linux, call
      `NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Face).Assembly, resolver)` where
      `resolver` only handles the library name `"libHarfBuzzSharp"` (return `IntPtr.Zero` for any
      other name, to fall through to default resolution unchanged).
- [ ] 1.5 Implement the resolver body: locate the bundled `.so` relative to
      `typeof(HarfBuzzSharp.Face).Assembly.Location` (matching the real shipped layout,
      `native/<rid>/native/libHarfBuzzSharp.so`), then call a direct
      `[DllImport("libdl.so.2")] static extern IntPtr dlopen(string filename, int flags)` with
      `RTLD_NOW | RTLD_DEEPBIND` (`2 | 0x00008` on glibc) and return the resulting handle.
- [ ] 1.6 Add the graceful-fallback path: if the `.so` can't be located, or `dlopen` returns
      `IntPtr.Zero`/fails, log a warning (`api.Logger.Warning`) and return `IntPtr.Zero` from the
      resolver so default resolution still runs — never throw from the resolver delegate.
- [ ] 1.7 Add a one-line log (`api.Logger.Notification`) on success, naming the resolved `.so` path,
      so a future crash log (or its absence) can confirm whether this registration ran and succeeded
      before any crash.

## 2. Verify locally (what's testable without a Linux machine)

- [ ] 2.1 Run the existing Core test suite (`dotnet test` under `tests/Core.Tests`) — expect no
      change, since this touches only `src/Mod/` and Core must never reference the VS API.
- [ ] 2.2 Restage a Debug build (`build/restage.sh Debug`, client fully quit first) and smoke-test on
      this machine (macOS): confirm the mod still loads normally, no new log warnings, no behavior
      change (expected, since the guard means nothing runs on macOS). **Needs the user in-game.**

## 3. Community verification (the only real test of the fix)

- [ ] 3.1 Publish a build with the fix and ask SnuwWulfie (who already has working `coredumpctl`
      tooling from the prior investigation) to test it on the same CachyOS/KDE Plasma system.
- [ ] 3.2 Confirm the new success/failure log line appears on world join, and note which it is.
- [ ] 3.3 If it still crashes: capture a fresh `coredumpctl info <pid> > file.txt` backtrace (same
      method as before — redirect straight to a file, not console copy/paste) and check whether the
      `libharfbuzz.so.0` interposition frame (previously frame #4) is still present. If it's gone but
      a different crash appears, that's a new, different bug — do not assume this fix caused it
      without checking whether the new crash pre-existed. If it's still there unchanged, the
      `RTLD_DEEPBIND` registration did not take effect — check the log from 1.7 first to see whether
      registration even ran/succeeded.
- [ ] 3.4 If it no longer crashes: confirmed fix. Close the loop on the ModDB threads and Discord
      where the investigation was posted, and update `assess-libgui-decoupling/design.md` §5 and
      `fix-linux-sans-serif-font-crash/`'s memory record with the outcome.
- [ ] 3.5 Ask Jack_Frost independently too, since that report was never confirmed to be the same
      mechanism as SnuwWulfie's.

## 4. Upstream communication (tracked here, not blocking)

- [ ] 4.1 Once verified working, relay this exact pattern to ripls56 on the existing GitHub issue
      (ripls56/vslibgui#2) as a candidate *fix* (not just diagnosis) — a small, concrete patch
      LibGUI's own maintainer could adopt directly to fix this for every LibGUI-based mod, not just
      ones that adopt Scribe's own workaround.
