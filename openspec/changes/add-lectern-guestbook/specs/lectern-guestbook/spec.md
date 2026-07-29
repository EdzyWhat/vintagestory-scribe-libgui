## ADDED Requirements

### Requirement: Server records a visitor entry on GUI open
When a player opens the Lectern GUI, the server SHALL record a visitor entry consisting of the
player's display name, the player's current group membership (all group names comma-joined;
`"-"` if the player belongs to no groups), and the current in-game calendar date. The entry
SHALL be recorded at most once per player per in-game day — opening the GUI multiple times on
the same day SHALL NOT produce duplicate entries. Group names are captured as a snapshot at
record time (from `IPlayer.Groups[].GroupName`). The recording SHALL be server-authoritative.

#### Scenario: First open of the day creates an entry
- **WHEN** a player opens the Lectern GUI for the first time on a given in-game day
- **THEN** the server adds an entry `{ playerName, groups, inGameDate }` to the guestbook

#### Scenario: Repeated opens on the same day are idempotent
- **WHEN** a player opens the same Lectern GUI more than once on the same in-game day
- **THEN** only one entry for that player+day combination exists in the guestbook

#### Scenario: Different players each get their own entry
- **WHEN** two different players open the same Lectern on the same in-game day
- **THEN** the guestbook contains one entry per player

#### Scenario: Entry is recorded on a new day even if the player visited before
- **WHEN** a player who has a prior entry opens the Lectern on a later in-game day
- **THEN** a new entry is added for the new date; the prior entry is retained

### Requirement: Guestbook entries persist across world reload
Guestbook entries SHALL be persisted in the block entity's tree attributes alongside the
document, using the same `ToTreeAttributes` / `FromTreeAttributes` pattern. Entries SHALL
survive world save/load and block chunk unload/reload.

#### Scenario: Entries survive server restart
- **WHEN** a lectern has guestbook entries and the world is saved and reloaded
- **THEN** the same entries are present after reload

#### Scenario: Entries survive chunk unload
- **WHEN** the chunk containing the lectern is unloaded and later reloaded
- **THEN** the guestbook entries are unchanged

### Requirement: Rolling cap limits total entries
The guestbook SHALL enforce a maximum entry count (default 100). When a new entry would exceed
the cap, the oldest entry SHALL be dropped to make room. The cap SHALL be a server-side
constant (not yet user-configurable in v1).

#### Scenario: New entry at cap drops the oldest
- **WHEN** the guestbook is at its 100-entry cap and a new entry is recorded
- **THEN** the oldest entry is removed and the new entry is added, keeping the total at 100

### Requirement: Guestbook tab displays entries as a read-only two-column table
The Lectern GUI SHALL expose a Guestbook nav tab positioned as the 4th nav slot (after Pins,
before the Settings gear). The tab SHALL NOT be the active view on dialog open — the lectern
opens in Read view by default regardless of whether the player has previously viewed the Guestbook tab.

The tab SHALL display entries as a three-column table:
- Column 1 header: **"Visitor"** — the player's display name.
- Column 2 header: **"Group"** — the player's group memberships at time of visit, comma-joined; `"-"` if none.
- Column 3 header: **"Date of visit"** — the in-game calendar date of the entry.
- All three column headers SHALL be rendered in the title font (Caudex Bold).
- Rows SHALL be displayed in reverse-chronological order (most-recent entry first).
- The table SHALL be read-only — no player can edit or delete individual entries from the GUI.

#### Scenario: Tab shows entries as a three-column table, newest-first
- **WHEN** a player opens the Guestbook tab on a lectern with multiple entries
- **THEN** a table is shown with "Visitor", "Group", and "Date of visit" column headers in Caudex Bold, and rows listed most-recent-first

#### Scenario: Group column shows dash when player has no groups
- **WHEN** the visiting player belongs to no groups
- **THEN** the Group column for their entry shows "-"

#### Scenario: Group column shows all groups comma-joined
- **WHEN** the visiting player belongs to two groups "Builders" and "Explorers"
- **THEN** the Group column shows "Builders, Explorers"

#### Scenario: Tab is 4th in the nav column, not the default view
- **WHEN** a player opens the Lectern dialog
- **THEN** the lectern opens in Read view, not the Guestbook tab

#### Scenario: Tab is read-only
- **WHEN** a player views the Guestbook tab
- **THEN** there are no controls to edit or delete individual entries

#### Scenario: Empty state when no entries
- **WHEN** a player opens the Guestbook tab on a lectern that has never been opened before
- **THEN** the tab shows an empty state (e.g. "No visitors yet")

### Requirement: Updated guestbook syncs to the opening client
After the server writes a new visitor entry, it SHALL send the updated guestbook to the
opening client so the Guestbook tab reflects the just-recorded entry without requiring a
second open.

#### Scenario: Client sees their own entry immediately
- **WHEN** a player opens the Lectern GUI and triggers a new visitor entry
- **THEN** the Guestbook tab on that client shows the new entry without needing to close and reopen

### Requirement: Guestbook design is block-agnostic for forward compatibility
The Core `GuestbookStore` and `GuestbookEntry` types SHALL have no references to
`BlockEntityScribeLectern` — the Mod layer passes the pre-formatted group string and date string
in. This allows the Desk (v0.3) to reuse the same types without modification.

#### Scenario: GuestbookStore has no VS API dependency
- **WHEN** the Core.Tests project compiles and runs with no game install
- **THEN** `GuestbookStore` and `GuestbookEntry` compile and all their unit tests pass
