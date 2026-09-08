## 1. Catalog: parse gather objectives and derive per-objective labels

- [ ] 1.1 Add `gatherObjectives` to `ScribeQuestCatalog.RawQuest` (mirroring `killObjectives`
  etc.) and include it in `BuildObjectives`/a parallel list, tagged with a new
  `ScribeQuestObjectiveKind.Gather`. Verify: `dotnet build src/Mod` succeeds; a manual read of a
  quest JSON with `gatherObjectives` shows it present in the parsed entry.
- [ ] 1.2 Add a `scribe:scribe-questobjective-gather` lang key (mirroring the existing kill/
  block-place/block-break keys) for the generic-label fallback. Verify: key present in
  `assets/scribe/lang/en.json`.
- [ ] 1.3 Add a label-resolution helper that, given an objective's `ValidCodes`, returns a
  resolved single item/block/entity code + display name when `ValidCodes` has exactly one
  non-wildcard entry (item registry for Gather, block registry for BlockPlace/BlockBreak, entity
  registry for Kill), or `null` when it doesn't (wildcard or multiple candidates). Verify: unit
  test (or manual check) confirms a single exact code resolves and a wildcard/multi-code list
  returns null.
- [ ] 1.4 Add a helper that formats a generic fallback label as `"{KindLabel} {demand}"` using the
  same lang keys `FormatProgress` already uses (plus the new gather key from 1.2). Verify: manual
  check against a sample objective of each kind.

## 2. Generate static children on VS Quest Quest Link creation

- [ ] 2.1 In `OnClickAddQuestLink` (`ScribeDialogBase.Editor.cs`), add an `else if (entry.Source
  == ScribeQuestSource.VsQuest)` branch alongside the existing Progression Framework branch: for
  each of the quest's kill/gather/block-place/block-break objectives, call
  `ReconcileQuestObjectives(parentTaskId, ..., createMissing: true)` once, passing each
  objective's resolved item code (task 1.3) or `null`, its resolved/fallback label (task 1.4),
  and its demand as the target. Verify: `dotnet build src/Mod` succeeds.
- [ ] 2.2 Confirm no `SetQuestObjectiveProgress` (or any other reconciliation) call is ever made
  for a VS-Quest-sourced parent anywhere in the codebase — the one call in 2.1 is the only touch
  point. Verify: `grep` for `ReconcileQuestObjectives`/`SetQuestObjectiveProgress` call sites and
  confirm none are reachable for `ScribeQuestSource.VsQuest`.
- [ ] 2.3 Confirm a quest with zero kill/gather/block-place/block-break objectives (e.g.
  action-objectives-only) generates no children and does not error. Verify: manual read-through
  of the loop (an empty objectives list produces zero `ReconcileQuestObjectives` calls).

## 3. Verification

- [ ] 3.1 `dotnet test tests/Core.Tests` passes (this change touches Mod-layer files only,
  confirm the Core suite is unaffected).
- [ ] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 3.3 Manual playtest: with vsquest + VS Village installed, create a Quest Link for "Shivers
  from another world" (or another kill-objective quest) — confirm a static subtask appears
  showing the required kill count, and that it does NOT change as the player makes progress.
- [ ] 3.4 Manual playtest: create a Quest Link for a quest with a gather objective — confirm a
  static subtask appears for it too (previously impossible, since gather has no live tracking at
  all).
- [ ] 3.5 Manual playtest: create a Quest Link for a quest whose objective resolves to a single
  concrete item (e.g. a specific gather item) — confirm the subtask shows the real item icon and
  name, not a generic icon.
- [ ] 3.6 Regression check: create a Progression Framework Quest Link — confirm its objective
  subtasks still generate and still live-update exactly as before (this change adds a new branch,
  it does not touch the PF branch).

## 4. Spec sync and verification

- [ ] 4.1 Confirm `specs/quest-objective-task/spec.md`'s new requirement ("VS Quest Quest Links
  get one-shot static criteria subtasks") matches the implemented behavior. Verify: `openspec
  validate add-vsquest-criteria-subtask --strict` passes.
