## ADDED Requirements

### Requirement: Bundled HarfBuzzSharp loads with symbol isolation on Linux/glibc

On Linux systems running glibc, Scribe SHALL force the bundled `libHarfBuzzSharp.so` native library
to load with the `RTLD_DEEPBIND` flag, so its internal symbol resolution prefers its own copy of any
overlapping symbol over one from any other native library already resident in the process (such as a
system-installed `libharfbuzz.so.0`). This registration SHALL run before any other mod's
`StartClientSide`, so no HarfBuzz call made by Scribe, LibGUI's own default rendering, or any other
LibGUI-based mod on the same client can reach the default (unisolated) load path first.

#### Scenario: Registration runs before any HarfBuzz call

- **WHEN** the client starts and mods are loaded
- **THEN** Scribe's native-load-isolation registration runs before any mod's first call into
  `HarfBuzzSharp`, on a client where at least one LibGUI-based mod is installed

#### Scenario: Deep-binding takes effect on glibc Linux

- **WHEN** the client is running on Linux with a glibc `libc` implementation
- **THEN** the bundled `libHarfBuzzSharp.so` is loaded via a direct `dlopen` call requesting
  `RTLD_DEEPBIND`, rather than through the runtime's default native-library resolution

#### Scenario: No effect on non-Linux platforms

- **WHEN** the client is running on Windows or macOS
- **THEN** Scribe registers no custom native-library resolver for `HarfBuzzSharp`, and native-library
  loading proceeds exactly as it did before this change

### Requirement: Graceful fallback when isolation cannot be applied

If the deep-bind load attempt cannot be completed for any reason — a non-glibc Linux system, a
`dlopen` failure, or an inability to locate the bundled native library on disk — Scribe SHALL fall
back to the runtime's default native-library resolution rather than failing to load the library at
all or throwing from the resolver.

#### Scenario: Non-glibc Linux falls back to default resolution

- **WHEN** the client is running on a Linux system without glibc (e.g. a musl-based distribution)
- **THEN** Scribe does not attempt a custom `dlopen` call, and native-library loading proceeds via the
  runtime's default resolution

#### Scenario: A failed deep-bind attempt does not block loading

- **WHEN** Scribe's custom `dlopen(path, RTLD_DEEPBIND)` call fails for any reason
- **THEN** Scribe's resolver returns control to the runtime's default native-library resolution
  instead of throwing or leaving the library unloaded
