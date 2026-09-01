## 1. Replace the mechanism in `ScribeHarfBuzzLoadFix`

- [x] 1.1 Remove the raw-`dlopen`-race implementation (the `StartClientSide`/`ExecuteOrder() => -1.0`
      body, the `dlopen` P/Invoke, and the direct `.so`-path resolution it does today) — keep the
      class as a standalone `ModSystem` (not a `ScribeModSystem` partial) and keep its existing
      Linux-only / glibc-only gating (`OperatingSystem.IsLinux()`, `PlatformConfiguration.IsGlibc`).
- [x] 1.2 Move the entry point to `StartPre` (design.md Decision 4 — patch timing no longer needs to
      race `gui`'s `StartClientSide`, just be in place before it).
- [x] 1.3 In `StartPre`, locate `Gui.NativeLibraryLoader` via `AccessTools.TypeByName` and its
      `Register` method via `AccessTools.Method`; if either is missing, log a warning and return
      (design.md Decision 2 — fail closed, same as gui-not-installed today).
- [x] 1.4 Apply a Harmony `Prefix` patch on `Register()` (new `Harmony` instance with a
      Scribe-specific patch id, e.g. `"scribe.harfbuzz-isolation"`) wrapped in try/catch — on any
      exception from `Patch(...)` itself, log a warning and return without patching.
      Used id `"scribe:harfbuzz-isolation"` (colon, matching the existing `handbookHarmony`
      convention in `ScribeModSystem.Handbook.cs` rather than the proposal's illustrative dot).
- [x] 1.5 Implement the prefix method: idempotently register an isolated `SetDllImportResolver` for
      the loaded `HarfBuzzSharp` assembly (guard against double-registration the same way the current
      fix's file already reasons about it being called at most once), then return `false` to skip
      `gui`'s original `Register()` body. Wrap the resolver-registration call in try/catch; on
      failure, log and return `true` instead (let `gui`'s original method run).
- [x] 1.6 Implement the isolated resolver closure: resolve the bundled `.so` the same way the current
      fix already does (relative to `typeof(HarfBuzzSharp.Face).Assembly.Location`,
      `native/<rid>/native/libHarfBuzzSharp.so`), then `dlopen(path, RTLD_NOW | RTLD_DEEPBIND)`. On a
      missing path or a failed `dlopen`, fall back to `NativeLibrary.TryLoad(name, ...)` (design.md
      Decision 3).
- [x] 1.7 Update the class's XML-doc remarks to describe the new mechanism (Harmony prefix, not a
      dlopen race), preserving the still-relevant explanation of why this stays a standalone
      `ModSystem` and not a `ScribeModSystem` partial (the macOS-regression history), and add a
      one-line credit comment referencing Seralth's independent fix
      (`github.com/Seralth/harfbuzzfix`) and the shared upstream issue (`ripls56/vslibgui#2`) as
      prior art the mechanism was validated against.
- [x] 1.8 Add or update `dlopen`'s `DllImport` declaration and any now-unused `using` directives
      (e.g. drop the raw file-path `File.Exists` check if no longer needed outside the resolver
      closure).
      `File.Exists` is still used, just relocated into `ResolveNativeLibrary`'s resolver closure
      (checked per-import-name, not once at startup) — nothing was actually orphaned.

## 2. Build and regression checks

- [x] 2.1 `dotnet build` clean (0 warnings/errors) across `src/Core` and `src/Mod`.
- [x] 2.2 Run the Core test suite (`dotnet test tests/Core.Tests`) — no regressions expected (this
      change is Mod-layer only). 625/625 passed.
- [x] 2.3 Confirm no other file references the removed `dlopen`-race members (e.g. any test or
      diagnostic tooling that inspected `ScribeHarfBuzzLoadFix`'s old shape).
      Only reference found (`grep`) was a comment in `ScribeModSystem.cs:347` pointing at this file
      by name — still accurate, no change needed.

## 3. Manual verification (Linux/glibc required — cannot be smoke-tested on macOS/Windows)

- [ ] 3.1 Manual test on a Linux/glibc desktop (the same class of environment the original
      `coredumpctl` crash was confirmed on): install Scribe with this change, join a world, and open
      every Scribe dialog that triggers font shaping (matches the original crash repro) — confirm no
      crash and the client log shows the new isolation-applied notification instead of the old one.
- [ ] 3.2 Manual test: temporarily rename/break the patch target (e.g. via a debug build that skips
      finding `Register()`) to simulate a future `gui` signature change — confirm Scribe logs the
      fail-closed warning and the client still starts normally (falls back to `gui`'s original,
      unisolated loader) rather than crashing.
- [ ] 3.3 Manual test: install Scribe alongside another `gui`-dependent mod (no separate HarfBuzz fix
      installed) on the same Linux/glibc client — confirm both function normally, matching the
      original fix's cross-mod protection intent.

## 4. Upstream coordination

- [x] 4.1 Post a comment on `ripls56/vslibgui#2` (opened by this repo's maintainer) summarizing the
      confirmed root cause (bundled HarfBuzzSharp vs. a system `libharfbuzz` already resident,
      `RTLD_DEEPBIND` isolation), crediting Seralth's independently-shipped fix as corroborating
      evidence, and linking this change's mechanism as a second independent implementation — making
      the case for a durable fix inside `gui` itself so every dependent mod's users benefit without
      per-mod patching.
      Done — author raised this directly with ripls (LibGUI's maintainer) in a thread referencing
      this fix.
