## Context

Candidates come from a read-only audit: each was grepped across `src/`, `tests/`, `build/` and `tools/`. Members
reached by reflection, Harmony, the engine, or JSON deserialization were excluded as live.

## Goals / Non-Goals

**Goals:** remove only members with no callers; keep the build and both test suites green.

**Non-Goals:** removing test-only convenience builders (`ScribeDocument.AddTracker/AddCraft/AddLink/AddGuideLink`,
`ScribePinStore.IsPinned`), unreferenced lang keys, or unused constants (a second audit pass may cover those).

## Decisions

- **Delete `TryCreateAccepted` and its tests.** The behavior it tested no longer exists in production. The one
  Atlas caller used it as fixture setup, so it moves to the path players take (`TryCreateSent` then the accept
  action), which is a more faithful setup. Alternative: keep it as a test helper. Rejected: it keeps a second
  creation path alive that production never takes.
- **Remove the Clockmaker tick registration with its handler.** The handler body is a comment only. Trace
  `_timerTickId` fully first so the unregister in `OnGuiClosed` is removed too.
- **Check `_docPositions` after removing `TryResolvePos`.** If only `RegisterDoc`/`UnregisterDoc` touch it, remove
  it and those two calls' bodies; if they have callers, keep the methods as no-ops only if needed to compile
  (prefer removing the callers).

## Risks / Trade-offs

Low. A mis-flagged reflection or JSON member would fail at runtime only; the exclusions above cover the known cases.
Atlas covers the assignment path; the Clockmaker tick removal is covered by a smoke test of the Timer tab.
