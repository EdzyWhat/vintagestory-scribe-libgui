# read-view-filter-pills Specification

## Purpose
Lets a player narrow a Read View's row list to just what they care about right now — active work,
finished work, pinned rows, or freeform content — and remembers that choice per item/block so it
doesn't reset every time they reopen it.

## Requirements

### Requirement: Read View shows a five-category filter-pill row
Every surface that supports this feature (see `scribe-dialog-base`'s capability flag) SHALL render
a row of filter pills above its Read View row list: All, Active, Completed, Pinned, Other. Exactly
one pill is active at a time (radio behavior, matching the Assignment Inbox's existing pills).

#### Scenario: Five pills are shown
- **WHEN** a player opens Read View on a supporting surface
- **THEN** All, Active, Completed, Pinned, and Other pills are all shown, with exactly one marked
  active

#### Scenario: Selecting a pill re-filters the row list
- **WHEN** a player clicks a pill other than the currently active one
- **THEN** that pill becomes active and the row list updates to the new filter immediately

### Requirement: Each block kind maps to exactly one Active/Completed/Other category
`Text` and `QuestObjective` blocks have no complete-state and SHALL always match **Other**.
`Task`, `Tracker`, `Craft`, and `Link` blocks each carry a complete-state and SHALL match
**Active** when not complete, **Completed** when complete.

#### Scenario: A note always matches Other
- **WHEN** a `Text` block is evaluated against Active/Completed/Other
- **THEN** it matches Other, regardless of any other state

#### Scenario: A Quest objective always matches Other
- **WHEN** a `QuestObjective` block is evaluated against Active/Completed/Other
- **THEN** it matches Other, regardless of its own checked state

#### Scenario: An unchecked task matches Active
- **WHEN** a `Task`, `Tracker`, `Craft`, or `Link` block is not yet complete
- **THEN** it matches Active

#### Scenario: A checked task matches Completed
- **WHEN** a `Task`, `Tracker`, `Craft`, or `Link` block is complete
- **THEN** it matches Completed

### Requirement: Pinned is evaluated independently of Active/Completed/Other
A row SHALL match **Pinned** whenever it is currently pinned by the viewing player, regardless of
which of Active/Completed/Other it also matches. A row MAY match two pills at once.

#### Scenario: A pinned, completed row matches both Pinned and Completed
- **WHEN** a completed `Task` row is also pinned by the viewing player
- **THEN** it matches both the Pinned pill and the Completed pill, and is counted toward both

### Requirement: Non-All pills show a bracketed count only when greater than zero
Each of Active, Completed, Pinned, and Other SHALL show its matching row count in brackets after
the label (e.g. "Active [3]") whenever that count is greater than zero, and the label alone (no
brackets) when the count is zero. The All pill never shows a count.

#### Scenario: A populated pill shows its count
- **WHEN** 3 rows match the Completed category
- **THEN** the Completed pill reads "Completed [3]"

#### Scenario: An empty pill shows no count
- **WHEN** 0 rows match the Other category
- **THEN** the Other pill reads "Other" with no bracketed number

### Requirement: Pill colors follow the muted, reused-hue convention
The All pill SHALL render in the same warm gray as the Assignment Inbox's All pill. The Completed
pill SHALL render in blue, the Active pill in green, the Pinned pill in gold, and the Other pill in
purple — each a muted hue consistent with the rest of the GUI's nav-icon backgrounds, not an
independently invented, higher-saturation color.

#### Scenario: Active pill is active-colored when selected
- **WHEN** the Active pill is the currently selected pill
- **THEN** it renders filled with its green hue; the other four pills render as unfilled/outline

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
