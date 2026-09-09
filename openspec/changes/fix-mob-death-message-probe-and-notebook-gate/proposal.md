## Why

Every time a player is killed by a creature, `BuildDeathMessage` discovers the size of the
`scribe-mob-death-N` flavor pool by calling `Lang.Get(key)` with zero format arguments against
templates that contain `{0}`/`{1}` placeholders. `string.Format` throws on each of these 17 probes;
the engine's `TranslationService.TryFormat` catches its own exception and falls back to the raw
string, so this cannot crash the server — but it does write a scary `[Error]` + `[Warning]` pair to
the log for every single one, on every creature-killed-player death, server-wide. A server admin
running a large modpack read this log spam as evidence of a crash and reported it to us. Separately,
this same message-construction work runs unconditionally for every player death, even when neither
participant carries a Notebook — the BossKill path already avoids this by only doing its work inside
the per-notebook loop; the Death/PvpKill path does not.

## What Changes

- Replace the zero-arg `Lang.Get(key)` pool-size probe with an existence check that does not format
  the template, so discovering the pool size can no longer trigger `TryFormat`'s internal
  exception/warning path. The flavor pool itself — its content, its per-death randomization, the
  fact that it produces a variant-correct "was slain by a brown bear"-style sentence — is unchanged
  and must still work exactly as before; this is a fix to how the pool's SIZE is discovered, not to
  the feature.
- `ScribeModSystem.DevTools.cs`'s `SeedMobDeathMessage` (used by the dev-content seeder) has an
  independent copy of the exact same buggy probe loop. Replace both call sites with one shared,
  cached helper so the bug can't exist in two places, or reappear in one after being fixed in the
  other.
- Cache the discovered pool size once (it's static shipped content) instead of re-probing it on
  every single player death or every seed run.
- Hoist a "does the relevant player carry at least one Notebook" check before
  `BuildDeathMessage`/PvP-message construction runs, so that work is skipped entirely when there is
  nowhere to record the result — mirroring the pattern the BossKill branch already uses correctly.
- Add regression coverage that exercises the mob-death-flavor pool end-to-end (confirms it still
  returns a non-empty, correctly-substituted line for a range of pool indices) and confirms the fix
  no longer emits a translation-format warning.

## Capabilities

### Modified Capabilities
- `notebook-history`: adds requirements that (a) discovering the mob-death flavor pool's size must
  not trigger a translation-format exception/warning, and (b) Death/PvpKill message construction
  must not run at all for a death event where the relevant player (victim, or killer for a PvP
  kill) carries no Notebook.

## Impact

- `src/Mod/ScribeModSystem.History.cs` — `BuildDeathMessage`, the mob-death pool-size discovery, and
  `OnEntityDeath`'s ordering of message-building vs. the carried-notebook check.
- `src/Mod/ScribeModSystem.DevTools.cs` — `SeedMobDeathMessage` has its own copy of the same buggy
  probe loop; it moves onto the shared, fixed helper.
- No changes to `src/Core/` (this is Mod-layer code only).
- No changes to `lang/en.json` content — the flavor pool's existing lines and count are preserved.
