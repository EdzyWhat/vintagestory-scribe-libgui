## Why

Scribe already reserves an unused `AssignedToUid` field on every task (`src/Core/ScribeBlock.cs`)
and a placeholder comment on the Scriptorium for a future "Assign & History / Inbox" surface —
the player-to-player assignment capability has been on the roadmap since v1.2 and is the last
major piece of the Scriptorium organization cluster. Separately, a survey of the VS quest-mod
ecosystem (`reference/QuestsInvestigations/`) found that the dominant quest mod (VS Quest) can be
observed read-only with no hard dependency, and players already juggle quest-giver quests
alongside their own Scribe tasks with no way to see them in one place. Both features share the
same underlying need — a task that arrived from outside the player's own hand, with a state the
player can act on — so this change builds one Inbox pipeline that serves both.

## What Changes

- Add a new **Assignment Desk** block (Assignment tab + Inbox tab) as the sole surface for
  creating and sending a task to another player.
- Add a new standalone **Inbox** block (Inbox tab only) as a lightweight receiving-only surface.
- Add an **Inbox-viewing nav button** (no create capability) to the Lectern, Scriptorium, and
  Chalkboard, deep-linking into the same Inbox tab GUI.
- Add a 7-named-state assignment state machine (New/Unaccepted/Accepted/Declined/Cancelled/
  Discarded/Completed — New is a client-visible unseen flag on Unaccepted, not a distinct
  persisted state) with distinct Assigner/Assignee permissions per transition, documented as a
  matrix in `design.md`.
- Add an ambient particle indicator on every Inbox-capable block when the viewing player has an
  unseen assignment nearby.
- Add accepted-task placement rules (currently-held surface → inventory, player picks if
  multiple → Accept disabled if none), a distinct visual marker for accepted tasks across every
  view, and non-editable text for accepted tasks (Delete on an accepted task performs the Discard
  transition rather than a silent, unsynced removal).
- Add a **Quest Link** task type (reads only the static public quest-catalog JSON of an installed
  quest mod — no dependency, no telemetry).
- Add **soft auto-detection** of VS Quest's live dialog state (accept-state and kill/place/break
  objective progress, via Harmony reflection into the mod's own open dialog — never gather-type
  progress, which vsquest itself doesn't track incrementally) with two independent per-player
  policies (Accept, Completion; each Always/Never/Prompt, default Prompt).
- All quest-related UI (Settings rows, handbook docs, the Quest Link picker option) is **hidden
  entirely unless `IsModEnabled("vsquest")`**; a Quest Link degrades to an inert plain Link with
  its captured text if vsquest is later uninstalled.
- **BREAKING (save data)**: `ScribeBlock.AssignedToUid` (a bare string) is replaced by a richer
  assignment-state object (assigner, state, in-game assigned-date). Existing saves have this
  field unset today (reserved, never populated), so no migration path is needed — this is a
  same-cycle field replacement, not a live-data breaking change.

## Capabilities

### New Capabilities
- `assignment-desk-block`: the new Assignment Desk block — its Assignment tab (create/send) and
  Inbox tab, GUI dimensions and widget reuse from the existing Lectern/Scriptorium dialogs.
- `inbox-block`: the new standalone, receive-only Inbox block.
- `assignment-state-machine`: the state matrix, Assigner/Assignee permissions, Completed-as-
  derived-from-done-flag, Delete-triggers-Discard, terminal-state rules, New/unseen-flag
  behavior, and accepted-task placement resolution.
- `inbox-tab`: the shared Inbox tab UI — inline-expand rows (leading chevron, chevron-only
  trigger), state filter/picker, and the per-block ambient particle indicator.
- `quest-auto-detect`: VS-Quest-only soft auto-detection via Harmony reflection, progress
  mirroring, the two Accept/Completion policies, and the `IsModEnabled`-gated visibility rule.

### Modified Capabilities
- `link-task`: adds a Quest Link reference-target namespace, gated on `IsModEnabled("vsquest")`,
  reading only the static public quest catalog.
- `task-note-document`: `ScribeBlock`'s reserved `AssignedToUid` becomes a full assignment-state
  reference (assigner UID, state, in-game assigned-date) instead of a bare UID.
- `lectern-block`: gains the Inbox-viewing nav button and particle indicator.
- `scriptorium-block`: gains the Inbox-viewing nav button and particle indicator; retires its
  previously-reserved Scriptorium-only Assign/History role in favor of the dedicated Assignment
  Desk (`BlockEntityScriptorium.cs`'s existing reservation comment is superseded).
- `chalkboard-block`: gains the Inbox-viewing nav button and particle indicator.
- `settings-tab`: gains two new Quest policy rows (Accept, Completion), visible only when
  vsquest is installed.

## Impact

- **New blocks/items**: Assignment Desk block + block entity + GUI dialog; Inbox block + block
  entity + GUI dialog.
- **`src/Core/`**: `ScribeBlock.AssignedToUid` replaced by a richer assignment-state type; a new
  assignment state enum and transition-validation logic (game-agnostic, no VS API); a new
  `ScribeQuestAcceptPolicy` / `ScribeQuestCompletionPolicy` enum pair alongside the existing
  `ScribeCompletionPolicy`.
- **`src/Mod/`**: two new GUI dialogs (reusing `ScribeRowButton`, `ScribeTrackerCounterText`,
  `ScribeLinkIcon` row primitives); nav-button + particle additions to
  `BlockEntityScribeLectern`/`BlockEntityScriptorium`/the Chalkboard block entity; a new Harmony
  patch/reflection layer scoped to vsquest's dialog class only; Settings dialog additions;
  handbook additions gated on `IsModEnabled("vsquest")`.
- **No new mod dependency.** vsquest support is entirely soft (asset read + reflection), never a
  compiled reference — consistent with the project's "no new mod deps" guardrail (Harmony itself
  already ships with the base game, per prior precedent).
- **Persistence/sync**: follows the existing vanilla-Sign pattern (`ToTreeAttributes`/
  `FromTreeAttributes`, `SendBlockEntityPacket`, `MarkDirty`, server-authoritative) for the new
  blocks and the richer assignment-state field.
- **Out of scope**: Alegacy Quest Framework integration (design-inspiration only, per its
  license's adaptation-contact clause); mirroring gather-objective progress (vsquest itself has
  no incremental counter for it); any two-way write into a quest mod's own state.
