## ADDED Requirements

### Requirement: The Quest Link picker only offers quests the player has started
The "Add Quest Link" picker SHALL list only catalog entries the player has been observed to have
started (accepted or completed) during the current client session, rather than every entry in the
installed backend's static catalog. "Started" is determined per backend from the same server-synced
state the auto-detect watcher already reads: for Progression Framework, any recorded status
(`active` or `completed`) for that quest code; for VS Quest, a quest whose acceptance has been
observed via a loaded quest-giver entity this session. A quest not yet observed as started SHALL be
hidden from the picker, even if it exists in the installed catalog and even if the player started it
in a prior session whose quest-giver hasn't been encountered again yet.

#### Scenario: A started Progression Framework quest appears in the picker
- **WHEN** the player has an "active" or "completed" status recorded for a Progression Framework
  quest this session
- **THEN** that quest appears as a candidate in the Quest Link picker

#### Scenario: An unstarted quest is hidden from the picker
- **WHEN** a catalog entry exists for a quest the player has never been observed to accept or
  complete this session
- **THEN** that quest does NOT appear in the Quest Link picker

#### Scenario: A vsquest quest started in a prior session stays hidden until its giver is seen again
- **WHEN** a player accepted a VS Quest quest in an earlier session, and its quest-giver entity has
  not been loaded/scanned again this session
- **THEN** that quest does NOT appear in the Quest Link picker until its giver is encountered again
  this session and the acceptance is freshly observed

#### Scenario: A player with nothing started this session sees an empty or near-empty picker
- **WHEN** the player has not been observed to start or complete any cataloged quest this session
- **THEN** the Quest Link picker's quest list is empty (or contains only quests separately observed
  as started), which is expected behavior, not an error
