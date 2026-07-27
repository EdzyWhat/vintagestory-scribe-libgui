## ADDED Requirements

### Requirement: The HUD pin list updates by reconciliation
The pinned-task HUD SHALL reflect changes to the displayed pin list — a pin push from the server, an
undo-window expiry, or a row toggle — by updating its persistent content in place through
reconciliation, rather than by unmounting and recreating the whole HUD widget tree. Rows SHALL carry
a stable identity so that a change to one row, or to the list order, preserves the other rows' state
(including any in-progress animation). The HUD MAY still open and close itself as a whole when the
player's pin count crosses the zero boundary.

#### Scenario: A pin-set change reconciles the HUD list
- **WHEN** the player's pin set changes while the HUD is shown (a pin added, removed, completed, or
  its snapshot refreshed)
- **THEN** the HUD updates the affected rows in place through reconciliation, preserving the other
  rows and any running animation, without recreating the entire HUD tree

#### Scenario: A row toggle does not rebuild the whole HUD
- **WHEN** the player toggles a pinned task's checkbox on the HUD
- **THEN** the change is reflected by updating the affected row's state, not by rebuilding the whole
  HUD tree

#### Scenario: Zero-boundary open/close is still whole-HUD
- **WHEN** the player's pin count crosses from zero to non-zero or back
- **THEN** the HUD MAY open or close itself as a whole, which is distinct from the in-place
  reconciliation used for changes while it is already open
