## 1. Resolve the attacker via cause entity

- [x] 1.1 In `ScribeModSystem.BuildDeathMessage`, replace the `dmg.SourceEntity` lookup with
      `dmg.GetCauseEntity()` when deriving the non-PvP death-message cause, so a melee attacker's
      entity is used (today melee falls through to the "died." fallback). Keep the non-entity
      (`dmg.Source`) branch for environmental (fall/fire/hunger) deaths.
- [x] 1.2 In `OnEntityDeath`'s PvpKill block, gate on `dmg.GetCauseEntity() is EntityPlayer`
      (whose `Player` is a different `IServerPlayer` than the victim) instead of `dmg.SourceEntity`,
      so melee kills are attributed. Guard against a null `Player` (killer left/despawned) by
      skipping the PvP branch rather than throwing.

## 2. Weapon-aware PvP kill verb + message

- [x] 2.1 Add mod-owned lang keys to `src/Mod/assets/scribe/lang/en.json`:
      (a) per-weapon-category verbs `pvp-verb-tool-<enumname>` for the `EnumTool` members
      (bow → "pincushioned", crossbow → "bolted", sling/firearm → "shot", sword → "slashed",
      spear/javelin/pike → "impaled", axe/poleaxe → "chopped", club/mace/hammer/warhammer →
      "bashed", knife → "knifed", plus any others chosen);
      (b) per-damage-type verbs `pvp-verb-damage-slashingattack|piercingattack|bluntattack`;
      (c) a generic no-repeat pool `pvp-verb-generic-0..N` (slew, cut down, felled, dispatched,
      bested); (d) the assembly template `pvp-death-message` with args `{0}`=killer, `{1}`=verb,
      `{2}`=victim (e.g. "{0} {1} {2}.").
- [x] 2.2 Add a private verb-resolution helper: given the killer entity + `DamageSource`, return a
      verb by the 3-tier fallback — (1) `killer.RightHandItemSlot?.Itemstack?.Collectible.Tool`
      (`EnumTool?`) → `pvp-verb-tool-<tool>` when the key exists; (2) else `dmg.Type` →
      `pvp-verb-damage-<type>` when it exists; (3) else the generic pool, choosing the index from
      the killer notebook's current PvpKill entry count (no `Random`, no immediate repeat). Use the
      `Lang.Get` key-echo miss check already used in `BuildDeathMessage` to detect "key absent".
- [x] 2.3 In `OnEntityDeath`, when the attacker is a different player, build ONE PvP message from
      `pvp-death-message` (killer, verb, victim) and use it for BOTH the victim's Death entry Detail
      and the killer's PvpKill entry Detail, so they read identically. Confirm both entries come from
      the single shared "attacker is a different player" predicate (one death → up to two entries),
      and that a non-player/self death produces neither and still uses the vanilla reconstruction.

## 3. Flavored mob-death message (killed by a creature)

- [x] 3.1 Add a mod-owned mob-death flavor pool `scribe-mob-death-0..N` to `en.json`
      (`{0}`=victim, `{1}`=creature), incl. "was slain by", "tried to hug", "had a messy accident
      with", "got clipped by", "mauled by", "gored by", etc. Contiguous from -0 (self-sizing probe
      stops at the first gap); translators may ship as few as one line. `{1}` is the creature named
      as an object phrase WITH its indefinite article baked in (see 3.2) — templates must NOT add
      their own "a"/"an".
- [x] 3.2 In `BuildDeathMessage`, when `GetCauseEntity()` is a (non-player) creature, name it via
      `Entity.GetPrefixAndCreatureName()` (always the correct variant — "a nightmare drifter", "a
      brown bear" — where vanilla ships `deathmsg-` keys for almost no creatures) and pick a random
      line from the mob-death pool (`sapi.World.Rand`). Keep vanilla `deathmsg-{cause}-{N}`
      reconstruction only for the environmental (non-entity) branch.

## 4. History-row rendering + retention

- [x] 4.1 Auto-narrated combat entries (Death, PvpKill) carry the whole sentence in `Detail` with an
      EMPTY `ActorName`, matching the BossKill convention — the History row prepends "ActorName — "
      otherwise, which double-printed the victim's name (the sentence already names them).
- [x] 4.2 Raise the `HistoryStore` retention caps: `MaxDeaths` and `MaxPvpKills` 10 → 30,
      `MaxBossKills` 10 → 20 (`MaxStorms` unchanged). Tests reference the constants symbolically.

## 5. Demo seed parity

- [x] 5.1 Rebuild the `/scribe seed` history from the live lang keys so demo content can't drift:
      two PvP entries (bow death + sword kill, both weapon tiers) and two mob deaths (Nightmare
      Drifter + brown bear) via `SeedPvpMessage`/`SeedMobDeathMessage`, all with empty `ActorName`.

## 6. Build + Core suite

- [x] 6.1 `dotnet build src/Mod/Mod.csproj -c Release` compiles clean (no new warnings).
- [x] 6.2 `dotnet test tests/Core.Tests` still passes (the cap-constant bump in 4.2 is the only
      Core change; the cap tests read the constants symbolically, so they still pass).

## 7. In-game verification (multiplayer)

- [ ] 7.1 Player A holds a Notebook; Player B kills A with a MELEE weapon. A's notebook Death entry
      names B as the killer (not "A died."). (Covers notebook-history-tab 9.4.)
- [ ] 7.2 Player A holds a Notebook and kills Player B with a melee weapon. A's notebook gets a
      PvpKill entry naming B. (Covers notebook-history-tab 9.5.)
- [ ] 7.3 Repeat 7.1/7.2 with a RANGED weapon (bow) — confirm the projectile path still works after
      switching to `GetCauseEntity()` AND that the verb reads "pincushioned" (bow weapon-category tier).
- [ ] 7.4 Spot-check verb variety: a sword kill reads "slashed", a spear "impaled". If a combat mod
      is installed, confirm a firearm/crossbow reads "shot" (tool tier) or at least a sensible verb
      (damage-type/generic fallback), never a wrong attribution.
- [ ] 7.5 Player A holding a Notebook dies to a MOB (e.g. a nightmare drifter or bear) — the Death
      entry reads a flavored line naming the correct creature variant ("...by a nightmare drifter."),
      NOT "A died.", and no PvpKill entry is created. Confirm no doubled name in the row.
- [ ] 7.6 Player A holding a Notebook dies to fall / fire / hunger — confirm the Death entry still
      uses the vanilla reconstructed (environmental) message and no PvpKill entry is created.
- [ ] 7.7 `/scribe seed all` on a held Notebook shows the fuller chronicle (drifter death, bow PvP
      death, bear death, boss kill, storm, sword PvP kill) with no doubled names.
