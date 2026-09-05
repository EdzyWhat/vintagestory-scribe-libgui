## 1. Core: QuestObjective block kind

- [x] 1.1 In `src/Core/ScribeBlock.cs`, append `QuestObjective` to `ScribeBlockKind` (next unused byte
      value after `Craft = 4`, i.e. `5` — never renumber existing values). Add `IsQuestObjective`
      following the existing `IsTracker`/`IsCraft` pattern. Verify `dotnet build` (Core) succeeds.
- [x] 1.2 Confirm (add a unit test if not already covered) that `IsCarriedCountTracked` remains
      exactly `Kind is Tracker or Craft` — i.e. a `QuestObjective` block's `IsCarriedCountTracked` is
      `false`. Verify `dotnet test` passes.
- [x] 1.3 Document (XML doc-comment on the new enum value) the field reuse: `LinkTarget` holds the
      objective's own stable code (match key, not a clickable target), `LinkLabel` holds the captured
      display label for a non-item objective, `TargetItemCode` is set only when the objective resolves
      to one concrete item.

## 2. Core: reconcile + progress write

- [x] 2.1 Add `ScribeDocument.ReconcileQuestObjectives(Guid questLinkTaskId, IReadOnlyList<(string
      Code, string? ItemCode, string? Label, int Required)> objectives, bool createMissing = true)`,
      mirroring `ReconcileCraftIngredients`'s owned-run shape: find the parent by `TaskId` +
      `IsLink` + quest target, get its `OwnedRun`, match existing depth-1 `QuestObjective` children by
      `LinkTarget == Code`, rescale `TargetQuantity` on a match, insert a new child at `runEnd` when
      `createMissing` and no match exists, never delete unmatched existing children. Add unit tests
      covering: insert-on-first-call, rescale-on-repeat-call, no-op when the objective already matches,
      a player-deleted child is not recreated. Verify `dotnet test` passes.
- [x] 2.2 Add `ScribeDocument.SetQuestObjectiveProgress(Guid taskId, int currentQuantity)`: find the
      block by `TaskId` gated on `IsQuestObjective` (not the shared `IsCarriedCountTracked`-gated
      `SetTrackerCurrentQuantity`), clamp `currentQuantity` to `[0, TargetQuantity]` — unlike Tracker's
      overflow-visible current count, an objective's progress cannot exceed its required count in
      Progression Framework's own model. Add a unit test. Verify `dotnet test` passes.

## 3. Codecs: persist the new kind

- [x] 3.1 `ScribeDocumentCodec.cs` (binary): confirm the existing generic `(byte)block.Kind` write /
      `(ScribeBlockKind)r.ReadByte()` read round-trips a `QuestObjective` block with its
      `TargetItemCode`/`TargetQuantity`/`CurrentQuantity`/`LinkTarget`/`LinkLabel` fields with no
      per-kind special-casing needed (confirm by reading the existing Tracker/Craft field-write blocks
      — add any missing field write if the codec is per-kind-gated rather than field-generic). Add a
      round-trip unit test. Verify `dotnet test` passes.
- [x] 3.2 `ScribeDocumentJsonCodec.cs`: same round-trip confirmation/fix + unit test.
- [x] 3.3 `ScribeDocumentTsvCodec.cs`: same round-trip confirmation/fix + unit test (TSV export/import
      is human-facing — confirm a `QuestObjective` row exports/imports sensibly, e.g. as a Tracker-like
      line).

## 4. Catalog: re-add correctly-typed objective item resolution

