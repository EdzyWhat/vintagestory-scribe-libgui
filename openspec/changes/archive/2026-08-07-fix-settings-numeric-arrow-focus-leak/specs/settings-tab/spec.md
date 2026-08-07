## MODIFIED Requirements

### Requirement: Each preference maps to a labeled input control
The settings surface SHALL expose one input control per preference: the completion policy and the HUD
anchor SHALL each use a dropdown of their allowed values; the HUD maximum rows, HUD row width, HUD
horizontal and vertical offsets, and the HUD and window font-size scales SHALL each use a **numeric-entry
control** (not a slider) constrained to its allowed range and stepping by a sensible increment, so that
adjusting a value never captures the scroll wheel or otherwise interferes with scrolling the form; the
font-size scales SHALL be entered as a percent; the HUD horizontal and vertical offsets SHALL be laid out
together on one row; the collapsed-HUD flag SHALL use a checkbox. A numeric-entry control SHALL clamp its
value to the allowed range when it LOSES FOCUS (not on each keystroke), so a player can select all of a
field's text and type a replacement value without the field snapping to a bound mid-edit; on losing focus
an out-of-range entry SHALL settle onto the clamped value. Every control SHALL be labeled, and the
enumerated options of the dropdowns SHALL be shown by human-readable, localized labels.

A numeric-entry control SHALL also support stepping its value with the up and down arrow keys while the
control is focused: the up arrow SHALL increment by the control's step and the down arrow SHALL decrement
by it. Because a step writes the new value through immediately (which rebuilds the settings form), focus
SHALL RELIABLY remain on the stepped numeric control across CONSECUTIVE arrow presses, so each press steps
the value again rather than only the first. In particular, when a Scribe document editor (Lectern,
Notebook, or Tablet) is also open, arrow-key stepping SHALL NOT leak focus to a document editor row after
the write-through rebuild — the arrow keys SHALL continue to drive the focused numeric control, not that
row's text caret.

#### Scenario: Enum preferences are chosen from dropdowns
- **WHEN** a player opens the completion-policy or HUD-anchor control
- **THEN** it presents the allowed values as a dropdown of localized labels

#### Scenario: Numeric preferences use numeric-entry controls
- **WHEN** a player adjusts the HUD rows, row width, an offset, or a font-size scale
- **THEN** the value is changed through a numeric-entry control (not a slider), and doing so does not
  capture the scroll wheel or scroll the form

#### Scenario: A field can be cleared and retyped without a mid-edit snap
- **WHEN** a player selects all of a numeric field's text and types a new value, passing through
  intermediate strings (including an empty field) whose value would be out of range
- **THEN** the field does not clamp while it is being edited, so the player can finish typing the intended
  value before any clamping occurs

#### Scenario: An out-of-range value clamps when the field loses focus
- **WHEN** a player leaves a numeric field (blur / focus moves elsewhere) with a value outside its allowed range
- **THEN** the field settles onto the clamped value for that preference

#### Scenario: Consecutive arrow presses keep stepping the focused numeric field
- **WHEN** a player focuses a numeric field on the settings surface and presses the up or down arrow key three or more times in a row
- **THEN** the value steps on EVERY press (not just the first), and focus remains on that numeric field throughout

#### Scenario: Arrow stepping does not leak focus to an open document editor row
- **WHEN** a player has a Scribe document editor (Lectern, Notebook, or Tablet) open with a recently-touched editor row, then focuses a settings numeric field and presses the up or down arrow repeatedly
- **THEN** each arrow press steps the numeric field's value, and focus never silently transfers to the document editor row so that the arrows never drive that row's text caret instead

#### Scenario: Editor rows still navigate by visual line when genuinely focused
- **WHEN** a document editor row (not a settings numeric field) is the focused control and the player presses the up or down arrow
- **THEN** the caret moves between visual lines within that row as defined by the arrow-key-line-caret-nav behavior, unchanged by this fix

#### Scenario: Offsets share one row
- **WHEN** the HUD Appearance section is shown
- **THEN** the horizontal and vertical HUD offset controls appear together on a single row
