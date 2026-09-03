# assignment-multi-item-creation Specification

## Purpose
TBD - created by archiving change refine-assignment-desk-inbox-ux. Update Purpose after archive.
## Requirements
### Requirement: Staging an existing Scribe item on the Create Assignments tab
The Create Assignments tab SHALL provide a single item slot that accepts any item whose
`Collectible` is an `IScribeDocumentItem` (Notebook, Clockmaker's Notebook, Tablet) or a
`BlockScribeWritingStation` (a picked-up Lectern, Scriptorium, or Assignment Desk item),
matching the accept-filter `BlockEntityScriptorium`'s copy slots already use. Placing a stageable
item into the slot SHALL read its document fresh on every rebuild and render its rows for
selection (see the next requirement). An empty slot, or a slot holding an item with no readable
Scribe document, SHALL render the tab's empty state instead of a row list.

#### Scenario: Staging a Notebook
- **WHEN** the player places their own Notebook into the Create Assignments tab's staging slot
- **THEN** the tab reads the Notebook's document and renders its rows for selection

#### Scenario: Staging a picked-up Lectern
- **WHEN** the player places a picked-up Lectern item (carrying its document via
  `BlockScribeWritingStation.GetDrops`) into the staging slot
- **THEN** the tab reads that document and renders its rows for selection, identically to staging
  a Notebook

#### Scenario: Empty slot shows the empty state
- **WHEN** the staging slot is empty, or holds an item with no readable Scribe document
- **THEN** the Create Assignments tab shows its empty state rather than a row list or a batch-send
  control

### Requirement: Staged rows render Read-view-style with an independent selection checkbox
Each row of the staged document SHALL render using the same per-kind content as the Read view
(task text, item icon and name for a Tracker/Craft row, link label for a Link row, subtask
indent/depth), but with a Selected checkbox in place of the Read view's completion checkbox. The
Selected checkbox SHALL be independent of that row's own Done/completion state — selecting a row
for the batch SHALL NOT mark it complete, and a row's completion state SHALL NOT affect whether it
can be selected. Staged rows SHALL NOT offer the Read view's pin affordance or its "switch to
editor" control; this is a selection surface only, not an editable or completable one.

#### Scenario: Selecting a row does not complete it
- **WHEN** the player checks a staged Task row's Selected checkbox
- **THEN** the row is included in the batch to send, and its own Done/completion state is
  unchanged

#### Scenario: Every row kind is selectable
- **WHEN** the staged document contains a Task, a Tracker, a Craft row, a Link row, a Text
  section, and a parent row with subtasks
- **THEN** every one of those rows renders with its own Selected checkbox, and each can be
  independently selected

### Requirement: Selecting a parent row cascades to its subtasks once, then every row is independent
Checking a parent row's Selected checkbox SHALL also set every one of its immediate subtasks'
Selected state to true, as a convenience default. After that cascade, every affected row — the
parent and each subtask — SHALL be an independently overridable toggle: unchecking one subtask
afterward SHALL leave the parent and its sibling subtasks selected, dropping only that one subtask
from the batch. There SHALL be no re-locking, re-graying, or forced re-cascade once the initial
cascade has run.

#### Scenario: Checking a parent selects its subtasks
- **WHEN** the player checks a parent row's Selected checkbox
- **THEN** every immediate subtask of that parent becomes Selected as well

#### Scenario: A subtask can be deselected independently afterward
- **WHEN** the player, after the cascade above, unchecks one subtask's Selected checkbox
- **THEN** that subtask is dropped from the batch while the parent and its other subtasks remain
  Selected

### Requirement: Sending a batch creates one independent assignment per selected row
Sending the batch SHALL create one independent assignment record per selected row, all addressed
to the single recipient chosen via the existing target-player picker. Each created assignment
SHALL carry that row's full shape (kind, text, target item/quantity, link fields, depth) and SHALL
behave exactly like any other assignment from that point on — its own Accept/Decline/Cancel/
Discard lifecycle, its own row in the recipient's Inbox. No bundling identifier or "batch" concept
SHALL be introduced; declining or completing one row's assignment SHALL have no effect on any other
row sent in the same batch.

#### Scenario: A mixed-kind batch arrives as independent assignments
- **WHEN** the player selects a Task row, a Tracker row, and a Link row from the staged document
  and sends them to one recipient
- **THEN** the recipient's Inbox shows three separate assignments, each with its own state and its
  own Accept/Decline/Cancel/Discard controls

#### Scenario: One row's outcome does not affect its batch-mates
- **WHEN** a recipient declines one assignment that was sent as part of a multi-row batch
- **THEN** the other assignments sent in that same batch are unaffected and keep their own
  independent state

### Requirement: "Delete from source on send" is an optional, non-persisted, per-send choice
The Create Assignments tab SHALL offer a "Delete from source on send" checkbox, defaulting to
unchecked every time the tab is opened or rebuilt. This checkbox SHALL NOT be written to
`ScribePlayerSettings` or any other persisted store — it is plain UI session state, reset on every
open, not a saved preference. When checked at the moment of a successful send, every selected row
SHALL be removed from the staged document (a move, not a copy) and the staging slot's contents
re-synced to reflect the change. When left unchecked (the default), the staged document SHALL be
left completely unmodified by the send.

#### Scenario: Checked - selected rows are removed from the source
- **WHEN** the player checks "Delete from source on send" and successfully sends a batch of
  selected rows
- **THEN** those rows are removed from the staged document and the staging slot's displayed
  content reflects their removal

#### Scenario: Unchecked (default) - the source document is untouched
- **WHEN** the player sends a batch without checking "Delete from source on send"
- **THEN** the staged document still contains every row exactly as it did before the send

#### Scenario: The checkbox resets on reopen
- **WHEN** the player checks "Delete from source on send", sends a batch, and then closes and
  reopens the Create Assignments tab (or the dialog)
- **THEN** the checkbox shows unchecked again, regardless of its state on the previous send

