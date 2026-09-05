## Context

See proposal.md - Why. Two more concrete technical facts, confirmed by decompiling the installed
Progression Framework build (`reference/ProgressionInvestigations/progressionframework-decompiled/`)
and the actually-installed `seafarer_0.5.15.zip`:

- `QuestSystem.GetQuestNodeReadonly` resolves a server-scoped quest's status/objectives from one
  shared tree per quest code — server-side `serverQuestState[code]`, client-side its synced mirror
  `clientServerQuestState[code]` — regardless of which player is asking. There is no per-player
  key anywhere in this path.
- That shared tree is NOT synced via `WatchedAttributes` (unlike the player-scoped
  `progressionframework:questlog` tree Scribe already reads). It syncs over Progression
  Framework's own network channel (`progressionframework:questsync`, message types
  `ServerQuestSnapshot`/`ServerQuestUpdate`), refreshed into `clientServerQuestState` by that mod's
  own message handlers, and exposed to other code only as a private field on its `QuestSystem`
  ModSystem instance.

`ScribeQuestWatcher` already has a precedent for reading data that isn't cleanly exposed: VS
Quest's live progress-count mirroring reads a private field (`activeQuests`) off that mod's own
open dialog via `HarmonyLib.AccessTools`, wrapped in try/catch with permanent self-disable on
first failure. This change reuses that exact posture for a different private field on a different
mod's ModSystem instance.

## Goals / Non-Goals

**Goals:**
- Detect a Progression Framework server-scoped quest's shared activation/completion and drive the
  same Accept/Completion policy flow already built for player-scoped quests.
- Let a Quest Link render that shared quest's live objective progress, identically for every
  player who links it.
- Include server-scoped quests in the catalog/picker on the same terms as player-scoped ones.

**Non-Goals:**
- Inventing a per-player "joined" concept the source mod doesn't have (resolved in conversation:
  follow Progression Framework's own model — one shared status, triggered by the first
  interaction).
- A duck-typed network listener on `progressionframework:questsync`. Reflection against the
  already-deserialized `clientServerQuestState` field is simpler, requires no message-shape
  duplication, and matches an existing pattern in this codebase; the network approach is not
  pursued.
- Any UI distinction marking a Link as "shared" vs. "personal" in the row display. Server-scoped
  quests are rare relative to player-scoped ones in real content (Seafarer: 6 of 11), and nothing
  in the existing row rendering needs to change for the progress text to already be correct: it
  reads the same live status either way. A future visual cue is possible but not required here —
  see Open Questions.

## Decisions

**Read mechanism: reflection against `QuestSystem.clientServerQuestState`, not a network
listener.** Progression Framework's `TreeAttribute` values are a real `VintagestoryAPI` type, so
once the field's `Dictionary<string, TreeAttribute>` is obtained via one reflected field read, every
downstream read (`GetString("status")`, `GetTreeAttribute("objectives")`, etc.) is a normal,
non-reflective vanilla API call — the reflection surface is exactly one field access, done once
per tick (cheap) with the result cast to a real, referenceable type. A duck-typed network listener
would need to independently reverse-engineer and maintain `ServerQuestSnapshot`/`ServerQuestUpdate`'s
exact wire shape and would break silently on any change to that shape, with no compile-time or
even reflective signal — strictly worse for a best-effort, self-disabling path than reflecting a
field whose declared type (`Dictionary<string, TreeAttribute>`) is unlikely to change even if its
name does.

**Get the `QuestSystem` instance via `IModLoader.GetModSystem(string fullName)`.** This non-generic
overload (distinct from `GetModSystem<T>()`, which needs a compile-time type) returns the base
`ModSystem` for a fully-qualified type name string — `"ProgressionFramework.Quests.QuestSystem"` —
with no assembly reference. `IsAvailable`'s existing `IsModEnabled(ModId)` check already gates
this; the instance lookup and field reflection both live inside the same try/catch that already
wraps `ScanProgressionFramework`, self-disabling on any failure (missing type, missing field,
unexpected field type) exactly like the existing failure paths.

