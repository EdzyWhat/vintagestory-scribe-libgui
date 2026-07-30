## ADDED Requirements

### Requirement: HUD position is recomputed only when inputs change
The HUD SHALL cache the inputs that determine its window position (screen dimensions,
anchor setting, offsets, minimap visibility). The position math SHALL be skipped on any
frame where all cached inputs match the current values.

#### Scenario: Position is stable when nothing changes
- **WHEN** the game window size, HUD settings, and minimap visibility are unchanged between frames
- **THEN** `ApplyAnchor` skips recomputation and `WindowPos` is not reassigned

#### Scenario: Position updates on screen resize
- **WHEN** the game window is resized
- **THEN** `ApplyAnchor` detects the changed screen dimensions on the next frame and recomputes `WindowPos`
