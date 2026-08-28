## MODIFIED Requirements

### Requirement: Task rows expose a pin-toggle affordance
Each task row in the editor view SHALL provide a control that toggles the task's pinned
flag. Text-section (note) rows SHALL also expose this control so notes can be pinned.

#### Scenario: Toggling pin from the GUI
- **WHEN** the player activates a task row's pin-toggle control
- **THEN** the task's pinned flag flips, and the control's visual state reflects the new
  value

#### Scenario: Text sections expose a pin control
- **WHEN** a text-section row is composed in the editor
- **THEN** a pin-toggle control is present for that row

### Requirement: Read-view rows expose a pin-toggle affordance
Each task row in the read view SHALL provide a control that toggles the task's pinned state for the
acting player, addressed by stable identity, mirroring the editor view's pin control. Text-section
rows SHALL also expose this control. The control's visual state SHALL reflect whether the task is
currently pinned for the player.

#### Scenario: Toggling pin from a read-view row
- **WHEN** the player activates a read-view task row's pin-toggle control
- **THEN** the task's pinned state for that player flips and the control's visual state reflects the
  new value

#### Scenario: Read-view text sections expose a pin control
- **WHEN** a text-section row is composed in the read view
- **THEN** a pin-toggle control is present for that row
