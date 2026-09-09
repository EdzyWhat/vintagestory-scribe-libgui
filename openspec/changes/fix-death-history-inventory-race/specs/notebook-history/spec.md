## MODIFIED Requirements

### Requirement: History store persists seven event kinds in the ItemStack
The system SHALL maintain a `HistoryStore` per Notebook, serialized as `SHST v3` binary in
`ItemStack.Attributes["scribeHistory"]`. The store SHALL record entries of the following kinds:
`Crafted`, `PickedUp`, `Death`, `PvpKill`, `BossKill`, `TemporalStorm`, `LoreDiscovery`, and
`Manual`. Each entry SHALL carry a kind, an actor name (player name or empty for world events), a
detail string, a formatted in-game calendar date, a stable per-entry identifier (a `Guid`,
meaningful only for `Manual` entries; empty for every other kind), and a sortable in-game
timestamp captured at the moment the entry is recorded — distinct from the formatted date string,
which remains display-only and unchanged in format. The store SHALL maintain its entries in
chronological order by this timestamp (ties broken by write order) rather than by raw
write/insertion order, so an entry recorded later in real time for an earlier in-game moment (for
example, a Death entry whose recording was delayed) is placed in its correct chronological
position rather than always at the end. The store SHALL be versioned with `PriorVersion` and
`ApplyMigrations` scaffolding following the `ScribeDocumentCodec` pattern; a `v2` payload (no
timestamp field) migrates by assigning each of its entries a synthetic timestamp that preserves
the payload's existing relative order and sorts before every timestamp any entry recorded after
this change can produce; a `v1` payload (no per-entry identifier field, and also no timestamp)
migrates by first filling an empty identifier for every entry, then applying the same synthetic
timestamp treatment.

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
- **WHEN** a `scribeHistory` attribute written before the identifier field existed (`SHST v1`) is
  deserialized
- **THEN** every entry loads correctly with an empty identifier, no error occurs, and the entries'
  existing relative order is preserved via the same synthetic-timestamp treatment as a v2 payload

#### Scenario: A v2 payload migrates cleanly, preserving existing order
- **WHEN** a `scribeHistory` attribute written before this change (`SHST v2`, no sortable
  timestamp) is deserialized
- **THEN** every entry loads correctly, each is assigned a synthetic timestamp that reproduces the
  payload's original relative order exactly, and every one of those synthetic timestamps sorts
  before any timestamp a newly-recorded (post-migration) entry can carry — matching the fact that
  they really were recorded before this change shipped

#### Scenario: Two entries recorded on the same in-game day keep their real relative order
- **WHEN** two entries are recorded for the same displayed in-game calendar date but at different
  real moments (for example, a player dies twice in the same in-game day)
- **THEN** they appear in the order they actually occurred, not merely the order their identical
  displayed dates happen to sort by insertion

### Requirement: Per-kind caps enforce a rolling window
The system SHALL enforce the following per-kind caps, dropping the chronologically oldest entry of
that kind (by the store's own ordering, per the requirement above) when the cap is reached for
sliding-window kinds:

| Kind          | Cap | Policy        |
|---------------|-----|---------------|
| Crafted       | 1   | never replaced (only ever written once) |
| PickedUp      | unlimited | deduped by ActorName (one entry per player ever) |
| Death         | 30  | sliding window (chronologically oldest dropped) |
| PvpKill       | 30  | sliding window |
| BossKill      | 20  | sliding window |
| TemporalStorm | 10  | sliding window |
| Manual        | 30  | sliding window (chronologically oldest Manual entry dropped) |

#### Scenario: Death cap drops oldest
- **WHEN** a notebook already has 30 Death entries and the holder dies again
- **THEN** the chronologically oldest Death entry is removed and the new one is inserted in its
  correct chronological position, keeping exactly 30

#### Scenario: TemporalStorm cap drops oldest
- **WHEN** a notebook already has 10 TemporalStorm entries and another storm begins while its
  holder is online
- **THEN** the chronologically oldest TemporalStorm entry is removed and the new one is inserted in
  its correct chronological position, keeping exactly 10

#### Scenario: PickedUp deduplication
- **WHEN** a player who already has a PickedUp entry for their name opens the notebook again
- **THEN** no new PickedUp entry is added

#### Scenario: Manual cap drops the oldest manual entry
- **WHEN** a notebook already has 30 Manual entries and its holder successfully adds another
- **THEN** the chronologically oldest Manual entry (regardless of which player authored it) is
  removed and the new one is inserted in its correct chronological position, keeping exactly 30

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

Because a live scan of the dying player's inventory can race against another mod's own death
handling (e.g. one that moves the player's items into a corpse), the system SHALL also maintain a
periodically-refreshed, per-player record of which Notebook/Tablet documents (by their own document
id) were carried on that player's person as of the last refresh. If, at the moment of death, a
document that was present in that record is no longer found by the live scan, the system SHALL
queue the Death entry — including the in-game timestamp captured at the actual moment of death —
against that document's own id rather than dropping it. A queued entry SHALL be written into its
target document the next time that specific document is observed in any live carried slot,
regardless of who is currently carrying it — never redirected to a different document, including
one newly crafted or received afterward by the same player. Because the queued entry carries its
original death-time timestamp, it SHALL be inserted into its correct chronological position when
flushed (per the store-ordering requirement above), exactly as if it had been written at the moment
of death, even if other entries were added to that same notebook during the delay. A queued entry
SHALL persist across a server restart. Queued entries SHALL NOT be pruned by age or count. This
fallback applies only to the Death entry of the player who died; it does not extend to the killer's
side of a PvP kill (see the PvpKill requirement), since nothing evicts the killer's inventory when
they are not the one dying.

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
- **WHEN** a player dies while NOT holding a Notebook, and no Notebook was on their person as of
  the last periodic snapshot either
- **THEN** no Death entry is added to any Notebook, live or queued

#### Scenario: A notebook evicted by a competing mod's death handling still records, on a delay
- **WHEN** a player was carrying a Notebook as of the last periodic snapshot, but by the time
  Scribe's own death handling runs, another mod's own `OnEntityDeath` handler has already moved
  that Notebook out of the player's inventory (e.g. into a corpse), so Scribe's live scan finds
  nothing
- **THEN** the Death entry is queued against that specific Notebook's document id instead of being
  dropped, and is written into it the next time that document is observed in any live carried
  slot — whether the original player reclaims it or someone else picks it up

#### Scenario: A queued entry never lands on the wrong notebook
- **WHEN** a player has a Death entry queued against a specific document, and before that document
  is ever seen again, the same player crafts a brand-new Notebook or receives one from another
  player
- **THEN** the new or received notebook does NOT gain the queued entry — it is written only to the
  exact document it was queued against, never substituted for a different one

#### Scenario: A flushed entry inserts into its correct chronological position
- **WHEN** a queued Death entry is flushed into its notebook after that notebook has already
  gained other entries, recorded at real in-game moments both before and after the actual death,
  during the gap
- **THEN** the queued entry is inserted at the chronological position matching the actual moment of
  death, not appended after everything already present — it displays exactly where it would have
  if it had been written immediately

#### Scenario: A queued entry survives a server restart
- **WHEN** a Death entry is queued against a document, and the server restarts before that
  document is next seen in a live carried slot
- **THEN** the queued entry is still present and is written into the document once it is next
  observed after the restart

#### Scenario: A queued entry is never pruned
- **WHEN** a document a Death entry was queued against is never seen again for an extended period
  (or ever again)
- **THEN** the queued entry remains stored indefinitely — it is not dropped by age or by any count
  limit
