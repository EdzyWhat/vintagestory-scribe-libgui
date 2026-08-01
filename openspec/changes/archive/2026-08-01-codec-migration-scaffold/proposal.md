## Why

`ScribeDocumentCodec` currently accepts exactly two versions: the current one and the one
immediately before it. Each future version bump (v6 signatures, chronicle timestamps) silently
drops the oldest accepted version, so a player who skips a release could have their saves
rejected with no path forward. This change establishes a clean, tested migration chain — one
explicit per-version upgrade step with a unit-tested "reads an older blob" fixture — before any
upcoming feature bumps the version and inherits the risk.

## What Changes

- `ScribeDocumentCodec` gains an explicit migration chain: the reader recognises every
  supported prior version, upgrades each to the current schema via a small, named step, and
  the accepted version window is documented in one place. The existing v4→v5 upgrade (supply
  `DefaultTitle`) is refactored into this pattern rather than living as an inline `isCurrent`
  branch.
- `ScribePinCodec` gets the same treatment: a `PriorPinVersion` constant and a migration
  path for adding future pin-format fields without stranding existing saves.
- `ScribeDocumentCodecTests` gains a "reads an older blob" fixture for each supported prior
  version (currently v4), verifying the exact upgrade outcome rather than only that the read
  succeeds.
- The single-version-line rule (append fields in version order; never two "v5"s) is reinforced
  by a `CODEC-MIGRATION.md` note in `docs/` and a comment template in both codec files, so
  the next developer sees the pattern before writing a new version branch.

## Capabilities

### New Capabilities

- `codec-migration`: Versioned forward-migration support in `ScribeDocumentCodec` and
  `ScribePinCodec`: explicit per-version upgrade steps, a discoverable accepted-version window,
  and unit-tested older-blob fixtures that prove each migration step in isolation.

### Modified Capabilities

- `task-note-document`: The serialization requirement "accepts both the current version and the
  immediately prior version" is tightened to specify that each supported prior version is
  upgraded via a documented, named migration step (not ad-hoc inline branching), and that each
  step is covered by a dedicated "older blob" unit test.

## Impact

- `src/Core/ScribeDocumentCodec.cs` — refactor the `isCurrent` inline branch into a named
  migration step; add the accepted-version table in the doc-comment.
- `src/Core/ScribePinCodec.cs` — add `PriorPinVersion` constant and the migration scaffolding
  (no actual field changes — just the structure so the next version bump has a clear home).
- `tests/Core.Tests/ScribeDocumentCodecTests.cs` — new "reads an older blob" fixture tests for
  each supported prior version.
- `docs/CODEC-MIGRATION.md` — new, short how-to doc covering the append-only rule, the
  accepted-version window, and how to add a new version step.
- No game API changes, no network packet changes, no new mod dependencies. Pure `src/Core/`
  and `tests/Core.Tests/` — fully testable with `dotnet test` and no game install.
