## Context

`ScribeModSystem.OnEntityDeath` (subscribed to `api.Event.OnEntityDeath`, server-side) records
notebook history for player deaths and PvP kills. Two code sites resolve the attacker via
`DamageSource.SourceEntity`:

- `BuildDeathMessage` (line ~1682): if `SourceEntity` is non-null, uses its `Code.Path` as the
  death-message cause; otherwise falls back to the damage `Source` enum name.
- The PvpKill block (line ~1627): gates on `dmg.SourceEntity is EntityPlayer`.

The VS API doc-comment on `DamageSource` (verified by decompiling `VintagestoryAPI.dll`) states:
`SourceEntity` **"will be null for non-projectile damage e.g. melee attacks: to get the attacking
entity properly for both melee and projectile damage, use `GetCauseEntity()`."** `GetCauseEntity()`
returns `CauseEntity ?? SourceEntity`. Because a sword/spear PvP kill is melee, `SourceEntity` is
null, so:

- The PvpKill block never fires → no kill recorded on the killer's notebook.
- `BuildDeathMessage` takes the `else` branch with `cause = "entity"` (the melee
  `EnumDamageSource.Entity` name); no `deathmsg-entity-*` key exists, so it falls through to the
  hard-coded "`<name>` died." fallback → the victim's Death entry never names the killer.

Separately, vanilla ships **no** `deathmsg-player-*` / `deathmsg-pvp-*` lang key (confirmed by
scanning `assets/game/lang/en.json` — only mob/environment templates exist), so a PvP death cannot
reuse a vanilla string even once the attacker is resolved. Vanilla's own server-side
`GetDeathMessage` (decompiled from `VintagestoryLib.dll`) confirms this: it resolves the attacker
with `src.GetCauseEntity()` and, for a player killer with no matching `deathmsg` key, falls to a
generic `"Player {0} got killed by {1}"` — it does not name the weapon.

**Can we name the weapon?** Investigated via the DLLs, with a surprising result:

- **Damage *type* does NOT distinguish vanilla melee weapons.** `EntityAgent.OnInteract` builds the
  melee `DamageSource` with `Type = EnumDamageType.BluntAttack` **hardcoded**, and sword / falx /
  spear / knife / club do not override it. So `dmg.Type` reports `BluntAttack` for every vanilla
  melee kill. Only projectiles carry a meaningful type (arrows parse `damageType` from item
  attributes, default `PiercingAttack`). Mapping `dmg.Type → verb` alone would mislabel every
  vanilla melee kill.
- **The killer's weapon IS reachable.** `CollectibleObject` exposes `EnumTool? Tool` (Sword, Spear,
  Bow, Club, Mace, Hammer, Knife, Firearm, Crossbow, Javelin, Pike, …). Vanilla's own death-audit
  log reads exactly this off the killer (`entityPlayer.RightHandItemSlot?.Itemstack?.Collectible.
  Code`). `EnumTool` has 33 members; the firearm/crossbow/mace/etc. members exist for mods (no
  vanilla item uses them). So the killer's held-item tool is the accurate weapon-category signal,
  and it supports modded weapons **if** those mods tag their items with a `tool`.

## Goals / Non-Goals

**Goals:**
- Attribute PvP deaths and kills correctly for both melee and projectile attacks.
- The victim's Death entry names the killer; the killer's notebook gets a PvpKill entry.
- Give the message flavor via a weapon-aware verb ("shot"/"slashed"/"bashed"/…) that is accurate
  for vanilla and degrades gracefully for modded weapons and unknown cases.
- Non-PvP deaths (mobs, fall, fire, hunger, …) keep the current vanilla-message reconstruction.

**Non-Goals:**
- No change to the boss-kill, temporal-storm, pickup, or craft paths.
- No new event kinds (`Death`/`PvpKill` already exist in Core), no save-format change.
- Not attempting to perfectly mirror vanilla's own PvP death broadcast wording — a small
  mod-owned message set is sufficient for a chronicle.
- Not maintaining a per-mod weapon map. We read the generic `EnumTool` / `EnumDamageType` signals
  and fall back to a generic verb; individual mod items are never special-cased by code/name.

