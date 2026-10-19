## Context

`ComputeAssignmentTargetPlayers()` (`src/Mod/ScribeDialogBase.ViewSwitching.cs:632-640`) builds
the picker's option list live from `capi.World.AllOnlinePlayers` with no persisted backing — see
proposal.md - Why.

Two existing pieces this design reuses directly:

- **`ScribePlayerLocationStore`** (`src/Core/ScribePlayerLocationStore.cs`): the closest existing
  precedent for a world-scoped, UID-keyed, game-agnostic registry with its own binary
  serialize/`LoadFrom`, wired to a savegame key in `ScribeModSystem.Delivery.cs` (constant
  `PlayerLocationStoreSaveKey = "scribe:playerlocations:v1"`, field `playerLocationStore`,
  instantiated in `ScribeModSystem.cs:551`).
- **The per-player push-on-join pattern**: `OnPlayerNowPlaying` (`ScribeModSystem.
  ServerLifecycle.cs:146-152`) already calls `PushAssignmentsTo`, `PushPinsTo`, `PushTimerTo`, and
  `PushQuestDecisionsTo` for the joining player. Each concept has its own message class registered
  once, in a frozen append-only order, on the mod's single network channel (`ScribeModSystem.
  cs:382-420`, `NetworkChannelName`), with a matching `SetMessageHandler<T>` in `StartClientSide`
  (`ScribeModSystem.cs:468-478`). None of these existing pushes are broadcasts — every one is
  scoped to the joining player's own data.

There is no existing precedent for broadcasting one shared dataset to every online client; this
design introduces the first one (`ScribeKnownPlayersSyncMessage`), justified below.

## Goals / Non-Goals

**Goals:**
- Every player who has ever connected to the world stays selectable in the picker, forever,
  regardless of connection method (direct/LAN/dedicated) or current online state.
- Already-open Assignment Desk dialogs pick up a newly-joined player without requiring a reopen.
- Alphabetical ordering and last-assigned-target defaulting, per the new spec's requirements.

**Non-Goals:**
- Pruning the registry (e.g. removing a player who will never return). Matches
  `ScribePlayerLocationStore`'s own unbounded-growth precedent; world player counts are small
  enough that this isn't a real concern.
- Detecting/reconciling a player's display-name change while they're offline. The registry only
  refreshes a name on that player's next join — acceptable per the spec's "most recently observed
  display name" wording.
- Any change to the Hybrid delivery-mode range-check math itself (already implemented). This
  change only makes offline targets reachable in the picker in the first place.

## Decisions

**1. New Core class `ScribeKnownPlayersStore`, modeled directly on `ScribePlayerLocationStore`.**
`Dictionary<string playerUid, string displayName>`, with `Upsert(uid, name)`, `Snapshot()` (all
entries), and the same `Serialize()`/`LoadFrom(byte[]?)` binary shape (magic bytes, version byte,
count, then uid/name pairs) — game-agnostic, no VS API reference, matching `src/Core/`'s hard
constraint. Wired into `ScribeModSystem` exactly like `playerLocationStore`: a new savegame key
(`"scribe:knownplayers:v1"`), instantiated alongside it, saved/loaded at the same lifecycle points.

*Alternative considered*: extend `ScribePlayerLocationStore` itself to also carry names. Rejected
— it's positional data for the Hybrid range check, a distinct concern from "who is a known
player," and the two already have independent lifecycles (location updates only on disconnect;
the roster needs to update on join).

**2. Populate on `OnPlayerNowPlaying`, not on disconnect.** The join event fires uniformly
regardless of how the player connected (direct join, singleplayer-opened-to-LAN, or dedicated
server — confirmed no VS-API-level distinction exists between these paths for this event), so
upserting `(uid, currentName)` there — right alongside the existing `PushAssignmentsTo`/
`PushPinsTo`/etc. calls — captures every player the moment they're confirmed present, including
a LAN guest who later disconnects mid-session (whereas disconnect-only capture would miss a crash
or force-quit).

**3. Broadcast the full roster to every online client whenever it changes, as a new
`ScribeKnownPlayersSyncMessage`.** On each `OnPlayerNowPlaying` upsert, after saving, iterate
`sapi.World.AllOnlinePlayers.OfType<IServerPlayer>()` (the existing all-online-iteration pattern
already used elsewhere, e.g. `ScribeModSystem.History.cs:166`/`408`) and send each of them the
updated full snapshot. This is the first broadcast-shaped message on the channel — appended to the
end of the frozen registration order per the existing convention — but it's justified: this is
genuinely shared, non-per-player data, and the spec's "no reopen required" scenario means every
already-open dialog must observe the addition live, not just the new joiner's own push.

*Alternative considered*: piggyback the roster only on each player's own join-push (option (b)
from the sync-pattern precedent research). Rejected — it would never inform players who are
already online when someone else joins, failing the "no reopen required" scenario outright.

*Alternative considered*: send only the delta (the one new/updated entry) instead of a full
snapshot. Rejected for now — rosters are small (a handful of players per world), so a full
snapshot keeps the client-side merge logic (decision 4) simple with negligible bandwidth cost.

**4. Client caches the synced snapshot; `ComputeAssignmentTargetPlayers()` merges it with
`AllOnlinePlayers` at render time, sorting fresh each call.** The client stores the latest
`ScribeKnownPlayersSyncMessage` snapshot in a field (mirroring how `MySentAssignments` is cached).
`ComputeAssignmentTargetPlayers()` unions that cache with `capi.World.AllOnlinePlayers` (an online
entry's live name wins over a possibly-stale cached one for the same uid), then sorts the merged
list case-insensitively by each entry's underlying player name — computed before the self-
assignment label substitution, so the local player's distinctly-labeled entry still sorts by their
real name. Sorting at read time (rather than maintaining sort order in the store) means the Core
store can stay a plain `Dictionary` with no ordering concerns of its own.

**5. Last-assigned-target default: a new client-local `ScribePlayerSettings.LastAssignmentTargetUid`
string preference** (default `""`), following the exact pattern of existing client-local
preferences like `PreferredTimerMode` — never server-synced, normalized/defaulted on load. Set
optimistically on the client the moment a Send succeeds. The picker's default-resolution reads
this value: if it matches an entry in the current merged+sorted list, that entry is preselected;
otherwise (no history, or that player has since vanished from every source — which shouldn't
happen given decision 1's "everyone who ever joined stays in the registry," but is handled
defensively) it falls back to the first alphabetical entry.

## Risks / Trade-offs

- **[Risk]** Broadcasting the full roster on every join is O(online-players) sends per join.
  → **Mitigation**: rosters are small (a handful of concurrent players in this mod's target
  worlds); the message payload is just uid+name pairs, same order of magnitude as the existing
  per-player pushes already sent on every join.
- **[Risk]** A registry that only ever grows could, over a very long-lived world with heavy
  server population turnover, accumulate many stale entries. → **Mitigation**: out of scope per
  Non-Goals — `ScribePlayerLocationStore` already accepts the same trade-off, and pruning can be
  proposed later if it ever becomes a real problem.
- **[Risk]** A player's display name changes while offline; the registry shows their old name
  until they next join. → **Mitigation**: acceptable per spec wording ("most recently observed");
  VS player name changes are rare and self-correct on the player's next join.

## Migration Plan

Additive only: one new savegame key (absent on old saves → empty registry, same
graceful-empty-on-missing-key pattern as `ScribePlayerLocationStore.LoadFrom(null)`), one new
appended network message type, and one new default-valued `ScribePlayerSettings` field (absent in
an old client config JSON → the property initializer's default, per that class's existing
"new preferences append as new properties" contract). No existing save data, message, or config
key changes shape. No rollback concerns beyond the normal "revert the commit" path.
