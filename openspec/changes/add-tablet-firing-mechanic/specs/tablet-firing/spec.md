## ADDED Requirements

### Requirement: A soft clay tablet fires in a firepit into a fired clay tablet

A soft (unfired) clay tablet SHALL be smeltable in a firepit — without a crucible/container, at the clay
firing temperature — producing a fired clay tablet, following the same firepit path Vintage Story uses for
firing clay pottery. The fired output SHALL record `fired = true`. A wax tablet SHALL NOT be fireable, and
an already-fired clay tablet SHALL NOT be re-fireable into another tablet.

#### Scenario: A soft clay tablet fires to a fired clay tablet

- **WHEN** a soft clay tablet is placed in a firepit and reaches the firing temperature for the required
  duration
- **THEN** it becomes a fired clay tablet whose stack records `fired = true`

#### Scenario: Wax and already-fired tablets do not fire

- **WHEN** a wax tablet or an already-fired clay tablet is placed in a firepit
- **THEN** it does not smelt into another tablet

### Requirement: Firing carries the tablet's document and clay type through the transformation

Firing a clay tablet SHALL preserve on the fired output the entire document (tasks, notes, and title) and
the `clayType` recorded on the soft tablet, mirroring how a Notebook's data carries into a Clockmaker's
Notebook. Because the firepit builds its output from a fixed smelt stack that does not copy input
attributes, the tablet SHALL override the smelt behavior to copy the document and clay type onto the fired
output.

#### Scenario: Task data survives firing

- **WHEN** a soft clay tablet carrying a document with tasks and notes is fired
- **THEN** the resulting fired tablet carries the same document (same tasks, notes, and title)

#### Scenario: Clay type survives firing

- **WHEN** a soft clay tablet recording `clayType = blue` is fired
- **THEN** the resulting fired tablet still records `clayType = blue` (and `fired = true`)

### Requirement: A fired tablet is read-only

A fired clay tablet SHALL be immutable: opening it SHALL present its document in a view-only mode with no
way to add, check, pin, reorder, or edit tasks, and no way to edit the title. The always-edit behavior of a
soft tablet SHALL NOT apply to a fired tablet.

#### Scenario: A fired tablet cannot be edited

- **WHEN** a player opens a fired clay tablet that carries tasks and notes
- **THEN** the content is shown read-only and no task can be added, checked, pinned, reordered, or edited,
  and the title cannot be changed

### Requirement: A fired tablet with no writing shows an empty-state message

When a fired clay tablet carries no document content (no tasks and no notes — e.g. a tablet obtained
already fired from Creative Inventory), its dialog SHALL show a small centered message indicating it was
fired without any tasks, rather than an empty editable surface.

#### Scenario: A blank fired tablet explains itself

- **WHEN** a player opens a fired clay tablet that has no tasks and no notes
- **THEN** the dialog shows a small centered message that the tablet was fired without any writing, and
  offers no editing affordance
