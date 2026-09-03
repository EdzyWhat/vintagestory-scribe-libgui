## ADDED Requirements

### Requirement: Accept-time placement records a destination label
When the Assignee's Accept places the resulting task onto a resolved Scribe document (per
the placement requirement above), the system SHALL also record a short display label
identifying that destination item — `<Type> "<Title>"` (e.g. `Notebook "Book of Nick"`),
falling back to the item's bare name when the document has no meaningful title — on the
assignment record, alongside its existing accepted-date stamp. When placement does not
occur (the Accept control's no-eligible-surface case, or a defensive no-op on an
unresolvable/no-capacity target), no label is recorded.

#### Scenario: Accept records the destination label
- **WHEN** the Assignee accepts an assignment and it is placed into a Notebook titled
  "Book of Nick"
- **THEN** the assignment record's destination label is set to `Notebook "Book of Nick"`

#### Scenario: An assignment titled with the default title falls back to the bare item name
- **WHEN** the Assignee accepts an assignment and it is placed into a Notebook that still
  carries the default (never-renamed) title
- **THEN** the assignment record's destination label is the bare item name, not the
  default title

#### Scenario: No label when placement does not occur
- **WHEN** an Accept request resolves to an ineligible or no-capacity target and the
  assignment stays Accepted but unplaced
- **THEN** no destination label is recorded for that assignment
