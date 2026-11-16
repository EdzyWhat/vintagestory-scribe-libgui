## MODIFIED Requirements

### Requirement: Guestbook visit dates with a timestamp follow the viewer
When the Guestbook tab draws an entry that carries a real in-game timestamp, the system SHALL
format that entry's Date of visit from the timestamp via the viewer's Date order template (`scribe:date-format` under Language default) and the vanilla
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
- **THEN** the Date of visit shows only the in-game date (e.g. "8 August, Year 1") with no time component

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
