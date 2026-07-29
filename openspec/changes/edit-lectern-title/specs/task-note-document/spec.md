## ADDED Requirements

### Requirement: Document carries a title field
`ScribeDocument` SHALL carry a `Title` string property (max 80 chars). The default value when
unset or empty after trim SHALL be `"Lectern"`. The title SHALL be serialized in the document
codec and SHALL survive a round-trip through serialize/deserialize.

#### Scenario: Title round-trips through serialization
- **WHEN** a document with `Title = "My Lectern"` is serialized and deserialized
- **THEN** the resulting document has `Title == "My Lectern"`

#### Scenario: Missing title deserializes to default
- **WHEN** bytes from a prior codec version (which has no title field) are deserialized
- **THEN** the resulting document has `Title == "Lectern"`

#### Scenario: Empty title is not stored — default is used
- **WHEN** a document is saved with a title that is empty after trimming
- **THEN** the persisted title is `"Lectern"`, not an empty string
