## ADDED Requirements

### Requirement: Guestbook visit dates with a timestamp follow the viewer
When the Guestbook tab draws an entry that carries a real in-game timestamp, the system SHALL
format that entry's Date of visit from the timestamp via `scribe:date-format` and the vanilla
month-name keys in the viewing player's current locale. The date SHALL remain date-only (no time
of day). An entry that has no timestamp (a v1 blob) SHALL keep showing its stored identity date
string. Player-authored note text SHALL remain the author's typed string.

#### Scenario: A new visit date follows the viewer, not the server
- **WHEN** a visit is recorded while the server locale is English, and a player whose client
  locale is not English opens that lectern's Guestbook tab
- **THEN** the Date of visit is built from the stored timestamp in that client's locale, not the
  English date string the server used as the entry's identity

#### Scenario: A v1 visit keeps its baked date
- **WHEN** a lectern contains a v1 guestbook entry with no timestamp, and a player whose client
  locale is not English opens the Guestbook tab
- **THEN** that row's Date of visit is still the stored date string

#### Scenario: Notes are not rewritten
- **WHEN** a player whose client locale is not English opens a guestbook whose notes were typed
  in English
- **THEN** each note body is still the original English text

### Requirement: Guestbook store persists a timestamp with v1 compatibility
The guestbook SHALL be serialized as `SGBK v2` binary in the block entity's tree attributes.
Each v2 entry SHALL carry the player's display name, the formatted identity date string, the
optional note, and a numeric in-game timestamp captured at record time. The store SHALL read
v1 blobs (name, date string, note; no timestamp) and treat those entries as having no timestamp.
A version below 1 or above 2 SHALL fail-safe to an empty guestbook rather than partially
reading. Note edits, per-day dedup, and client focus keys SHALL continue to address an entry by
`(playerName, inGameDate)` — the identity date string, not the displayed locale form.

#### Scenario: A v2 blob round-trips the timestamp
- **WHEN** a guestbook with a timestamped visit is saved and reloaded
- **THEN** that visit still has the same player name, identity date string, note, and timestamp

#### Scenario: A v1 blob loads without a timestamp
- **WHEN** a guestbook attribute written before timestamps (`SGBK v1`) is deserialized
- **THEN** every entry loads with its original name, date string, and note, and has no timestamp

#### Scenario: Note edit still uses the identity date string
- **WHEN** a player whose client locale is not English edits the note on their own timestamped
  visit
- **THEN** the edit is addressed by the stored identity date string and updates that visit, not
  a different day's entry

## MODIFIED Requirements

### Requirement: Server records a visitor entry on GUI open
When a player opens the Lectern GUI, the server SHALL record a visitor entry consisting of the
player's display name, the current in-game calendar **date only** (no time component) as an
identity string, and a numeric in-game timestamp for that same instant. The entry SHALL be
recorded at most once per player per in-game day — opening the GUI multiple times on the same
day SHALL NOT produce duplicate entries. Dedup SHALL use `(playerName, identity date string)`,
not the locale-formatted display date. The recording SHALL be server-authoritative.

#### Scenario: First open of the day creates an entry
- **WHEN** a player opens the Lectern GUI for the first time on a given in-game day
- **THEN** the server adds an entry `{ playerName, inGameDate, timestamp }` to the guestbook

#### Scenario: Repeated opens on the same day are idempotent
- **WHEN** a player opens the same Lectern GUI more than once on the same in-game day
- **THEN** only one entry for that player+day combination exists in the guestbook

#### Scenario: Different players each get their own entry
- **WHEN** two different players open the same Lectern on the same in-game day
- **THEN** the guestbook contains one entry per player

#### Scenario: Entry is recorded on a new day even if the player visited before
- **WHEN** a player who has a prior entry opens the Lectern on a later in-game day
- **THEN** a new entry is added for the new date; the prior entry is retained

### Requirement: Guestbook design is block-agnostic for forward compatibility
The Core `GuestbookStore` and `GuestbookEntry` types SHALL have no references to
`BlockEntityScribeLectern` — the Mod layer passes the identity date string and the numeric
in-game timestamp in. This allows the Desk (v0.3) to reuse the same types without modification.

#### Scenario: GuestbookStore has no VS API dependency
- **WHEN** the Core.Tests project compiles and runs with no game install
- **THEN** `GuestbookStore` and `GuestbookEntry` compile and all their unit tests pass
