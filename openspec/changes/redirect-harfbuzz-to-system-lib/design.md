## Context

`src/Mod/ScribeHarfBuzzLoadFix.cs` (from `strengthen-harfbuzz-linux-fix`) Harmony-patches
`Gui.NativeLibraryLoader.Register()` with a `Prefix` that installs Scribe's own
`NativeLibrary.SetDllImportResolver` for the `HarfBuzzSharp` assembly, skipping `gui`'s own
(unisolated) registration. The resolver body currently `dlopen()`s the bundled
`libHarfBuzzSharp.so` with `RTLD_DEEPBIND`, isolating its internal symbol lookups from a
system `libharfbuzz` already resident in the process (the confirmed root cause of a
`free(): invalid pointer` crash on KDE/Plasma, via coredumpctl backtrace — see
`fix-linux-harfbuzz-symbol-collision`).

This mechanism worked for one Linux tester (lunardiver) in an earlier RC built from the
dlopen-race predecessor (`broaden-linux-harfbuzz-fix`), but the Harmony-patch version
regressed for him in `1.4.0-rc.1` — root cause not yet confirmed pending his crash log.
Separately, `RTLD_DEEPBIND` is a glibc-only extension; a community member (Seralth)
independently flagged that the isolation approach (his own mod uses the identical
technique) does not help non-glibc distros.

This design proposes redirecting HarfBuzzSharp's native resolution to the *system's*
`libharfbuzz` outright, on the theory that eliminating the second copy removes the
collision precondition entirely, rather than trying to isolate one of two copies harder.

## Goals / Non-Goals

**Goals:**
- Prefer the system `libharfbuzz` for HarfBuzzSharp's native calls on Linux, falling back
  to the existing bundled+`RTLD_DEEPBIND` behavior unchanged if the system library isn't
  available.
- Extend a real (not blanket-skipped) attempt to non-glibc Linux, since this path needs no
  glibc-specific extension.
- Add startup logging naming which path was used and, when obtainable, the resolved
  library's version — visibility we have none of today.
- Preserve every existing safety property of the current fix unchanged: the Harmony
  `Prefix` skeleton, its fail-closed guarantees, and Linux-only scoping.

**Non-Goals:**
- Not replacing `strengthen-harfbuzz-linux-fix`'s mechanism in `main` or the next real
  release. This ships only as an isolated test-RC build for a specific tester, same as
  prior HarfBuzz RCs, until a real Linux run confirms it.
- Not attempting to proactively verify ABI compatibility between HarfBuzzSharp's expected
  function surface and whatever system HarfBuzz version is present, beyond what a normal
  load/first-call failure already surfaces (see Risks).
- Not removing the bundled-file+`RTLD_DEEPBIND` code path — it stays as the fallback for
  the (still-real) case where no usable system library exists.

## Decisions

### 1. Try system library first; keep the existing fallback exactly as-is

`ResolveNativeLibrary` attempts `NativeLibrary.TryLoad("libharfbuzz.so.0")`, then
`NativeLibrary.TryLoad("libharfbuzz.so")`, before falling through to today's
`Path.Combine(nativeDir, "libHarfBuzzSharp.so")` + `dlopen(..., RTLD_DEEPBIND)` logic,
completely unchanged. `.so.0` is tried first because it's the versioned SONAME distros
actually resolve at runtime via `ldconfig`; `.so` (the unversioned dev symlink) is a cheap
second attempt that costs nothing if the first fails.

**Alternative considered**: require the system library and drop the bundled fallback
entirely. Rejected — a user with no system `libharfbuzz` resident in the process was never
at risk of the collision in the first place (nothing to collide with), so today's bundled
isolation is still a safe, working choice for them; removing it would regress a currently-
fine case for no benefit.

### 2. Narrow the glibc gate to only the fallback branch

Today's `StartPre` skips all isolation on non-glibc Linux, since `RTLD_DEEPBIND` doesn't
exist there. The system-library attempt needs no such extension — `NativeLibrary.TryLoad`
is a portable API — so it should run on any Linux, glibc or not. Only the bundled-file
`dlopen(..., RTLD_DEEPBIND)` fallback stays glibc-gated.

