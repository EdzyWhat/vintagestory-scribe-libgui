## Why

A real crash dump (`coredumpctl`, not log-line inference) confirms Linux clients on KDE
Plasma/KWin abort with `free(): invalid pointer` because LibGUI's bundled `libHarfBuzzSharp.so`
(HarfBuzz 8.3.1) suffers symbol interposition against the desktop's own, separately-loaded system
`libharfbuzz.so.0` — an internal call inside the bundled library's own `hb_font_create` resolves to
the system's ABI-incompatible version instead of staying inside itself, corrupting the heap. This is
the exact mechanism the user filed upstream months ago
([ripls56/vslibgui#2](https://github.com/ripls56/vslibgui/issues/2)), whose suggested mitigation
(`RTLD_DEEPBIND`) was never implemented by the maintainer, with no ETA. The previously-shipped
`fix-linux-sans-serif-font-crash` change targeted a different (font-resolution) theory and has been
community-tested and disproven — it does not fix this crash. This change replaces it with a fix
targeted at the confirmed mechanism.

## What Changes

- Add a Scribe-owned, Linux-only startup step that forces the bundled `libHarfBuzzSharp.so` to load
  with `RTLD_DEEPBIND`, isolating its internal symbol lookups from any system `libharfbuzz.so.0`
  already resident in the process, via .NET's public `NativeLibrary.SetDllImportResolver` extension
  point (confirmed via decompilation that `HarfBuzzSharp.dll` registers no resolver of its own today,
  so there is nothing to override — just an empty hook to fill first).
- Force this registration to run before any other mod's `StartClientSide`, by overriding
  `ModSystem.ExecuteOrder()` to a value lower than the unmodified default (`0.1`) every other mod
  (including LibGUI's own `GuiModSystem`) currently uses — so the fix wins the race against whichever
  mod makes the first HarfBuzz call of the client session, protecting other LibGUI-based mods
  (HudUI/ChatUI, etc.) on the same client too, not just Scribe's own trigger.
- Fail safe on any platform or condition where this doesn't apply or doesn't work: non-Linux
  platforms register no resolver at all; a failed `dlopen`/`RTLD_DEEPBIND` attempt (e.g. non-glibc
  Linux) falls through to the runtime's normal default resolution, never introducing a new crash risk
  while attempting to fix an existing one.
- Retire `fix-linux-sans-serif-font-crash`'s alias mechanism as the intended fix for this crash class
  (its own change record stays archived as a disproven hypothesis, not deleted).

## Capabilities

### New Capabilities
- `harfbuzz-native-load-isolation`: Scribe forces the bundled HarfBuzzSharp native library to load
  with symbol-isolation flags on Linux, ahead of any other mod's startup code, so the default
  (unisolated) load path is never reached on an affected system.

### Modified Capabilities
(none — `bundled-font-rendering`, touched by the disproven prior change, is not modified here; this
change addresses a different, non-font-related mechanism.)

## Impact

- **Affected code:** `src/Mod/ScribeModSystem.cs` (or a new small file) — one new `ModSystem`
  registration/override, pure additive C#. No `src/Core/` changes (Core must never reference the VS
  API, and this doesn't need to).
- **No Core/API surface, network, or save-format changes.**
- **Dependencies:** none added. Uses only `System.Runtime.InteropServices.NativeLibrary` (BCL) and a
  direct `dlopen` P/Invoke against `libdl.so.2` (already-present system library on any glibc Linux).
- **Risk surface:** structurally cannot regress Windows/macOS (no resolver registered there) or an
  unaffected Linux setup (falls through to today's exact default behavior on any failure).
- **Verification:** community-assisted only (no local Linux test machine) — same ModDB/Discord
  reporters as the prior change, primarily SnuwWulfie (has working `coredumpctl` tooling already).
