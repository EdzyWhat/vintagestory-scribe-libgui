## MODIFIED Requirements

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
- **THEN** its calendar date is built from the stored in-game timestamp via the viewer's Date order template (`scribe:date-format` under Language default)
  and the vanilla month-name keys in the viewer's locale, not from a server-formatted date string

### Requirement: History calendar dates with a real timestamp follow the viewer
When the History tab draws any entry whose stored in-game timestamp is a real calendar day (a
non-negative `Calendar.TotalDays` value captured at record time), the system SHALL format that
row's calendar date from the timestamp via the viewer's Date order template (`scribe:date-format` under Language default) and the vanilla month-name keys
in the viewing player's current locale. This SHALL apply to `Crafted`, `PickedUp`, and `Manual`
rows as well as live-schema system rows, including legacy-baked rows that already carry a real
timestamp (`SHST` v3 and later). Kind labels and `Manual` body text are unchanged by this
requirement. The system SHALL NOT reverse-derive a timestamp from a stored date string.

#### Scenario: A Crafted date follows the viewer, not the server
- **WHEN** a Crafted entry is stored while the server locale is English, and a player whose
  client locale is not English opens that notebook's History tab
- **THEN** the Crafted row's calendar date is built from the stored timestamp in that client's
  locale, not the English date string the server would have formatted

#### Scenario: A Manual date follows the viewer; the note text does not
- **WHEN** a player whose client locale is not English opens a notebook containing a Manual
  entry whose body was typed in English
- **THEN** the Manual calendar date is built from the stored timestamp in that client's locale,
  and the Manual body is still the original English text

#### Scenario: A v3 baked Death keeps its sentence and live-formats its date
- **WHEN** a notebook contains a v3 Death entry whose `Detail` is an already-substituted English
  sentence and whose timestamp is a real calendar day, and a player whose client locale is not
  English opens History
- **THEN** that row still shows the stored English sentence, and its calendar date is built from
  the timestamp in the viewer's locale
