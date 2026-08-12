## ADDED Requirements

### Requirement: The collapse mechanism is drivable by a container that infers departures from a data diff
The row-collapse mechanism SHALL be usable not only by a host surface that explicitly tracks
departing rows, but also by a reusable container that infers departures by diffing its
identity-keyed item set between builds. When driven this way, the departing-row bookkeeping — the
per-identity snapshot of the departing row, the slot/display-index at which it collapses, and the
deferral of its final removal until the collapse completes — SHALL be provided by the container
itself, so a hosting surface does not re-implement that bookkeeping to obtain the collapse.

#### Scenario: A container drives the collapse from a data diff alone
- **WHEN** a container renders identity-keyed rows and an identity present on the previous build is
  absent on the next
- **THEN** the collapse mechanism animates that row out at its former slot and removes it on
  completion, driven entirely by the container's diff without the hosting surface tracking the
  departing row

#### Scenario: The collapse still survives rebuilds when container-driven
- **WHEN** a container-driven collapse is in progress and the host rebuilds (reconciles) the tree
  before it finishes
- **THEN** the collapse continues smoothly from where it was, neither snapping to zero nor
  restarting from full height
