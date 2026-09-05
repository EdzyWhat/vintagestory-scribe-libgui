## Why

Progression Framework quests come in two scopes: player-scoped (already supported by
`add-progression-framework-quest-support`) and server-scoped — one shared quest instance that
every player on the world contributes to together (e.g. Seafarer's `dawnmarie-plantation`).
Server-scoped quests are currently excluded entirely from Scribe's Progression Framework catalog
(`ScribeProgressionFrameworkQuestCatalog.ReadCatalog` skips any entry with `"scope": "server"`),
so a player who is actively delivering goods toward one of these has no way to see or track it in
Scribe at all — not even manually. Investigation while fixing `fix-quest-catalog-domain-scoping`
surfaced that Progression Framework's own data model has no per-player "joined" concept for these
quests: there is exactly one shared status per quest code (`QuestSystem.serverQuestState`,
mirrored to clients as `clientServerQuestState`), and it flips from unavailable to `"active"` the
first time any player triggers `QuestSystem.StartQuest` for it (typically by talking to the
quest-giver NPC). That shared transition is the same kind of real, engagement-triggered moment
that VS Quest's and Progression Framework's player-scoped accept already hook into — just scoped
to the whole world instead of to one player. This change extends detection and linking to cover
it on those terms, rather than inventing a per-player "joined" concept the source mod doesn't have.

## What Changes

- The Progression Framework catalog reader stops excluding `"scope": "server"` quests; they
  appear in the Quest Link picker like any other cataloged quest.
- `ScribeQuestWatcher` gains a second, independently-gated Progression Framework detection path
  for server-scoped quests: it watches the shared catalog-known quest codes for a transition from
  unavailable/inactive to `"active"`, and separately for a transition to `"completed"`, firing the
  same Accept/Completion policy-driven chat message + HUD prompt flow already used for
  player-scoped quests.
- A server-scoped Quest Link's live progress mirrors the same shared state every player sees —
  linking it doesn't create a personal copy; it points at everyone's shared progress, matching
  what the Progression Framework Quest Log (`L`) itself shows.
- Reading the shared, server-synced state requires a new read mechanism: Progression Framework
  does not expose it via `WatchedAttributes` (unlike its player-scoped tree) — it syncs over the
  mod's own private network channel (`progressionframework:questsync`). This change adds a
  best-effort, self-disabling reflection read against the mod's own `QuestSystem` instance
  (`clientServerQuestState`), following the same fail-safe pattern VS Quest's dialog-progress
  reflection already uses, rather than a duck-typed network listener.
- No change to VS Quest handling, to player-scoped Progression Framework detection, or to the
  Accept/Completion policy settings themselves.

## Capabilities

### New Capabilities
(none — this extends the existing `quest-auto-detect` capability)

### Modified Capabilities
- `quest-auto-detect`: adds a third, independently-gated Progression Framework detection path for
  server-scoped (shared) quests, alongside the existing VS Quest and Progression Framework
  player-scoped paths added by `add-progression-framework-quest-support`. That change is still
  in-progress as of this writing; this change's delta is written against its intended end state
  (the current `quest-auto-detect` requirements already reflect it) and assumes it lands first.

## Impact

- `src/Mod/ScribeProgressionFrameworkQuestCatalog.cs`: drop the server-scope exclusion in
  `ReadCatalog`; the existing `ToPickerEntry`/`ScribePfObjectiveDef` shapes are reused as-is.
- `src/Mod/ScribeQuestWatcher.cs`: add the server-scope detection tick path (own catalog lookup,
  own `_seen` dedup, own self-disabling try/catch), and a reflection-based reader for
  `clientServerQuestState`.
- `src/Mod/ScribeModSystem.Quest.cs`: the existing `OnQuestAccepted`/`OnQuestCompleted`/prompt
  plumbing is reused; no server-scope-specific branching needed there, since a shared quest's
  accept/complete events look identical to a player one from that code's perspective.
- No `src/Core/` changes (this stays entirely within the Mod-layer, VS-API-touching adapter).
- No new mod dependency: Progression Framework remains an optional, `IsModEnabled`-gated soft
  dependency, read via the same no-reflection-for-data / reflection-only-for-dialogs pattern
  already established (reflection here targets a ModSystem field, not a compile-time type
  reference).
