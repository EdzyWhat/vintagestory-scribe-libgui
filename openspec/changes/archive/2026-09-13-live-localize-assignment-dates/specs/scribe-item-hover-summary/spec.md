## ADDED Requirements

### Requirement: Task Notice assigned date with a timestamp follows the viewer
When a sealed Task Notice hover card (or the matching Accept-dialog / held-item "Assigned by"
line) draws an assigned date that carries a real in-game timestamp, the system SHALL format that
date from the timestamp via `scribe:date-format` and the vanilla month-name keys in the viewing
player's current locale. A notice whose assignment has no timestamp SHALL keep showing its
stored identity date string.

#### Scenario: A sealed Task Notice date follows the viewer, not the server
- **WHEN** a Task Notice is sealed while the server locale is English, and a player whose client
  locale is not English hovers that notice
- **THEN** the Assigned-by date is built from the stored timestamp in that client's locale, not
  the English date string the server stamped

#### Scenario: A pre-timestamp notice keeps its baked date
- **WHEN** a player whose client locale is not English hovers a sealed Task Notice stored before
  timestamps existed
- **THEN** the Assigned-by date is still the stored date string
