## Why

VS Quest-based quests (e.g. VS Village's "Shivers from Another World") get no acceptance-criteria
detail today: a VS Quest Quest Link shows only the quest's flavor title/description (e.g. "The
woods simply aren't safe anymore...") with no indication of what's actually required (kill 50
Drifters, in that example). Progression Framework Quest Links already get live-updating
`QuestObjective` subtasks (`quest-objective-task`) because PF exposes per-objective progress over
`WatchedAttributes`; VS Quest exposes no equivalent live signal for gather objectives at all, and
only a best-effort, dialog-open-only reflection read for kill/place/break (`quest-auto-detect`'s
"Progress mirroring covers kill/place/break objectives, not gather" requirement). But VS Quest's
static `config/quests/*.json` catalog — which Scribe already reads once at Quest Link creation
time — DOES carry every objective's required count and matching codes. That's enough to show a
one-time, non-live "what you need to do" line, closing most of the usefulness gap without needing
any new live-detection machinery.

Separately, the user asked whether a VS Quest Quest Link's "follow" action (activating it) could
open something useful, the way a Progression Framework Quest Link opens that mod's Ledger. Reading
`vsquest`'s own MIT source (`reference/QuestsInvestigations/vsquest-src/`) confirms this is not
possible: `VsQuest.QuestSelectGui` — the only quest-detail dialog vsquest ships — is only ever
opened by `EntityBehaviorQuestGiver.SendQuestInfoMessageToClient`, triggered exclusively by a
sneak-interact on the specific giver entity or a dialogue-tree trigger inside an active
conversation with it. There is no standalone quest journal, hotkey, or chat command anywhere in
the source. This proposal documents that as a confirmed, permanent limitation rather than
attempting a workaround.

## What Changes

- At VS Quest Quest Link creation time, generate a static criteria subtask (or subtasks) derived
  from the quest's catalog objective lists (kill/gather/block-place/block-break — including gather,
  which today's live-progress path deliberately excludes), each showing the required count and a
  human-readable label resolved from the objective's matching codes.
- These subtasks are generated once, at creation, and never reconciled or updated afterward — VS
  Quest has no live per-objective signal outside an open quest dialog, so this is explicitly a
  static snapshot, not a tracker. The player can freely edit or delete them like any other subtask.
- Reuse the existing `QuestObjective` block kind and target-count model (`quest-objective-task`),
  while rendering VS Quest's static children as ordinary task text with a dedicated objective icon
  rather than Quest Link styling. A resolved concrete item keeps its inventory icon and acts as a
  Handbook link. `CurrentQuantity` is left at 0 since there is nothing live to report.
- **Non-Goal (confirmed, not attempted):** making a VS Quest Quest Link's "follow"/activate action
  open anything. No such surface exists in vsquest outside an active quest-giver interaction (see
  Why above). `link-task`'s existing requirement ("no resolvable target does nothing rather than
  throwing") already covers this — no spec change needed for it.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `quest-objective-task`: adds VS Quest as a second source of `QuestObjective` children, generated
  once at Quest Link creation from the static catalog (kill/gather/block-place/block-break
  objectives), with no ongoing reconciliation — distinct from Progression Framework's live,
  repeatedly-reconciled objectives, which are unaffected.

## Impact

- `src/Mod/ScribeQuestCatalog.cs`: extend the catalog reader to also parse `gatherObjectives` (today
  omitted entirely) and expose a human-readable label per objective (resolved from `validCodes`,
  falling back to the raw code).
- `src/Mod/ScribeDialogBase.Editor.cs` (`OnClickAddQuestLink`): add a VS Quest branch alongside the
  existing Progression Framework branch, generating static `QuestObjective` children once via
  `ScribeDocument.ReconcileQuestObjectives(createMissing: true)` (no follow-up progress calls).
- `src/Mod/ScribeModSystem.Quest.cs` and `ScribeAutoLinkQuestMessage.cs`: carry the same static VS
  Quest criteria through accept-time auto-linking, while keeping progress writes gated to Progression
  Framework.
- Quest-objective row rendering: distinguish a static VS Quest child from a live Progression
  Framework child through its parent Quest Link, use normal task coloring plus a dedicated objective
  marker for the static child, and route resolved item activation to the Handbook.
  No persisted data-format change is anticipated.