- [x] 4.1 In `ScribeProgressionFrameworkQuestCatalog.cs`, re-add an `Items` reader on `RawObjective`
      matching Progression Framework's real shape (`QuestItemRequirement`: `{item, quantity,
      alternates}` — confirmed in `reference/ProgressionInvestigations/.../QuestItemRequirement.cs`).
      Extend `ScribePfObjectiveDef` with a nullable resolved item code, set only when `Items` has
      exactly one entry with no alternates. Verify `dotnet build src/Mod` succeeds and re-run the
      catalog against the real installed `seafarer_0.5.15.zip` (manual check, mirrors the earlier
      domain-scoping fix's verification) to confirm every delivery objective still parses (no repeat of
      the earlier silent-drop bug).

## 5. Watcher: read progress, drive reconcile

- [x] 5.1 In `ScribeQuestWatcher.ScanProgressionFramework` (player-scoped), read each objective
      tree's `GetInt("progress", 0)` alongside the existing `GetString("status")`, extending
      `pfObjectiveStatus`'s per-objective storage (or a parallel dictionary) to carry the numeric
      progress. Verify `dotnet build src/Mod` succeeds.
- [x] 5.2 In `ScanProgressionFrameworkServerQuests` (server-scoped), same extension for the reflected
      tree. Verify `dotnet build src/Mod` succeeds and the existing self-disabling try/catch still
      wraps this read (a malformed progress value must disable only progress reads, not status
      detection — per the `quest-auto-detect` delta's fail-safe scenario).
- [x] 5.3 When a Quest Link for a Progression Framework quest is accepted (existing `onAccepted`
      callback path), call `ReconcileQuestObjectives(createMissing: true)` once, seeding from the
      catalog's objective defs. Verify by unit/integration test that accepting a quest with N
      objectives produces N `QuestObjective` children.
- [x] 5.4 On each tick where a linked quest's objective progress has changed from its last-seen value,
      call `SetQuestObjectiveProgress` (via the new network message from Task 6) for the matching
      child, mirroring `ScribeDialogBase.TrackerCount.cs`'s old-vs-new comparison so an unchanged count
      sends nothing. Verify `dotnet build src/Mod` succeeds.

## 6. Network: progress write-through

- [x] 6.1 Add `ScribeSetQuestObjectiveProgressMessage` (DocId, TaskId, Quantity — mirrors
      `ScribeSetTrackerQuantityMessage`'s shape) and register it in `ScribeModSystem.cs`
      (`RegisterMessageType` + `SetMessageHandler`). Verify `dotnet build src/Mod` succeeds.
- [x] 6.2 Add `IScribeDocumentHost.SetQuestObjectiveProgressFromReader(Guid taskId, int
      currentQuantity)` and implement it in `NotebookHost`, `BlockEntityScribeWritingStation`, and the
      Tablet host — each gated on `IsQuestObjective` (not `IsTracker`), calling
      `SetQuestObjectiveProgress` + `Flush()`/persist on real change only, mirroring
      `SetTrackerCurrentQuantityFromReader`'s shape per host. Verify `dotnet build src/Mod` succeeds.
- [x] 6.3 Add the server-side handler (`OnServerReceivedSetQuestObjectiveProgress`, mirroring
      `OnServerReceivedSetTrackerQuantity`) resolving the doc host and calling the new host method.
      Verify `dotnet build src/Mod` succeeds.

## 7. Rendering

- [x] 7.1 In `ScribeDialogBase.Layout.cs`'s row-item resolution (`ResolveRowItem` or equivalent),
      resolve a `QuestObjective` row's icon/name via `TargetItemCode` when set (reusing the existing
      Tracker path) or the captured `LinkLabel` + generic icon when not (reusing the existing
      guide-page-Link fallback path). Verify `dotnet build src/Mod` succeeds.
- [x] 7.2 Extend `OpenRowLink`'s dispatch to open the Handbook page for an item-backed
      `QuestObjective`, matching the existing `IsTracker || IsCraft` branch. A label-only
      `QuestObjective` does nothing on click (no branch matches — confirm this is the actual fallthrough
      behavior, not an exception). Verify `dotnet build src/Mod` succeeds.
- [x] 7.3 Confirm (read the relevant switch/pattern-match sites — row style, indentation, completion
      checkbox visibility) that a `QuestObjective` row renders with no checkbox (it's not player-
      completable the way a Task is) and at depth 1 like a Craft ingredient. Adjust any site that
      pattern-matches on `Kind is Tracker or Craft` for row-shape purposes (as opposed to counting
      purposes) to also include `QuestObjective` where the visual shape should match a Tracker.

## 8. Verification

- [x] 8.1 `dotnet test` (Core) green, including all new unit tests from Tasks 1-3.
- [x] 8.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 8.3 Manual playtest: accept a Progression Framework quest with multiple objectives (e.g. a
      Seafarer delivery quest) via a Quest Link — confirm objective subtasks appear at depth 1 with
      correct target counts.
- [ ] 8.4 Manual playtest: deliver toward a linked quest's objective — confirm the subtask's live
      count updates to match Progression Framework's own Quest Log, without the player needing to
      carry the item at the moment of the update.
- [ ] 8.5 Manual playtest: confirm carrying (but not delivering) the objective's item does NOT change
      the subtask's displayed count (the carried-inventory engine must not touch it).
- [ ] 8.6 Manual playtest: a non-item objective (e.g. a kill-count) shows a generic icon + readable
      label with a correct live count.
- [ ] 8.7 Manual playtest: delete one objective subtask, then trigger further progress on that same
      objective — confirm it is not recreated.
- [ ] 8.8 Regression check: an ordinary Tracker and a Craft task's ingredient subtasks still count
      from carried inventory exactly as before (no regression from any shared-predicate touch-up in
      Task 7.3).
