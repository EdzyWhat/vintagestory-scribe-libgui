## MODIFIED Requirements

### Requirement: Guestbook tab displays entries as a two-column table
The Lectern GUI SHALL expose a Guestbook nav tab positioned as the 4th nav slot (after Pins,
before the Settings gear). Its tooltip SHALL read `"Guest Book"` (two words). The tab SHALL NOT
be the active view on dialog open — the lectern opens in Read view by default.

The tab SHALL display entries as a two-column table (plus an editable Note field):
- Column 1 header: **"Visitor"** — the player's display name, in Caudex Bold.
- Column 2 header: **"Date of visit"** — the in-game calendar date (date only, no time), in Caudex Bold at `0.8 ×` the window font size and `alpha = 0.8` (slightly smaller and slightly transparent to de-emphasise it).
- Column 3 header: **"Note"** — a short optional note left by the visitor (max 140 chars), in Caudex Bold.
- Rows SHALL be displayed in reverse-chronological order (most-recent entry first).
- A player's own entry SHALL show the Note field as an editable text input; all other entries SHALL show Note as read-only plain text.
- When a player has more than one entry (they visited on more than one in-game day), EACH of that player's own entries SHALL present its OWN independently editable Note field. Clicking one such field SHALL place the caret in that field ONLY (no other field SHALL show a caret simultaneously), and keystrokes SHALL be routed to the clicked field regardless of its position in the list.
- The scroll area SHALL always display a visible scrollbar track (`AutoHide = false`).

#### Scenario: Tab shows entries as a two-column table, newest-first
- **WHEN** a player opens the Guestbook tab on a lectern with multiple entries
- **THEN** a table is shown with "Visitor" and "Date of visit" (and "Note") headers in Caudex Bold, rows listed most-recent-first

#### Scenario: Date of visit is date-only
- **WHEN** a player's entry is displayed
- **THEN** the Date of visit shows only the in-game date (e.g. "8 August, Year 0") with no time component

#### Scenario: Tab is 4th in the nav column, not the default view
- **WHEN** a player opens the Lectern dialog
- **THEN** the lectern opens in Read view, not the Guestbook tab

#### Scenario: Own-entry Note is editable; others are read-only
- **WHEN** a player views the Guestbook tab
- **THEN** their own entry's Note field is an editable input (max 140 chars); other players' Note fields are plain text

#### Scenario: Each of a player's multiple own entries is independently editable
- **WHEN** a player who has visited on more than one in-game day opens the Guestbook tab and clicks the Note field of a specific one of their entries
- **THEN** only that field shows a caret and receives keystrokes; the player's other own-entry Note fields show no caret and are unaffected

#### Scenario: Empty state when no entries
- **WHEN** a player opens the Guestbook tab on a lectern that has never been opened before
- **THEN** the tab shows an empty state (e.g. "No visitors yet")

## ADDED Requirements

### Requirement: A guestbook note is addressed by its specific entry
Editing a guestbook note SHALL target the specific entry it belongs to, addressed by the pair
`(playerName, inGameDate)`, not merely by document and player. The edit path — the client-to-server
message, the server handler, and the Core store operation — SHALL carry the entry's `inGameDate`
discriminator so that a player who has multiple entries (one per in-game day visited) updates only
the intended day's note. A player SHALL only be permitted to edit notes on entries whose
`playerName` matches their own. When no entry matches the addressed `(playerName, inGameDate)`, the
edit SHALL be a no-op (no other entry is modified).

#### Scenario: Editing one day's note leaves other days' notes intact
- **WHEN** a player who has entries on two different in-game days edits the note on the newer day's entry
- **THEN** only the newer day's entry note is changed; the older day's entry note is unchanged

#### Scenario: Note edit routes to the matching entry, not the first entry
- **WHEN** the server receives a guestbook note edit addressed by `(playerName, inGameDate)`
- **THEN** the note is written to the entry whose `PlayerName` and `InGameDate` both match, not simply the player's first entry

#### Scenario: A note edit for a non-existent entry is a no-op
- **WHEN** the server receives a guestbook note edit whose `(playerName, inGameDate)` matches no stored entry
- **THEN** no entry is modified and no change is synced

#### Scenario: Note edits persist and sync per entry
- **WHEN** a player edits the note on a specific entry and the world is later reloaded
- **THEN** that entry retains its note and other entries retain theirs, matching what was last committed

### Requirement: A player's note-less entries are pruned past a soft per-player cap
To keep a single player's guestbook history readable, once adding a new entry would give that player
more than a soft cap of entries (10), the store SHALL prune that player's OLDEST entry that carries no
note text. An entry that carries a note SHALL never be pruned, and the entry just added SHALL never be
the one pruned. If every one of the player's other entries carries a note, no pruning occurs and the
player MAY exceed the soft cap. Pruning SHALL affect only the entry-adding player's own entries, never
another player's. This soft per-player cap is independent of the hard whole-store entry cap.

#### Scenario: Oldest note-less entry is pruned when a player exceeds the soft cap
- **WHEN** a player whose entries are all note-less reaches the soft per-player cap and visits again on a new day
- **THEN** their oldest note-less entry is removed and the newly added day's entry is kept, holding their entry count at the cap

#### Scenario: Entries with notes are never pruned
- **WHEN** a player who has left a note on every entry up to the soft cap visits again on a new day
- **THEN** no entry is removed and the player's entry count exceeds the soft cap

#### Scenario: Pruning targets the oldest note-LESS entry, not the oldest overall
- **WHEN** a player's oldest entry carries a note but a later entry does not, and the player exceeds the soft cap
- **THEN** the noted oldest entry is kept and the oldest note-less entry is pruned instead

#### Scenario: Pruning only affects the acting player
- **WHEN** one player exceeds the soft cap and triggers pruning
- **THEN** only that player's own note-less entry is considered; other players' entries are untouched
