## Why

An audit after the comment sweep found a small set of members with no callers. They add reading load without
doing anything, and one of them (`OnTimerTick`) wakes the Clockmaker dialog every 250 ms to do nothing.
`ScribeAssignmentStore.TryCreateAccepted` is the largest: it was built for Task Notice accept, but that flow now
creates the record at send time (`TryCreateSent`) and accepts through the normal state transition, so only tests
call it.

## What Changes

- Delete `ScribeStamp.EaseOutCubic`, `GuiDialogClockmakerNotebook.FormatDuration`, `ScribeDialogBase.IsReadView`,
  and `GlyphBundle.Characters`.
- Delete the Clockmaker `OnTimerTick` no-op together with its `RegisterGameTickListener` registration and the
  `_timerTickId` field and unregister.
- Delete `ScribePinStore.KnownDocIds`, `PlayersPinning` and `TryResolvePos`; drop `_docPositions` if it becomes
  write-only.
- Delete `ScribeAssignmentStore.TryCreateAccepted` and its six Core tests; rewrite the Atlas setup in
  `EditorAssignmentCompletionSyncScenarios` to create a record and apply the accept action.
- Keep: reflection-loaded `ScribeSkipLibGuiOnboarding` and `ScribeMealPageHandbookPatch`, `IRotatable.OnTransformed`,
  test seams (`AssignmentStore`, `KnownPlayersStore`, `OutstandingNoticeCount`, `PendingDocumentCount`), and
  Newtonsoft DTO fields.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
None. No player-visible behavior changes (`skip_specs: true`).

## Impact

`src/Mod` (Clockmaker dialog, pin store, dialog base, stamp), `src/Core` (assignment store, glyph bundle),
`tests/Core.Tests/ScribeAssignmentStoreTests.cs`, `tests/Integration.Tests/EditorAssignmentCompletionSyncScenarios.cs`.
About 50 production lines and about 150 test lines removed.
