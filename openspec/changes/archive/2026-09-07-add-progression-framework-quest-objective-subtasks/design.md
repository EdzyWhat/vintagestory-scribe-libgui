## Context

See proposal.md - Why. Two implementation facts that shaped this design, found while investigating:

- **`RecomputeTrackers` (`ScribeDialogBase.TrackerCount.cs`) sweeps every block where
  `IsCarriedCountTracked` is true** (currently Tracker or Craft) and overwrites its `CurrentQuantity`
  from the viewer's carried inventory every ~1s and on every carried-slot change. If a quest
  objective's subtask reused the plain `Tracker` kind, this scan would immediately stomp its
  watcher-driven progress back to whatever (if anything) the player happens to be carrying — actively
  wrong for a delivery objective (carrying 27 rope is not the same as having *delivered* 27 rope to an
  NPC) and always wrong for a non-item objective (nothing to carry-count at all).
- **The existing Tracker-quantity write-through path is inconsistently gated.** Core's
  `ScribeDocument.SetTrackerCurrentQuantity` checks `IsCarriedCountTracked` (Tracker or Craft), but the
  Mod-layer `NotebookHost.SetTrackerCurrentQuantityFromReader` independently checks `block.IsTracker`
  (stricter — excludes Craft). Widening either check to also admit a new kind risks touching a path
  already carrying that subtle inconsistency; a parallel, dedicated path avoids the risk entirely (see
  Decisions).

This change also assumes `add-progression-framework-quest-support` and
`add-progression-framework-server-quest-tracking` land (and, ideally, archive) first — its
`quest-auto-detect` delta is written against those changes' intended end-state text, not the
currently-archived VS-Quest-only spec, for the same reason documented in
`add-progression-framework-server-quest-tracking`'s own design.md (memory:
`openspec-archive-order-header-drift` if archive order ends up reversed).

## Goals / Non-Goals

**Goals:**
- A linked Progression Framework quest's objectives appear as real depth-1 subtasks with live
  have/need counts, generated once and reconciled in place thereafter.
- Zero interference with the existing Tracker/Craft carried-inventory engine.

**Non-Goals:**
- VS Quest objective subtasks — VS Quest's own per-objective data shape wasn't investigated here;
  scoped to Progression Framework only, matching this project's existing VS-Quest-vs-PF mutual
  exclusivity in practice.
- Editing a QuestObjective's target/progress by hand — like a Craft ingredient, it is watcher-driven;
  a player CAN delete it (never resurrected, per the reconcile requirement) but not edit its numbers
  meaningfully (the next reconcile pass would just reflect the backend's real state again on the
  status/count side, though a deleted row stays deleted).
- Rescaling an objective's `Required` after creation — Progression Framework quests don't appear to
  change objective requirements post-definition; the reconcile mechanism CAN rescale (mirroring
  Craft's pattern) but nothing in practice is expected to trigger it.

## Decisions

**New `QuestObjective` kind, explicitly excluded from `IsCarriedCountTracked`.** The alternative —
reusing `Tracker` with `TargetItemCode` sometimes null — was rejected because `IsCarriedCountTracked`
is exactly `Kind is Tracker or Craft`; any Tracker-kind row is swept into carried-inventory counting
by construction, and there's no existing per-block "opt out of counting" flag to bolt on without
touching that same check anyway. A dedicated kind makes the exclusion a one-line, obviously-correct
addition (`IsCarriedCountTracked => Kind is Tracker or Craft` stays unchanged; `QuestObjective` is
just never in that set) rather than threading a new "counting mode" concept through an existing kind.

**Reuse `LinkTarget` as the objective's stable match key; reuse `LinkLabel` as the captured
fallback display text.** Both fields already exist on the shared `ScribeBlock` class with
kind-specific meaning elsewhere (`LinkTarget` for a Link's reference, `LinkLabel` for a Link's
captured title) — repurposing them for `QuestObjective` avoids adding two new fields to a class that
already carries several kind-specific optional fields. `TargetItemCode` keeps its existing meaning
(an item to show/resolve) but, for `QuestObjective`, is populated only when the objective itself
resolves to one; `ResolveDisplay`'s existing null-code fallback path (already used for guide-page and
quest-Link targets) covers the label-only case with no new resolution logic.