## Decisions

- **Resolve the attacker with `GetCauseEntity()` in both sites**, replacing `SourceEntity`.
  Rationale: it is the API's documented accessor for "the attacking entity for both melee and
  projectile damage" (and what vanilla's own `GetDeathMessage` uses), so it fixes both symptoms at
  their shared root. Alternative considered: special-casing `Source == EnumDamageSource.Entity` and
  reading `SourceEntity`/`CauseEntity` manually — rejected as reimplementing `GetCauseEntity()`.
- **Detect the killer as `GetCauseEntity() is EntityPlayer` whose `Player` is a different
  `IServerPlayer`** than the victim (guarding self-kills). This one predicate drives both the
  victim's message branch and the killer's PvpKill entry, keeping the two symptoms fixed by one
  condition.
- **Resolve the kill verb by a 3-tier fallback**, best signal first, so vanilla is accurate and
  mods/unknowns degrade gracefully:
  1. **Weapon category** — read the killer's held item `Collectible.Tool` (`EnumTool?`) from
     `killerEntity.RightHandItemSlot?.Itemstack` and map the category to a verb via a lang key
     `scribe:pvp-verb-tool-<enumname>` (lowercased), e.g. `bow` → "pincushioned",
     `crossbow` → "bolted", `sling`/`firearm` → "shot", `sword` → "slashed",
     `spear`/`javelin`/`pike` → "impaled", `axe`/`poleaxe` → "chopped",
     `club`/`mace`/`hammer`/`warhammer` → "bashed", `knife` → "knifed". Missing key → next
     tier. This is the ONLY accurate tier for vanilla melee (see Context: damage type is hardcoded
     Blunt), and it is what makes modded firearms/crossbows work out of the box when tagged.
  2. **Damage type** — when `Tool` is null or its key is unmapped, map `DamageSource.Type` via
     `scribe:pvp-verb-damage-<enumname>` (`slashingattack` → "slashed", `piercingattack` →
     "pierced", `bluntattack` → "bashed"). Catches modded weapons that set a damage type but no
     tool.
  3. **Generic pool** — when neither yields a verb, pick from a small no-repeat pool
     (`scribe:pvp-verb-generic-0..N`: slew, cut down, felled, dispatched, bested). "No immediate
     repeat" is achieved by advancing an index derived from the killer notebook's existing PvpKill
     entry count (already in the History store) rather than `Random` — deterministic, no new
     persisted state, and it rotates naturally across successive kills.
- **Write each PvP log from its own owner's perspective — two templates, one resolved verb key.**
  The verb is resolved to a lang *key* (not a pre-formatted string) so both a passive and an active
  form are available from the same signal:
  - The victim's **Death** entry is victim-first & passive:
    `scribe:scribe-pvp-death-message` = `"{0} was {1} by {2}."` → "Junkmuffin was slain by Raptor."
    ({0} = victim, {1} = participle, {2} = killer).
  - The killer's **PvpKill** entry is killer-first & active:
    `scribe:scribe-pvp-kill-message` = `"{0} {1} {2}."` → "Raptor slew Junkmuffin."
    ({0} = killer, {1} = active verb, {2} = victim).
  - The passive form comes from an optional `<verb-key>-participle` override
    (`VerbParticiple` → `Lang.Get("{key}-participle")` if present, else the active verb from
    `VerbActive`). In the shipped English set only the generic pool differs ("slew" → "slain"); the
    weapon/damage verbs ("shot", "slashed", …) read fine in both slots, so they ship no participle
    override and fall back to the active form.
  Rationale: sharing one killer-first sentence made the victim's own notebook read as though they
  did the killing. Deriving both forms from one verb key keeps the two logs in sync (same weapon
  signal) while letting each read naturally from its owner's point of view. Reworded from the
  original single shared `scribe:pvp-death-message` template.
- **All verbs + the template are mod-owned lang keys** in `scribe/lang/en.json`, so wording is
  fully editable without a code change and translatable later.
