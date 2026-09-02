## 1. Resolver changes

- [x] 1.1 In `ScribeHarfBuzzLoadFix.ResolveNativeLibrary`, attempt
      `NativeLibrary.TryLoad("libharfbuzz.so.0")` then `NativeLibrary.TryLoad("libharfbuzz.so")`
      before the existing bundled-file `dlopen(..., RTLD_DEEPBIND)` logic; keep the existing
      logic completely unchanged as the fallback when both system attempts fail.
- [x] 1.2 Narrow `StartPre`'s non-glibc skip so it only gates the bundled-file+`RTLD_DEEPBIND`
      fallback branch, not the new system-library attempt (which needs no glibc-specific
      extension).
- [x] 1.3 Add a helper that, on successful load (system or fallback), attempts
      `NativeLibrary.GetExport(handle, "hb_version_string")` and P/Invokes it for a log line;
      degrade gracefully (log without a version) if the export isn't found.
- [x] 1.4 Add startup log lines distinguishing the three outcomes: system library used (with
      version if obtained), bundled-isolated fallback used, or neither (existing fail-closed
      no-op path).

## 2. Verification

- [x] 2.1 `dotnet build` succeeds; no `src/Core/` changes introduced.
- [x] 2.2 Confirm via code review that the Harmony `Prefix` skeleton on
      `Gui.NativeLibraryLoader.Register()` and its fail-closed guarantees are unchanged.
- [x] 2.3 Build an isolated test-RC (throwaway branch/worktree, version-bumped locally only,
      not merged to `main`) following the same pattern as prior HarfBuzz RCs. Built on top of
      `spike/harfbuzz-ridfix` at `spike/harfbuzz-sysredirect` (worktree
      `/tmp/scribe-sysredirect-worktree`), version `1.4.0-spike.sysredirect`, packaged to
      `Releases/scribe_1.4.0-spike.sysredirect.zip` — includes the RID-lookup fix too.
- [ ] 2.4 Manual/in-game verification is Linux-only and cannot be smoke-tested on this
      (macOS) dev machine — hand off to lunardiver (and optionally Seralth, for non-glibc
      coverage) for the real test. Author is testing locally first, in a VM matching
      lunardiver's setup (CachyOS/KDE Plasma), before involving testers again.

## 3. Follow-up (pending tester results)

- [ ] 3.1 If confirmed working: decide whether this replaces or coexists with
      `strengthen-harfbuzz-linux-fix`'s mechanism before merging anything to `main`.
- [ ] 3.2 Reconcile this change's `linux-harfbuzz-native-isolation` delta spec with
      `strengthen-harfbuzz-linux-fix`'s (both currently open/unarchived) once one mechanism
      is confirmed and the other is dropped or superseded.
- [ ] 3.3 Update the regression-investigation memory with the tester's actual result.
