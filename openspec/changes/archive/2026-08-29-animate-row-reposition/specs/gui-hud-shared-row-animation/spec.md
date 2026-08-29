## MODIFIED Requirements

### Requirement: Non-removing HUD state changes are not container departures
A HUD pin whose identity remains in the item set SHALL NOT be treated as a container departure. In
particular, a Sink / UnpinSink completion that moves a still-present pin to the bottom SHALL be an
in-set reorder, not a container departure, and its existing sink countdown/overlay SHALL continue to
render correctly even while a different row is collapsing out. The reorder itself SHALL animate via
the shared reposition mechanism (`animated-task-list`) — every row whose slot shifts because of the
move displaces smoothly to its new position — rather than jumping instantly.

#### Scenario: Sink moves a row without departing it
- **WHEN** a HUD pin is completed under the Sink policy
- **THEN** its identity stays in the item set and it moves to the bottom via the shared reposition
  animation, with no collapse-and-remove animation

#### Scenario: Sink overlay coexists with a concurrent collapse
- **WHEN** one HUD row is sinking while another has departed the item set and is collapsing
- **THEN** both render correctly — the sinking row shows its countdown/overlay and the departed row
  collapses — without interfering with each other

#### Scenario: Rows displaced by a sink move animate their own shift
- **WHEN** a pin sinks to the bottom, shifting the slots of the rows it moves past
- **THEN** each displaced row animates its own displacement to its new slot via the same reposition
  mechanism a Top-insertion or removal would use, rather than snapping
