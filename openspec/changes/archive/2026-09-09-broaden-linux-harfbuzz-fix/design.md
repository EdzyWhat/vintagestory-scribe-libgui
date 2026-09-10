## Context

LibGUI 3.1.0 ships a native `libHarfBuzzSharp.so` whose exported `hb_*` symbols can be
interposed by a different `libharfbuzz.so.0` already loaded in the Vintage Story process.
The CachyOS/KDE reproduction has a real backtrace showing a bundled
`hb_font_create` path reaching system HarfBuzz before glibc aborts on heap corruption.
The accompanying user report reproduces the crash on GTK desktops, which broadens the
trigger from a KDE/Qt condition to a host-process/library condition. The same report says
Alpine, Void, and Gentoo do not work with the current attempt; those observations need
separate evidence because the proposed isolation flag is glibc-specific.

The existing `fix-linux-harfbuzz-symbol-collision` change defines the first glibc
workaround. This change makes its platform boundary explicit, adds safe diagnostics, and
defines a testable compatibility contract. `src/Core/` remains untouched.

## Goals / Non-Goals

**Goals:**

- Treat GTK, Qt, and other desktop/toolkit combinations as possible sources of a
  process-global system HarfBuzz, without hardcoding a desktop environment.
- Preserve and instrument the glibc `RTLD_DEEPBIND` isolation path.
- Identify libc/platform cases where the workaround is unavailable and log enough
  information to distinguish unsupported isolation from a native crash.
- Establish reproducible community test evidence for glibc and non-glibc Linux systems.
- Keep the workaround client-only, additive, and free of new dependencies.

**Non-Goals:**

- Claiming that Scribe can provide portable symbol isolation on musl or other non-glibc
  libcs using managed code alone.
- Rebuilding LibGUI, HarfBuzzSharp, or native HarfBuzz binaries in this repository.
- Diagnosing every non-glibc Vintage Story crash as HarfBuzz-related without a backtrace.
- Replacing the upstream fix: LibGUI still needs proper native dependency isolation or
  hidden/versioned symbols for an ecosystem-wide solution.

## Decisions

### Use platform/libc capability detection, not desktop detection

The implementation SHALL gate behavior on Linux and libc capability rather than checking
KDE, Qt, GTK, or distro names. The desktop report demonstrates that toolkit identity is
not a reliable boundary, while the native flag availability is.

Alternatives considered:

- Desktop-specific branches were rejected because they would miss GTK and future toolkit
  combinations.
- Distro-name branches were rejected because package versions and desktop processes vary
  within a distro.

### Keep `RTLD_DEEPBIND` as the glibc mitigation

On glibc, the resolver SHALL load the bundled library with `RTLD_NOW | RTLD_DEEPBIND`
before the first HarfBuzz P/Invoke. This is the smallest local workaround supported by
the confirmed symbol-interposition evidence.

Alternatives considered:

- `LD_PRELOAD` was rejected as a reliable application fix because it changes load order,
  not the bundled library's internal symbol binding.
- `NativeLibrary.Load` was rejected because it does not expose the required `dlopen`
  flags.
- `dlmopen` is a possible stronger upstream design, but is not a portable managed
  workaround and is outside this change's implementation scope.

### Make unsupported environments observable, not falsely “fixed”

On non-glibc Linux, Scribe SHALL skip the glibc-specific loader and emit a bounded
diagnostic identifying that isolation was unavailable. The normal loader path remains the
fallback so the workaround cannot prevent systems that otherwise load successfully from
starting. The message SHALL not assert that the observed failure is HarfBuzz-related.

Alternatives considered:

- Attempting `RTLD_DEEPBIND` through guessed libc symbols was rejected as unsafe.
- Silently falling through was rejected because it makes community testing unable to
  distinguish “not applicable” from “successfully isolated.”

### Use structured, low-volume diagnostics

Log one startup result with platform/libc category and isolation result, plus the resolved
native path on success. Do not log environment dumps, user paths unrelated to the native
library, or repeated per-frame/per-text messages.

### Verify by matrix, not anecdotal distro labels

Community verification SHALL record desktop toolkit family, libc, Vintage Story/LibGUI
versions, whether another HarfBuzz was resident, startup result, and (when applicable)
the native backtrace. GTK and Qt are test dimensions; they are not causal assumptions.

## Risks / Trade-offs

- **[Risk] `RTLD_DEEPBIND` remains glibc-specific** → **Mitigation:** capability detection,
  explicit non-glibc diagnostics, and no claim of equivalent isolation elsewhere.
- **[Risk] A non-glibc failure is unrelated to HarfBuzz** → **Mitigation:** require a
  backtrace or loader evidence before classifying it as the same defect.
- **[Risk] Resolver registration races another mod** → **Mitigation:** retain an early
  `ExecuteOrder`; document that upstream LibGUI remains the durable ecosystem fix.
- **[Risk] Manual loading changes runtime resolution behavior** → **Mitigation:** return
  the standard handle, fail back to default resolution on all errors, and test macOS plus
  unaffected Linux startup.
- **[Risk] Community matrix is incomplete** → **Mitigation:** make each result explicit
  and preserve unknown combinations as unverified rather than generalizing from them.

## Migration Plan

No save, network, or data migration is required. Ship the client-side diagnostics and
glibc workaround in a test build, collect matrix results, then either promote the
workaround or revise it based on fresh backtraces. Rollback is a normal code revert.

## Open Questions

- Can a non-glibc loader provide safe equivalent isolation without a rebuilt native
  library, or must those systems be handled upstream?
- Does the runtime invoke the resolver early enough on every supported .NET 10 packaging
  layout, including extracted and zipped mod installations?
- Does the same collision occur in GTK environments because of GTK/Pango or another
  process dependency, and can a reporter confirm the loaded library set?
