## ADDED Requirements

### Requirement: A Scribe-owned scrolling list container reconciles rows from current data
The system SHALL provide a Scribe-owned scrolling list container that renders its rows from the
current data on each parent rebuild, WITHOUT requiring a full widget-tree teardown to reflect a
change. When the host rebuilds the container (e.g. via a parent `SetState`), each row SHALL reflect
the current underlying data — a row whose data changed SHALL update even if the row count is
unchanged. This container is what the mod uses where the stock framework list would otherwise force
a full-tree rebuild to reflect an external change.

#### Scenario: A same-count data change is reflected without a full rebuild
- **WHEN** the underlying list data changes but the number of rows stays the same (e.g. an external
  edit toggles one existing task)
- **THEN** the affected row updates to show the new data after a parent rebuild of the container,
  without the host unmounting and recreating its entire widget tree

#### Scenario: An external resync repaints the list
- **WHEN** an authoritative update arrives from outside the local view (another viewer edits the
  document, or an autosave lands)
- **THEN** the container's rows repaint from the new authoritative data

### Requirement: List rows have stable identity across insertions, removals, and reorders
Each row in the container SHALL carry a stable identity key tied to the item it represents (not its
position), so that on an insertion, removal, or reorder the framework matches each surviving row to
its existing element — preserving that row's own state (focus, caret, in-progress edit, animation)
rather than re-seeding it from a shifted position.

#### Scenario: A surviving row keeps its state across a sibling's removal
- **WHEN** a row is removed from the list and other rows remain
- **THEN** each surviving row keeps its own state (e.g. a focused row keeps its caret and unsaved
  text), because rows are matched by identity, not by index

#### Scenario: A reordered row keeps its identity
- **WHEN** rows are reordered
- **THEN** each row's state travels with its item to the new position rather than being applied to
  whatever item now sits at the old position
