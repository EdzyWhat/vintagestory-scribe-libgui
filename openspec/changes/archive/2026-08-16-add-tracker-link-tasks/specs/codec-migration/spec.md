## REMOVED Requirements

### Requirement: Document codec accepts prior version via a named migration step
**Reason**: The strict single-transition model this requirement encoded ("accept only the
immediately-prior version; older versions fail") no longer matches the shipped codec. Adding the
Tracker/Link fields (v6) and per-block link label (v7) on top of the already-shipped title
version (v5) means multiple prior layouts now live in real saves at once, so the reader accepts a
*window* of versions read progressively rather than a single migration step. Replaced by
"Document codec reads an accepted window of prior versions via progressive reads" below.
**Migration**: No data migration — the on-disk byte formats are unchanged and append-only. The
old per-step `ApplyV{N}To{N+1}Migrations` naming is superseded by a single `ApplyPreV6Defaults`
defaulting method invoked behind `version >=` gates.

### Requirement: Each supported prior version has a dedicated older-blob unit test
**Reason**: Header retitled and its scenario set replaced to describe the progressive window
(v5 and v6 accepted-older tests plus a below-floor fail-safe test) instead of the single
v4→title-default test. Replaced by "Each accepted prior version has a dedicated older-blob unit
test" below.
**Migration**: None — test-only requirement; the corresponding tests already exist
(`TryDeserialize_V5Bytes_Succeeds_AndDefaultsTrackerLinkFields`,
`TryDeserialize_V6Bytes_Succeeds_AndDefaultsLinkLabel`, `TryDeserialize_V4Bytes_FailsSafely`).

## ADDED Requirements

### Requirement: Document codec reads an accepted window of prior versions via progressive reads
`ScribeDocumentCodec` SHALL read any format version within an accepted window
`[MinVersion, Version]` (currently `[5, 7]`) using **progressive append-only reads**: each
version's trailing fields are read only behind a `version >=` threshold, and fields absent from
an older-but-accepted blob are set to their documented defaults by a single named defaulting
method (`ApplyPreV6Defaults`), which SHALL be the sole place per-version defaulting lives.
Ad-hoc inline branching on a `bool isCurrent` flag SHALL NOT be used. A version below
`MinVersion` or above `Version` SHALL fail-safe (`TryDeserialize` returns `false`) rather than
partially reading. `MinVersion` advances only when the oldest shipped layout is deliberately
dropped; shipped v5 documents (v1.1.1) live in real saves, so the window must not narrow below
them silently. (Mirrors `ScribePinCodec`'s v2→v3 accepted window.)

#### Scenario: Accepted older versions are read via progressive gates and defaulted
- **WHEN** bytes written in an accepted older version (v5 or v6) are deserialized
- **THEN** deserialization succeeds, fields that version does not carry (tracker/link fields for
  v5; the per-block link label for v6) are set to their documented defaults via
  `ApplyPreV6Defaults`, and no other field differs from a current-version document with the same
  blocks

#### Scenario: Current-version bytes bypass migration steps
- **WHEN** bytes written in the current version (v7) are deserialized
- **THEN** no defaulting method is invoked and every field is read directly from the byte stream

#### Scenario: A version outside the accepted window fails to deserialize
- **WHEN** bytes written below `MinVersion` (e.g. v3 or v4) or above `Version` are deserialized
- **THEN** `TryDeserialize` returns `false` (fail-safe) rather than partially reading

### Requirement: Each accepted prior version has a dedicated older-blob unit test
For every prior-version format that `ScribeDocumentCodec` accepts, `ScribeDocumentCodecTests`
SHALL contain a test that hand-builds a byte array in exactly that prior format and asserts
specific field values on the deserialized document — not merely that deserialization returns
`true`. The test name SHALL make the version and the defaulting being verified obvious (e.g.
`TryDeserialize_V5Bytes_Succeeds_AndDefaultsTrackerLinkFields`,
`TryDeserialize_V6Bytes_Succeeds_AndDefaultsLinkLabel`). A version outside the accepted window
SHALL likewise have a fail-safe test (e.g. `TryDeserialize_V4Bytes_FailsSafely`).

#### Scenario: v5 older-blob test asserts tracker/link fields default
- **WHEN** a hand-built v5 byte array (with no tracker/link fields) is deserialized
- **THEN** the test asserts the specific defaulted values of the tracker/link fields on the
  restored blocks (not merely `ok == true`)

#### Scenario: v6 older-blob test asserts link label defaults
- **WHEN** a hand-built v6 byte array (with no per-block link label) is deserialized
- **THEN** the test asserts the defaulted link-label value on the restored blocks (not merely
  `ok == true`)

#### Scenario: Below-floor version test asserts fail-safe
- **WHEN** a hand-built v4 byte array (below `MinVersion`) is deserialized
- **THEN** the test asserts `TryDeserialize` returns `false` (not a partial read)
