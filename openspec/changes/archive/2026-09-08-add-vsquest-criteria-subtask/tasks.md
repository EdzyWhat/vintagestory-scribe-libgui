## 1. Catalog: parse gather objectives and derive per-objective labels

- [x] 1.1 Add `gatherObjectives` to `ScribeQuestCatalog.RawQuest` (mirroring `killObjectives`
  etc.) and include it in `BuildObjectives`/a parallel list, tagged with a new
  `ScribeQuestObjectiveKind.Gather`. Verify: `dotnet build src/Mod` succeeds; a manual read of a
  quest JSON with `gatherObjectives` shows it present in the parsed entry.
- [x] 1.2 Add a `scribe:scribe-questobjective-gather` lang key (mirroring the existing kill/
  block-place/block-break keys) for the generic-label fallback. Verify: key present in
  `assets/scribe/lang/en.json`.
- [x] 1.3 Add a label-resolution helper that, given an objective's `ValidCodes`, returns a
  resolved single item/block/entity code + display name when `ValidCodes` has exactly one
  non-wildcard entry (item registry for Gather, block registry for BlockPlace/BlockBreak, entity
  registry for Kill), or `null` when it doesn't (wildcard or multiple candidates). Verify: unit
  test (or manual check) confirms a single exact code resolves and a wildcard/multi-code list
  returns null.
- [x] 1.4 Add a helper that formats a generic fallback label as `"{KindLabel} {demand}"` using the
  same lang keys `FormatProgress` already uses (plus the new gather key from 1.2). Verify: manual
  check against a sample objective of each kind.

## 2. Generate static children on VS Quest Quest Link creation

- [x] 2.1 In `OnClickAddQuestLink` (`ScribeDialogBase.Editor.cs`), add an `else if (entry.Source
  == ScribeQuestSource.VsQuest)` branch alongside the existing Progression Framework branch: for
  each of the quest's kill/gather/block-place/block-break objectives, call
  `ReconcileQuestObjectives(parentTaskId, ..., createMissing: true)` once, passing each
  objective's resolved item code (task 1.3) or `null`, its resolved/fallback label (task 1.4),
  and its demand as the target. Verify: `dotnet build src/Mod` succeeds.
- [x] 2.2 Confirm no `SetQuestObjectiveProgress` call is ever made for a VS-Quest-sourced parent.
  The only reconciliation calls reachable for VS Quest are the one-shot creation calls in the
  manual and accept-time auto-link paths. Verify: inspect every `ReconcileQuestObjectives`/
  `SetQuestObjectiveProgress` call site and confirm the progress paths are gated to Progression
  Framework.
- [x] 2.3 Confirm a quest with zero kill/gather/block-place/block-break objectives (e.g.
  action-objectives-only) generates no children and does not error. Verify: manual read-through
  of the loop (an empty objectives list produces zero `ReconcileQuestObjectives` calls).
- [x] 2.4 Carry the same static VS Quest criteria through accept-time auto-linking by reusing the
  existing objective wire payload. Gate server-side `SetQuestObjectiveProgress` calls to Progression
  Framework so VS Quest children remain at zero and are never updated. Verify: `dotnet build src/Mod`
  succeeds and call-site inspection confirms both manual and auto-link creation routes reconcile the
  static children exactly once.
- [x] 2.5 Classify a `QuestObjective` child as static VS Quest from the reserved
  `vsquest-objective-` stable-key namespace, then use that classification across Read, Editor,
  Pinned, and HUD rendering: generic static objectives get ordinary task text color plus the
  dedicated `scribeobjective` bullseye marker, while exact-item objectives keep their inventory icon
  and activate the item's Handbook entry. Generic static objectives remain non-interactive;
  Progression Framework rendering and activation remain unchanged. Verify with a build and focused
  renderer/click-path inspection.
- [x] 2.6 Align a generic static VS Quest objective's Editor drag handle with its bullseye marker and
  label using the ordinary item-row vertical geometry. Verify with a build and focused inspection
  that the content and grip consume the same static-objective visual band; task 3.7 owns the in-game
  appearance check.
- [x] 2.7 Preserve the ordinary pinned-row highlight for generic static VS Quest objectives in Editor
  view. Verify with a build and focused inspection that the Editor row-fill condition includes the
  static-objective variant; task 3.7 owns the in-game pin/highlight check.

## 3. Verification

- [x] 3.1 `dotnet test tests/Core.Tests` passes (this change touches Mod-layer files only,
  confirm the Core suite is unaffected).
- [x] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [x] 3.3 Manual playtest: with vsquest + VS Village installed, create a Quest Link for "Shivers
  - Confirmed 2026-09-08: TESTING.md `000000b0` "(no note)" (submission 2026-09-08T08-55-42)
  from another world" (or another kill-objective quest) — confirm a static subtask appears
  showing the required kill count, and that it does NOT change as the player makes progress.
- [x] 3.4 Manual playtest: create a Quest Link for a quest with a gather objective — confirm a
  - Confirmed 2026-09-08: TESTING.md `000000b7` "(no note)" (submission 2026-09-08T08-55-42)
  static subtask appears for it too (previously impossible, since gather has no live tracking at
  all).
- [x] 3.5 Manual playtest: create a Quest Link for a quest whose objective resolves to a single
  - Confirmed 2026-09-08: TESTING.md `000000b8` "(no note)" (submission 2026-09-08T10-23-43)
  concrete item (e.g. a specific gather item) — confirm the subtask shows the real item icon and
  name, and activating it opens that item's Handbook entry.
- [x] 3.6 Regression check: create a Progression Framework Quest Link — confirm its objective
  - Confirmed 2026-09-08: TESTING.md `000000b9` "(no note)" (submission 2026-09-08T11-20-24)
  subtasks still generate and still live-update exactly as before (this change adds a new branch,
  it does not touch the PF branch).
- [x] 3.7 Manual playtest: inspect a wildcard or multi-code VS Quest objective on Read, Editor,
  - Confirmed 2026-09-08: TESTING.md `000000ba` "(no note)" (submission 2026-09-08T11-20-24)
  Pinned, and HUD views — confirm it uses normal task coloring and the dedicated objective marker,
  does not look like a Quest Link, and performs no action when activated. In Editor view, confirm
  the drag handle aligns vertically with the bullseye and label, then pin the child and confirm the
  ordinary pinned-row highlight remains visible while the child also appears on the Pinned tab.

## 4. Spec sync and verification

- [x] 4.1 Confirm `specs/quest-objective-task/spec.md`'s new requirement ("VS Quest Quest Links
  get one-shot static criteria subtasks") matches the implemented behavior. Verify: `openspec
  validate add-vsquest-criteria-subtask --strict` passes.
