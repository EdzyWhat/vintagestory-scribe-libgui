## 1. Remove unreferenced members

- [x] 1.1 Delete `ScribeStamp.EaseOutCubic`, `GuiDialogClockmakerNotebook.FormatDuration`, `ScribeDialogBase.IsReadView` (and the `cref` to it), `GlyphBundle.Characters`; verify with `dotnet build` and a repo-wide grep of each name returning nothing
- [x] 1.2 Delete Clockmaker `OnTimerTick`, its `RegisterGameTickListener` call, `_timerTickId`, and the unregister in the close path; verify with `dotnet build` and no remaining `OnTimerTick`/`_timerTickId` hits
- [x] 1.3 Delete `ScribePinStore.KnownDocIds`, `PlayersPinning`, `TryResolvePos`; remove `_docPositions` and its register/unregister callers if it is write-only; verify with build and the pin Atlas scenarios (`PinScenarios`)

## 2. Remove TryCreateAccepted

- [x] 2.1 Delete `ScribeAssignmentStore.TryCreateAccepted` and its six `TryCreateAccepted_*` tests in `ScribeAssignmentStoreTests.cs`; verify Core tests pass
- [x] 2.2 Rewrite the setup in `EditorAssignmentCompletionSyncScenarios` to use `TryCreateSent` plus the accept transition; verify that scenario passes in the Atlas suite

## 3. Verify

- [x] 3.1 Run `build/verify.sh` with `VINTAGE_STORY` set: build, all Core tests, and the Atlas suite pass
- [x] 3.2 Playtest smoke: open the Clockmaker Timer tab, set and watch a timer; open the HUD and pin/unpin a task (add as Backlogged in TESTING.md first if deferred)
