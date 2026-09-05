## Context

See proposal.md - Why/What Changes for the root cause. Both catalog readers call
`IAssetManager.GetMany<T>(ILogger, string pathBegins, string domain = null)`. Per that API's own
doc-comment: "If no domain is specified, all domains will be searched." Both readers currently
pass their backend's own modid as `domain`, which is the wrong scope — confirmed against both
frameworks' own quest loaders, which never restrict to their own domain:

- vsquest's `QuestSystem.AssetsLoaded` (`reference/QuestsInvestigations/vsquest-src/src/Systems/QuestSystem.cs:99-108`)
  loops `foreach (var mod in api.ModLoader.Mods)` and calls
  `GetMany<List<Quest>>(logger, "config/quests", mod.Info.ModID)` once per installed mod, unioning
  every result.
- Progression Framework's `QuestSystem.LoadQuests`
  (`reference/ProgressionInvestigations/progressionframework-decompiled/ProgressionFramework.Quests/QuestSystem.cs:310`)
  calls `api.Assets.GetMany("config/quests/", (string)null, true)` — domain explicitly `null`, one
  call.

Both readers already resolve title/description and domain-prefix bare quest codes using each
entry's own asset `location.Domain` (the domain the JSON file actually lives in), not the
backend's modid — that logic is domain-agnostic already and needs no change.

## Goals / Non-Goals

**Goals:**
- Make both catalog readers actually see quest content from real dependent mods (Seafarer for
  Progression Framework, VS Village for VS Quest), matching what each framework's own loader sees.
- Keep the fix minimal and mechanical — a one-line change per reader, no new abstractions.

**Non-Goals:**
- Re-deriving or duplicating either framework's own quest-loading logic beyond what Scribe's
  read-only catalog mirror already does (unchanged from the prior two changes).
- Any change to detection (`ScribeQuestWatcher`), destination resolution, backend attribution
  (`ScribeLinkTarget`), or UI/picker plumbing — all untouched by this fix.

## Decisions

### Decision 1: Pass `domain: null` rather than reproducing vsquest's per-mod loop

Both readers keep their existing single `GetMany<List<RawQuest>>(logger, "config/quests", ...)`
call shape and simply pass `null` for the domain argument (Progression Framework's own approach),
rather than reproducing vsquest's `foreach (var mod in api.ModLoader.Mods)` loop.

**Alternative considered**: mirror vsquest's own per-mod loop exactly. Rejected — functionally
identical to a single `domain: null` call (the API's own doc-comment confirms `null` searches all
domains), and a single call is simpler than iterating `ModLoader.Mods` for no behavioral gain.

### Decision 2: No filtering by declared mod dependency

The unrestricted read does not attempt to verify that a contributing mod actually depends on the
relevant framework (`vsquest`/`progressionframework`) — it simply indexes every `config/quests`
asset found, regardless of which mod's `modinfo.json` dependencies say. This matches exactly what
each framework's own loader already does (neither vsquest's nor Progression Framework's loader
checks contributing mods' declared dependencies either) — Scribe's catalog mirror stays a faithful
read of "whatever the framework itself would load," not a stricter or looser filter.

**Alternative considered**: only include quest files from mods that declare a dependency on the
relevant framework mod. Rejected — adds a dependency-graph check with no precedent in either
framework's own behavior, and would silently diverge from what actually shows up in-game if a mod
author omits the dependency declaration (soft/optional deps are common in this ecosystem).

## Risks / Trade-offs

- **[Risk] An unrelated mod could coincidentally ship assets under a `config/quests` path prefix
  for an unrelated purpose.** → **Mitigation**: this is the same risk both frameworks' own loaders
  already accept by construction (neither restricts by domain either) — Scribe's catalog is a
  read-only mirror of what the framework itself would treat as quest content, so a false-positive
  here would already be a false-positive (or a crash) in the framework's own loading, not a new
  Scribe-specific failure mode. Existing malformed-JSON handling (`catch` → empty list) still
  applies per-file/per-mod via the same `GetMany` deserialization path.
- **[Risk] `vsquest` and `progressionframework` cross-reading each other's catalog if both were
  ever installed simultaneously.** → **Accepted, unchanged from prior design docs**: both
  `add-assignment-and-quest-support` and `add-progression-framework-quest-support` already
  concluded the two mods cannot in practice both be installed at once (confirmed no known modpack
  or use case combines them), so this remains a defensive non-scenario, not a live risk introduced
  by widening the domain search — a `quest:{source}/{code}` Link already records which backend a
  given quest came from regardless of catalog contents (Decision 1 of the prior PF change).
- **[Trade-off] This fix ships without the manual playtest that would have caught it originally**
  (task 8.3 of `add-progression-framework-quest-support` was never actually run before rc.3
  shipped). → **Mitigation**: this change's tasks.md requires the manual test against both
  Seafarer (Progression Framework) and VS Village (VS Quest) before considering this closed — VS
  Village is the concrete stand-in for "a real vsquest-dependent content mod," the same role
  Seafarer plays for Progression Framework.
