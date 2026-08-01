# hud-anchor-optimization Specification

## Purpose
This capability covers the per-frame cost of positioning the always-on Scribe pin HUD. The HUD's
window position is a pure function of a small set of inputs (screen dimensions, anchor setting,
offsets, minimap visibility); recomputing it every frame is wasteful when those inputs are
unchanged. The capability requires the position math to be gated on an input-change check so
`ApplyAnchor` only reassigns `WindowPos` when something that affects it actually moved.

## Requirements

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
