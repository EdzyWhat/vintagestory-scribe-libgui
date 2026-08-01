## 1. Resolve the attacker via cause entity

- [ ] 1.1 In `ScribeModSystem.BuildDeathMessage`, replace the `dmg.SourceEntity` lookup with
      `dmg.GetCauseEntity()` when deriving the non-PvP death-message cause, so a melee attacker's
      entity code is used (today melee falls through to the "died." fallback). Keep the non-entity
      (`dmg.Source`) branch for environmental deaths.
- [ ] 1.2 In `OnEntityDeath`'s PvpKill block, gate on `dmg.GetCauseEntity() is EntityPlayer`
      (whose `Player` is a different `IServerPlayer` than the victim) instead of `dmg.SourceEntity`,
      so melee kills are attributed. Guard against a null `Player` (killer left/despawned) by
      skipping the PvP branch rather than throwing.

## 2. Weapon-aware PvP kill verb + message

- [ ] 2.1 Add mod-owned lang keys to `src/Mod/assets/scribe/lang/en.json`:
      (a) per-weapon-category verbs `pvp-verb-tool-<enumname>` for the `EnumTool` members
      (bow → "pincushioned", crossbow → "bolted", sling/firearm → "shot", sword → "slashed",
      spear/javelin/pike → "impaled", axe/poleaxe → "chopped", club/mace/hammer/warhammer →
      "bashed", knife → "knifed", plus any others chosen);
      (b) per-damage-type verbs `pvp-verb-damage-slashingattack|piercingattack|bluntattack`;
      (c) a generic no-repeat pool `pvp-verb-generic-0..N` (slew, cut down, felled, dispatched,
      bested); (d) the assembly template `pvp-death-message` with args `{0}`=killer, `{1}`=verb,
      `{2}`=victim (e.g. "{0} {1} {2}.").
- [ ] 2.2 Add a private verb-resolution helper: given the killer entity + `DamageSource`, return a
      verb by the 3-tier fallback — (1) `killer.RightHandItemSlot?.Itemstack?.Collectible.Tool`
      (`EnumTool?`) → `pvp-verb-tool-<tool>` when the key exists; (2) else `dmg.Type` →
      `pvp-verb-damage-<type>` when it exists; (3) else the generic pool, choosing the index from
      the killer notebook's current PvpKill entry count (no `Random`, no immediate repeat). Use the
      `Lang.Get` key-echo miss check already used in `BuildDeathMessage` to detect "key absent".
- [ ] 2.3 In `OnEntityDeath`, when the attacker is a different player, build ONE PvP message from
      `pvp-death-message` (killer, verb, victim) and use it for BOTH the victim's Death entry Detail
      and the killer's PvpKill entry Detail, so they read identically. Confirm both entries come from
      the single shared "attacker is a different player" predicate (one death → up to two entries),
      and that a non-player/self death produces neither and still uses the vanilla reconstruction.

## 3. Build + Core suite

- [ ] 3.1 `dotnet build src/Mod/Mod.csproj -c Release` compiles clean (no new warnings).
- [ ] 3.2 `dotnet test tests/Core.Tests` still passes (Core untouched — confirms no accidental
      coupling; `Death`/`PvpKill` kinds already exist).

## 4. In-game verification (multiplayer)

- [ ] 4.1 Player A holds a Notebook; Player B kills A with a MELEE weapon. A's notebook Death entry
      names B as the killer (not "A died."). (Covers notebook-history-tab 9.4.)
- [ ] 4.2 Player A holds a Notebook and kills Player B with a melee weapon. A's notebook gets a
      PvpKill entry naming B. (Covers notebook-history-tab 9.5.)
- [ ] 4.3 Repeat 4.1/4.2 with a RANGED weapon (bow) — confirm the projectile path still works after
      switching to `GetCauseEntity()` AND that the verb reads "pincushioned" (bow weapon-category tier).
- [ ] 4.4 Spot-check verb variety: a sword kill reads "slashed", a spear "impaled". If a combat mod
      is installed, confirm a firearm/crossbow reads "shot" (tool tier) or at least a sensible verb
      (damage-type/generic fallback), never a wrong attribution.
- [ ] 4.5 Player A holding a Notebook dies to a mob / fall / fire — confirm the Death entry still
      uses the vanilla reconstructed message and no PvpKill entry is created.
