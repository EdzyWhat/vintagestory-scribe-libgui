## Why

Scribe's Quest Link support only recognizes VS Quest. Progression Framework — the quest/training
framework behind mods like Seafarer — is a second, independent quest system with its own catalog
and its own per-player progress storage; players who use Progression-Framework-based content have
no way to link or auto-track those quests today. Separately, while investigating this, a real gap
surfaced in the *existing* VS Quest auto-link flow: when a player accepts a quest, the system
silently picks the first carried Scribe document to attach the link to, with no choice offered —
the exact "silently accepts onto whichever book you'd last had open" problem the Assignment
system's Accept flow already solved with a multi-candidate picker. Both quest backends should
share that same fix rather than one of them shipping the older, silent behavior.

## What Changes

- Add a second, independently-gated soft-dependency reader for Progression Framework
  (`progressionframework`), mirroring the existing VS Quest integration's shape: detect via
  `IsModEnabled` only (no compiled reference), read its `config/quests/*.json` catalog for
  Quest Link name/description, and auto-detect accept/progress/completion for **player-scoped**
  quests by reading Progression Framework's own `WatchedAttributes` tree
  (`progressionframework:questlog`) — no reflection required for this backend's common case.
- A Quest Link's captured data unambiguously records which backend mod (VS Quest vs Progression
  Framework) it came from, so auto-detection, policy prompts, and progress mirroring always
  consult the correct backend for a given link — including when both mods are installed at once.
- Progress mirroring surfaces **per-objective** progress (e.g. "3 of 12 delivered") for
  Progression Framework quests with multiple delivery objectives, matching how VS Quest's
  kill/place/break progress already mirrors under an open quest dialog — real Seafarer quest data
  shows this is the common case for this backend, not an edge case.
- **Fix (applies to both backends):** the Quest auto-link accept flow (Accept Policy = Prompt or
  Always) resolves its destination Scribe document using the same multi-candidate picker the
  Assignment system's Accept flow already uses, instead of silently taking the first carried
  document. When Accept Policy is Always and 2+ eligible documents are carried, it falls back to
  a Prompt-style choice for that one link rather than guessing.
- **Following a Progression Framework Quest Link opens that backend's own ledger dialog.**
  Clicking a Quest Link is currently a deliberate no-op for both backends
  (`ScribeItemRef.OpenHandbookPage`'s `IsQuest(code)` early-return). For Progression Framework
  links specifically, replace that no-op with opening PF's own `GuiDialogLedger` (a standalone
  toggle-hotkeyed dialog, `capi.Input.HotKeys["progressionframeworkledger"].Handler`) — no new
  UI, reuses the existing single click-dispatch point that already serves every surface (read
  view, editor, Pin Tab, HUD). VS Quest links keep the current no-op (see Non-Goals).
- **BREAKING (behavioral, not save-compat):** players carrying 2+ eligible Scribe documents will
  now be asked which one receives an auto-linked quest, where previously it was chosen silently.

### Non-Goals (this change)

- Progression Framework **Training** progress (a separate subsystem) — not covered.
- Progression Framework **server-scoped** quests (shared world-state quests with no
  `WatchedAttributes` equivalent, requiring reflection into the framework's own `QuestSystem`
  instance) — deferred; only player-scoped quests are in scope for v1.
- **VS Quest Link follow-to-dialog symmetry.** VS Quest's `QuestSelectGui` has no standalone
  toggle hotkey and is constructed per-NPC-interaction (needs a live quest-giver entity id Scribe
  doesn't currently capture at accept time). Real parity would mean adding and persisting that
  id — a separate, larger change; clicking a VS Quest Link remains a no-op for now.
- Any UI or behavior change for players with neither quest mod installed.
- Almanac (Illuminated or Trades/Callings/Mastery) compatibility — investigated and confirmed
  moot: neither Almanac mod has any code-level reference to `progressionframework`, its
  `questlog` attributes, or any live PF integration; Illuminated's "cross-compatibility" is
  purely a content-authoring convention (a third-party `almanac/guides/*.json` pack could
  describe a PF quest in prose), not a live data path Scribe could conflict with.

## Capabilities

### New Capabilities
(none — this extends two existing capabilities' requirements rather than introducing a new one)

### Modified Capabilities
- `quest-auto-detect`: generalize from "VS Quest only" to a second, independently-gated backend
  (Progression Framework); correct the requirement text that currently describes VS Quest
  accept-state detection as reflection-based (the shipped mechanism reads `WatchedAttributes`
  for accept/complete and reserves reflection for live progress-count mirroring only); add the
  multi-candidate destination-picker requirement for quest auto-link accept (Part B); add
  Progression Framework's Non-Goals (Training, server-scoped quests) explicitly.
- `link-task`: generalize the Quest Link requirement's "currently only VS Quest" wording to admit
  a second backend, and require that a Quest Link's captured data records which backend mod it
  targets.

## Impact

- `src/Mod/ScribeQuestCatalog.cs` — add a second catalog reader for Progression Framework's
  `config/quests/*.json` schema (new DTO; same `IsAvailable`-gated shared-asset-system read).
- `src/Mod/ScribeQuestWatcher.cs` — add a second, independently-gated detection path reading
  `progressionframework:questlog` from `WatchedAttributes`, alongside the existing vsquest
  entity-scan path.
- `src/Mod/ScribeModSystem.Quest.cs` — quest auto-link accept flow gains eligible-candidate
  resolution (mirroring `ScribeDialogBase.ViewSwitching.ComputeAcceptCandidates`) instead of
  `FindNotebookInInventory`'s first-match behavior; dispatches to the correct backend by the
  Quest Link's recorded source.
- `src/Mod/ScribeAutoLinkQuestMessage.cs` — carries the chosen destination inventory/slot,
  mirroring `ScribeAssignmentActionMessage`.
- `src/Core/ScribeLinkTarget.cs` — `quest:` target scheme gains a backend-source tag.
- `src/Core/ScribeQuestPolicy.cs` — no structural change expected (Accept/Completion policies
  stay backend-agnostic); confirm during design.
- `src/Mod/ScribeItemRef.cs` — `OpenHandbookPage`'s `IsQuest(code)` branch dispatches on the
  Link's recorded source: Progression Framework invokes PF's ledger hotkey handler; VS Quest
  stays a no-op (documented Non-Goal, not silently missing).
- No `src/Core/` reference to the Vintage Story API is introduced (backend detection stays in
  `src/Mod/`). No new hard mod dependency — both quest mods remain soft/optional via
  `IsModEnabled`.
