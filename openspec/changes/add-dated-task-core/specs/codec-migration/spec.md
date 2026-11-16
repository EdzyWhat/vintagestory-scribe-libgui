## MODIFIED Requirements

### Requirement: Document codec reads an accepted window of prior versions via progressive reads
`ScribeDocumentCodec` SHALL read any format version within an accepted window
`[MinVersion, Version]` using **progressive append-only reads**: each
version's trailing fields are read only behind a `version >=` threshold, and fields absent from
an older-but-accepted blob are set to their documented defaults by a single named defaulting
method (`ApplyPreV6Defaults`), which SHALL be the sole place per-version defaulting lives.
Ad-hoc inline branching on a `bool isCurrent` flag SHALL NOT be used. A version below
`MinVersion` or above `Version` SHALL fail-safe (`TryDeserialize` returns `false`) rather than
partially reading. `MinVersion` advances only when the oldest shipped layout is deliberately
dropped; shipped v5 documents (v1.1.1) live in real saves, so the window must not narrow below
them silently. (Mirrors `ScribePinCodec`'s progressive window.) Comments, XML docs, and
`docs/CODEC-MIGRATION.md` SHALL name the same window as the code's current `MinVersion` and
`Version` constants, and SHALL be updated in the same change that bumps either.

#### Scenario: Accepted older versions are read via progressive gates and defaulted
- **WHEN** bytes written in an accepted older version (v5, v6, or v7) are deserialized
- **THEN** deserialization succeeds, fields that version does not carry (tracker/link fields for
  v5; the per-block link label for v6; `RecipeSignature` for v7) are set to their documented defaults via
  `ApplyPreV6Defaults`, and no other field differs from a current-version document with the same
  blocks

#### Scenario: Current-version bytes bypass migration steps
- **WHEN** bytes written in the current `Version` are deserialized
- **THEN** no defaulting method is invoked and every field is read directly from the byte stream

#### Scenario: A version outside the accepted window fails to deserialize
- **WHEN** bytes written below `MinVersion` (e.g. v3 or v4) or above `Version` are deserialized
- **THEN** `TryDeserialize` returns `false` (fail-safe) rather than partially reading

#### Scenario: Docs and comments match the code window
- **WHEN** `ScribeDocumentCodec.cs` comments, `ScribeDocumentCodecTests` comments, and
  `docs/CODEC-MIGRATION.md` are inspected
- **THEN** they state the same `Version` and `MinVersion` as the code constants
