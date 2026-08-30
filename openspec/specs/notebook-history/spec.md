# notebook-history Specification

## Purpose
TBD - created by archiving change notebook-history-tab. Update Purpose after archive.
## Requirements
### Requirement: History store persists seven event kinds in the ItemStack
The system SHALL maintain a `HistoryStore` per Notebook, serialized as `SHST v2` binary in
`ItemStack.Attributes["scribeHistory"]`. The store SHALL record entries of the following kinds:
`Crafted`, `PickedUp`, `Death`, `PvpKill`, `BossKill`, `TemporalStorm`, `LoreDiscovery`, and
`Manual`. Each entry SHALL carry a kind, an actor name (player name or empty for world events), a
detail string, a formatted in-game calendar date, and a stable per-entry identifier (a `Guid`,
meaningful only for `Manual` entries; empty for every other kind). The store SHALL be versioned
with `PriorVersion` and `ApplyMigrations` scaffolding following the `ScribeDocumentCodec` pattern; a
`v1` payload (no per-entry identifier field) migrates by filling an empty identifier for every
entry.

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

#### Scenario: A v1 payload migrates cleanly
- **WHEN** a `scribeHistory` attribute written before this change (`SHST v1`, no per-entry
  identifier) is deserialized
- **THEN** every entry loads correctly with an empty identifier, and no error occurs

### Requirement: Per-kind caps enforce a rolling window
The system SHALL enforce the following per-kind caps, dropping the oldest entry when the
cap is reached for sliding-window kinds:

| Kind          | Cap | Policy        |
|---------------|-----|---------------|
| Crafted       | 1   | never replaced (only ever written once) |
| PickedUp      | unlimited | deduped by ActorName (one entry per player ever) |
| Death         | 30  | sliding window (oldest dropped) |
| PvpKill       | 30  | sliding window |
| BossKill      | 20  | sliding window |
| TemporalStorm | 10  | sliding window |
| Manual        | 30  | sliding window (oldest Manual entry dropped) |

#### Scenario: Death cap drops oldest
- **WHEN** a notebook already has 30 Death entries and the holder dies again
- **THEN** the oldest Death entry is removed and the new one is appended, keeping exactly 30

#### Scenario: TemporalStorm cap drops oldest
- **WHEN** a notebook already has 10 TemporalStorm entries and another storm begins while its
  holder is online
- **THEN** the oldest TemporalStorm entry is removed and the new one is appended, keeping exactly 10

#### Scenario: PickedUp deduplication
- **WHEN** a player who already has a PickedUp entry for their name opens the notebook again
- **THEN** no new PickedUp entry is added

