# notebook-history Specification

## Purpose
TBD - created by archiving change notebook-history-tab. Update Purpose after archive.
## Requirements
### Requirement: History store persists seven event kinds in the ItemStack
The system SHALL maintain a `HistoryStore` per Notebook, serialized as `SHST v4` binary in
`ItemStack.Attributes["scribeHistory"]`. The store SHALL record entries of the following kinds:
`Crafted`, `PickedUp`, `Death`, `PvpKill`, `BossKill`, `TemporalStorm`, `LoreDiscovery`, and
`Manual`. Each entry SHALL carry a kind, an actor name (player name or empty for world events), a
detail string (Manual body, or a legacy baked sentence), a formatted in-game calendar date (used
for legacy baked rows), a stable per-entry identifier (a `Guid`, meaningful only for `Manual`
entries; empty for every other kind), a sortable in-game timestamp captured at the moment the
entry is recorded, a schema discriminator (legacy baked vs live facts), and the live fact fields
needed to format system-generated rows (subject name, other name, two opaque ref-code strings, and
an integer flavor seed). The timestamp SHALL remain distinct from the formatted date string. The
store SHALL maintain its entries in chronological order by this timestamp (ties broken by write
order) rather than by raw write/insertion order, so an entry recorded later in real time for an
earlier in-game moment (for example, a Death entry whose recording was delayed) is placed in its
correct chronological position rather than always at the end. The store SHALL be versioned with
progressive reads of v1–v4; a `v2` payload (no timestamp field) migrates by assigning each of its
entries a synthetic timestamp that preserves the payload's existing relative order and sorts
before every timestamp any entry recorded after the v3 change can produce; a `v1` payload (no
per-entry identifier field, and also no timestamp) migrates by first filling an empty identifier
for every entry, then applying the same synthetic timestamp treatment; a `v3` payload (no live
schema or fact fields) migrates by marking each entry legacy-baked with empty fact fields. The
server-side pending-death queue SHALL persist the same per-entry layout so a queued live entry
survives a restart.

#### Scenario: Fresh notebook has an empty history store
- **WHEN** a player obtains a new Notebook with no `scribeHistory` attribute
- **THEN** opening it shows an empty History tab with no entries

#### Scenario: History survives inventory moves and world restart
- **WHEN** a Notebook with history entries is moved to a different slot, then the world is
  saved and reloaded
- **THEN** all history entries are present with the same recorded facts when the notebook is next
  opened

#### Scenario: History travels with the item when traded
- **WHEN** a player gives their Notebook to another player
- **THEN** the receiving player's History tab shows all entries written while the original
  player held it

#### Scenario: A v1 payload migrates cleanly
- **WHEN** a `scribeHistory` attribute written before the identifier field existed (`SHST v1`) is
  deserialized
- **THEN** every entry loads correctly with an empty identifier, no error occurs, the entries'
  existing relative order is preserved via the same synthetic-timestamp treatment as a v2 payload,
  and each entry is treated as legacy-baked

#### Scenario: A v2 payload migrates cleanly, preserving existing order
- **WHEN** a `scribeHistory` attribute written before sortable timestamps (`SHST v2`) is
  deserialized
- **THEN** every entry loads correctly, each is assigned a synthetic timestamp that reproduces the
  payload's original relative order exactly, every one of those synthetic timestamps sorts
  before any timestamp a newly-recorded (post-v3) entry can carry, and each entry is treated as
  legacy-baked

#### Scenario: A v3 payload migrates cleanly as legacy-baked
- **WHEN** a `scribeHistory` attribute written before live facts (`SHST v3`) is deserialized
- **THEN** every entry loads correctly with its original kind, names, `Detail`, date, identifier,
  and timestamp, and is displayed as a legacy baked row

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

