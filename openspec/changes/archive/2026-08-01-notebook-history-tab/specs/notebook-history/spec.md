## ADDED Requirements

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
The system SHALL record a `PickedUp` entry the first time each distinct player opens the
Notebook dialog. Subsequent opens by the same player SHALL NOT add entries.

#### Scenario: First open records entry
- **WHEN** a player opens a Notebook for the first time
- **THEN** a PickedUp entry with their name and current in-game date is added

#### Scenario: Second open by same player adds no entry
- **WHEN** the same player opens the Notebook again
- **THEN** the PickedUp entry count for that player remains 1

### Requirement: Death event recorded when holder dies while carrying notebook
The system SHALL record a `Death` entry on the server when a player dies while holding a
Notebook in their active hotbar. The Detail field SHALL contain the reconstructed vanilla
death message for that player and damage source.

#### Scenario: Death while holding records entry
- **WHEN** a player holding a Notebook in their hotbar dies from any cause
- **THEN** a Death entry is added with their name, the reconstructed death message, and
  the in-game date

#### Scenario: Death without notebook records nothing
- **WHEN** a player dies while NOT holding a Notebook
- **THEN** no Death entry is added to any Notebook

### Requirement: PvpKill event recorded when holder kills another player
The system SHALL record a `PvpKill` entry on the server when the holder of a Notebook
delivers the killing blow to another player.

#### Scenario: Killing another player records entry
- **WHEN** a player holding a Notebook kills another player
- **THEN** a PvpKill entry is added to the killer's notebook with the victim's name

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
The system SHALL record a `TemporalStorm` entry for every player currently holding a
Notebook when a temporal storm begins. The storm strength (light/medium/heavy) SHALL be
stored in the Detail field.

#### Scenario: Storm start records entry for each holder
- **WHEN** a temporal storm begins and two players are holding Notebooks
- **THEN** each of their notebooks gains a TemporalStorm entry with the storm strength

#### Scenario: No notebooks held during storm start records nothing
- **WHEN** a temporal storm begins and no player is holding a Notebook
- **THEN** no TemporalStorm entries are added anywhere

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
