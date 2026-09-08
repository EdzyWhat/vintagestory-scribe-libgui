## Why

A Progression Framework Quest Link today is a single flat row — the player can see a quest is
active but never *what's left* (e.g. "3/27 Rope"). Progression Framework already syncs a numeric
`"progress"` int per objective (alongside the `"status"` string Scribe already reads), in the same
already-synced trees both player-scoped (`WatchedAttributes`) and server-scoped (reflected
`clientServerQuestState`) detection already uses — no new sync mechanism is needed, only reading one
more field per objective. The missing piece is modeling: Crafting Tasks already solved this exact
shape for a different domain (a depth-0 parent whose owned depth-1 run is reconciled — inserted,
rescaled, never deleted — against a live target/current pair), and quest objectives should mirror it.

## What Changes

- New `ScribeBlockKind.QuestObjective` (appended, never renumbering existing kinds) — a depth-1
  subtask representing one Progression Framework quest objective, living in the Quest Link parent's
  owned run. Carries `TargetQuantity` (the objective's `Required`) and `CurrentQuantity` (PF's live
  `"progress"` int) like a Tracker, but is explicitly **excluded** from carried-inventory counting
  (`IsCarriedCountTracked`) — its count comes from Progression Framework's own server state, never
  from what the player happens to be carrying.
- `TargetItemCode` is set (for a real item icon) only when the objective's definition resolves to a
  single concrete item (a delivery-type objective with no alternates); otherwise the row shows a
  generic icon with a captured readable label — mirroring how a guide-page Link already degrades when
  it has no item to show.
- New `ScribeDocument.ReconcileQuestObjectives(questLinkTaskId, objectives, createMissing)` mirroring
  `ReconcileCraftIngredients`'s owned-run insert/rescale/never-delete shape, matching existing rows by
  the objective's own stable code (not item code — two objectives could share one).
- `ScribeQuestWatcher` reads PF's `"progress"` int (both player- and server-scoped paths) alongside
  the existing `"status"` read, and pushes objective-subtask progress writes through a new
  server-authoritative message (mirroring the existing Tracker-quantity write-through) when a Quest
  Link is pinned/open.
- A Quest Link's objective subtasks are generated once at Link creation (from the catalog's objective
  defs) and their `CurrentQuantity` kept live afterward — never re-derived from scratch on open (same
  posture as Crafting Task's self-heal: create once, reconcile in place, never auto-delete).

## Capabilities

### New Capabilities
- `quest-objective-task`: the `QuestObjective` block kind, its fields, its exclusion from
  carried-inventory tracking, and the reconcile mechanics that generate/update it from a Progression
  Framework quest's objective list.

### Modified Capabilities
- `quest-auto-detect`: extends the existing player-scoped and server-scoped Progression Framework
  detection paths to also read each objective's numeric `"progress"` (not just `"status"`), and to
  drive `quest-objective-task` subtask progress from it.

## Impact

- `src/Core/ScribeBlock.cs`: new `QuestObjective` kind value, `IsQuestObjective`, `IsCarriedCountTracked`
  exclusion.
- `src/Core/ScribeDocument.cs`: `ReconcileQuestObjectives`, `SetQuestObjectiveProgress`.
- `src/Core/ScribeDocumentCodec.cs` + `ScribeDocumentJsonCodec.cs` + `ScribeDocumentTsvCodec.cs`: persist
  the new kind through every codec.
- `src/Mod/ScribeProgressionFrameworkQuestCatalog.cs`: re-add a correctly-typed objective item
  reference (this time matching PF's real `{item, quantity, alternates}` shape) for delivery-type
  objectives only.
- `src/Mod/ScribeQuestWatcher.cs`: read `"progress"`, drive subtask reconcile/progress writes.
- New network message + `IScribeDocumentHost` method (mirroring `ScribeSetTrackerQuantityMessage`) —
  implemented across `NotebookHost`, `BlockEntityScribeWritingStation` (Lectern), and the Tablet host.
- Row rendering (`ScribeDialogBase.Layout.cs`, `ScribeRowWidgets.cs`, `ScribeItemRef.cs`): a
  `QuestObjective` row's icon/name resolution and (when it has a real item) click-to-Handbook, matching
  Tracker's existing rendering path.
