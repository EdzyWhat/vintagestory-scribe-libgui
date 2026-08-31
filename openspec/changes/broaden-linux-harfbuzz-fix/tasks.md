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

## 3. Validate and document compatibility evidence

- [ ] 3.1 Add or update build-safe tests for platform gating, outcome classification,
      and bounded diagnostics without referencing the Vintage Story API from `src/Core/`.
- [ ] 3.2 Run the Core test suite and a macOS/client build smoke check.
- [ ] 3.3 Publish a diagnostic build for community testing on glibc/Qt, glibc/GTK,
      and non-glibc environments.
- [ ] 3.4 Record each report's desktop/toolkit, libc, library-load outcome, and native
      backtrace when available; classify unproven failures as unknown.
- [ ] 3.5 Update the upstream LibGUI issue with the cross-desktop evidence and clarify
      that the durable fix belongs in native-library packaging/loading.