**A dedicated `ReconcileQuestObjectives` (Core) mirroring `ReconcileCraftIngredients`'s shape, not a
generalization of it.** Matching key differs (objective code via `LinkTarget`, not item code via
`TargetItemCode`), and the two are reconciled from different triggers (Craft: an editor stepper edit;
QuestObjective: a watcher tick). Sharing one generic method would need a matching-key delegate and a
kind parameter threaded through — more indirection than the ~40-line method it would save. Precedent
elsewhere in this codebase (Tracker vs. Craft-ingredient-Tracker are the same kind but different
callers) already favors small, purpose-named methods over one generalized one.

**A dedicated network message + host method for progress writes, not widening the existing Tracker
one.** Given the inconsistent `IsTracker`-vs-`IsCarriedCountTracked` gating already present across
Core and the Mod-layer hosts (see Context), widening either risks an unintended behavior change to
Tracker/Craft. `ScribeSetQuestObjectiveProgressMessage` + `IScribeDocumentHost.
SetQuestObjectiveProgressFromReader(taskId, currentQuantity)` (implemented by `NotebookHost`,
`BlockEntityScribeWritingStation`, and the Tablet host, mirroring the existing trio) is a few lines of
duplication in exchange for zero risk to the existing path.

**Objective children are generated once at Quest Link creation, from the catalog.** The catalog
(`ScribeProgressionFrameworkQuestCatalog`) already carries each quest's objective list
(`ScribePfObjectiveDef`); `OnServerReceivedAutoLinkQuest` (and the manual-picker creation path) calls
`ReconcileQuestObjectives(createMissing: true)` immediately after `AddQuestLink`, seeding
`TargetQuantity` from `Required` and `CurrentQuantity` from whatever the watcher already has cached
for that quest (0 if nothing yet — the next tick's reconcile corrects it). This mirrors Crafting
Task's "generate once on creation, reconcile-in-place afterward, never regenerate wholesale" posture
exactly.

**Delivery-objective item resolution needs re-adding a correctly-typed `items` reader.** The earlier
`RawObjective.Items` field was deleted entirely (fix-quest-catalog-domain-scoping) after its wrong
type (`List<string>`) silently broke every delivery quest's catalog entry. This change re-adds it with
PF's real shape (`QuestItemRequirement`: `{item, quantity, alternates}`, confirmed via decompiled
source) — but reads it only far enough to detect "exactly one item, no alternates" for display
purposes; it does not attempt to model alternates or multi-item delivery as anything richer than a
label-only row (Non-Goal-adjacent: could be revisited later, not blocking).

## Risks / Trade-offs

- **[Risk]** A `QuestObjective` row with no resolvable item shows only a generic icon + label — less
  visually distinct than a real item Tracker. → **Mitigation**: none needed; this already matches how
  a guide-page Link renders today (established, accepted pattern), and most real Seafarer objectives
  in practice are delivery-type (resolvable).
- **[Risk]** Reconciling on every watcher tick (rather than only on a real progress change) could
  churn unnecessarily. → **Mitigation**: mirror the existing `RecomputeTrackers` pattern of comparing
  old vs. new value and only sending/marking dirty on an actual change (already proven at
  `ScribeDialogBase.TrackerCount.cs:178`, `if (have == oldCurrent) continue;`).
- **[Risk]** Introducing a new persisted `ScribeBlockKind` value is a forward-compat question for an
  older client reading a save written by a newer one. → **Mitigation**: none beyond what already
  exists — the binary codec casts the kind byte with no validation today (true since `Craft = 4` was
  added), so this change introduces no new exposure, only extends an already-accepted one.

## Migration Plan

Additive: new kind value, new fields' reuse, new message type, new host method. No existing
persisted data changes shape. A pre-existing flat Quest Link (created before this change ships) gains
its objective children the next time it's reconciled (on next detection tick after the player has it
pinned/open) rather than retroactively on load — acceptable since a flat Link with no children today
still displays and functions exactly as before; the enrichment is additive, not a required migration.
Rollback is a plain revert (existing `QuestObjective` rows become inert unknown-kind data if a future
downgrade ever mattered, per the same forward-compat posture as any other appended kind).

## Open Questions

- Should a `QuestObjective` row be visually distinguished from a Tracker (e.g. a distinct icon
  frame), the way Craft parents are distinguished from Trackers today? Deferred to implementation —
  no spec requirement demands it, and the existing "book glyph for no-item Links" visual vocabulary
  may already be sufficient.
