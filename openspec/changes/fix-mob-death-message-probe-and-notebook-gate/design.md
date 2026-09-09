## Context

See `proposal.md` - Why. Both fixes live in `OnEntityDeath`/`BuildDeathMessage`
(`src/Mod/ScribeModSystem.History.cs`) and were discovered together while investigating a server
admin's report, but they're logically independent defects:

1. The mob-death flavor pool's *size discovery* formats a template with zero arguments, which
   throws (caught internally by the engine, but logged) whenever the template has placeholders —
   which every `scribe-mob-death-N` entry does, by design.
2. Death/PvP message construction runs unconditionally for every player death, even when no
   Notebook exists anywhere to record the result — unlike the BossKill branch, which already gates
   its own work per-notebook.

## Goals / Non-Goals

**Goals:**
- Make pool-size discovery structurally incapable of triggering the translation service's
  exception/warning path, regardless of how many placeholders the probed template has.
- Preserve the flavor pool's exact existing behavior and content — same lines, same per-death
  random selection, same variant-correct creature naming. This is a fix to *how the pool's size is
  discovered*, not to the feature itself.
- Skip Death/PvP message construction entirely for a death event where no relevant party (victim,
  or killer for a PvP kill) carries a Notebook.
- Add regression coverage that would have caught this: confirms the pool still produces a correct
  line, and confirms no translation-format warning is logged doing so.

**Non-Goals:**
- Hardening `TryLang` or the PvP verb-pool discovery (`scribe-pvp-verb-tool-*` /
  `-damage-*` / `-generic-*`). Those templates carry no placeholders today, so they're not exposed
  to this bug class; changing them preemptively isn't needed to fix the reported issue.
- Any change to `lang/en.json` content, wording, or the count of `scribe-mob-death-N` entries.
- Diagnosing the admin's reported server crash itself, or any third-party mod — tracked separately,
  pending fuller server logs.

## Decisions

**1. Replace the zero-arg `Lang.Get(key) != key` pool-size probe with
`Vintagestory.API.Config.Lang.HasTranslation(key, findWildcarded: false, logErrors: false)`.**
`HasTranslation` resolves to a plain `entryCache.ContainsKey` lookup in `TranslationService` — it
never calls `string.Format`, so it's structurally incapable of reaching `TryFormat`'s
exception/warning path, no matter how many placeholders the target template has. `findWildcarded:
false` matches the exact-key intent of the original check; `logErrors: false` avoids a
"Lang key not found" debug line firing on the one expected miss that terminates the loop.
- *Alternative considered:* wrap the existing zero-arg `Lang.Get` call in a local try/catch to
  swallow the exception ourselves. Rejected — the throw would still happen (and .NET exceptions
  aren't free even when caught), and it leaves the door open for a future edit to "helpfully"
  remove the catch and reintroduce the log spam. `HasTranslation` prevents the exception from ever
  being raised in the first place.

**2. Cache the discovered pool size once, behind one shared helper used by both call sites.**
`ScribeModSystem.DevTools.cs`'s `SeedMobDeathMessage` (the dev-content seeder) has its own
independent copy of the identical zero-arg probe loop — so this bug currently exists in two places.
Both `BuildDeathMessage` and `SeedMobDeathMessage` move onto one shared helper (e.g. a lazily-
initialized field on the `ScribeModSystem` partial class) that discovers the pool size once via
`HasTranslation` and caches it for the life of the running server. The pool is static shipped
content — nothing invalidates it mid-session.
- *Alternative considered:* fix each call site's loop independently in place. Rejected — that's
  exactly how the bug ended up duplicated the first time (one path copied from the other); a shared
  helper makes a future re-divergence structurally harder, not just less likely.
- *Alternative considered:* leave it re-probed every time, now that `HasTranslation` makes each
  probe cheap. Rejected as pointless repeated work for a value that never changes.

**3. Materialize the victim's (and, for a PvP kill, the killer's) carried-notebook list *before*
building any message, and gate message construction on "at least one of these lists is
non-empty."** Reuse those same materialized lists for the later write loop, rather than
re-walking the victim's inventory a second time. This mirrors the pattern the BossKill branch
already uses correctly (its `Lang.Get` call lives *inside* the per-notebook loop).
- *Alternative considered:* leave message construction unconditional, relying on fix #1 to make it
  cheap enough not to matter. Rejected — the concern raised was specifically that this work runs
  "all the time" regardless of relevance, independent of how cheap any one call is; gating is the
  direct fix for that, and it also removes a redundant inventory walk.

**4. Leave `TryLang`, the PvP verb-pool discovery, and all `lang/en.json` content unchanged** (see
Non-Goals). Preemptively rewriting code paths that aren't exposed to this bug today would be the
kind of over-engineering this proposal is explicitly trying to avoid.

## Risks / Trade-offs

- [Risk] A future dev adds a new existence-probed key elsewhere by copying the *old*
  `Lang.Get(key) != key` pattern instead of `HasTranslation`, reintroducing this bug class at a new
  call site. → Mitigation: leave a short comment at the fixed call site explaining why
  `HasTranslation` (not `Get`) is required there.
- [Risk] `OnEntityDeath`'s gating logic needs a live `sapi`/`World.Rand` to exercise end-to-end,
  which `src/Core`'s plain unit tests can't provide (Core has no VS API reference). → Mitigation:
  cover the pool-size discovery fix and flavor-line substitution at the Mod-layer testing tier this
  project already uses for VS-API-dependent code (the local Atlas integration suite), rather than
  inventing a `sapi` mock that doesn't match project convention.
- [Non-risk, noted for reviewers] Moving `FindAllCarriedNotebookRecords(sp)` earlier in the method
  doesn't change *when* it runs relative to game state — it's still the same synchronous
  `OnEntityDeath` call, with no intervening tick between the old and new call sites.

## Migration Plan

None needed. Same-version bugfix, no data/schema change, no player-facing migration — ships in the
next Scribe release.
