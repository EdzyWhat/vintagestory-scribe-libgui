## ADDED Requirements

### Requirement: Rows carry a one-level indentation depth
Every block SHALL carry an integer `Depth` in `{0, 1}` that determines its indentation in the row
list. Depth 0 is a top-level row; depth 1 is a subtask, rendered indented beneath the row above it.
The model SHALL clamp `Depth` into `[0, 1]` on set; values outside that range SHALL NOT be stored.
This capability is **kind-agnostic**: any block kind (Task, Text/Note, Tracker, Link, Craft) MAY be
a subtask. Depth SHALL persist through every codec (binary, JSON, TSV) with no format change — the
`Depth` field and its serialization already exist.

#### Scenario: A depth-1 row renders indented
- **WHEN** a row has `Depth` 1 and the row above it exists
- **THEN** the row is drawn indented relative to depth-0 rows, marking it as a subtask of the group above

#### Scenario: Depth is clamped to one level
- **WHEN** a row's `Depth` is set to 2, a larger value, or a negative value
- **THEN** the stored `Depth` is clamped into `[0, 1]`

#### Scenario: Depth survives a save/load round-trip
- **WHEN** a document containing a depth-1 row is serialized and deserialized (binary, JSON, or TSV)
- **THEN** the row's `Depth` is preserved as 1

### Requirement: The drag grip toggles a row's depth on tap
The row drag grip SHALL support two distinct gestures: a **press-and-hold drag** reorders the row
(existing behavior), and a **tap** (press and release over the grip without dragging) toggles the
row's `Depth` between 0 and 1. Tapping the grip of a depth-0 row makes it a depth-1 subtask; tapping
the grip of a depth-1 row returns it to depth 0. The tap gesture SHALL apply to any block kind.

#### Scenario: Tapping a top-level row's grip makes it a subtask
- **WHEN** the player taps (presses and releases without dragging) the grip of a `Depth` 0 row
- **THEN** the row's `Depth` becomes 1 and it renders indented

#### Scenario: Tapping a subtask's grip promotes it back to top level
- **WHEN** the player taps the grip of a `Depth` 1 row
- **THEN** the row's `Depth` becomes 0 and it renders un-indented

#### Scenario: Press-and-hold still reorders rather than toggling depth
- **WHEN** the player presses the grip and drags to reorder the row
- **THEN** the row is reordered and its `Depth` is not toggled by that gesture

### Requirement: Indentation is bounded to one level
The system SHALL support at most one level of indentation. There SHALL be no depth-2 (sub-subtask)
rendering or toggle path; a subtask cannot own further subtasks. Any operation that would produce a
depth greater than 1 SHALL be clamped to 1.

#### Scenario: No second indentation level is reachable
- **WHEN** any code path attempts to set a row's `Depth` above 1
- **THEN** the depth is clamped to 1 and no deeper indentation is rendered