- **Creature (non-PvP) deaths get their own flavor pool.** Vanilla ships `deathmsg-<creature>` keys
  for almost no creatures — decompiling `VintagestoryLib.dll`'s `GetDeathMessage` shows it builds
  `"deathmsg-" + Code.Path.Replace("-", "")`, but `game/lang/en.json` only defines
  `deathmsg-drifter-normal-1..3` plus environmental keys; nightmare/tainted/corrupt drifters, bears,
  wolves, etc. have none, so those kills fell through to "died." Instead, name the creature with
  `Entity.GetPrefixAndCreatureName()` — the same accessor vanilla's fallback uses, which yields the
  variant-correct phrase *with its indefinite article baked in* ("a nightmare drifter", "a brown
  bear"; falls back to "a wild animal"). Templates therefore must NOT add their own "a"/"an". A
  self-sizing `scribe-mob-death-0..N` pool (probe upward until a key is missing) supplies the line;
  translators may ship as few as one. Note vanilla has no "grizzly" bear (brown/black/panda/polar/
  sun) and no bare "drifter" — always a variant.
- **Auto-narrated combat entries use empty `ActorName`.** The History row renders
  `ActorName.Length > 0 ? "{ActorName} — {Detail}" : Detail`. Every death/PvP sentence already names
  the victim, so setting `ActorName` would double the name ("Alrik — ...Alrik..."). The BossKill path
  already relied on this empty-`ActorName` convention; Death and PvpKill now match it.
- **Locate notebooks by a carried-inventory allow-list, and update every match.** The original
  `FindNotebookInInventory` walked `player.InventoryManager.InventoriesOrdered` and returned the first
  notebook found. Decompiling `VintagestoryLib.dll` shows `InventoriesOrdered` is EVERY inventory the
  player owns — `GlobalConstants` names them `hotbar`, `backpack`, `character`, `creative`, `ground`,
  `mouse`, `craftinggrid` — and `InventoryPlayerCreative : InventoryBasePlayer` is in that set. Both
  notebook items are creative-listed (`"creativeinventory": {…}`), so in a creative world the creative
  inventory enumerates notebook *template* stacks. The first-match walk therefore (a) could resolve a
  creative template and write history into it — which is then handed back out as every future spawned
  copy (the observed "new notebook auto-populates past kills"), (b) resolved whichever inventory
  happened to enumerate first, so a backpack notebook was skipped when another matched earlier (the
  "only records from the hotbar" symptom), and (c) stopped at the first match, so only 1 of N carried
  notebooks updated. Fix: an allow-list `CarriedInventoryClasses = { hotbar, backpack, character,
  mouse }` (the player's real carried stacks; `mouse` is the live cursor-drag stack — a real held
  item, unlike the infinite creative templates), and a `FindCarriedNotebooks` enumerator that yields
  a host for EVERY matching notebook. All four live recorders (victim Death, killer PvpKill, boss
  kill, storm) loop over it; a thin `FirstOrDefault()` wrapper serves the single-target seed command.
  This is Mod-side only (no `Core`/save-format change), and documented in VSAPI-NOTES so the
  `InventoriesOrdered`-includes-creative gotcha is not re-derived.
- **Record the one-time PickedUp entry on notebook open, via a client→server signal.** The intent
  was: the crafter gets a "Crafted by X" entry, and every *other* player who ever picks up the
  notebook gets a one-time "picked up" entry the first time they open it. In practice it never fired.
  `RecordPickedUpIfNew` lives in `NotebookHost.AttachServerContext`, which only runs when the SERVER
  resolves a host — a task pin/complete round-trip or a death event. Opening a notebook is a
  client-only action (`OnHeldInteractStart` → `OpenNotebookDialog` builds a client-side `NotebookHost`
  and never touches the server), so a player who merely picked up and read a notebook was never seen
  by the server and got no entry. Fix: a new `ScribeNotebookOpenedMessage` (client→server, carrying
  the opened doc's `DocId` bytes) sent from BOTH notebook items' open paths right after `RegisterHost`.

  **Resolve by the active-hand slot, not by DocId, and record history-only.** The first attempt had
  the handler resolve the notebook via `TryResolveDocHost(docId, …)` → `AttachServerContext` →
  `RecordPickedUpIfNew`, but that never fired for the actual pickup case, for a subtle reason: a
  notebook's `DocId` is generated **client-side** (`Guid.NewGuid()` in the `NotebookHost` ctor) and
  only reaches the server on the FIRST edit-flush; crafting writes only `scribeHistory`, never
  `scribeDocument`. So a freshly picked-up (never-edited) notebook has no server-side document and no
  DocId to match — `TryResolveDocHost` scans every carried slot, matches nothing, and the recorder
  never runs. It would only have worked on a notebook the player had already edited. Worse, "fixing"
  it by letting the server build a full `NotebookHost` would stamp a fresh **server-random**
  `scribeDocument` onto the stack, which `OnServerReceivedNotebookSave` (DocId-guarded) would then use
  to **reject the owner's real edits** (DocId mismatch). The fix therefore (a) resolves the notebook
  by the player's ACTIVE-HAND slot — the slot they right-clicked to open, exactly as the save handler
  already does — and (b) records via a new history-only helper `NotebookHost.TryRecordPickedUpOnSlot`
  that touches ONLY the `scribeHistory` attribute and never the document, then echoes the updated
  history back (a `ScribeNotebookSaveMessage` with null `DocumentBytes`) so an open dialog refreshes
  its History tab. The recorder suppresses the crafter (their Crafted entry already stands in for
  acquisition — detected by an existing `Crafted` entry with the same `ActorName`); every other player
  is still deduplicated to one PickedUp entry each by `HistoryStore.TryAddEntry`.

  Alternative considered: firing the entry from a server-side "item entered inventory" hook —
  rejected as broader than "picked up & looked at it" and lacking a reliable per-player once
  semantics; open-triggered matches the user's stated intent ("the first time they picked up a
  notebook" = the first time they open it). Mod-side only (new message class + handler + history-only
  helper + crafter-suppression); no `Core`/save-format change.

  **Latent gap deliberately NOT fixed here:** because a never-edited notebook's `DocId` isn't known
  server-side until the first edit, any *DocId-addressed* server lookup of a fresh notebook (e.g.
  `TryResolveDocHost`, notebook-task pin snapshots) can't resolve it either. This pickup fix routes
  around that via the active-hand slot, but the underlying "notebook DocId is client-authoritative
  until first edit" gap remains. Closing it would mean writing `scribeDocument` at craft time — but
  that is neither sufficient (looted/traded/`/give`/creative-spawned notebooks never run
  `OnCreatedByCrafting`) nor free of risk (it interacts with the Clockmaker upgrade path, which
  copies the source document bytes to preserve the DocId). Out of scope for this change; noted for a
  future one.
- **Retention caps raised** in `HistoryStore` so a carried notebook keeps a longer chronicle:
  `MaxDeaths`/`MaxPvpKills` 10 → 30, `MaxBossKills` 10 → 20 (`MaxStorms` unchanged). This is the only
  `Core` change; the sliding-window cap tests reference the constants symbolically and still pass.

## Risks / Trade-offs

- [Killer disconnects/despawns before the death event] → `GetCauseEntity()` may still return the
  `EntityPlayer`; if its `Player` is null we skip the PvP branch and fall back to the existing
  behavior (vanilla reconstruction), so no crash and no worse than today.
- [Held item changed between the killing blow and the death event] → the weapon read is a
  best-effort heuristic (we read the current right-hand item, as vanilla's audit log does). Worst
  case the verb tier falls through to damage-type or generic — never wrong attribution, just a
  less specific verb. Acceptable for a chronicle.
- [A mod weapon tags neither `tool` nor a distinct `damageType`] → lands in the generic pool. This
  is the graceful-degradation goal, not a failure.
- [Message wording is mod-authored, not vanilla] → Acceptable: vanilla has no PvP template to
  match, and the History tab is a mod feature with its own voice.
- [`GetCauseEntity()` / `EnumTool` semantics change across VS versions] → Low risk; both are stable
  public API. Documented in VSAPI-NOTES against re-derivation.

## Open Questions

- None blocking. Exact verb wording per tier is a copy choice (captured above as the default),
  editable in lang without touching code.