**Alternative considered**: leave the existing blanket glibc gate in place and treat this
as strictly additive on top of it. Rejected — this would continue skipping non-glibc users
entirely, the exact gap Seralth flagged, when the fix for that gap (don't need
`RTLD_DEEPBIND` if you're not touching the bundled copy) is already in hand.

### 3. Log the resolved path and version, using HarfBuzz's own stable version API

After a successful load (system or fallback), attempt `NativeLibrary.GetExport` for
`hb_version_string` on the resolved handle and P/Invoke it for a log line. This function
has existed unchanged since HarfBuzz's earliest stable releases — using it for a
diagnostic log line carries negligible risk even against an unknown system version. If the
export isn't found, log the outcome without a version rather than failing the whole
resolution over a cosmetic detail.

### 4. No proactive ABI-compatibility check beyond load/first-call success

**Risk carried forward, not solved**: HarfBuzz maintains strict ABI stability across
releases by design (a deliberate project goal, since it's embedded everywhere — Pango,
Cairo, Qt, browsers — without recompilation), which is the entire premise this design
leans on. But that guarantee is about *not breaking existing exported functions*, not
about *guaranteeing every function HarfBuzzSharp's bindings call exists* on an arbitrarily
old system version. A missing function surfaces as `EntryPointNotFoundException` on first
call — catchable, manageable. A function that exists but has since changed *behavior*
(not signature — HarfBuzz avoids this, but "avoids" isn't "cannot") would not be
catchable, the same category of risk the original bug already carries. This is why this
change stays a spike pending real-world confirmation rather than an immediate replacement.

**Alternative considered**: gate the system-library attempt behind a minimum detected
version (e.g. parse `hb_version()` and require ≥ some floor). Rejected for this spike as
speculative complexity with no known floor to pick — worth revisiting only if a real
version-mismatch failure is ever observed in the field.

## Risks / Trade-offs

- **[Risk] A system HarfBuzz that's ABI-compatible-in-practice but has subtly different
  runtime behavior for some code path HarfBuzzSharp exercises** → **Mitigation**: none
  beyond HarfBuzz's own stability guarantee and this being a gated spike, not a shipped
  default, until confirmed by a real Linux tester.
- **[Risk] `NativeLibrary.TryLoad` succeeding doesn't guarantee every symbol
  HarfBuzzSharp's bindings need is present on an old system version** → **Mitigation**:
  missing-symbol failures raise a catchable managed exception on first use, which the
  existing fail-closed `RegisterPrefix`/`ResolveNativeLibrary` structure already handles by
  falling through to the next attempt.
- **[Trade-off] Reconciling with `strengthen-harfbuzz-linux-fix`'s still-open spec for the
  same capability name** — both changes currently define
  `linux-harfbuzz-native-isolation` independently since neither is archived yet; whichever
  mechanism testing confirms should be the one actually archived into `openspec/specs/`,
  with the other's delta spec reconciled or dropped at that point (see the project's own
  prior note on archive-order header drift for this exact class of conflict).

## Migration Plan

- Build as an isolated test-RC (same throwaway-branch/worktree pattern as prior HarfBuzz
  RCs), version-bumped only in that branch, not merged to `main`.
- Ship to the specific tester(s) already engaged (lunardiver, and optionally Seralth given
  his non-glibc testing capability) rather than a public RC number.
- Rollback is trivial: this branch is never merged until confirmed, so there is nothing to
  revert on `main`.
- No data model, no persisted state, no wire-protocol involvement.

## Open Questions

- If this mechanism is confirmed working, does it fully replace
  `strengthen-harfbuzz-linux-fix`'s bundled-isolation-only approach, or do both stay as
  system-first-with-bundled-fallback (this design already assumes the latter — worth
  confirming once real results are in)?
- Is a runtime diagnostic command worth adding (matching the `.scribelight`/`.geartune`
  precedent) to let a tester report which path was chosen without needing the full log?
  Deferred — not needed for this spike.
