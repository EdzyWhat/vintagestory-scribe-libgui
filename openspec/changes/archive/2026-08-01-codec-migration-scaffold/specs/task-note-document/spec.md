## MODIFIED Requirements

### Requirement: Serialization round-trip includes assignment field and stable identifiers
The system's document serialization SHALL preserve the document's `DocId`, each block's
`TaskId`, and each block's assignment field, in addition to the fields already preserved (kind,
text, completed flag, depth). Serialization SHALL write the current format version, and
deserialization SHALL accept both the current version and the immediately prior version via a
**named migration step** (not ad-hoc inline branching); the prior version does not carry a title
field, so deserializing it SHALL supply `ScribeDocument.DefaultTitle`. Bytes from any version
older than the immediately prior one SHALL fail to deserialize rather than silently defaulting
or misreading fields. Each accepted prior-version format SHALL have a dedicated unit test that
hand-builds bytes in that format and asserts specific migrated field values.

#### Scenario: Round-trip preserves identifiers and assignment
- **WHEN** a document is serialized in the current format and then deserialized
- **THEN** the resulting document has the same `DocId`, each block has the same `TaskId`, and
  each block's assignment field matches the original

#### Scenario: Prior version deserializes with generated identifiers
- **WHEN** bytes produced by the immediately prior format version are deserialized
- **THEN** deserialization succeeds, the document is assigned a fresh `DocId`, and each block is
  assigned a fresh `TaskId`

#### Scenario: An unsupported older version fails to deserialize
- **WHEN** bytes produced by a format version older than the immediately prior one are
  deserialized
- **THEN** deserialization reports failure rather than producing a document with incorrect or
  default values

#### Scenario: Prior-version bytes are upgraded via a named migration step
- **WHEN** bytes in the immediately prior format version (v4) are deserialized
- **THEN** a named migration method (not an inline `isCurrent` flag) supplies the missing title
  field as `ScribeDocument.DefaultTitle`, and the rest of the document is read normally

#### Scenario: Prior-version older-blob test asserts migrated field values
- **WHEN** a hand-built prior-version byte array is passed to `TryDeserialize`
- **THEN** a dedicated unit test asserts the specific value of each migrated field (e.g.
  `restored.Title == ScribeDocument.DefaultTitle`), not merely that the call returns `true`
