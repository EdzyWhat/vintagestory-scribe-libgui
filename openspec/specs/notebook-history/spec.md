# notebook-history Specification

## Purpose
TBD - created by archiving change notebook-history-tab. Update Purpose after archive.
## Requirements
### Requirement: History store persists seven event kinds in the ItemStack
The system SHALL maintain a `HistoryStore` per Notebook, serialized as `SHST v1` binary in
`ItemStack.Attributes["scribeHistory"]`. The store SHALL record entries of the following
kinds: `Crafted`, `PickedUp`, `Death`, `PvpKill`, `BossKill`, `TemporalStorm`, and
`Manual`. Each entry SHALL carry a kind, an actor name (player name or empty for world
events), a detail string, and a formatted in-game calendar date. The store SHALL be
versioned with `PriorVersion` and `ApplyMigrations` scaffolding following the
`ScribeDocumentCodec` pattern.

#### Scenario: Fresh notebook has an empty history store
- **WHEN** a player obtains a new Notebook with no `scribeHistory` attribute
- **THEN** opening it shows an empty History tab with no entries

#### Scenario: History survives inventory moves and world restart
- **WHEN** a Notebook with history entries is moved to a different slot, then the world is
  saved and reloaded
- **THEN** all history entries are present and unchanged when the notebook is next opened

#### Scenario: History travels with the item when traded
- **WHEN** a player gives their Notebook to another player
- **THEN** the receiving player's History tab shows all entries written while the original
  player held it

### Requirement: Per-kind caps enforce a rolling window
The system SHALL enforce the following per-kind caps, dropping the oldest entry when the
cap is reached for sliding-window kinds:

| Kind          | Cap | Policy        |
|---------------|-----|---------------|
| Crafted       | 1   | never replaced (only ever written once) |
| PickedUp      | unlimited | deduped by ActorName (one entry per player ever) |
| Death         | 10  | sliding window (oldest dropped) |
| PvpKill       | 10  | sliding window |
| BossKill      | 10  | sliding window |
| TemporalStorm | 5   | sliding window |
| Manual        | 10  | reject when full (return false) |

#### Scenario: Death cap drops oldest
- **WHEN** a notebook already has 10 Death entries and the holder dies again
- **THEN** the oldest Death entry is removed and the new one is appended, keeping exactly 10

#### Scenario: PickedUp deduplication
- **WHEN** a player who already has a PickedUp entry for their name opens the notebook again
- **THEN** no new PickedUp entry is added

#### Scenario: Manual cap rejects when full
- **WHEN** a player tries to add an 11th manual entry
- **THEN** the operation fails and the store remains at 10 manual entries

### Requirement: Crafted event recorded at notebook creation
The system SHALL record a `Crafted` entry on the server when the Notebook item exits a
crafting grid, storing the crafting player's display name and the in-game calendar date.

#### Scenario: Crafting records entry
- **WHEN** a player crafts a Notebook
- **THEN** the notebook's History store contains exactly one Crafted entry with the
  crafting player's name and the current in-game date

#### Scenario: Crafted entry is not duplicated
- **WHEN** a notebook already has a Crafted entry and the item is manipulated in any way
- **THEN** no second Crafted entry is added

