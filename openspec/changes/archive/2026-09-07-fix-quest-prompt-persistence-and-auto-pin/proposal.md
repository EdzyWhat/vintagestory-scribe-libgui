## Why

Quest accept/completion prompts are re-raised on every relog because nothing about a player's
decision on a given quest is ever persisted: `ScribeQuestWatcher`'s dedup sets and
`ScribeModSystem.Quest.cs`'s pending-prompt queue are explicitly session-only, in-memory state
(their own doc-comments say so). Accepting a quest happens to stop the re-prompt only because the
resulting Quest Link makes a later `alreadyLinked` check pass — but that check exists to prevent a
double-link on the server, not to suppress prompts, and it does nothing for a dismissed (not
accepted) quest, which resurfaces every single login. This is a real, player-visible bug, not a
missing feature.

Separately, accepting a quest today never pins the resulting task, even though accepting is the
clearest possible "I want to track this" signal a player can give — and there's no way to change
that behavior, whereas other completion/pin behaviors are already player-configurable per-player
settings.

## What Changes

- Add a durable per-player, **per-world** "quest decision ledger" recording, per quest source
  (vsquest vs Progression Framework) and quest code, whether the player has accepted-via-Scribe or
  explicitly dismissed that quest's accept-prompt, and separately whether they've dismissed its
  completion-prompt. This must be server-authoritative state persisted with the save game and
  synced to its owning player — the same shape as the existing per-player pin store
  (`player-pins`) — NOT a `ScribePlayerSettings` field: that class is explicitly client-local and
  identical across every world a player plays (per its own doc-comment), so a quest code decided
  in one world would wrongly suppress the prompt in an unrelated world if stored there.
- `ScribeQuestWatcher` consults this ledger before enqueueing a prompt: a quest already
  accepted-via-Scribe or already dismissed at its current state is never re-enqueued. A quest
  whose underlying state changes (e.g. progresses from active to completable) is a new,
  independent decision — the ledger entry for the *prior* state does not suppress the *new* one.
- Add a new per-player setting, `AutoPinOnQuestAccept` (bool, default `true`), alongside the
  existing `QuestAcceptPolicy`/`QuestCompletionPolicy` settings.
- **BREAKING (behavior default change)**: accepting a quest via the HUD banner or the center-modal
  prompt now pins the resulting linked task by default, when `AutoPinOnQuestAccept` is enabled.
  Players who dislike this can disable the new setting to restore today's behavior.
- Add the new setting's row to the Settings dialog (`ScribeSettingsContent.cs`), next to the
  existing quest policy rows, hidden under the same "only visible if a quest mod is installed"
  rule `quest-auto-detect` already establishes.

## Capabilities

### New Capabilities
(none — both changes extend existing capabilities' requirements)

### Modified Capabilities
- `quest-auto-detect`: adds a durable per-quest accept/dismiss decision ledger that
  `ScribeQuestWatcher` must consult before re-raising a prompt, replacing today's implicit
  session-only dedup as the source of truth for "don't ask again."
- `player-pins`: adds a new per-player preference (`AutoPinOnQuestAccept`, default on) and a new
  requirement that accepting a quest pins the resulting linked task when that preference is
  enabled.

## Impact

- **Core**: a new small model for the per-player quest decision ledger (quest source + code +
  decision + the quest-state fingerprint the decision was made against), following the existing
  per-player pin store's shape; `ScribePlayerSettings.cs` gets only the new
  `AutoPinOnQuestAccept` bool (client-local, cross-world — a behavior preference, not a per-quest
  record, so it belongs there like `HudShowIcons`/`MuteUiSounds`).
- **Mod**: a new server-side store for the ledger (persisted with the save game, synced to its
  owning player, mirroring the pin store's persistence/sync pattern) plus a new client→server
  network message for recording a dismiss decision (accept already round-trips through
  `SendAutoLinkQuest`/`OnServerReceivedAutoLinkQuest`, so that path just also needs to record its
  ledger entry); `ScribeQuestWatcher.cs` (consult the synced ledger before enqueueing instead of
  its in-memory dedup sets), `ScribeModSystem.Quest.cs` (record the ledger entry on accept and on
  dismiss, and on accept trigger the new auto-pin call gated by the new setting),
  `ScribeModSystem.PinOperations.cs` (the pin call the accept path will invoke),
  `ScribeSettingsContent.cs` (new settings row).
- No new mod dependencies; no `VintagestoryAPI` reference added to `src/Core/`.
