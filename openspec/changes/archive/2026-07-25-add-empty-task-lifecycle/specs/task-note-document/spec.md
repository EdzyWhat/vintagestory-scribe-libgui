## MODIFIED Requirements

### Requirement: Document stores task text verbatim
The document model SHALL store a task's text exactly as supplied, without trimming leading,
trailing, or interior whitespace, and without rejecting any value. The model SHALL NOT enforce a
non-blank content invariant on task text: an add or text-change with blank or whitespace-only task
text SHALL succeed and store that text verbatim, exactly as it does for a freeform text section.
Ensuring an empty task is not *persisted* (removing an abandoned or cleared empty task) is the
responsibility of the editing layer, not the document model — consistent with the model's role of
storing text verbatim while normalization and content policy live in the editing layer.

#### Scenario: Adding a task preserves surrounding whitespace
- **WHEN** a task is added with text that has leading and/or trailing whitespace around
  non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied, rather than being
  trimmed

#### Scenario: Changing a task's text preserves surrounding whitespace
- **WHEN** a task block's text is changed to a value with leading and/or trailing whitespace
  around non-blank content
- **THEN** the stored task text retains that whitespace exactly as supplied

#### Scenario: Empty or whitespace-only task text is accepted
- **WHEN** a task is added, or an existing task's text is changed, with text that is empty or
  contains only whitespace
- **THEN** the operation succeeds and the document stores that empty/whitespace-only text
  verbatim, rather than reporting failure and leaving the document unchanged