### Requirement: PickedUp event recorded on first dialog open per player
The system SHALL record a one-time `PickedUp` entry on the server for each player who opens a
Notebook, EXCEPT the crafter (who already has a `Crafted` entry standing in for their acquisition).
Because opening the dialog is a client-only action the server does not otherwise observe, the client
SHALL notify the server on open (a `ScribeNotebookOpenedMessage` carrying the opened document's id),
and the server handler SHALL resolve the opening player's held notebook so the recorder runs
server-side where the write persists. The entry SHALL be deduplicated to at most one per player
(by actor name).

#### Scenario: First open records entry
- **WHEN** a player who did not craft the Notebook opens it for the first time
- **THEN** a single `PickedUp` entry naming that player and the in-game date is added to the
  notebook

#### Scenario: The crafter opening their own notebook records no PickedUp entry
- **WHEN** the player who crafted the Notebook (whose name matches its `Crafted` entry) opens it
- **THEN** no `PickedUp` entry is added — the existing `Crafted` entry already records their
  acquisition

#### Scenario: Second open by same player adds no entry
- **WHEN** a non-crafter who already has a `PickedUp` entry opens the same notebook again
- **THEN** no additional `PickedUp` entry is added (deduplicated per actor)

### Requirement: Death event recorded when holder dies while carrying notebook
The system SHALL record a `Death` entry on the server, on EVERY Notebook the dying player is
carrying on their person, when that player dies. "Carried on their person" is defined by
inventory TYPE, not a fixed list of known names: any inventory that is an
`InventoryBasePlayer` — i.e. genuinely part of the player's own inventory manager state, per the
engine's own definition of "on the player" — counts as carried, EXCEPT the creative inventory
(whose stacks are infinite templates — writing history there mutates the template so every future
copy carries phantom entries) and the transient ground staging inventory. This includes the
player's hotbar, backpack bags, worn character/clothing slots, mouse-cursor drag slot, the crafting
grid, AND any inventory added by another mod directly to the player's own inventory manager (e.g. a
bonus storage space granted by a skill or ability mod) — such mod-added inventories are included
automatically without Scribe needing to recognize their name in advance. It explicitly EXCLUDES any
inventory that is not an `InventoryBasePlayer` — e.g. a chest, oven, or trader stall the player
merely has open nearby, which the engine temporarily attaches to the player's inventory manager for
the duration its dialog is open but which is not genuinely "on" the player. The Detail field SHALL
contain a self-contained sentence that names the victim, chosen by the killing damage source's
cause entity (which covers both melee and projectile attacks): when the killer is another player, a
mod-owned PvP death message naming the killer, victim, and a kill verb; when the killer is a
creature, a mod-owned flavored message that names the creature by its own variant-correct display
name; otherwise (environmental death) the reconstructed vanilla `deathmsg-<cause>-<N>` message. It
SHALL NOT fall back to an unattributed "died" message while a cause entity is resolvable. The Detail
sentence already names the victim, so the entry SHALL leave `ActorName` empty (the display prepends
"ActorName — " otherwise).

#### Scenario: Death while holding records entry
- **WHEN** a player carrying one or more Notebooks (in hotbar, backpack, character, cursor, or
  crafting-grid slots) dies from any cause
- **THEN** a Death entry is added to EACH of those notebooks with the appropriate death message and
  the in-game date, with the whole sentence in Detail (no separate actor-name prefix that would
  repeat the victim's name)

#### Scenario: Notebook in a backpack bag still records
- **WHEN** a player dies with a Notebook in a backpack bag (not the active hotbar slot)
- **THEN** that backpack notebook receives the Death entry — recording is not limited to the active
  hotbar slot

#### Scenario: Notebook in the crafting grid now records
- **WHEN** a player dies with a Notebook sitting in their 3×3 crafting grid
- **THEN** that notebook receives the Death entry — the crafting grid is treated as carried

#### Scenario: Notebook in a mod-added bonus inventory records
- **WHEN** a player dies with a Notebook stored in an inventory a third-party mod added directly
  to their own inventory manager (e.g. a bonus storage space granted by a skill/ability mod), and
  that inventory's `ClassName` is not one Scribe has ever seen before
- **THEN** that notebook still receives the Death entry — inclusion is determined by the
  inventory's type, not by recognizing its name

#### Scenario: Creative-inventory template notebooks are never written
- **WHEN** a player in creative mode dies while notebook template stacks exist in their creative
  inventory
- **THEN** no history entry is written to any creative-inventory stack (only the notebooks carried
  in hotbar/backpack/character/cursor/crafting-grid slots, or a mod-added carried inventory, are
  updated), so a later-spawned copy from the creative tab does not carry phantom entries

#### Scenario: Notebook in a transiently-opened external container does not record
- **WHEN** a player has a chest, oven, or trader stall open nearby (temporarily attached to their
  inventory manager for the dialog's duration) with a Notebook stored inside it, and the player
  dies while that dialog is open
- **THEN** no Death entry is added to that notebook — a transiently-opened external container is
  never treated as "on the player," regardless of whether it happens to be open at the moment of
  death

#### Scenario: Death by a creature names the correct variant
- **WHEN** a player holding a Notebook is killed by a creature (not another player), whether by
  melee or projectile
- **THEN** the Death entry's message names that creature by its variant-correct display name (e.g.
  "a nightmare drifter", "a brown bear"), drawn from the entity's own name rather than a fixed
  string, and does NOT fall back to the generic "<victim> died." message

#### Scenario: Death by another player names the killer with a weapon-aware verb, victim-first
- **WHEN** a player holding a Notebook is killed by another player, whether by melee or
  projectile
- **THEN** the Death entry's message is written from the victim's perspective — victim-first and
  passive ("Junkmuffin was slain by Raptor.") — naming the killer and using a passive kill verb
  chosen from the killer's weapon category when available (e.g. a bow → "shot", a sword →
  "slashed"), and does NOT fall back to the generic "<victim> died." message