### Requirement: Recording a PickedUp history entry never persists a document
Recording a Notebook or Tablet's one-time PickedUp history entry (on first dialog open, or
whenever the server otherwise resolves a document host for a carried item — including the
periodic carried-notebook sweep that also detects `TemporalStorm` starts and flushes queued
history entries every ~10 seconds for every online player) SHALL persist only the history
store. It SHALL NOT cause a fresh `DocId` to be minted and persisted onto a stack the player
has not yet saved a document to themselves, regardless of how many times or from how many
call sites a document host is constructed over that stack in the meantime.

#### Scenario: PickedUp recording on a brand-new notebook writes no document
- **WHEN** the server resolves a document host over a Notebook or Tablet that has never had a
  document saved to it, and records that player's first-pickup history entry
- **THEN** the stack still has no `scribeDocument` attribute afterward — only `scribeHistory`
  was written

#### Scenario: The periodic sweep still records Death/PvpKill/BossKill/TemporalStorm on a
brand-new notebook
- **WHEN** the periodic sweep or a live history-recording event (Death, PvpKill, BossKill,
  TemporalStorm) fires while a player carries a Notebook or Tablet that has never had a
  document saved to it
- **THEN** the history entry is still recorded normally (carrying a document is not a
  precondition for history), and no `DocId` is stamped onto the stack as a side effect

#### Scenario: PickedUp recording on an already-documented notebook is unaffected
- **WHEN** a document host is constructed over a Notebook or Tablet that already has a saved
  document, and records a PickedUp entry
- **THEN** history recording proceeds exactly as before this change, and the existing document
  is untouched

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
the duration its dialog is open but which is not genuinely "on" the player. A live-schema Death
entry SHALL store facts from which the History tab can render a self-contained sentence that names
the victim, chosen by the killing damage source's cause entity (which covers both melee and
projectile attacks): when the killer is another player, the victim name, killer name, weapon/tool
category when known, damage type, and a generic-pool seed; when the killer is a creature, the
victim name, the creature's entity code, and a flavor seed; otherwise (environmental death) the
victim name, the vanilla death-cause token, and a variant seed. It SHALL NOT fall back to an
unattributed "died" record while a cause entity is resolvable. The sentence already names the
victim at display time, so the entry SHALL leave `ActorName` empty (the display prepends
"ActorName — " otherwise).

#### Scenario: Death while holding records entry
- **WHEN** a player carrying one or more Notebooks (in hotbar, backpack, character, cursor, or
  crafting-grid slots) dies from any cause
- **THEN** a Death entry is added to EACH of those notebooks with the appropriate death facts and
  the in-game timestamp, with `ActorName` empty so the rendered sentence does not repeat the
  victim's name

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
- **THEN** the Death entry stores that creature's entity code (not a finished sentence), and the
  History tab's rendered message names that creature by its variant-correct display name in the
  viewer's locale (e.g. "a nightmare drifter", "a brown bear"), drawn from the entity code rather
  than a fixed string, and does NOT fall back to the generic "<victim> died." message while the
  code is present

#### Scenario: Death by another player names the killer with a weapon-aware verb, victim-first
- **WHEN** a player holding a Notebook is killed by another player, whether by melee or
  projectile
- **THEN** the Death entry stores the victim name, killer name, and weapon/damage signal, and the
  History tab renders it from the victim's perspective — victim-first and passive ("Junkmuffin was
  slain by Raptor.") — naming the killer and using a passive kill verb chosen in the viewer's
  locale from the killer's weapon category when available (e.g. a bow → that locale's bow verb),
  and does NOT fall back to the generic "<victim> died." message

#### Scenario: Kill verb degrades gracefully for unknown weapons
- **WHEN** the killer's weapon has no recognized tool category but the damage carries a known
  damage type (e.g. a modded weapon dealing piercing damage)
- **THEN** the displayed kill verb is derived from the damage type in the viewer's locale; and when
  neither a tool category nor a damage-type mapping is available there, a generic kill verb from
  that locale's pool is used, with the stored seed preventing an immediate repeat across successive
  kills recorded on the same notebook when the locale has more than one generic

