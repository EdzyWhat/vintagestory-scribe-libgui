## ADDED Requirements

### Requirement: Linux native-load behavior is desktop-independent
Scribe SHALL determine whether to apply native HarfBuzz isolation from the operating
system and libc capabilities, not from KDE, Qt, GTK, distro, or desktop-process names.

#### Scenario: GTK-based glibc desktop
- **WHEN** Scribe starts on Linux with glibc under a GTK-based desktop and the bundled
  HarfBuzz library is available
- **THEN** Scribe SHALL use the same glibc isolation path used under Qt-based desktops

#### Scenario: Qt-based glibc desktop
- **WHEN** Scribe starts on Linux with glibc under a Qt-based desktop and the bundled
  HarfBuzz library is available
- **THEN** Scribe SHALL use the same glibc isolation path without requiring KDE-specific
  detection

### Requirement: Supported glibc Linux loads the bundled HarfBuzz with isolation
On Linux systems using glibc, Scribe SHALL attempt to load the bundled
`libHarfBuzzSharp.so` with flags that make its internal symbol resolution prefer its own
symbols, before any Scribe or dependent-mod HarfBuzz call can use the default loader path.

#### Scenario: Bundled library loads successfully
- **WHEN** Scribe starts on glibc Linux and resolves the bundled native library path
- **THEN** Scribe SHALL load that path with `RTLD_NOW | RTLD_DEEPBIND`, register the
  assembly resolver, and log a single success diagnostic naming the resolved library

#### Scenario: Bundled library cannot be located
- **WHEN** Scribe starts on glibc Linux but the expected bundled native library path
  cannot be resolved
- **THEN** Scribe SHALL log a warning and return control to the runtime's default
  native-library resolution without throwing

### Requirement: Unsupported libc environments are explicitly diagnosed
On Linux systems where glibc-specific isolation is unavailable, Scribe SHALL skip the
custom `dlopen` attempt, log that isolation was unavailable and identify the detected
platform category, then allow normal native-library resolution to proceed.

#### Scenario: Non-glibc Linux startup
- **WHEN** Scribe starts on a non-glibc Linux system
- **THEN** Scribe SHALL not call the glibc `dlopen` entry point, SHALL emit one bounded
  diagnostic that the workaround was not applied, and SHALL not claim the system is
  protected from HarfBuzz collisions

#### Scenario: Non-glibc system otherwise loads normally
- **WHEN** the normal runtime loader can load HarfBuzz on a non-glibc system
- **THEN** Scribe SHALL leave that behavior unchanged apart from the diagnostic

### Requirement: Native-load outcomes are distinguishable in logs
Scribe SHALL emit at most one startup diagnostic for the native-load outcome, distinguishing
successful isolation, unsupported platform/libc, missing library, and failed native load.
The diagnostic SHALL not include unrelated environment data or repeat during text rendering.

#### Scenario: Community crash investigation
- **WHEN** a Linux user supplies the client log from a startup crash investigation
- **THEN** the log SHALL allow investigators to determine whether Scribe's isolation path
  succeeded, was unsupported, or fell back to default loading

### Requirement: Compatibility evidence records environment dimensions
The change SHALL maintain verification evidence separately for libc and desktop/toolkit
dimensions, including at minimum glibc/Qt, glibc/GTK, and at least one non-glibc system.
An unverified or unrelated failure SHALL not be recorded as confirmation of the
HarfBuzz collision.

#### Scenario: GTK reproduction report
- **WHEN** a reporter reproduces the crash on a GTK-based desktop
- **THEN** the report SHALL record the libc, desktop/toolkit, library-load outcome, and
  a native backtrace when available

#### Scenario: Non-glibc failure report
- **WHEN** a reporter reports failure on Alpine, Void, Gentoo, or another non-glibc system
- **THEN** the report SHALL record the exact failure and workaround diagnostic, while
  classifying the cause as unknown until HarfBuzz involvement is evidenced
