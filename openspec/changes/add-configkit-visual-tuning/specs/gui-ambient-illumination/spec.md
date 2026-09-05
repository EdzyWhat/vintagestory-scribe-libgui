## MODIFIED Requirements

### Requirement: Illumination transitions are smoothed, not stepped

As the light reaching the player changes (walking between light and shadow, day/night shift, a
held light coming and going), the GUI's rendered brightness and hue SHALL ease toward the new
value over a short interval (configurable; ~400ms by default) rather than snapping instantly
between values. The smoothing SHALL be frame-rate independent. A newly opened dialog SHALL adopt
the current light immediately (no visible fade-up on open).

#### Scenario: Brightness glides as the player moves

- **WHEN** the player walks from a lit area into shadow (or vice versa) with a Scribe GUI open
- **THEN** the GUI's brightness transitions smoothly over a short interval rather than jumping
  abruptly from one level to the next

#### Scenario: A configured smoothing interval is honored

- **WHEN** the visual-tuning config sets the smoothing time constant to a value different from
  the default
- **THEN** the brightness/hue transition eases over an interval reflecting the configured value
  rather than the ~400ms default