#### Scenario: Manual cap drops the oldest manual entry
- **WHEN** a notebook already has 30 Manual entries and its holder successfully adds another
- **THEN** the oldest Manual entry (regardless of which player authored it) is removed and the new
  one is appended, keeping exactly 30

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
The system SHALL allow a Notebook's current holder to create a `Manual` history entry via the
History tab's "Add Entry" control. A new entry starts as a local, unsent draft: an empty, focused
text field shown at the top of the History tab alongside an automatically-supplied player name and
in-game date (both uneditable, matching every other entry's display). The draft is sent to the
server and becomes a real, persisted entry ONLY the first time its text is committed non-empty
(losing focus, pressing Enter, or the dialog closing); if the draft is abandoned while still empty
(dialog closed, or a new "Add Entry" click replaces it before any text was committed), it is
discarded locally and never reaches the server.

Once created, a `Manual` entry's text SHALL be freely re-editable, any number of times, by its
original author only — re-editing to empty text does NOT delete the entry; only a NEVER-created
draft is discarded (see above). A `Manual` entry's player name and in-game date, once created, are
never editable by anyone. A `Manual` entry MAY be deleted, at any time, by its original author only.
Authorization for both edit and delete SHALL be determined server-side by matching the requesting
player's own name against the entry's stored author name — never a client-claimed identity —
mirroring the Guestbook note edit path's sender-identity check. A request that fails this check
SHALL be silently ignored (no error), matching every other server-authoritative write in this mod.
Entry text SHALL be clamped to `ScribeDocumentCodec.MaxTaskTextLength` (1000 characters), both by
the input field and, authoritatively, by the server.

A `Manual` entry SHALL NOT be pinnable and SHALL NOT display a drag/grip handle, regardless of who
is viewing it.

#### Scenario: Manual entry created and displayed
- **WHEN** a player clicks "Add Entry," types "Found the Resonance Archives," and the field commits
- **THEN** the entry appears in the History tab with the player's name, the current in-game date,
  and the typed text, and it persists after closing and reopening the notebook

#### Scenario: Draft abandoned while empty is discarded
- **WHEN** a player clicks "Add Entry" and closes the dialog without typing anything
- **THEN** no entry is added to the History tab, on this open or any future one

#### Scenario: Manual entry text can be re-edited any number of times
- **WHEN** a player edits an existing Manual entry of their own authorship, more than once, over
  separate dialog sessions
- **THEN** each edit is accepted and the latest text is what displays and persists

#### Scenario: Editing text to empty does not delete an already-created entry
- **WHEN** a player clears all the text of a Manual entry they already created and successfully
  saved at least once, and commits the empty text
- **THEN** the entry still exists, now with blank text — it is not automatically removed

#### Scenario: A different player cannot edit or delete another player's manual entry
- **WHEN** a Notebook holder attempts to edit or delete a Manual entry whose author name does not
  match their own player name (e.g. an inherited notebook that changed hands)
- **THEN** the request is ignored and the entry is unchanged; no delete/edit affordance is even shown
  to them for that entry

#### Scenario: Manual entry can be deleted by its author
- **WHEN** the author of a Manual entry clicks its delete affordance
- **THEN** that entry is permanently removed from the History tab

#### Scenario: Manual entry text is clamped at 1000 characters
- **WHEN** a player pastes text longer than 1000 characters into a Manual entry's field
- **THEN** the stored and displayed text is truncated to 1000 characters

#### Scenario: Manual cap prevents unbounded growth
- **WHEN** a notebook already has 30 Manual entries and its holder adds a 31st
- **THEN** the oldest Manual entry is dropped (per the Per-kind caps requirement) rather than the
  add being rejected

#### Scenario: Manual entry has no pin or drag affordance
- **WHEN** any player views a Manual entry in the History tab, including its own author
- **THEN** no drag/grip handle is shown and no control exists to pin it

### Requirement: History tab displays all entries newest-first
The History tab in the Notebook dialog SHALL display all history entries, including `Manual` ones,
in reverse chronological order (newest first) alongside a persistent "Add Entry" control. Auto-
recorded entries SHALL remain fully read-only. A `Manual` entry's kind-line label SHALL read
"`{ActorName}'s Note`" (its author's name, possessive) in place of a generic kind label, for every
viewer regardless of authorship — the entry's in-game date SHALL remain shown uneditable alongside
it, identical in style to an automatic entry's. Only the entry's text content is interactive, and
only for its own author (a non-author sees the same text rendered as plain, non-interactive text,
matching every automatic entry's presentation). A `Manual` entry's delete affordance, when shown to
its author, SHALL appear only while the pointer hovers the entry's text/input line — matching the
Editor tab's own per-row delete button — floating over that line rather than reserving a permanent
column; it SHALL NOT be visible otherwise. A faint divider (15% opacity of the theme's ink/`OnSurface`
color) SHALL separate each pair of adjacent History entries (including a pending draft); no divider
SHALL render before the first entry or after the last. The "Add Entry" control SHALL match the Read
tab's "Task Editor" footer button in font, size, and layout (same fixed 14px Caudex label, same
full-width footer placement). Typing into a `Manual` entry's field SHALL
suppress the game's movement/hotbar hotkeys exactly as typing into an Editor-tab, Pin Tab, or
Guestbook field already does. The tab SHALL be empty-state aware (show a prompt when no entries and
no in-progress draft exist).

#### Scenario: Entries appear newest-first
- **WHEN** the History tab is opened with three entries added in order A, B, C
- **THEN** C appears first, then B, then A

#### Scenario: Empty state is shown when no entries exist
- **WHEN** the History tab is opened on a fresh notebook with no in-progress draft
- **THEN** a message is displayed indicating no history has been recorded yet

#### Scenario: A non-author sees a manual entry's text as read-only
- **WHEN** a player who did not author a given Manual entry views the History tab
- **THEN** that entry's text renders as plain uneditable text, exactly like an automatic entry's
  Detail text, with no delete affordance shown for it, and the kind-line label still reads
  "`{ActorName}'s Note`"

#### Scenario: Manual entry's delete button only appears on hover
- **WHEN** the author of a Manual entry is NOT hovering its text/input line
- **THEN** no delete button is rendered for that entry
- **WHEN** the author moves the pointer over that entry's text/input line
- **THEN** a delete button appears, floating over the line's right edge, without reserving space
  when hidden

#### Scenario: A divider separates entries but never leads or trails the list
- **WHEN** the History tab shows three entries A, B, C (newest-first) and no pending draft
- **THEN** a faint divider renders between A and B and between B and C, but not above A or below C

#### Scenario: Typing in a Manual entry field does not move the player
- **WHEN** a player is typing into a Manual entry's text field (their own draft or an existing
  entry they authored)
- **THEN** WASD/hotbar/other movement hotkeys are suppressed exactly as they are while editing an
  Editor tab row, Pin Tab row, or Guestbook note

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
