## ADDED Requirements

### Requirement: Assignment calendar dates with a timestamp follow the viewer
When Inbox or Sent Assignment History draws an assignment date (sent, received, accepted,
declined, cancelled, discarded, completed, or redirected) that carries a real in-game
timestamp, the system SHALL format that date from the timestamp via `scribe:date-format` and
the vanilla month-name keys in the viewing player's current locale. The date SHALL remain
date-only. An assignment with no timestamp (a store blob from before this change) SHALL keep
showing its stored identity date string. Task and note text SHALL remain the author's typed
string.

#### Scenario: A new assigned date follows the viewer, not the server
- **WHEN** an assignment is sent while the server locale is English, and a player whose client
  locale is not English expands that row in Inbox
- **THEN** the sent date is built from the stored timestamp in that client's locale, not the
  English date string the server used as the identity stamp

#### Scenario: A pre-timestamp assignment keeps its baked date
- **WHEN** Inbox contains an assignment stored before timestamps existed, and a player whose
  client locale is not English expands that row
- **THEN** that row's dates are still the stored date strings

#### Scenario: Transition dates follow the viewer
- **WHEN** an assignment is accepted while the server locale is English, and a player whose
  client locale is not English expands that row
- **THEN** the accepted date is built from the accepted timestamp in that client's locale
