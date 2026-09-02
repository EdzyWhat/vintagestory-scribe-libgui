**STATUS (2026-09-02): Parked, not adopted.** The sibling `fix-harfbuzz-native-dir-rid-lookup`
spike also went 3/3 clean on the same VM and was chosen instead — it depends only on
`RuntimeInformation.ProcessArchitecture` (architecture, not host package state), so it carries
no cross-distro risk to verify. This spike additionally depends on the target system actually
having a `libharfbuzz` that's both present AND ABI-compatible with what `HarfBuzzSharp` calls,
which varies by distro/age in a way we have zero field data on (see Decision 4 in design.md).
Revisit this if: (a) a Linux user reports the RID-lookup fix itself still crashing (would mean
`RTLD_DEEPBIND` isolation is broken somehow, not just the RID lookup, and this spike's
system-library route becomes the more promising direction), or (b) a non-glibc distro user
(Alpine/void/Gentoo) reports the crash — `RTLD_DEEPBIND` is a glibc-only extension the RID-fix
alone can't help, and this spike's non-glibc path is the only one of the two that even attempts
a fix for them. The branch (`spike/harfbuzz-sysredirect`) and its 3/3-clean-run VM log evidence
are kept for exactly this contingency; do not delete.

## Why

`strengthen-harfbuzz-linux-fix` isolates Scribe's bundled `libHarfBuzzSharp.so` from a
system `libharfbuzz` via `RTLD_DEEPBIND`, but that mechanism regressed for a real Linux
tester (lunardiver) in `1.4.0-rc.1` after working in the dlopen-race predecessor — root
cause not yet confirmed, pending his crash log. Independently, `RTLD_DEEPBIND` is a
glibc-only extension, a gap a community member (Seralth) already flagged as unfixed for
non-glibc distros (Alpine, void, Gentoo). Rather than trying to isolate the bundled copy
harder, this proposes eliminating the two-copies-collision at its source: route
HarfBuzzSharp's native calls to the *system's* HarfBuzz directly, so there is only ever one
HarfBuzz resident in the process to begin with. HarfBuzz's own project deliberately
maintains strict ABI stability across releases specifically so it can be embedded
everywhere without breaking consumers, which is why this is a plausible replacement rather
than trading one instability for another.

## What Changes

- `ScribeHarfBuzzLoadFix.ResolveNativeLibrary` tries the system `libharfbuzz.so.0`, then
  `libharfbuzz.so`, before falling back to today's bundled-file `dlopen(...,
  RTLD_DEEPBIND)` behavior — unchanged if both system attempts fail.
- The non-glibc skip in `StartPre` is narrowed: the system-library attempt no longer
  requires glibc (it needs no `RTLD_DEEPBIND`), so non-glibc Linux gets a real attempt at
  this fix for the first time, instead of being skipped outright.
- Startup logging is added identifying which path was actually used (system library,
  bundled+isolated fallback, or neither) — no such visibility exists today, and we have no
  field data on what system HarfBuzz versions testers actually run.
- No change to the Harmony `Prefix` skeleton on `Gui.NativeLibraryLoader.Register()`, or to
  any of its fail-closed guarantees — those are load-bearing for a separate reason
  (`SetDllImportResolver` throws if called twice for the same assembly, and `gui`'s own
  call has no try/catch), independent of which resolver body runs.

**Scope note — this is a prepared spike, not a committed replacement.** Built the same way
`strengthen-harfbuzz-linux-fix` was: as an isolated test-RC build for a specific Linux
tester, not merged into `main`/the next real release until confirmed. The existing
bundled-isolation code path is kept intact as the fallback, not removed.

## Capabilities

### New Capabilities
- `linux-harfbuzz-native-isolation`: same capability name `strengthen-harfbuzz-linux-fix`
  already defined (that change is still open, not yet archived/synced to
  `openspec/specs/`, so there is nothing there to modify from this change's view) —
  this change extends it with a system-library-first resolution order. Whichever of the
  two changes' mechanisms is confirmed by testing should be the one actually archived;
  this spec should be reconciled with (or superseded by) `strengthen-harfbuzz-linux-fix`'s
  at that point rather than both landing independently.

### Modified Capabilities
(none in `openspec/specs/` — see above)

## Impact

- `src/Mod/ScribeHarfBuzzLoadFix.cs` — `ResolveNativeLibrary` gets a system-library-first
  attempt; `StartPre`'s non-glibc skip is narrowed to only skip the `RTLD_DEEPBIND` fallback
  branch, not the system-library attempt.
- No `src/Core/` impact (Mod-layer only).
- No new mod dependency (Harmony already ships with the base game).
- Build artifact: a throwaway version-bumped test RC, same pattern as prior HarfBuzz RCs,
  for a specific Linux tester — not a numbered release candidate until confirmed.