Because a live scan of the dying player's inventory can race against another mod's own death
handling (e.g. one that moves the player's items into a corpse), the system SHALL also maintain a
periodically-refreshed, per-player record of which Notebook/Tablet documents (by their own document
id) were carried on that player's person as of the last refresh. If, at the moment of death, a
document that was present in that record is no longer found by the live scan, the system SHALL
queue the Death entry — including the in-game timestamp captured at the actual moment of death and
the live facts — against that document's own id rather than dropping it. A queued entry SHALL be written into its
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

### Requirement: PvpKill event recorded when holder kills another player
The system SHALL record a `PvpKill` entry on the server, on EVERY Notebook the killer is carrying on
their person (same carried-inventory scope as the Death requirement — any `InventoryBasePlayer`
inventory except creative and ground, which now includes the crafting grid and any mod-added
carried inventory), when that player delivers the killing blow to another player. The killer SHALL
be resolved from the damage source's cause entity so that melee kills are attributed, not only
projectile kills. A live-schema PvpKill SHALL store the same victim, killer, weapon/damage, and
seed facts as the matching Death entry so both rows share one signal and each template is applied
at display time.

#### Scenario: Killing another player records entry
- **WHEN** a player carrying one or more Notebooks kills another player
- **THEN** a PvpKill entry is added to EACH of the killer's carried notebooks, and the History tab
  renders it from the killer's perspective — killer-first and active ("Raptor slew Junkmuffin.") —
  naming the victim and using the active form of the same weapon-aware verb resolved for the
  victim's Death row (the two logs share one stored signal but each reads from its own owner's
  point of view in the viewer's locale)

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
within 100 blocks of a player holding a Notebook. A live-schema BossKill SHALL store the slayer's
name and a boss key from which the History tab renders that boss's full narrative sentence in the
viewer's locale. `ActorName` SHALL be empty so the rendered sentence is not prefixed with the
slayer's name a second time.

#### Scenario: Boss dies within 100 blocks records entry
- **WHEN** an Eidolon or Mad Crow dies and the notebook holder is within 100 blocks
- **THEN** a BossKill entry is added to the holder's notebook and the History tab shows that
  boss's narrative sentence in the viewer's locale, naming the slayer

#### Scenario: Boss dies beyond 100 blocks records nothing
- **WHEN** a boss dies more than 100 blocks from the notebook holder
- **THEN** no BossKill entry is added

### Requirement: TemporalStorm event recorded at storm start for all online holders
The system SHALL record a `TemporalStorm` entry for every player currently holding a Notebook,
using the same carried-inventory scope as the Death requirement (any `InventoryBasePlayer`
inventory except creative and ground, which now includes the crafting grid and any mod-added
carried inventory), when a temporal storm begins. A live-schema TemporalStorm SHALL store the
storm-strength token (light/medium/heavy). The History tab SHALL render the localized strength
word from that token in the viewer's locale.

#### Scenario: Storm start records entry for each holder
- **WHEN** a temporal storm begins and two players are holding Notebooks
- **THEN** each of their notebooks gains a TemporalStorm entry with the storm strength, and each
  player's History tab shows that strength in their own locale

#### Scenario: No notebooks held during storm start records nothing
- **WHEN** a temporal storm begins and no player is holding a Notebook
- **THEN** no TemporalStorm entries are added anywhere

#### Scenario: Notebook in a mod-added bonus inventory records during a storm
- **WHEN** a temporal storm begins while a player has a Notebook stored in an inventory a
  third-party mod added directly to their own inventory manager
- **THEN** that notebook gains a TemporalStorm entry — inclusion is determined by the inventory's
  type, not by recognizing its name

### Requirement: System-generated History rows render in the viewer's language
When the History tab draws a live-schema `Death`, `PvpKill`, `BossKill`, or `TemporalStorm` entry,
the system SHALL format that row's sentence and calendar date in the viewing player's current
locale. The stored facts SHALL NOT include a finished sentence. Kind labels SHALL continue to
resolve in the viewer's locale as they do today. `Manual` entry text SHALL remain the author's
typed string. `Crafted` and `PickedUp` rows SHALL continue to render from the kind label and
`ActorName` only.

#### Scenario: A live Death row follows the viewer, not the server
- **WHEN** a live-schema creature-Death entry is stored while the server locale is English, and a
  player whose client locale is not English opens that notebook's History tab
- **THEN** the Death sentence is taken from that client's `scribe-mob-death-*` pool (or its
  fallbacks) and is not the English sentence that would have been baked under the previous schema

