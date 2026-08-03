## ADDED Requirements

### Requirement: The tablet dialog has a read-only mode for a fired tablet

The tablet dialog SHALL resolve whether its stack is fired (via the tablet's `fired` read helper) at one
place and, when fired, open in a read-only mode: it SHALL NOT enter editor mode in its constructor, SHALL
render the document view-only, and SHALL present no editor entry, add/check/pin, reorder, or title-edit
affordance. A soft (unfired) tablet SHALL keep its existing always-edit behavior unchanged.

#### Scenario: A fired tablet opens view-only

- **WHEN** a player opens a fired clay tablet
- **THEN** the dialog does not enter editor mode and shows the document read-only with no editing
  affordances

#### Scenario: A soft tablet is unaffected

- **WHEN** a player opens a soft (unfired) clay tablet
- **THEN** the dialog opens always-edit exactly as before

### Requirement: The fired tablet dialog shows an empty-state message when blank

When the tablet dialog opens a fired tablet whose document has no tasks and no notes, it SHALL show a
small centered message (a Scribe lang key) indicating the tablet was fired without any writing, in place
of an empty content region.

#### Scenario: Blank fired tablet shows the centered message

- **WHEN** a player opens a fired clay tablet with no tasks and no notes
- **THEN** the dialog shows a small centered "fired without any writing" message and no editable surface
