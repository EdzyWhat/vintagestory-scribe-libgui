## MODIFIED Requirements

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
