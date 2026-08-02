## ADDED Requirements

### Requirement: ScribeDocumentPolicy is a Core, VS-API-free rule type

The system SHALL define a `ScribeDocumentPolicy` value type in `src/Core/` that expresses per-tier
limits with the fields `int? MaxBlocks`, `int? MaxPins`, and `bool ReadOnly`. A `null` limit SHALL
mean "uncapped". The type MUST NOT reference the Vintage Story API, so it is unit-testable with
`dotnet test` and no game install.

#### Scenario: Uncapped policy imposes no limits

- **WHEN** a `ScribeDocumentPolicy` is constructed with `MaxBlocks = null`, `MaxPins = null`, and
  `ReadOnly = false`
- **THEN** its `CanAdd` and `CanPin` predicates permit any count, matching the Notebook/Lectern's
  uncapped behavior

#### Scenario: Type lives in Core with no game reference

- **WHEN** the Core project is compiled without a Vintage Story API reference
- **THEN** `ScribeDocumentPolicy` compiles and its tests run with no game install

### Requirement: Tablet preset caps a tablet at 10 tasks and 1 pin

The type SHALL expose a `Tablet` preset with `MaxBlocks = 10` (counting task blocks) and
`MaxPins = 1`. This preset is the tablet tier's limit and SHALL be the value `TabletHost` applies.

#### Scenario: Tablet preset values

- **WHEN** code reads the `Tablet` preset
- **THEN** `MaxBlocks` is 10 and `MaxPins` is 1

### Requirement: CanAdd and CanPin predicates enforce the caps

`ScribeDocumentPolicy` SHALL provide a `CanAdd` predicate that returns false once the current task
count has reached `MaxBlocks`, and a `CanPin` predicate that returns false once the current pin
count has reached `MaxPins`. When a limit is `null` the corresponding predicate SHALL always return
true.

#### Scenario: Eleventh task is refused under the Tablet preset

- **WHEN** a document already holds 10 task blocks under the `Tablet` preset and `CanAdd` is
  consulted
- **THEN** `CanAdd` returns false, so the "add task" affordance is disabled

#### Scenario: Second pin is refused under the Tablet preset

- **WHEN** a tablet already has 1 pin under the `Tablet` preset and `CanPin` is consulted
- **THEN** `CanPin` returns false

#### Scenario: Tenth task is still allowed

- **WHEN** a document holds 9 task blocks under the `Tablet` preset and `CanAdd` is consulted
- **THEN** `CanAdd` returns true

### Requirement: Policy is applied at the mutation boundary, not inside the model

The caps SHALL be enforced at the host/editor mutation boundary (e.g. `TabletHost`), NOT inside
`ScribeDocument`. `ScribeDocument` SHALL remain tier-agnostic and uncapped so the Notebook and
Lectern are unaffected, and so the same document model backs every tier.

#### Scenario: ScribeDocument remains uncapped

- **WHEN** `ScribeDocument.AddTask` is called directly with any number of existing tasks
- **THEN** it succeeds regardless of any policy, because the cap is enforced only at the boundary
  that consults `ScribeDocumentPolicy`