#### Scenario: Handing a notebook over changes live wording, not the events
- **WHEN** player A records a live-schema PvpKill on a notebook and gives the notebook to player B,
  whose client locale differs from A's
- **THEN** player B's History tab still shows that PvpKill event with the same names and weapon
  signal, formatted with B's locale templates and verbs

#### Scenario: Manual notes are not rewritten
- **WHEN** a player whose client locale is not English opens a notebook containing a Manual entry
  typed in English
- **THEN** the Manual body is still the original English text

#### Scenario: Live dates follow the viewer's date format
- **WHEN** a live-schema entry is shown on the History tab
- **THEN** its calendar date is built from the stored in-game timestamp via `scribe:date-format`
  and the vanilla month-name keys in the viewer's locale, not from a server-formatted date string

### Requirement: Pre-v4 History entries keep their baked text
An entry deserialized from `SHST` v1–v3, or any entry whose schema is the legacy baked form, SHALL
display its stored `Detail` sentence and stored `InGameDate` string unchanged. The system SHALL NOT
attempt to re-translate or re-index those strings.

#### Scenario: An old English death line stays English
- **WHEN** a notebook contains a v3 Death entry whose `Detail` is an already-substituted English
  sentence, and a player whose client locale is not English opens History
- **THEN** that row still shows the stored English sentence and stored date

#### Scenario: New deaths on the same notebook are live
- **WHEN** that same notebook later records a new Death after this change
- **THEN** the new row is live-schema and renders in the viewer's locale, while the old row stays
  baked

### Requirement: Viewer flavor pools may be shorter than English
At display time the system SHALL discover the viewer's contiguous `scribe-mob-death-N` pool (and,
separately, the viewer's contiguous `scribe-pvp-verb-generic-N` pool) with existence checks that do
not format a template against zero arguments and do not treat an English fallback as a hit. A
stored flavor seed SHALL be mapped into that pool by remainder (`seed` modulo pool size) so the
chosen index always exists in the viewer's file. A locale MAY ship as few as one entry in either
pool. English's shipped catalog SHALL remain reachable for an English viewer.

#### Scenario: A two-line locale never shows an English joke
- **WHEN** the viewer's locale defines only `scribe-mob-death-0` and `scribe-mob-death-1`, and a
  live Death entry's flavor seed would index past 1 in the English catalog
- **THEN** the displayed line is one of those two keys, never an English `scribe-mob-death-*` value
  and never a raw key

#### Scenario: An English viewer still reaches every shipped joke
- **WHEN** the viewer's locale defines the full contiguous English `scribe-mob-death-0` through
  `scribe-mob-death-(N-1)` catalog
- **THEN** every one of those N entries remains reachable as a possible displayed line across
  recorded seeds

#### Scenario: An empty viewer pool falls back without echoing a key
- **WHEN** the viewer's locale defines no `scribe-mob-death-*` keys at all
- **THEN** the Death row still names the victim and creature using the shipped `death-slain-by`
  (or equivalent) fallback rather than printing a raw lang key

