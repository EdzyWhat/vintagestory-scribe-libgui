## Why

PvP deaths and kills are not attributed in the notebook History. When a notebook-holder is
killed by another player, the Death entry reads only "`<name>` died." with no killer, and the
killer's notebook records no PvpKill entry at all. Root cause: both code paths read
`DamageSource.SourceEntity`, which the VS API documents as **null for melee damage** — the
common PvP case. The correct accessor is `GetCauseEntity()` (`CauseEntity ?? SourceEntity`),
which resolves the attacker for both melee and projectile damage. A second gap compounds it:
vanilla ships no `deathmsg-player-*` template, so even with the killer resolved there is no
message string that names both victim and killer.

## What Changes

- Resolve the attacking entity via `DamageSource.GetCauseEntity()` instead of `SourceEntity` in
  both the death-message reconstruction and the PvpKill-detection paths, so melee kills are
  attributed (today only projectile kills would have been). This matches vanilla's own
  `GetDeathMessage`, which uses `GetCauseEntity()`.
- Build a **weapon-aware, flavored PvP kill/death message** via a 3-tier resolution, so the entry
  reads e.g. "Raptor shot Junkmuffin" (bow) or "Raptor slashed Junkmuffin" (sword) rather than a
  flat "died":
  1. **Weapon category** — the killer's held-item `Collectible.Tool` (`EnumTool`: Bow/Sword/Spear/
     Club/Firearm/Crossbow/…) → a category verb. This is the accurate tier for vanilla, and it
     supports modded weapons (firearms, crossbows, combat-overhaul) out of the box *if* they tag
     their items with a `tool`.
  2. **Damage type** — when `Tool` is null/unmapped, fall back to `DamageSource.Type`
     (Slashing/Piercing/Blunt) → a verb. Catches modded weapons that set a damage type but no tool.
  3. **Generic pool** — when neither signal is usable, pick from a small pool of neutral kill verbs
     with no immediate repeat, for flavor/distinction.
- Store all verbs and message templates as mod-owned lang keys (victim + killer args) so wording is
  editable without a code change.
- **Flavor non-PvP creature deaths too.** Vanilla ships `deathmsg-<creature>` keys for almost no
  creatures (only the surface drifter; nothing for nightmare drifters, bears, wolves, …), so those
  kills fell through to a flat "died." Name the creature via the entity's own
  `GetPrefixAndCreatureName()` (always the correct variant, article baked in — "a nightmare drifter",
  "a brown bear") and pick a random line from a mod-owned mob-death flavor pool. Keep vanilla
  `deathmsg-<cause>-<N>` reconstruction only for the environmental (fall/fire/hunger) branch.
- **Fix a doubled-name render** and **raise retention.** Auto-narrated combat entries now carry the
  whole sentence in `Detail` with an empty `ActorName` (matching the BossKill convention — the
  History row prepends "ActorName — " otherwise, printing the victim's name twice). Retention caps
  rise so a notebook keeps a longer chronicle: `MaxDeaths`/`MaxPvpKills` 10 → 30, `MaxBossKills`
  10 → 20.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `notebook-history`: the Death and PvpKill requirements are refined so that (a) attacker
  resolution uses cause-entity semantics that cover melee, (b) a PvP death's recorded message
  attributes the killer with a weapon-aware, flavored verb (tiered: weapon category → damage type →
  generic pool) rather than falling back to an unattributed "died" string, and (c) a creature
  (non-PvP) death names the correct creature variant with a flavored line rather than "died."

## Impact

- Code: `src/Mod/ScribeModSystem.cs` — `OnEntityDeath` (PvpKill block + empty-`ActorName` combat
  entries), `BuildDeathMessage` (attacker lookup + tiered PvP verb resolution + no-repeat generic
  pool cursor + mob-death flavor pool with `GetPrefixAndCreatureName`), and the demo seed
  (`SeedHistory`/`SeedPvpMessage`/`SeedMobDeathMessage`).
- Assets: `src/Mod/assets/scribe/lang/en.json` — PvP verb keys (per-`EnumTool` category,
  per-`EnumDamageType`, generic pool), the victim+killer message template, and the mob-death
  flavor pool.
- Core: `src/Core/HistoryStore.cs` — retention-cap constants only (`MaxDeaths`/`MaxPvpKills` →
  30, `MaxBossKills` → 20). No event-kind or save-format change (`Death`/`PvpKill` already exist);
  no new dependencies. Unblocks `notebook-history-tab` playtest tasks 9.4 and 9.5.
