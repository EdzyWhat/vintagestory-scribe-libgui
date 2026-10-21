## Context

Packet handlers are Vintage Story API entry points; `tests/Core.Tests` cannot reach them. `tests/Integration.Tests`
(Atlas) runs a real server and may or may not drive each handler.

## Goals / Non-Goals

**Goals:** fewer repeated guards; identical behavior, including trace strings.

**Non-Goals:** changing which packets exist, validation rules, or the trace wording; unifying colour rounding;
adding a handler framework or base class.

## Decisions

- **Map handler coverage first.** Before editing, list which Atlas scenarios send each packet through the real
  handler (for example `ServerAuthoritativeEditScenarios`, `SingleEditorLockScenarios`, `PinScenarios`) and record
  the gaps in this file. Handlers with no Atlas coverage are refactored last, one per commit, and flagged for
  manual testing.
- **Private helpers in the same partial class**, not a new type. They close over `sapi` and `Trace`, so a separate
  class would need them passed in. Alternative: a handler base class. Rejected as too large for the saving.
- **`TryReadDocTask` takes the label** and builds the same "MALFORMED packet (docId/taskId not 16 bytes) - ignored"
  line so existing log greps still match.
- **`Clamp01` lives in one internal static class** (`ScribeMath`) in Mod. Moving it into Core (testable) is
  optional and only if no Mod type is needed.
- **`ScribeLang` helpers take the lookup flags as parameters**, so `HistoryDisplay`'s quiet lookup is preserved.

## Risks / Trade-offs

Medium for the handlers because Core tests do not cover them; mitigated by the coverage map, one-handler-per-commit
changes, and a manual smoke test of pin, complete, edit, delete and tracker count for uncovered handlers.
