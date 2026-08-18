## ADDED Requirements

<!-- The "Transcribe" tab name is owned by the scriptorium-inventory capability (modified by this
     change). The requirements below define the copy/paste, stamp, and import/export behavior that
     lives inside that Transcribe view. -->

### Requirement: Copy a document between the two Transcribe slots

The Transcribe view SHALL provide an Original (source) slot and a Duplicate (target) slot and a copy
action that duplicates the Original item's stored `ScribeDocument` onto the target item, using the
`ScribeDocumentAttributes` serialization path. The Original item SHALL NOT be consumed or modified by
the copy. The copy SHALL be server-authoritative: the client sends a copy request; the server performs
the duplication and syncs the result back.

#### Scenario: Copy onto an empty target

- **WHEN** the Original slot holds a Scribe item with a document and the Duplicate slot holds a Scribe item with no contents
- **AND** the player triggers the copy action
- **THEN** the Duplicate item receives a copy of the Original's document (its tasks, notes, and other blocks)
- **AND** the Original item is unchanged
- **AND** the Duplicate slot's summary card updates to show the copied contents

#### Scenario: Copy action is unavailable until both slots are filled

- **WHEN** either the Original or the Duplicate slot is empty
- **THEN** the copy action is disabled and an explainer indicates a Scribe item is needed in each slot

#### Scenario: Copied document is independent of the source

- **WHEN** a document has been copied from the Original to the Duplicate item
- **AND** the player later edits either item's document
- **THEN** the edit affects only that item; the two documents do not share state

### Requirement: Overwrite confirmation when the target already has contents

When the Duplicate slot's item already contains tasks, the copy action SHALL require an explicit
second confirmation before overwriting, and SHALL surface how many tasks would be replaced. When the
target has no contents, no confirmation SHALL be required.

#### Scenario: First press on a non-empty target asks to confirm

- **WHEN** the Duplicate item has N existing tasks and the player triggers the copy action
- **THEN** the action does not yet copy; it changes to a distinct confirm state indicating "overwrite N tasks"
- **AND** a second trigger of the action performs the copy, replacing the target's document with the Original's

#### Scenario: Empty target copies without confirmation

- **WHEN** the Duplicate item has no contents and the player triggers the copy action
- **THEN** the copy proceeds immediately with no confirmation step

#### Scenario: Confirmation resets if the slots change

- **WHEN** the copy action is in its "confirm overwrite" state
- **AND** the contents of either slot change before the confirming press
- **THEN** the action returns to its initial (unconfirmed) state

### Requirement: Wax-seal stamp affordance and press animation

The copy action SHALL be presented as a wax-seal / stamp affordance. Triggering it SHALL play a
button-triggered press animation (a 2D seal pressing down with scale, slight rotation, and fade) that
leaves a brief imprint on the Duplicate slot before the copied summary card appears. The animation SHALL
be a non-load-bearing flourish: if the animation is disabled or removed, the copy SHALL still function
identically. The stamp SHALL be implemented as a reusable animation component so it can later be reused
elsewhere.

#### Scenario: Stamp animation plays on copy

- **WHEN** the player triggers a successful copy
- **THEN** the seal plays its press animation and leaves a brief imprint on the Duplicate slot
- **AND** the copied summary card is shown when the animation settles

#### Scenario: Copy works without the animation

- **WHEN** the stamp animation is unavailable
- **THEN** the copy still completes and the Duplicate slot still reflects the copied contents

### Requirement: Import/Export section is present but unwired

The Transcribe view SHALL include a distinct Import/Export section, below the copy pair, with a
**placeholder slot** ("note to export from / import into") and controls for Export JSON, Export CSV,
and Import. The section SHALL be visibly present but inert ("coming in a later update"): the controls
perform no action when clicked and the placeholder slot does not yet accept or store an item. This
reserves the final layout without wiring the import/export logic and without adding a persisted
block-entity slot (the placeholder becomes a real slot when import/export is wired).

#### Scenario: Placeholder controls are shown and disabled

- **WHEN** the player opens the Transcribe view
- **THEN** an Import/Export section is visible with a placeholder slot and Export JSON, Export CSV, and Import controls
- **AND** those controls are disabled and indicate they are coming in a later update
- **AND** clicking a disabled control performs no import or export

#### Scenario: The placeholder section does not affect the copy slots

- **WHEN** the player interacts with the Import/Export section
- **THEN** the two copy slots (Original and Duplicate) are unaffected and no copy occurs
- **AND** the Scriptorium's persisted inventory still has exactly its two real (copy) slots
