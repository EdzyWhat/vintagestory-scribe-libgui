## MODIFIED Requirements

### Requirement: The last-used filter pill persists per Scribe item/block instance
Each Scribe item or block that hosts Read View SHALL remember its own last-selected filter pill
across dialog close/reopen and across game sessions, independent of every other instance's
selection. An instance that has never had a pill explicitly selected defaults to All. The active
pill SHALL change only when the player explicitly selects a different pill — a row's own state
changing (e.g. a task being completed or un-completed, a row being pinned or unpinned) while that
row is visible under the current filter SHALL NOT change which pill is active.

#### Scenario: Reopening a document resumes its last filter
- **WHEN** a player selects Completed on a Notebook, closes the dialog, and reopens the same
  Notebook later (including after relogging)
- **THEN** the dialog opens already filtered to Completed

#### Scenario: Two instances remember independently
- **WHEN** a player sets one Notebook to Completed and a different Notebook to Pinned
- **THEN** reopening either one shows its own last-selected pill, unaffected by the other

#### Scenario: A never-configured instance defaults to All
- **WHEN** a freshly crafted or never-opened Scribe item/block is opened for the first time
- **THEN** its Read View opens with the All pill active

#### Scenario: Un-completing a row under the Completed pill does not reset the pill
- **WHEN** the Completed pill is active and a player un-checks a `Task` row that was rendering
  under it
- **THEN** the Completed pill remains active; the now-incomplete row stops matching Completed and
  is hidden or shadow-rendered per `read-view-subtask-collapse`'s group-visibility rules, exactly
  as if it had never matched
