## ADDED Requirements

### Requirement: ScribeDialogBase exposes a capability flag for filter pills and subtask collapse
`ScribeDialogBase` SHALL expose a single capability flag that gates whether a dialog's Read View
renders the filter-pill row (`read-view-filter-pills`) and subtask-collapse toggles
(`read-view-subtask-collapse`) together — one flag governs both features, since they're excluded
from the same surfaces as a unit. The flag SHALL default to enabled, so every existing subclass
(Lectern, Notebook, Clockmaker's Notebook, Chalkboard, Scriptorium, Assignment Desk, Inbox) gets
both features with no per-subclass change required.

#### Scenario: A subclass with no override supports both features
- **WHEN** a `ScribeDialogBase` subclass does not override the capability flag
- **THEN** its Read View renders the filter-pill row and any applicable subtask-collapse toggles

#### Scenario: A subclass that overrides the flag to disabled supports neither feature
- **WHEN** a `ScribeDialogBase` subclass overrides the capability flag to disabled
- **THEN** its Read View renders no filter-pill row and no subtask-collapse toggles, regardless of
  the underlying document's contents
