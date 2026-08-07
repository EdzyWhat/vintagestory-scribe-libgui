## ADDED Requirements

### Requirement: The tablet dialog has a read-only mode for a non-editable tablet

The tablet dialog SHALL resolve whether its stack is editable — editable ⇔ NOT hard AND NOT fired, via the
tablet's `hard`/`fired` read helpers — at one place and, when not editable, open in a read-only mode: it
SHALL NOT enter editor mode in its constructor, SHALL render the document view-only, and SHALL present no
editor entry, add/check/pin, reorder, or title-edit affordance. A wet (editable) tablet SHALL keep its
existing always-edit behaviour unchanged.

#### Scenario: A hard or fired tablet opens view-only

- **WHEN** a player opens a hard clay tablet or a fired clay tablet
- **THEN** the dialog does not enter editor mode and shows the document read-only with no editing
  affordances

#### Scenario: A wet tablet is unaffected

- **WHEN** a player opens a wet (unfired, un-hardened) clay tablet
- **THEN** the dialog opens always-edit exactly as before

### Requirement: The non-editable tablet dialog shows a state-appropriate empty-state message when blank

When the tablet dialog opens a non-editable tablet (hard or fired) whose document has no tasks and no notes,
it SHALL show a small centered message (a Scribe lang key) in place of an empty content region: for a fired
tablet, that it was fired without any writing; for a hard tablet, that it has dried out and can be edited
again after being dunked in water.

#### Scenario: Blank fired tablet shows the fired message

- **WHEN** a player opens a fired clay tablet with no tasks and no notes
- **THEN** the dialog shows a small centered "fired without any writing" message and no editable surface

#### Scenario: Blank hard tablet shows the dried message

- **WHEN** a player opens a hard clay tablet with no tasks and no notes
- **THEN** the dialog shows a small centered "dried out — dunk in water to edit" message and no editable
  surface

### Requirement: The tablet dialog backdrop is chosen by clay type and state

The tablet dialog SHALL select its full-page backdrop by both the clay type (the item variant) and the
tablet state (wet, hard, or fired), so each of the three states reads as visually distinct: wet is the
smoother/glossier soft appearance, hard is a lighter/drier appearance, and fired is the final ceramic
appearance. Where `hard` and `fired` are both set, the fired appearance SHALL take precedence.

#### Scenario: Each state shows a distinct backdrop

- **WHEN** the same clay-variant tablet is opened wet, then hard, then fired
- **THEN** the dialog shows three visually distinct backdrops (glossy wet, dried hard, ceramic fired) for
  that clay type
