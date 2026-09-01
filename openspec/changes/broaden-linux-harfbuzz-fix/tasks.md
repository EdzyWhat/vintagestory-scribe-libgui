## 1. Reconcile the Linux startup boundary

- [x] 1.1 Review the existing HarfBuzz isolation implementation plan and identify the
      shared client-startup entry point for platform/libc detection.
- [x] 1.2 Add a single bounded diagnostic outcome model for isolated, unsupported,
      missing-library, and failed-load states.
- [x] 1.3 Generalize comments and user-facing documentation so GTK, Qt, and other
      desktop/toolkit environments are treated uniformly.

## 2. Implement supported-platform behavior

- [x] 2.1 Register the native resolver early enough to run before Scribe or dependent
      LibGUI mods make their first HarfBuzz call.
- [x] 2.2 On glibc Linux, resolve the bundled library relative to the loaded
      HarfBuzzSharp assembly and load it with `RTLD_NOW | RTLD_DEEPBIND`.
- [x] 2.3 On missing paths or native-load errors, log the failure and return to the
      runtime's default resolution without throwing.
- [x] 2.4 On non-glibc Linux, skip the glibc entry point and log that isolation is
      unavailable without claiming the system is protected.
- [x] 2.5 Preserve no-op behavior on Windows and macOS.
- [x] 2.6 **Regression found + fixed 2026-08-31 by the macOS smoke check this change's own
      task 3.2 called for.** `ScribeHarfBuzzNativeLoader` (added by 2.1-2.5, folded into
      `ScribeModSystem` as a partial with `ExecuteOrder() => -1000`) crashed EVERY Scribe
      interaction on macOS: `client-crash.log` showed `ScribeModSystem.StartClientSide`
      throwing `System.BadImageFormatException`-style `dlopen` failures out of
      `RegisterCustomFonts`/`ScribeTaskFont.ProbeY` ("Failed to start system
      Scribe.ScribeModSystem"), which left the network channel never registered — so any
      later packet send (e.g. opening a Tablet) NRE'd. Root cause: `ExecuteOrder() => -1000`
      is not scoped to "before Scribe's own HarfBuzz call" (already guaranteed by call order
      within `StartClientSide`) — it reorders Scribe's ENTIRE startup ahead of `gui`'s own
      `StartClientSide`, which is what actually extracts/registers the bundled native
      HarfBuzzSharp library on every platform (including macOS's
      `native/osx/native/libHarfBuzzSharp.dylib`, which the .NET default resolver never finds
      on its own — only `gui`'s own loader knows that path). Racing ahead of `gui` left NO
      loader ready yet, regardless of the Linux-only resolver logic inside being correctly
      gated off on macOS. This exact collision-with-LibGUI's-own-loader risk was already
      discovered and fixed on the sibling `fix-linux-harfbuzz-symbol-collision` branch
      (commit `a16e7a6`, predating this change) by switching to a raw `dlopen`
      pre-load (no `SetDllImportResolver`, nothing to collide with) in a standalone
      `ScribeHarfBuzzLoadFix : ModSystem` at a mild `ExecuteOrder() => -1.0` — NOT a
      `ScribeModSystem` partial, so its low ExecuteOrder can't drag Scribe's whole startup
      ahead of `gui`. This change's `broaden` work branched before that fix landed and never
      incorporated it. Fixed by porting the `a16e7a6` implementation forward (same dlopen
      pre-load + standalone ModSystem + mild ExecuteOrder), keeping this change's
      cross-desktop (not KDE-only) framing. `src/Mod/ScribeHarfBuzzNativeLoader.cs` deleted;
      `src/Mod/ScribeHarfBuzzLoadFix.cs` added. `dotnet build`/Core suite (619 tests) green;
      the macOS-crash repro case is exactly what motivated the fix, so re-verify in-game after
      restaging before calling 3.2 fully closed.

## 3. Validate and document compatibility evidence

- [ ] 3.1 Add or update build-safe tests for platform gating, outcome classification,
      and bounded diagnostics without referencing the Vintage Story API from `src/Core/`.
- [x] 3.2 Run the Core test suite and a macOS/client build smoke check. The smoke check is
      what SURFACED the 2.6 regression (a macOS crash on every Scribe interaction); after
      restaging with the 2.6 fix, **confirmed in-game 2026-08-31**: Tablet opens normally
      on macOS again. Core suite (619 tests) green throughout.
- [ ] 3.3 Publish a diagnostic build for community testing on glibc/Qt, glibc/GTK,
      and non-glibc environments.
- [ ] 3.4 Record each report's desktop/toolkit, libc, library-load outcome, and native
      backtrace when available; classify unproven failures as unknown.
- [ ] 3.5 Update the upstream LibGUI issue with the cross-desktop evidence and clarify
      that the durable fix belongs in native-library packaging/loading.
