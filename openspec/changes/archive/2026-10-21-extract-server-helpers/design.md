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
- **Dropped: the `ScribeLang` helpers.** Audit of the call sites found three one-line
  `HasTranslation ? Get : fallback` ternaries (two in `ScribeQuestCatalog`, one in
  `ScribeProgressionFrameworkQuestCatalog`); `HistoryDisplay.HasKey` is an existence check with different flags, not
  the same pattern. A helper would add a file and save no lines, so it is not worth it.
- **`Clamp01` became `Scribe.Core.ScribeMath.Clamp01`** (in Core so it is unit-testable) implemented with
  `Math.Clamp`, which treats out-of-range and NaN inputs the same way the three hand-written copies did.

- **Dropped: `SendToClient` and moving `TryReadGuid`.** The two notebook-save echoes send different fields, and
  `TryReadGuid` is already a member of the `ScribeModSystem` partial class, so every handler can reach it.
- **Handler tests came first.** The user chose to cover the guard layer before touching it: the handlers became
  `internal` (matching `OnServerReceivedNotebookSave`) and `ServerPacketHandlerScenarios` drives them.
- **`ResolveStation` instead of `WithStation`**: returning the station avoids a lambda per packet and keeps each handler's
  body at the same indentation.

## Handler coverage map (task 1.1)

Atlas scenarios do not send packets. They call the logic behind the handlers directly (`SetPinForPlayer`,
`CompleteTaskForPlayer`, `lectern.ApplyEdit`, `lectern.OnRequestAccess`, `ApplyTaskNoticeAction`), and only
`OnServerReceivedNotebookSave` is invoked as a handler (`TabletNotebookDocIdStampRaceScenarios`,
`EditorAssignmentCompletionSyncScenarios`). The wrappers this change would touch are the guard layer in front of that
logic (null `sapi`, host lookup, 16-byte GUID checks and their trace text), and no automated test reaches it:

| Handler group | Reached by an automated test? |
|---|---|
| Edit, ReleaseLock, RequestAccess, RecordVisitor, EditGuestbookNote (station lookup) | No (logic behind them is) |
| SetPin, CompleteTask, EditPinnedTask, DeleteTask, SetTrackerQuantity, SetQuestObjectiveProgress (doc+task GUID guard) | No (logic behind them is) |
| Manual-history Add / SetText / Delete (entry GUID guard) | No |
| NotebookSave echo (`SendToClient`) | Yes, handler is called directly |

## Risks / Trade-offs

Medium for the handlers because Core tests do not cover them; mitigated by the coverage map, one-handler-per-commit
changes, and a manual smoke test of pin, complete, edit, delete and tracker count for uncovered handlers.