### Requirement: PvP verbs resolve in the viewer's locale without English leakage
When formatting a live PvP Death or PvpKill row, the system SHALL pick a verb with the same
three-tier order as today (held-tool category, then damage type, then generic pool), using
existence checks on the **viewer's** locale only. A missing tool or damage key in that locale
SHALL fall through to the next tier. `Lang.Get`'s English fallback SHALL NOT count as a hit. The
stored signal SHALL be the tool category and/or damage type plus a generic-pool seed, not a
resolved English verb string. The Death row SHALL use the passive sentence template and the
PvpKill row the active template, sharing one resolved verb (active vs participle override in the
viewer's file).

#### Scenario: An omitted bow verb falls through
- **WHEN** the killer used a bow and the viewer's locale has no `scribe-pvp-verb-tool-bow` key
- **THEN** the displayed verb comes from the viewer's damage-type key if present, otherwise from
  the viewer's generic pool, and is not the English word "pincushioned"

#### Scenario: Participle override is locale-specific
- **WHEN** the viewer's locale defines a participle override for the chosen verb key
- **THEN** the victim's Death line uses that participle and the killer's PvpKill line uses the
  active verb

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

### Requirement: Mob-death flavor pool size discovery does not trigger translation-format warnings
The system SHALL discover the size of the viewer's `scribe-mob-death-N` flavor pool without
formatting any pool template against zero arguments. Discovering the pool's size SHALL NOT trigger
the translation service's own error/warning logging, regardless of how many entries the pool
contains or how many placeholders its templates use. Recording a creature death SHALL store a
flavor seed without formatting a pool template. Display SHALL map that seed onto the viewer's pool
as specified in the viewer-pool requirement.

#### Scenario: Creature kill selects a flavored line with no warning logged
- **WHEN** a player carrying a Notebook is killed by a creature and a player opens that notebook's
  History tab
- **THEN** the Death row's message is a fully-substituted line from the viewer's `scribe-mob-death-N`
  pool naming the creature, and no translation-format warning is logged as a side effect of
  discovering the pool or formatting the line

#### Scenario: Pool size reflects all shipped entries
- **WHEN** the viewer's mob-death flavor pool contains N contiguous entries (`scribe-mob-death-0`
  through `scribe-mob-death-(N-1)`)
- **THEN** every one of those N entries remains reachable as a possible displayed line

### Requirement: Death and PvpKill message construction is skipped when no relevant Notebook exists
Before recording any Death or PvpKill facts (entity code, weapon signal, flavor seed, or vanilla
cause token), the system SHALL check whether at least one relevant party carries a Notebook: for a
non-PvP death, the dying player; for a PvP kill, the dying player OR the killing player. When
neither relevant party carries a Notebook, the system SHALL skip fact recording entirely and record
nothing, avoiding wasted computation on a death event with no Notebook anywhere to record it. When
at least one relevant party carries a Notebook, fact recording proceeds and is written to every
Notebook the relevant party carries.

#### Scenario: Non-PvP death with no notebook constructs no message
- **WHEN** a player who carries no Notebook dies to a creature or environmental cause
- **THEN** no Death facts are recorded and no history entry is written anywhere

#### Scenario: PvP kill with neither party carrying a notebook constructs no message
- **WHEN** a player who carries no Notebook is killed by another player who also carries no
  Notebook
- **THEN** no Death or PvpKill facts are recorded and no history entry is written anywhere

#### Scenario: PvP kill where only the killer carries a notebook still constructs the message
- **WHEN** a player carrying no Notebook is killed by a player who does carry a Notebook
- **THEN** the shared PvP facts are recorded, a PvpKill entry is written to the killer's
  notebook(s), and no Death entry is written for the victim

#### Scenario: PvP kill where only the victim carries a notebook still constructs the message
- **WHEN** a player carrying a Notebook is killed by a player who carries no Notebook
- **THEN** the shared PvP facts are recorded, a Death entry is written to the victim's
  notebook(s), and no PvpKill entry is written for the killer