#### Scenario: Kill verb degrades gracefully for unknown weapons
- **WHEN** the killer's weapon has no recognized tool category but the damage carries a known
  damage type (e.g. a modded weapon dealing piercing damage)
- **THEN** the kill verb is derived from the damage type; and when neither a tool category nor a
  damage-type mapping is available, a generic kill verb is used with no immediate repeat across
  successive kills recorded on the same notebook

#### Scenario: Death without notebook records nothing
- **WHEN** a player dies while NOT holding a Notebook
- **THEN** no Death entry is added to any Notebook

### Requirement: PvpKill event recorded when holder kills another player
The system SHALL record a `PvpKill` entry on the server, on EVERY Notebook the killer is carrying on
their person (same carried-inventory scope as the Death requirement — any `InventoryBasePlayer`
inventory except creative and ground, which now includes the crafting grid and any mod-added
carried inventory), when that player delivers the killing blow to another player. The killer SHALL
be resolved from the damage source's cause entity so that melee kills are attributed, not only
projectile kills.

#### Scenario: Killing another player records entry
- **WHEN** a player carrying one or more Notebooks kills another player
- **THEN** a PvpKill entry is added to EACH of the killer's carried notebooks, written from the
  killer's perspective — killer-first and active ("Raptor slew Junkmuffin.") — naming the victim
  and using the active form of the same weapon-aware verb resolved for the victim's Death entry (the
  two logs share one verb key but each reads from its own owner's point of view)

