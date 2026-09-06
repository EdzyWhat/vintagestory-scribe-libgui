## Why

The Quest Link picker (opened from "Add Quest Link" on any Edit tab) currently lists every quest
in the installed backend's static catalog — `ScribeQuestCatalog.ReadCatalog`/
`ScribeProgressionFrameworkQuestCatalog.ReadCatalog` return the mod's ENTIRE `config/quests/*.json`
catalog, unfiltered. On a content-heavy install (e.g. VS Village's vsquest catalog, or a large
Progression Framework quest pack) this is a massive, mostly-irrelevant list — a 2026-09-05 playtest
note asked for it to be trimmed to quests the player has actually started, or that are currently
live/available, instead of every quest that mod happens to ship.

## What Changes

- The picker's quest list is filtered per entry to "the player has started this quest" — determined
  differently per backend, since the two backends expose player quest-state differently:
  - **Progression Framework**: read directly, same tree `ScribeQuestWatcher.ScanProgressionFramework`
    already polls off the player's own `WatchedAttributes` (`status == "active"` or `"completed"`
    counts as started; a session-cached case, reliable regardless of proximity to any NPC).
  - **vsquest**: accept-state is stamped onto each quest-GIVER entity's synced attributes, not the
    player's — so a quest only becomes knowable as "started" once its giver has been loaded/scanned
    at least once this client session (`ScribeQuestWatcher.ScanGiver`'s existing `_acceptedSeen`/
    `_acceptedFingerprint`). Per 2026-09-05 direction, an entry Scribe hasn't yet confirmed as started
    this session is HIDDEN (not shown-by-default) — the picker favors a tight, accurate list over a
    noisy one that might include quests the player never touched. This is a known, disclosed
    limitation: a vsquest quest accepted in a prior session won't reappear in the picker until its
    giver is encountered again this session.
- No change to auto-detect, progress mirroring, or the Quest Accept/Completion prompt flow — this
  only changes which entries the manual "Add Quest Link" picker lists.
- If a player has genuinely started nothing yet this session (fresh login, no giver encountered),
  the picker may legitimately show an empty or near-empty quest list — this is accepted as correct
  behavior, not a bug to work around.

## Capabilities

### Modified Capabilities
- `link-task`: the "A Quest Link references an installed quest mod's catalog entry" requirement
  gains a filtering condition — the picker only offers catalog entries the player has been observed
  to have started this session, per the backend-specific detection above, rather than the full
  installed catalog.

## Impact

- `src/Mod/ScribeQuestWatcher.cs`: needs a new query surface exposing "has this quest code been
  observed as started/active this session" per catalog entry (vsquest via `_acceptedSeen`, PF via
  `pfStatusFingerprint`/objective status), for the picker to consult at open time.
- Whatever builds the "Add Quest Link" picker's candidate list (`ScribeAddKindPicker.cs`/
  `ScribeEditorContent.cs` — wherever `ScribeQuestCatalog.ReadCatalog`/
  `ScribeProgressionFrameworkQuestCatalog.ReadCatalog` are currently consumed for picker display):
  filter the returned list through the new watcher query before rendering.
- No `src/Core/` changes (this is purely which entries a Mod-side picker offers).
