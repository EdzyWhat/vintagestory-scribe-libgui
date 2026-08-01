## MODIFIED Requirements

### Requirement: Death event recorded on every carried notebook when holder dies
The system SHALL record a `Death` entry on the server, on EVERY Notebook the dying player is
carrying on their person, when that player dies. "Carried on their person" means a notebook in the
player's hotbar, backpack bags, worn character/clothing slots, or mouse-cursor drag slot; it
explicitly EXCLUDES the creative inventory (whose stacks are infinite templates — writing history
there mutates the template so every future copy carries phantom entries) and the transient ground
and crafting-grid staging inventories. The Detail field SHALL contain a self-contained sentence that
names the victim, chosen by the killing damage source's cause entity (which covers both melee and
projectile attacks): when the killer is another player, a mod-owned PvP death message naming the
killer, victim, and a kill verb; when the killer is a creature, a mod-owned flavored message that
names the creature by its own variant-correct display name; otherwise (environmental death) the
reconstructed vanilla `deathmsg-<cause>-<N>` message. It SHALL NOT fall back to an unattributed
"died" message while a cause entity is resolvable. The Detail sentence already names the victim, so
the entry SHALL leave `ActorName` empty (the display prepends "ActorName — " otherwise).

#### Scenario: Death while carrying records entry on all carried notebooks
- **WHEN** a player carrying one or more Notebooks (in hotbar, backpack, character, or cursor slots)
  dies from any cause
- **THEN** a Death entry is added to EACH of those notebooks with the appropriate death message and
  the in-game date, with the whole sentence in Detail (no separate actor-name prefix that would
  repeat the victim's name)

#### Scenario: Notebook in a backpack bag still records
- **WHEN** a player dies with a Notebook in a backpack bag (not the active hotbar slot)
- **THEN** that backpack notebook receives the Death entry — recording is not limited to the active
  hotbar slot

#### Scenario: Creative-inventory template notebooks are never written
- **WHEN** a player in creative mode dies while notebook template stacks exist in their creative
  inventory
- **THEN** no history entry is written to any creative-inventory stack (only the notebooks carried in
  hotbar/backpack/character/cursor slots are updated), so a later-spawned copy from the creative tab
  does not carry phantom entries

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

### Requirement: PvpKill event recorded on every carried notebook when holder kills another player
The system SHALL record a `PvpKill` entry on the server, on EVERY Notebook the killer is carrying on
their person (same carried-inventory scope as the Death requirement — hotbar/backpack/character/cursor,
never the creative or staging inventories), when that player delivers the killing blow to another
player. The killer SHALL be resolved from the damage source's cause entity so that melee kills are
attributed, not only projectile kills.

#### Scenario: Killing another player records entry on all carried notebooks, killer-first
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

### Requirement: PickedUp event recorded once per non-crafter when the notebook is opened
The system SHALL record a one-time `PickedUp` entry on the server for each player who opens a
Notebook, EXCEPT the crafter (who already has a `Crafted` entry standing in for their acquisition).
Because opening the dialog is a client-only action the server does not otherwise observe, the client
SHALL notify the server on open (a `ScribeNotebookOpenedMessage` carrying the opened document's id),
and the server handler SHALL resolve the opening player's held notebook so the recorder runs
server-side where the write persists. The entry SHALL be deduplicated to at most one per player
(by actor name).

#### Scenario: Non-crafter opening a notebook the first time records a PickedUp entry
- **WHEN** a player who did not craft the Notebook opens it for the first time
- **THEN** a single `PickedUp` entry naming that player and the in-game date is added to the
  notebook

#### Scenario: The crafter opening their own notebook records no PickedUp entry
- **WHEN** the player who crafted the Notebook (whose name matches its `Crafted` entry) opens it
- **THEN** no `PickedUp` entry is added — the existing `Crafted` entry already records their
  acquisition

#### Scenario: Re-opening a notebook does not add duplicate PickedUp entries
- **WHEN** a non-crafter who already has a `PickedUp` entry opens the same notebook again
- **THEN** no additional `PickedUp` entry is added (deduplicated per actor)
