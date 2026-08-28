## ADDED Requirements

### Requirement: Changing the task font does not change single-line row height
Selecting a different family in the task-font selector SHALL NOT change the laid-out height of a
single-line task row relative to Caudex at the same window font scale. The selector still changes
glyphs immediately (existing live-update requirement); only the line-box height is invariant. This
applies to Lectern Read and Edit views.

#### Scenario: Cycling the selector leaves a single-line Edit row in place
- **WHEN** a Lectern is on the Edit view showing a single-line task
- **AND** the player cycles through every option in the task-font selector
- **THEN** that row's vertical position and height do not jump (within 1 px) as the family changes

#### Scenario: Cycling the selector leaves a single-line Read row in place
- **WHEN** a Lectern is on the Read view showing a single-line task
- **AND** the player cycles through every option in the task-font selector
- **THEN** that row's vertical position and height do not jump (within 1 px) as the family changes
