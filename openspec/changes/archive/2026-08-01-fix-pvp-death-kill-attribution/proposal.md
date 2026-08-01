## Why

The notebook History chronicle was broadly not working as intended — this change is an overall
history refinement, not only a PvP-attribution fix (the original title's scope). Several distinct
defects, all found through playtesting, keep the chronicle from recording the events it promises:

1. **PvP attribution (the original scope).** When a notebook-holder is killed by another player, the
   Death entry reads only "`<name>` died." with no killer, and the killer's notebook records no
   PvpKill entry at all. Root cause: both code paths read `DamageSource.SourceEntity`, which the VS
   API documents as **null for melee damage** — the common PvP case. The correct accessor is
   `GetCauseEntity()` (`CauseEntity ?? SourceEntity`), which resolves the attacker for both melee and
   projectile damage. A second gap compounds it: vanilla ships no `deathmsg-player-*` template, so
   even with the killer resolved there is no message string that names both victim and killer.
2. **Notebook-locating scope (found later in playtesting).** History events wrote to the WRONG
   notebook — the first match in `InventoryManager.InventoriesOrdered`, which includes the creative,
   ground, mouse, and crafting inventories. In creative mode this mutated a creative-inventory
   *template* stack (so a freshly-spawned notebook already carried phantom entries while the carried
   one stayed empty), a backpack notebook was skipped when another matched first, and only 1 of N
   carried notebooks updated.
3. **PvP message perspective.** Even once attributed, both logs shared one killer-first sentence, so
   the victim's own notebook read as though they did the killing.
4. **PickedUp entries never recorded.** The one-time "picked up" entry (for anyone who isn't the
   crafter) effectively never fired — its recorder only ran when the server resolved a host for a
   task interaction or a death, and opening a notebook is a client-only action the server never saw.

Points 2–4 were not in the original proposal; they are folded in here because they are the same
capability (`notebook-history`) and the same code path, and the feature isn't actually "working"
until all four are fixed.

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
- **Write each PvP log from its own owner's perspective.** The two entries diverge rather than
  sharing one sentence: the victim's Death log is victim-first & passive ("Junkmuffin was slain by
  Raptor.") and the killer's PvpKill log is killer-first & active ("Raptor slew Junkmuffin."). Both
  derive from the same resolved verb *key*: the active verb for the kill log, and a passive
  participle for the death log via an optional `<key>-participle` lang override (falling back to the
  active verb — only "slew" → "slain" differs in the shipped English set). Two message templates
  (`scribe-pvp-death-message` victim-first, `scribe-pvp-kill-message` killer-first) replace the
  single shared one.
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
- **Fix notebook-locating scope + update every carried notebook.** Playtesting surfaced three related
  bugs, all rooted in `FindNotebookInInventory` walking `InventoryManager.InventoriesOrdered` (which
  includes the CREATIVE, ground, mouse, and crafting inventories) and returning the first match:
  (1) in a creative world the killer's real notebook got no kill while a *newly-spawned* creative
  notebook auto-populated past kills — the write had landed on a creative-inventory template stack
  (the notebook is creative-listed), which is then handed out as future copies; (2) a victim's PvP
  death only recorded when the notebook was in the hotbar, not a backpack; (3) only 1 of N notebooks
  updated on death. The fix scopes the search to the player's real carried inventories (an allow-list
  of `hotbar`/`backpack`/`character`/`mouse` — never the creative or staging inventories) and fans
  every live recorder (death, PvP kill, boss kill, storm) out over ALL matching notebooks instead of
  the first.
- **Make the one-time PickedUp entry actually record.** The intent: the crafter gets a "Crafted by X"
  entry, and every *other* player who opens the notebook gets a one-time "picked up" entry. In
  practice it almost never fired — `RecordPickedUpIfNew` only ran when the server resolved a host for
  a task pin/complete round-trip or a death event, and opening a notebook is a client-only action the
  server never saw. Add a client→server `ScribeNotebookOpenedMessage` (sent from both notebook items'
  open paths) whose handler resolves the held notebook, triggering the recorder. Suppress the entry
  for the crafter (their Crafted entry already stands in for acquisition); other players are still
  deduplicated to one PickedUp entry each.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `notebook-history`: refined across the whole chronicle so it records what it promises —
  (a) attacker resolution uses cause-entity semantics that cover melee; (b) a PvP death's message
  attributes the killer with a weapon-aware, flavored verb (tiered: weapon category → damage type →
  generic pool) rather than an unattributed "died" string; (c) a creature (non-PvP) death names the
  correct creature variant with a flavored line rather than "died"; (d) every event records on ALL
  notebooks the player carries on their person (never on creative-inventory template stacks);
  (e) the victim's Death log and the killer's PvpKill log each read from their own owner's
  perspective; and (f) the one-time PickedUp entry actually records (on open, for non-crafters).

## Impact

- Code: `src/Mod/ScribeModSystem.cs` — `OnEntityDeath` (attacker lookup + per-perspective PvP death
  vs. kill messages + empty-`ActorName` combat entries), `ResolvePvpVerbKey`/`VerbActive`/
  `VerbParticiple` (verb resolved as a key so both active + participle forms are available),
  `BuildDeathMessage` (mob-death flavor pool with `GetPrefixAndCreatureName`), the demo seed
  (`SeedHistory`/`SeedPvpDeathMessage`/`SeedPvpKillMessage`/`SeedMobDeathMessage`), the
  notebook-locating rework (a new `CarriedInventoryClasses` allow-list + `FindCarriedNotebooks`
  enumerator replacing the first-match `FindNotebookInInventory` walk, with all four live recorders
  fanned out over every carried notebook), and the PickedUp signal (`NotifyServerNotebookOpened` +
  `OnServerReceivedNotebookOpened`). Plus `src/Mod/ScribeNotebookOpenedMessage.cs` (new client→server
  message), `src/Mod/NotebookHost.cs` (`RecordPickedUpIfNew` crafter-suppression), and both notebook
  items' open paths (`ItemScribeNotebook`/`ItemClockmakerNotebook`).
- Assets: `src/Mod/assets/scribe/lang/en.json` — PvP verb keys (per-`EnumTool` category,
  per-`EnumDamageType`, generic pool + `-participle` overrides), the two victim-first / killer-first
  message templates, and the mob-death flavor pool.
- Core: `src/Core/HistoryStore.cs` — retention-cap constants only (`MaxDeaths`/`MaxPvpKills` →
  30, `MaxBossKills` → 20). No event-kind or save-format change (`Death`/`PvpKill` already exist);
  no new dependencies. Unblocks `notebook-history-tab` playtest tasks 9.4 and 9.5.