**Reuse the existing player-scoped detection plumbing rather than parallel dictionaries.**
`ScribeQuestWatcher`'s `pfAcceptedSeen`/`pfCompletedSeen`/`pfObjectiveStatus` and
`TryGetPfObjectiveStatus`/`TryGetPfObjectiveDefs` are keyed purely by quest code, with no
assumption baked in about scope. `ScribeModSystem.Quest.cs`'s `OnQuestAccepted`/`OnQuestCompleted`
and `TryGetQuestProgressText` dispatch on `Source == ScribeQuestSource.ProgressionFramework`, also
scope-agnostic. A new tick method (`ScanProgressionFrameworkServerQuests`, called alongside the
existing `ScanProgressionFramework` from `OnTick`) writes into those same dictionaries and calls
the same `onAccepted`/`onCompleted` callbacks — no changes needed to `ScribeModSystem.Quest.cs` or
to progress-text formatting at all. This is the reason the proposal's Impact section lists no
changes there.

**Catalog entries need an explicit scope flag.** `ScribeProgressionFrameworkQuestEntry` currently
carries no scope information (the reader discards it after filtering). Once server-scoped entries
are no longer filtered out, the watcher needs to know which lookup path (`WatchedAttributes` tree
vs. reflected `clientServerQuestState`) applies to a given cataloged code — so the entry gains an
`IsServerScoped` bool, set from the same `"scope"` field the reader already parses.

**This change assumes `add-progression-framework-quest-support` lands first.** That change is
still in-progress; this change's `quest-auto-detect` delta is written as a further modification on
top of its (not-yet-archived) requirement text rather than the currently-archived, VS-Quest-only
spec, because implementing this change only makes sense once that one's player-scoped detection
already exists to extend. If archive order ends up reversed, reconcile header wording at archive
time per the project's own documented archive-order gotcha (memory: `openspec-archive-order-header-drift`) —
keep the superset body either way.

## Risks / Trade-offs

- **[Risk]** Reflection against a private field is inherently fragile to a Progression Framework
  update renaming or restructuring `clientServerQuestState`. → **Mitigation**: same self-disabling
  try/catch posture already proven for VS Quest's dialog reflection — a failure here disables only
  this one detection path for the session, never crashes, never blocks manual Quest Links, and is
  logged once so it's diagnosable.
- **[Risk]** A shared Link's "activation" can only ever be detected once globally (the first
  player to trigger `StartQuest` for a given code) — a second/third player linking the same quest
  later gets no distinct "you joined" moment of their own, by design (Non-Goal above). → **Mitigation**:
  none needed; this is the agreed-on framing, not a bug. A player who wants to track an
  already-active server-scoped quest can still manually add a Link from the picker at any time
  (catalog inclusion has no gating on current activation status).
- **[Risk]** Two players independently completing objectives toward the same shared quest could
  both trigger `OnQuestCompleted`'s notification within the same tick window if both have it
  pinned. → **Mitigation**: already handled — `FindPinnedQuestLinks` + the existing
  `pfCompletedSeen` per-session dedup means each player's own client only ever notifies once for a
  given code, regardless of how many other players also have it linked.

## Migration Plan

Purely additive: no persisted data changes shape (a Quest Link's stored fields — source, quest
code, title, description — are identical for a server-scoped quest; only the live-read path
differs, and that's never persisted). No player-facing opt-in; server-scoped quests simply start
appearing in the catalog and detection once this ships. Rollback is a plain revert — no data
migration in either direction.

## Open Questions

- Should a server-scoped Link get some small visual cue in the row (e.g. "shared" label) so a
  player isn't surprised that its progress moves without their own action? Deferred — nothing in
  the spec requires it, real content has few server-scoped quests, and it's easy to add later
  without changing detection/read behavior at all.
