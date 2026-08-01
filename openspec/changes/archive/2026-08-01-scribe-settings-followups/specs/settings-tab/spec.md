## MODIFIED Requirements

### Requirement: Each preference maps to a labeled input control
The settings surface SHALL expose one input control per preference: the completion policy and the HUD
anchor SHALL each use a dropdown of their allowed values; the HUD maximum rows, HUD row width, HUD
horizontal and vertical offsets, and the HUD and window font-size scales SHALL each use a **numeric-entry
control** (not a slider) constrained to its allowed range and stepping by a sensible increment, so that
adjusting a value never captures the scroll wheel or otherwise interferes with scrolling the form; a
numeric-entry control SHALL also step its value by that increment when the up or down arrow key is pressed
while it is focused (clamped to its range); the font-size scales SHALL be entered as a percent; the HUD
horizontal and vertical offsets SHALL be laid out together on one row, the HUD maximum rows and HUD row
width SHALL be laid out together on one row, and the HUD and window font-size scales SHALL be laid out
together on one row; the collapsed-HUD flag SHALL use a checkbox laid out to hug its label rather than
stretch the full width of the form. A numeric-entry control SHALL reflect the clamped/normalized value
after a write (an out-of-range entry SHALL settle onto the clamped value). Every control SHALL be labeled,
and the enumerated options of the dropdowns SHALL be shown by human-readable, localized labels, with the
mid-edge HUD-anchor options presented as "Mid-Left" and "Mid-Right".

#### Scenario: Enum preferences are chosen from dropdowns
- **WHEN** a player opens the completion-policy or HUD-anchor control
- **THEN** it presents the allowed values as a dropdown of localized labels, and the mid-edge anchor
  options read "Mid-Left" and "Mid-Right"

#### Scenario: Numeric preferences use numeric-entry controls
- **WHEN** a player adjusts the HUD rows, row width, an offset, or a font-size scale
- **THEN** the value is changed through a numeric-entry control (not a slider), and doing so does not
  capture the scroll wheel or scroll the form

#### Scenario: Arrow keys step a focused numeric control
- **WHEN** a numeric-entry control is focused and the player presses the up or down arrow key
- **THEN** the value increases (up) or decreases (down) by that control's increment, clamped to its
  allowed range

#### Scenario: Paired controls share one row
- **WHEN** the Appearance section is shown
- **THEN** the horizontal and vertical HUD offsets appear together on one row, the HUD maximum rows and
  HUD row width appear together on one row, and the HUD and window font-size scales appear together on one
  row

#### Scenario: The collapse checkbox hugs its label
- **WHEN** the collapsed-HUD checkbox is shown
- **THEN** the checkbox and its label sit together at the start of the row rather than stretching across
  the full width of the form
