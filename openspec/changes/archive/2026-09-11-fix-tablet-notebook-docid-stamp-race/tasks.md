## 1. Stop `NotebookHost`'s constructor from writing to the ItemStack

- [x] 1.1 In `src/Mod/NotebookHost.cs`'s constructor, keep building the in-memory placeholder
  `ScribeDocument` when `ScribeDocumentAttributes.TryReadFrom` finds nothing, but remove the
  `ScribeDocumentAttributes.WriteTo(stack, doc)` call — verify by reading the diff: the ctor no
  longer calls `WriteTo` on the documentless branch.
- [x] 1.2 Audit every other `new NotebookHost(`/`new TabletHost(` call site
  (`ScribeModSystem.History.cs:100`, `ScribeModSystem.PinOperations.cs:364`,
  `ItemScribeNotebook.cs:115`, `ScribeModSystem.Network.cs:562`, `ScribeModSystem.Quest.cs:384`,
  `ItemClockmakerNotebook.cs:179`) and confirm none of them assumed construction leaves a
  `scribeDocument` attribute on the stack — verify by reading each call site's post-construction
  usage; none should read the `ItemStack.Attributes` directly expecting the write to have
  happened.
- [x] 1.3 Add an Atlas scenario asserting `NotebookHost`'s constructor does not mutate a
  documentless `ItemStack`'s attributes — verify the new test fails against the pre-fix behavior
  (temporarily revert 1.1 locally to confirm) and passes after. (No Mod-level unit-test project
  exists — `NotebookHost` needs the VintagestoryAPI types Core.Tests can't reference — so this is
  covered by Atlas rather than a lighter-weight unit test.) Added as
  `Constructing_a_notebook_host_over_a_documentless_stack_writes_nothing` and its Tablet sibling
  in `tests/Integration.Tests/TabletNotebookDocIdStampRaceScenarios.cs`.

## 2. Stop PickedUp-history bookkeeping from persisting the document

- [x] 2.1 In `src/Mod/NotebookHost.cs`'s `RecordPickedUpIfNew`, change `if (added) Flush();` to
  `if (added) FlushHistory();` — it only ever mutates `_history`, so it should never persist
  `_document` as a side effect — verify by reading the diff.
- [x] 2.2 Revert/confirm `src/Mod/ScribeModSystem.History.cs`'s `FindCarriedNotebooks` does NOT
  skip documentless stacks (that guard was considered and dropped — see design.md Decision 2 —
  because it would silently stop Death/PvpKill/BossKill/TemporalStorm recording on any notebook
  until after its first save) — verify by reading the method: it still constructs a host and
  calls `AttachServerContext` unconditionally for every carried `IScribeDocumentItem`.
- [x] 2.3 Add an Atlas scenario: seed a player with a documentless Notebook/Tablet, fire
  `OnHistoryScanTick` directly (it's `internal`, matching the pattern used for
  `OnServerReceivedNotebookSave` in the existing diagnostic tests) so it records that player's
  first PickedUp entry, and assert (a) the stack still has no `scribeDocument` attribute
  afterward, and (b) the history entry WAS recorded (`scribeHistory` now has a PickedUp entry) —
  verify the test passes and would fail against the pre-fix `Flush()` call. Assertion (b) doubles
  as the guard against re-introducing the "skip documentless stacks" guard rejected in design.md
  Decision 2 (a sweep that skipped documentless stacks entirely would fail this same assertion, so
  a separate storm/Death/PvpKill-specific scenario wasn't needed). Added as
  `Ambient_sweep_records_pickedup_on_a_documentless_notebook_without_stamping_it` and its Tablet
  sibling in `tests/Integration.Tests/TabletNotebookDocIdStampRaceScenarios.cs`.

## 3. Regression test for the actual end-to-end race (replaces the diagnostic scaffolding)

- [x] 3.1 Add an Atlas scenario reproducing the exact confirmed sequence from the reporter's log:
  a fresh, documentless Notebook is opened (constructing a `NotebookHost`, per fix 1.1 this no
  longer stamps anything), then `OnHistoryScanTick` fires and records a PickedUp entry (per fix
  2.1 this no longer stamps anything either), then a save flush arrives with the client's own
  freshly-minted `DocId` — assert the save is now ACCEPTED (the stack's `scribeDocument`
  attribute now holds that same `DocId` and the saved content), where before this change it would
  have been silently refused — verify the test passes only with fixes 1.1/2.1 in place (confirm
  by temporarily reverting one locally and seeing the new test fail). Added as
  `Notebook_save_after_the_ambient_sweep_races_it_and_is_accepted`.
- [x] 3.2 Repeat 3.1 for a Tablet (via `TabletHost`) to cover the item the original Discord report
  was about — verify the test passes. Added as
  `Tablet_save_after_the_ambient_sweep_races_it_and_is_accepted`.
- [x] 3.3 Delete `tests/Integration.Tests/TabletTaskLossDiagnosticScenarios.cs` (superseded by
  3.1/3.2, which test the real mechanism instead of the two ruled-out hypotheses) — verify the
  file no longer exists and the Atlas suite still builds.

## 4. Remove diagnostic scaffolding

- [x] 4.1 Remove every `TEMP DIAGNOSTIC (task-loss-on-tablet-close)` trace line from
  `src/Mod/ScribeModSystem.Network.cs` (the entry-point trace, the not-a-scribe-item trace, the
  readonly-refusal trace, the DocId-mismatch trace, the success-path trace, and the
  notebook-opened baseline trace) — verify via `grep -rn "task-loss-on-tablet-close" src/` finding
  zero matches.
- [x] 4.2 Remove every `TEMP DIAGNOSTIC (task-loss-on-tablet-close)` trace line from
  `src/Mod/ItemScribeTablet.cs` (`OnTransitionNow`, `DoSmelt`, `Soften`) — verify via the same grep
  finding zero matches in this file.

## 5. Verify

- [x] 5.1 Run the Core test suite (`dotnet test tests/Core.Tests`) and confirm it's green.
- [x] 5.2 Run the full Atlas integration suite with `VINTAGE_STORY` set
  (`dotnet test tests/Integration.Tests`) and confirm it's green, including the new scenarios
  from 1.3, 2.3, 2.4, 3.1, and 3.2.
- [x] 5.3 Manually restage a Debug build (`build/restage.sh Debug`, client not running) and
  smoke-test by hand: craft a fresh Notebook and a fresh Tablet, wait past 10 seconds with the
  dialog open before typing (the exact window that used to trigger the race), type a task, close,
  reopen — confirm the task is present both times.
- [x] 5.4 Update `CHANGELOG.md` with a fix entry describing the task-loss-on-close bug and its
  resolution.
