## ADDED Requirements

### Requirement: A read-only policy preset for a fired tablet

`ScribeDocumentPolicy` SHALL provide a read-only preset for a fired tablet whose `CanAdd` and `CanPin`
predicates always deny, so no task can be added or pinned. It SHALL remain a Core, VS-API-free rule type
(no Vintage Story reference), consistent with the existing presets. The soft `Tablet` preset (10 tasks,
1 pin) SHALL be unchanged.

#### Scenario: The fired preset denies all mutation

- **WHEN** the fired-tablet policy's `CanAdd` or `CanPin` is evaluated
- **THEN** it denies, regardless of current task/pin counts

#### Scenario: The soft Tablet preset is unchanged

- **WHEN** a soft clay tablet's policy is evaluated
- **THEN** it still allows up to 10 tasks and 1 pin as before
