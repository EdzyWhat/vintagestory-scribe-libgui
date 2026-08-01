# codec-migration Specification

## Purpose
TBD - created by archiving change codec-migration-scaffold. Update Purpose after archive.
## Requirements
### Requirement: Document codec accepts prior version via a named migration step
`ScribeDocumentCodec` SHALL accept byte arrays written in the immediately prior format version
(currently v4) by running them through a named private migration method before constructing
the final `ScribeDocument`. The migration method SHALL be named to reflect which version
transition it handles (e.g. `ApplyV4ToV5Migrations`) and SHALL be the sole place where
version-specific defaulting or field-inference for that step lives. Ad-hoc inline branching
on a `bool isCurrent` flag SHALL NOT be used; the `isCurrent` pattern is replaced by this
named-step pattern.

#### Scenario: v4 bytes are migrated to current schema via a named step
- **WHEN** bytes written in v4 format are deserialized
- **THEN** deserialization succeeds, the document's `Title` field is set to
  `ScribeDocument.DefaultTitle` (because v4 has no title field), and no other fields differ
  from a v5 document with the same blocks

#### Scenario: Current-version bytes bypass migration steps
- **WHEN** bytes written in the current version (v5) are deserialized
- **THEN** no migration methods are called and the title is read directly from the byte stream

### Requirement: Document codec accepted-version window is documented in one place
`ScribeDocumentCodec`'s class-level XML doc-comment SHALL contain an accepted-version table
that lists: the current version number, each accepted prior version number, and the fields
added in each version transition. This table SHALL be the single authoritative reference for
what the reader accepts. No other file SHALL maintain a parallel version registry.

#### Scenario: Accepted-version table is present and current in the doc-comment
- **WHEN** the `ScribeDocumentCodec.cs` source is read
- **THEN** the class doc-comment contains an explicit table with current version, accepted
  prior versions, and per-transition field history (verifiable by code review / inspection)

### Requirement: Pin codec migration scaffold is in place
`ScribePinCodec` SHALL declare a `PriorPinVersion` constant (currently equal to `PinVersion`
because no pin-format change has occurred yet) and a private `ApplyPinMigrations` helper
method stub, so the next pin-format version bump has a clear, documented home. The stub is
a no-op when `PriorPinVersion == PinVersion` (no migration needed yet) and is invoked from
the read path at the version branch.

#### Scenario: Pin codec compiles and its round-trip tests pass after scaffold is added
- **WHEN** the pin codec scaffold is added (constants + stub migration method)
- **THEN** all existing `ScribePinCodecTests` pass without modification

### Requirement: Codec migration how-to documentation exists
A `docs/CODEC-MIGRATION.md` file SHALL exist and SHALL cover: the append-only version
discipline (never reorder, never two versions with the same number), how to update the
accepted-version window when bumping the version, the named-migration-step pattern, and a
worked example using the v4→v5 title addition as the reference case.

#### Scenario: CODEC-MIGRATION.md is present in the repo
- **WHEN** the repo is inspected after this change lands
- **THEN** `docs/CODEC-MIGRATION.md` exists, is non-empty, and contains the four elements
  described above (verifiable by inspection / code review)

### Requirement: Each supported prior version has a dedicated older-blob unit test
For every prior-version format that `ScribeDocumentCodec` accepts, `ScribeDocumentCodecTests`
SHALL contain a test that hand-builds a byte array in exactly that prior format and asserts
specific field values on the deserialized document — not merely that deserialization returns
`true`. The test name SHALL follow the pattern
`TryDeserialize_V<N>Bytes_<FieldName>_IsUpgraded` or similar to make the migration step
being verified obvious.

#### Scenario: v4 older-blob test asserts title is set to default
- **WHEN** a hand-built v4 byte array (with no title field) is deserialized
- **THEN** the test asserts `restored.Title == ScribeDocument.DefaultTitle` (not merely `ok == true`)

