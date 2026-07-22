## ADDED Requirements

### Requirement: Document stores task text verbatim
The document model SHALL store a task's text exactly as supplied, without trimming leading,
trailing, or interior whitespace. The only content invariant the model enforces on a task is
that its text is not blank or whitespace-only: an add or text-change with blank/whitespace-only
task text SHALL be rejected and leave the document unchanged. Whitespace normalization (e.g.
trimming a trailing blank line from a committed edit) is the responsibility of the editing layer,
not the document model.

#### Scenario: Adding a task preserves surrounding whitespace
- **WHEN** a task is added with text that has leading and/or trailing whitespace around
  non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied, rather than being
  trimmed

#### Scenario: Changing a task's text preserves surrounding whitespace
- **WHEN** a task block's text is changed to a value with leading and/or trailing whitespace
  around non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied

#### Scenario: Blank or whitespace-only task text is rejected
- **WHEN** a task is added, or an existing task's text is changed, with text that is empty or
  contains only whitespace
- **THEN** the operation reports failure and the document is left unchanged
