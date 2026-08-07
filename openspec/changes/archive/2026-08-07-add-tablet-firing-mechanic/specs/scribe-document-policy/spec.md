## ADDED Requirements

### Requirement: A read-only policy preset for a non-editable tablet

`ScribeDocumentPolicy` SHALL provide a read-only preset for a non-editable tablet (hard or fired) whose
`CanAdd` and `CanPin` predicates always deny, so no task can be added or pinned. It SHALL remain a Core,
VS-API-free rule type (no Vintage Story reference), consistent with the existing presets. The wet `Tablet`
preset (10 tasks, 1 pin) SHALL be unchanged, and SHALL apply to a wet tablet only.

#### Scenario: The read-only preset denies all mutation

- **WHEN** the non-editable-tablet policy's `CanAdd` or `CanPin` is evaluated
- **THEN** it denies, regardless of current task/pin counts

#### Scenario: The wet Tablet preset is unchanged

- **WHEN** a wet clay tablet's policy is evaluated
- **THEN** it still allows up to 10 tasks and 1 pin as before
