## ADDED Requirements

### Requirement: System HarfBuzz is preferred over the bundled, isolated copy
On Linux, Scribe SHALL first attempt to resolve HarfBuzzSharp's native calls against the
host system's installed `libharfbuzz`, before falling back to loading the bundled copy
with `RTLD_DEEPBIND` isolation. This removes the two-copies-in-one-process condition that
symbol interposition depends on, rather than trying to isolate one of the two copies.

#### Scenario: System libharfbuzz is present and loadable
- **WHEN** Scribe resolves a HarfBuzzSharp native call on Linux and the system exposes a
  loadable `libharfbuzz.so.0` or `libharfbuzz.so`
- **THEN** Scribe binds to the system library and does not load the bundled copy at all

#### Scenario: System libharfbuzz is absent or fails to load
- **WHEN** neither `libharfbuzz.so.0` nor `libharfbuzz.so` can be loaded from the system
- **THEN** Scribe falls back to exactly the existing bundled-file, `RTLD_DEEPBIND`-isolated
  resolution already shipped by `strengthen-harfbuzz-linux-fix`, unchanged

### Requirement: Non-glibc Linux receives a real attempt, not a blanket no-op
Because the system-library attempt does not require `RTLD_DEEPBIND` (a glibc-only
extension), Scribe SHALL attempt it on non-glibc Linux (e.g. musl) as well — narrowing the
existing "no-op outside Linux/glibc" behavior so that only the bundled-file+`RTLD_DEEPBIND`
fallback branch is glibc-gated, not the system-library attempt itself.

#### Scenario: Non-glibc Linux with a system libharfbuzz present
- **WHEN** Scribe starts on a non-glibc Linux client (e.g. musl) that has a system
  `libharfbuzz` installed
- **THEN** Scribe binds to the system library, the same as on glibc

#### Scenario: Non-glibc Linux with no usable system libharfbuzz
- **WHEN** Scribe starts on a non-glibc Linux client with no loadable system `libharfbuzz`
- **THEN** Scribe logs that isolation is unavailable (the bundled-file fallback requires
  `RTLD_DEEPBIND`, a glibc extension) and takes no further action — never worse than the
  default, unpatched resolution

### Requirement: Startup logging identifies which resolution path was used
Scribe SHALL log, at startup, which of the three outcomes occurred for HarfBuzz native
resolution — system library, bundled-isolated fallback, or neither — including the
resolved library's version string when it can be obtained without extra risk. No such
field visibility exists today; testers' actual system HarfBuzz versions are currently
unknown to the maintainer.

#### Scenario: System library path taken
- **WHEN** Scribe binds to the system `libharfbuzz`
- **THEN** it logs that the system library was used, including its version string if
  obtainable

#### Scenario: Fallback path taken
- **WHEN** Scribe falls back to the bundled, isolated copy
- **THEN** it logs that the fallback was used and why the system attempt did not apply
