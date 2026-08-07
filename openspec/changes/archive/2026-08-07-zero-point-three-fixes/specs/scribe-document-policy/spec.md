## ADDED Requirements

### Requirement: A refused add is surfaced to the player, not silently swallowed

When the host consults `CanAdd` before growing a document and the predicate refuses (the tier's task cap
is reached), the host SHALL make that refusal observable to the player through the game's standard
in-game error path, rather than only declining to add the block. The `ScribeDocumentPolicy` predicates
SHALL remain pure boolean rule functions with no Vintage Story API dependency (the Core invariant); the
observability requirement is on the host/editor mutation boundary that consults them, which is where the
in-game feedback SHALL be raised.

#### Scenario: A capped host reports the refusal

- **WHEN** a host at a capped tier consults `CanAdd` at the cap and it returns false
- **THEN** the host does not add the block AND surfaces an in-game error to the player, rather than
  returning silently

#### Scenario: The policy type stays API-free

- **WHEN** `CanAdd` and `CanPin` are evaluated
- **THEN** they behave as pure boolean predicates over the counts and limits, with no reference to the
  Vintage Story API, so they remain unit-testable without a game install
