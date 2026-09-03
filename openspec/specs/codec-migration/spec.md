# codec-migration Specification

## Purpose
TBD - created by archiving change codec-migration-scaffold. Update Purpose after archive.
## Requirements
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

### Requirement: Document codec reads an accepted window of prior versions via progressive reads
`ScribeDocumentCodec` SHALL read any format version within an accepted window
`[MinVersion, Version]` (currently `[5, 8]`) using **progressive append-only reads**: each
version's trailing fields are read only behind a `version >=` threshold, and fields absent from
an older-but-accepted blob are set to their documented defaults by a single named defaulting
method (`ApplyPreV6Defaults`), which SHALL be the sole place per-version defaulting lives.
Ad-hoc inline branching on a `bool isCurrent` flag SHALL NOT be used. A version below
`MinVersion` or above `Version` SHALL fail-safe (`TryDeserialize` returns `false`) rather than
partially reading. `MinVersion` advances only when the oldest shipped layout is deliberately
dropped; shipped v5 documents (v1.1.1) live in real saves, so the window must not narrow below
them silently. (Mirrors `ScribePinCodec`'s progressive window.) Comments, XML docs, and
`docs/CODEC-MIGRATION.md` SHALL name this same `[5, 8]` window — not a stale `[5, 7]`.

#### Scenario: Accepted older versions are read via progressive gates and defaulted
- **WHEN** bytes written in an accepted older version (v5, v6, or v7) are deserialized
- **THEN** deserialization succeeds, fields that version does not carry (tracker/link fields for
  v5; the per-block link label for v6; `RecipeSignature` for v7) are set to their documented defaults via
  `ApplyPreV6Defaults`, and no other field differs from a current-version document with the same
  blocks

#### Scenario: Current-version bytes bypass migration steps
- **WHEN** bytes written in the current version (v8) are deserialized
- **THEN** no defaulting method is invoked and every field is read directly from the byte stream

#### Scenario: A version outside the accepted window fails to deserialize
- **WHEN** bytes written below `MinVersion` (e.g. v3 or v4) or above `Version` are deserialized
- **THEN** `TryDeserialize` returns `false` (fail-safe) rather than partially reading

#### Scenario: Docs and comments match the code window
- **WHEN** `ScribeDocumentCodec.cs` comments, `ScribeDocumentCodecTests` comments, and
  `docs/CODEC-MIGRATION.md` are inspected
- **THEN** they state current Version = 8, MinVersion = 5, and do not claim the window ends at v7

### Requirement: Each accepted prior version has a dedicated older-blob unit test
For every prior-version format that `ScribeDocumentCodec` accepts, `ScribeDocumentCodecTests`
SHALL contain a test that hand-builds a byte array in exactly that prior format and asserts
specific field values on the deserialized document — not merely that deserialization returns
`true`. The test name SHALL make the version and the defaulting being verified obvious (e.g.
`TryDeserialize_V5Bytes_Succeeds_AndDefaultsTrackerLinkFields`,
`TryDeserialize_V6Bytes_Succeeds_AndDefaultsLinkLabel`,
`TryDeserialize_V7Bytes_Succeeds_AndDefaultsRecipeSignature`). A version outside the accepted window
SHALL likewise have a fail-safe test (e.g. `TryDeserialize_V4Bytes_FailsSafely`).

#### Scenario: v5 older-blob test asserts tracker/link fields default
- **WHEN** a hand-built v5 byte array (with no tracker/link fields) is deserialized
- **THEN** the test asserts the specific defaulted values of the tracker/link fields on the
  restored blocks (not merely `ok == true`)

#### Scenario: v6 older-blob test asserts link label defaults
- **WHEN** a hand-built v6 byte array (with no per-block link label) is deserialized
- **THEN** the test asserts the defaulted link-label value on the restored blocks (not merely
  `ok == true`)

#### Scenario: v7 older-blob test asserts RecipeSignature defaults
- **WHEN** a hand-built v7 byte array (with no `RecipeSignature` field) is deserialized
- **THEN** the test asserts the restored block's `RecipeSignature` is empty (not merely `ok == true`)

#### Scenario: Below-floor version test asserts fail-safe
- **WHEN** a hand-built v4 byte array (below `MinVersion`) is deserialized
- **THEN** the test asserts `TryDeserialize` returns `false` (not a partial read)

### Requirement: Codec how-to documents the v8 and pin-v5 bumps
`docs/CODEC-MIGRATION.md` SHALL describe the v7→v8 document bump (`RecipeSignature` appended per block) and the pin-codec v4→v5 bump (`Depth` appended per pin). Its summary table SHALL list `ScribeDocumentCodec` current version **v8** with accepted window **v5–v8**, not v7 / v5–v7.

#### Scenario: How-to table matches the code
- **WHEN** a developer reads the summary table in `docs/CODEC-MIGRATION.md`
- **THEN** document codec current is v8, window is v5–v8, and pin codec current is v5

