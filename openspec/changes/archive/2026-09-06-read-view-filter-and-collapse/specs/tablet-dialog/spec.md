## ADDED Requirements

### Requirement: The Tablet dialog does not render filter pills or subtask-collapse toggles
The Tablet dialog SHALL disable the `ScribeDialogBase` capability flag introduced by
`scribe-dialog-base` for filter pills and subtask collapse, so its Read View never shows a
filter-pill row and never shows a collapse toggle on any subtask-group parent, regardless of the
tablet's contents. This keeps the Tablet's minimal read-view intentional rather than an oversight,
consistent with its existing pared-down dialog (no tab navigation, per the "Central region keeps
the editable task list" requirement).

#### Scenario: Opening a Tablet shows no filter-pill row
- **WHEN** a player opens a Tablet's Read View, regardless of how many tasks/notes it holds
- **THEN** no filter-pill row is shown

#### Scenario: Opening a Tablet with a Craft or Quest parent shows no collapse toggle
- **WHEN** a Tablet's document contains a Craft or Quest Link parent with an owned run of subtasks
- **THEN** that parent's row shows no collapse toggle, and its owned run always renders in full
