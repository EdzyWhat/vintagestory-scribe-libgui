## ADDED Requirements

### Requirement: History calendar dates with a real timestamp follow the viewer
When the History tab draws any entry whose stored in-game timestamp is a real calendar day (a
non-negative `Calendar.TotalDays` value captured at record time), the system SHALL format that
row's calendar date from the timestamp via `scribe:date-format` and the vanilla month-name keys
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

## MODIFIED Requirements

### Requirement: Pre-v4 History entries keep their baked text
An entry deserialized from `SHST` v1–v3, or any entry whose schema is the legacy baked form, SHALL
display its stored `Detail` sentence unchanged. The system SHALL NOT attempt to re-translate or
re-index those strings. Calendar dates on those rows SHALL follow the History calendar-dates
requirement: a real (non-negative) timestamp is formatted in the viewer's locale; a synthetic
pre-v3 timestamp SHALL keep the stored `InGameDate` string, because that timestamp is sort-order
only and is not a calendar day.

#### Scenario: An old English death line stays English
- **WHEN** a notebook contains a v3 Death entry whose `Detail` is an already-substituted English
  sentence, and a player whose client locale is not English opens History
- **THEN** that row still shows the stored English sentence

#### Scenario: A pre-v3 migrated date stays as stored
- **WHEN** a notebook contains a v1 or v2 History entry whose timestamp is a synthetic negative
  used only to preserve order, and a player whose client locale is not English opens History
- **THEN** that row's calendar date is the stored `InGameDate` string, not a date rebuilt from the
  synthetic timestamp

#### Scenario: New deaths on the same notebook are live
- **WHEN** that same notebook later records a new Death after live-schema History shipped
- **THEN** the new row is live-schema and renders in the viewer's locale, while the old row's
  sentence stays baked
