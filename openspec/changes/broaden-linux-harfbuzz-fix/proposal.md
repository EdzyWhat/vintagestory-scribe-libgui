## Why

The confirmed HarfBuzz symbol-interposition crash is broader than the original CachyOS/KDE
reproduction: a user reports the same failure on GTK-based desktops and startup failures on
non-glibc distributions. The existing Scribe workaround is intentionally glibc-specific and
does not yet distinguish an unsupported libc environment from an unrelated Linux failure, so
the change needs a broader compatibility boundary and better evidence collection without
pretending that a managed workaround can provide portable native symbol isolation.

## What Changes

- Generalize the Linux crash model and user-facing diagnostics from KDE/CachyOS to any Linux
  desktop or toolkit that can load a conflicting system HarfBuzz into the Vintage Story process.
- Preserve the glibc native-load isolation workaround, but make its scope and success/failure
  observable in client logs.
- Detect Linux environments where the workaround cannot apply, including non-glibc systems,
  and emit actionable diagnostics rather than silently presenting the default loader path as
  equivalent protection.
- Add a compatibility-test and evidence matrix covering glibc desktop variants, GTK/Qt
  environments, and non-glibc systems; record exact loader and crash outcomes.
- Document the boundary between Scribe's local mitigation and the durable upstream fix in
  LibGUI/HarfBuzzSharp (hidden/versioned symbols or proper namespace isolation).
- Do not claim that Scribe fixes every Linux distribution or that non-glibc failures share the
  confirmed HarfBuzz mechanism unless a backtrace establishes that connection.

## Capabilities

### New Capabilities

- `linux-native-runtime-compatibility`: Detect, isolate where supported, and diagnose Linux
  native HarfBuzz loading conditions across desktop toolkits and libc implementations.

### Modified Capabilities

None. The existing bundled-font-rendering requirements are not changed; this capability
describes native-runtime compatibility and diagnostics around the existing rendering stack.

## Impact

- Affected code: client-side startup initialization in `src/Mod/`, plus diagnostic logging and
  build/test documentation.
- Affected OpenSpec artifacts: the existing Linux HarfBuzz isolation proposal is the baseline;
  this change adds broader compatibility and evidence requirements without changing Scribe's
  save format, network protocol, or `src/Core/`.
- No new mod or NuGet dependencies.
- Linux verification remains community-assisted for real desktop/libc combinations; macOS and
  Core tests can validate non-Linux behavior and build safety only.
