## ADDED Requirements

### Requirement: Isolation applies deterministically, not by startup-order race
On Linux with glibc, Scribe SHALL isolate the bundled HarfBuzzSharp native library's symbol
resolution from any system `libharfbuzz` already resident in the process, by replacing `gui`'s own
native-library registration for that assembly rather than by relying on Scribe's own startup order
winning a race against `gui`'s.

#### Scenario: Isolation applies regardless of mod load order
- **WHEN** Scribe and `gui` both start on a Linux/glibc client, in any relative order
- **THEN** the bundled HarfBuzzSharp native library is loaded with `RTLD_DEEPBIND` isolation before
  any HarfBuzz symbol is resolved

### Requirement: Isolation fails closed, never crashing or regressing below the unpatched state
If the isolation mechanism cannot apply — the target method cannot be found, patching it throws, or
the isolated resolver itself fails — Scribe SHALL log the failure and allow `gui`'s original,
unisolated native-library loading to proceed, rather than crashing startup or leaving the
HarfBuzzSharp assembly with no resolver registered at all.

#### Scenario: Target method missing or restructured in a future `gui` release
- **WHEN** Scribe cannot locate or patch `gui`'s native-library registration method
- **THEN** Scribe logs a warning and takes no further action; `gui` loads HarfBuzzSharp exactly as
  it would if Scribe's fix were not installed

#### Scenario: Isolated resolver itself fails to load the bundled library
- **WHEN** the isolated resolver's attempt to open the bundled native library fails
- **THEN** it falls back to the same unflagged native-library lookup `gui`'s own resolver would have
  used, rather than leaving the assembly unresolvable

### Requirement: No-op outside Linux/glibc
On any platform other than Linux, or on a non-glibc Linux libc, Scribe SHALL take no action related
to HarfBuzz native-library isolation.

#### Scenario: macOS or Windows client
- **WHEN** Scribe starts on macOS or Windows
- **THEN** no HarfBuzz isolation patch is attempted and `gui` loads its native library normally

#### Scenario: Non-glibc Linux client
- **WHEN** Scribe starts on a Linux client using a non-glibc libc (e.g. musl)
- **THEN** Scribe logs that isolation is unavailable (glibc-only) and takes no isolation action
