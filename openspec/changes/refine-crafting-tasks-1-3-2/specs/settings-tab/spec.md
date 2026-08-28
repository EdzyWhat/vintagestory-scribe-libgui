## ADDED Requirements

### Requirement: Subtask Behavior is a Settings picker
The Behavior section of Scribe Settings SHALL include a Subtask Behavior dropdown with **Bound to
parent** (default), **Independent**, and **Discard children**, each with localized helptext describing
complete and trash of a parent.

#### Scenario: Player can choose Independent
- **WHEN** the player selects Independent in Subtask Behavior
- **THEN** subsequent parent completions leave children as they are

### Requirement: HUD gear visibility is a Settings checkbox
The HUD section of Scribe Settings SHALL include a boolean to show or hide the pinned-task HUD
settings gear, default on.

#### Scenario: Toggle HUD gear from Settings
- **WHEN** the player turns off the HUD gear visibility checkbox
- **THEN** the HUD header no longer shows a settings gear, and the setting persists across sessions

### Requirement: HUD maximum rows may be set up to 30
The HUD maximum-rows numeric control SHALL allow values from 1 through **30**. Values above 30 SHALL
clamp to 30 on load and on blur. A stored value of 30 SHALL survive reload (it SHALL NOT clamp back to 10).

#### Scenario: Setting 30 sticks
- **WHEN** the player sets HUD maximum rows to 30 and reopens Settings
- **THEN** the control still shows 30
