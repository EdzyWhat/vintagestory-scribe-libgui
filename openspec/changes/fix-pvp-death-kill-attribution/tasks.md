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
      bested), plus optional `<key>-participle` passive overrides where the active verb doesn't read
      as a participle (in the shipped set only `pvp-verb-generic-0-participle` = "slain" for "slew");
      (d) TWO assembly templates — `scribe-pvp-death-message` = "{0} was {1} by {2}." (victim-first,
      passive: {0}=victim, {1}=participle, {2}=killer) and `scribe-pvp-kill-message` = "{0} {1} {2}."
      (killer-first, active: {0}=killer, {1}=active verb, {2}=victim).
- [x] 2.2 Add a `ResolvePvpVerbKey` helper: given the killer entity + `DamageSource`, return the verb
      KEY (not a formatted string) by the 3-tier fallback — (1)
      `killer.RightHandItemSlot?.Itemstack?.Collectible.Tool` (`EnumTool?`) → `pvp-verb-tool-<tool>`
      when the key exists; (2) else `dmg.Type` → `pvp-verb-damage-<type>` when it exists; (3) else the
      generic pool, choosing the index from the killer notebook's current PvpKill entry count (no
      `Random`, no immediate repeat). Use the `Lang.Get` key-echo miss check to detect "key absent".
      Add `VerbActive(key)` = `Lang.Get(key)` and `VerbParticiple(key)` = the `<key>-participle`
      override if present, else the active verb.
- [x] 2.3 In `OnEntityDeath`, when the attacker is a different player, resolve the verb key ONCE, then
      build TWO messages from the ONE key: the victim's Death entry Detail from
      `scribe-pvp-death-message` (victim, `VerbParticiple`, killer) and the killer's PvpKill entry
      Detail from `scribe-pvp-kill-message` (killer, `VerbActive`, victim), so each log reads from its
      own owner's perspective. Confirm both entries come from the single shared "attacker is a
      different player" predicate (one death → up to two entries), and that a non-player/self death
      produces neither and still uses the vanilla reconstruction.

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
      two PvP entries (bow death via `SeedPvpDeathMessage` + sword kill via `SeedPvpKillMessage`, both
      weapon tiers and both perspectives) and two mob deaths (Nightmare Drifter + brown bear) via
      `SeedMobDeathMessage`, all with empty `ActorName`.

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
- [ ] 7.8 Carry THREE notebooks (mix of hotbar + backpack) and die to a mob — confirm ALL three
      record the Death entry, not just one. (Bug #3.)
- [ ] 7.9 In a CREATIVE world set to survival, get a PvP kill while holding a notebook in the hotbar,
      then spawn a fresh notebook from the creative tab — confirm the kill IS on your carried notebook
      and the freshly-spawned creative notebook is EMPTY (no phantom past kills). (Bug #1.)
- [ ] 7.10 Die to a PvP attacker with the only notebook in a BACKPACK bag (not the active hotbar
      slot) — confirm the Death entry still records. (Bug #2.)

## 8. Fix notebook-locating scope + fan-out to all carried notebooks

Playtesting surfaced three related bugs, all rooted in `FindNotebookInInventory` walking the WRONG
inventory set and stopping at the first match: (1) in creative mode the killer got no kill record,
yet a newly-spawned creative notebook auto-populated past kills — because the write landed on a
creative-inventory *template* stack (`InventoriesOrdered` includes `creative`, and the notebook is
creative-listed), which is then handed out as future copies; (2) a victim's PvP death only recorded
when the notebook was in the hotbar, not a backpack — again because the first-match walk resolved a
different stack; (3) only 1 of 3 notebooks updated on death (the `return` after the first match).

- [x] 8.1 Replace `FindNotebookInInventory`'s "walk `InventoriesOrdered`, return first match" with a
      `FindCarriedNotebooks` enumerator scoped to the player's real carried inventories via a
      `CarriedInventoryClasses` allow-list (`hotbar`, `backpack`, `character`, `mouse` from
      `GlobalConstants`), yielding a host for EVERY matching notebook. Keep a thin
      `FindNotebookInInventory` = `FindCarriedNotebooks(...).FirstOrDefault()` wrapper for the single-
      target seed command. Excluding `creative` fixes the phantom-populate bug (never mutate a
      template stack); excluding `ground`/`craftinggrid` drops transient staging inventories.
- [x] 8.2 Fan out all four live recorders (`OnEntityDeath` boss-kill loop, victim Death, killer
      PvpKill, `OnStormTick`) over `FindCarriedNotebooks` so every carried notebook is updated, not
      just the first. Materialize the killer's notebooks once (`.ToList()`) so the generic-verb pool
      can index off the first while the PvpKill entry is written to all.
- [x] 8.3 `dotnet build` clean + `dotnet test tests/Core.Tests` green (no Core change; behavior is
      Mod-side).

## 9. Record the one-time PickedUp entry on notebook open

The one-time "picked up" entry (for anyone who isn't the crafter) effectively never fired:
`RecordPickedUpIfNew` runs in `NotebookHost.AttachServerContext`, which is only reached when the
SERVER resolves a host (task pin/complete round-trip or a death event). Opening a notebook is a
client-only action, so a player who merely picked one up and read it was never seen by the server.

- [x] 9.1 Add `src/Mod/ScribeNotebookOpenedMessage.cs` — a `[ProtoContract]` client→server message
      carrying the opened document's `DocId` bytes (`[ProtoMember(1)] byte[]? DocIdBytes`).
- [x] 9.2 Register the message type on the network channel and set a server handler
      (`OnServerReceivedNotebookOpened`) that resolves the opening player's held notebook host
      (`TryResolveDocHost(docId, out _, fromPlayer)`) so `AttachServerContext` → `RecordPickedUpIfNew`
      runs server-side where the write persists. Add `NotifyServerNotebookOpened(Guid docId)` that
      sends the packet from the client.
- [x] 9.3 Send `NotifyServerNotebookOpened(host.Document.DocId)` from BOTH notebook items' open paths
      (`ItemScribeNotebook.OpenNotebookDialog` + `ItemClockmakerNotebook.OpenNotebookDialog`) right
      after `RegisterHost`.
- [x] 9.4 Suppress the entry for the crafter in `RecordPickedUpIfNew` — skip when an existing
      `Crafted` entry has the same `ActorName` (their Crafted entry already records acquisition);
      other players stay deduplicated to one `PickedUp` each by `TryAddEntry`.
- [x] 9.5 `dotnet build` clean + `dotnet test tests/Core.Tests` green (no Core change; Mod-side).

- [ ] 9.6 In-game: a second player picks up and opens a notebook they did NOT craft — confirm a
      single "Picked up" entry appears naming them, re-opening adds no duplicate, and the crafter's
      own open adds no "Picked up" entry (only their "Crafted by X" line).
