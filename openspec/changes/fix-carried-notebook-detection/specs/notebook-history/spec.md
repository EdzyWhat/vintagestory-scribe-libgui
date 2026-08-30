## MODIFIED Requirements

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

## ADDED Requirements

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
