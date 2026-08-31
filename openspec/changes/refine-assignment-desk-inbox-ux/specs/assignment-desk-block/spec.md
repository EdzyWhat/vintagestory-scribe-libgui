## ADDED Requirements

### Requirement: The Assignment tab is labeled "Create Assignments" with a plus icon
The Assignment Desk's create-and-send tab SHALL be labeled "Create Assignments" (both its visible
tab label and its hover tooltip), and its nav button SHALL render a dedicated plus-glyph icon
rather than the scroll icon used for other Scribe surfaces' document-related affordances.

#### Scenario: The tab reads "Create Assignments"
- **WHEN** the Assignment Desk's dialog renders its Assignment/Inbox tab-switcher
- **THEN** the create-and-send tab's label and hover tooltip both read "Create Assignments"

#### Scenario: The tab shows a plus icon
- **WHEN** the Assignment Desk's dialog renders the Create Assignments nav button
- **THEN** it shows a plus-glyph icon, distinct from the scroll icon used elsewhere in the mod

### Requirement: The create-and-send form's target-player row uses a flex layout
The create-and-send form's target-player row SHALL be laid out as a single horizontal row using
LibGUI's `Row`/`Expanded` flex primitives: a fixed-width "Send to" label, an `Expanded(flex: 1)`
player picker taking the remaining width, and a fixed-width Send button — replacing a
vertically-stacked layout where the label, picker, and button each occupied their own full-width
row.

#### Scenario: Send-to row lays out horizontally
- **WHEN** the Create Assignments tab renders its create-and-send form
- **THEN** the "Send to" label, the player picker, and the Send button render side by side on one
  row, with the player picker occupying the flexible remaining space between the two fixed-width
  controls
