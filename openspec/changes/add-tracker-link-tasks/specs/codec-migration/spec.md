## MODIFIED Requirements

### Requirement: Document codec accepts prior version via a named migration step
`ScribeDocumentCodec` SHALL accept byte arrays written in the immediately prior format version
(currently v5) by running them through a named private migration method before constructing
the final `ScribeDocument`. The migration method SHALL be named to reflect which version
transition it handles (e.g. `ApplyV5ToV6Migrations`) and SHALL be the sole place where
version-specific defaulting or field-inference for that step lives. Ad-hoc inline branching
on a `bool isCurrent` flag SHALL NOT be used; the `isCurrent` pattern is replaced by this
named-step pattern. Migration steps SHALL remain single-transition (immediately-prior only); a
version older than the immediately prior one SHALL fail to deserialize rather than chaining
through multiple steps.

#### Scenario: Prior-version bytes are migrated to current schema via a named step
- **WHEN** bytes written in the immediately prior format version (v5) are deserialized
- **THEN** deserialization succeeds, the tracker/link fields (which v5 does not carry) are set to
  their documented defaults via the named migration method, and no other fields differ from a
  current-version document with the same blocks

#### Scenario: Current-version bytes bypass migration steps
- **WHEN** bytes written in the current version (v6) are deserialized
- **THEN** no migration methods are called and every field is read directly from the byte stream

#### Scenario: A version older than the immediately prior one fails to deserialize
- **WHEN** bytes written two or more versions behind the current version (e.g. v4) are deserialized
- **THEN** deserialization reports failure rather than migrating through multiple steps

### Requirement: Each supported prior version has a dedicated older-blob unit test
For every prior-version format that `ScribeDocumentCodec` accepts, `ScribeDocumentCodecTests`
SHALL contain a test that hand-builds a byte array in exactly that prior format and asserts
specific field values on the deserialized document — not merely that deserialization returns
`true`. The test name SHALL follow the pattern
`TryDeserialize_V<N>Bytes_<FieldName>_IsUpgraded` or similar to make the migration step
being verified obvious.

#### Scenario: v5 older-blob test asserts tracker fields default
- **WHEN** a hand-built v5 byte array (with no tracker/link fields) is deserialized
- **THEN** the test asserts the specific defaulted values of the tracker/link fields on the
  restored blocks (not merely `ok == true`)