#### Scenario: Melee kill is attributed
- **WHEN** a player holding a Notebook kills another player with a melee weapon (a case where
  the damage source's direct source entity is null)
- **THEN** a PvpKill entry is still added to the killer's notebook naming the victim

#### Scenario: Self-inflicted or environmental death records no PvpKill
- **WHEN** a notebook holder dies to a non-player cause, or by their own hand
- **THEN** no PvpKill entry is added to any notebook

#### Scenario: Notebook in the crafting grid records a PvpKill
- **WHEN** a player kills another player while carrying a Notebook in their crafting grid
- **THEN** a PvpKill entry is added to that notebook, same as any other carried slot

### Requirement: BossKill event recorded for nearby boss deaths
The system SHALL record a `BossKill` entry when an Eidolon or Mad Crow (Erel) entity dies
within 100 blocks of a player holding a Notebook. The Detail field SHALL contain the boss's
display name ("Eidolon" or "Mad Crow").

#### Scenario: Boss dies within 100 blocks records entry
- **WHEN** an Eidolon or Mad Crow dies and the notebook holder is within 100 blocks
- **THEN** a BossKill entry is added to the holder's notebook

#### Scenario: Boss dies beyond 100 blocks records nothing
- **WHEN** a boss dies more than 100 blocks from the notebook holder
- **THEN** no BossKill entry is added

### Requirement: TemporalStorm event recorded at storm start for all online holders
The system SHALL record a `TemporalStorm` entry for every player currently holding a Notebook,
using the same carried-inventory scope as the Death requirement (any `InventoryBasePlayer`
inventory except creative and ground, which now includes the crafting grid and any mod-added
carried inventory), when a temporal storm begins. The storm strength (light/medium/heavy) SHALL be
stored in the Detail field.

#### Scenario: Storm start records entry for each holder
- **WHEN** a temporal storm begins and two players are holding Notebooks
- **THEN** each of their notebooks gains a TemporalStorm entry with the storm strength

#### Scenario: No notebooks held during storm start records nothing
- **WHEN** a temporal storm begins and no player is holding a Notebook
- **THEN** no TemporalStorm entries are added anywhere

#### Scenario: Notebook in a mod-added bonus inventory records during a storm
- **WHEN** a temporal storm begins while a player has a Notebook stored in an inventory a
  third-party mod added directly to their own inventory manager
- **THEN** that notebook gains a TemporalStorm entry — inclusion is determined by the inventory's
  type, not by recognizing its name

### Requirement: Player can add and edit up to 10 manual entries
The system SHALL allow the notebook holder to create manual history entries (free text,
max 140 characters) via the History tab, up to a cap of 10. Existing manual entries SHALL
be editable in place. The in-game date is supplied by the server at creation time and is
not editable.

#### Scenario: Manual entry created and displayed
- **WHEN** a player submits a manual entry with text "Found the Resonance Archives"
- **THEN** the entry appears in the History tab with the current in-game date

#### Scenario: Manual entry text can be edited
- **WHEN** a player edits an existing manual entry's text
- **THEN** the updated text is stored and displayed

#### Scenario: Manual entry rejected at cap
- **WHEN** the player attempts to add an 11th manual entry
- **THEN** the operation fails and the "Add entry" control is hidden/disabled

### Requirement: History tab displays all entries newest-first
The History tab in the Notebook dialog SHALL display all history entries in reverse
chronological order (newest first). Auto-recorded entries SHALL be read-only. Manual
entries SHALL show an edit affordance. The tab SHALL be empty-state aware (show a prompt
when no entries exist).

#### Scenario: Entries appear newest-first
- **WHEN** the History tab is opened with three entries added in order A, B, C
- **THEN** C appears first, then B, then A

#### Scenario: Empty state is shown when no entries exist
- **WHEN** the History tab is opened on a fresh notebook
- **THEN** a message is displayed indicating no history has been recorded yet

### Requirement: LoreDiscovery kind is reserved for future use
The `HistoryEventKind` enum SHALL include a `LoreDiscovery` value. It SHALL NOT be wired
to any event in this change. The codec SHALL be able to serialize/deserialize entries with
this kind without error, so a future version can begin writing them without a breaking
change.

#### Scenario: LoreDiscovery round-trips through codec
- **WHEN** a HistoryEntry with Kind = LoreDiscovery is serialized and deserialized
- **THEN** the kind is preserved correctly

### Requirement: Notebooks inside a CarryOn-carried container also record Death, PvpKill, and TemporalStorm
When the CarryOn mod (`modid: carryon`) is installed and enabled, the system SHALL additionally
detect and record Death, PvpKill, and TemporalStorm entries on Notebooks stored inside a block
currently carried by the player via CarryOn (e.g. a chest carried on the back), in addition to the
`InventoryBasePlayer`-scoped detection above. This detection SHALL use CarryOn's public
`ICarryManager` API to enumerate the player's currently-carried blocks and read/write their frozen
block-entity data, without Scribe taking a build-time (compile) dependency on CarryOn or CarryOnLib.
When CarryOn is not installed, this detection SHALL be skipped entirely with no behavior change and
no error.

#### Scenario: Notebook inside a carried chest records a death
- **WHEN** CarryOn is installed, a player is carrying a chest containing a Notebook on their back,
  and that player dies
- **THEN** a Death entry is added to the notebook inside the carried chest, in addition to any
  notebooks carried directly on the player's person

#### Scenario: Notebook inside a carried container records a storm
- **WHEN** CarryOn is installed, a player is carrying a container with a Notebook inside it, and a
  temporal storm begins
- **THEN** a TemporalStorm entry is added to that notebook

#### Scenario: CarryOn not installed changes nothing
- **WHEN** CarryOn is not installed on the server
- **THEN** Notebook history recording behaves exactly as it does for the `InventoryBasePlayer`-scoped
  detection alone, with no error or performance difference

#### Scenario: A CarryOn API shape change degrades silently
- **WHEN** CarryOn is installed but a future version has changed the `ICarryManager` API surface in
  a way the reflection-based lookup can no longer navigate
- **THEN** the CarryOn detection path logs a failure once (not once per event) and is treated as
  inactive for the rest of the session — it SHALL NOT throw an unhandled exception that disrupts
  the player-death, PvP-kill, or storm-tick handlers

