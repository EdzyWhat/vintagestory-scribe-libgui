## MODIFIED Requirements

### Requirement: A gear control opens the settings surface
The system SHALL provide a gear control that opens Scribe's per-player settings surface. The gear
SHALL be present in the Lectern dialog's chrome (available from both the read and editor views) and on
the pinned-task HUD adjacent to its collapse control. Activating a gear control SHALL TOGGLE the
settings surface: if the settings surface is not currently shown it SHALL be shown, and if it is
already shown it SHALL be dismissed. The surface SHALL also offer a way to dismiss it and return to the
previously shown content.

#### Scenario: Gear opens settings from the Lectern
- **WHEN** a player activates the gear control in an open Lectern dialog while the settings surface is not shown
- **THEN** the settings surface is shown

#### Scenario: Gear opens settings from the HUD
- **WHEN** a player activates the gear control on the pinned-task HUD while the settings surface is not shown
- **THEN** the settings surface is shown

#### Scenario: Activating a gear again closes the open settings surface
- **WHEN** the settings surface is already open and the player activates the Lectern gear or the HUD gear
- **THEN** the settings surface is dismissed (toggled closed) rather than re-opened or left unchanged

#### Scenario: Dismissing settings returns to prior content
- **WHEN** the settings surface is shown and the player dismisses it
- **THEN** the previously shown content is restored

### Requirement: Settings are grouped into Behavior and Appearance sections
The settings surface SHALL present its controls in three labeled sections separated by horizontal
dividers: a **Mod Behavior** section, a **Window Appearance** section, and a **HUD Appearance** section.
The Mod Behavior section SHALL contain the completion policy and the collapsed-HUD flag (the latter as an
alternative to the HUD's own collapse control and hotkey). The Window Appearance section SHALL contain the
Pixel-Art Display toggle, the Pixel Art Size, and the window font-size scale. The HUD Appearance section
SHALL contain the HUD anchor, HUD maximum rows, HUD row width, HUD horizontal and vertical offsets, and the
HUD font-size scale.

#### Scenario: Controls appear under their section
- **WHEN** the settings surface is shown
- **THEN** the completion-policy and collapsed-HUD controls appear under Mod Behavior; the Pixel-Art
  Display, Pixel Art Size, and window font-scale controls appear under Window Appearance; and the HUD
  anchor, rows, width, offsets, and HUD font-scale controls appear under HUD Appearance

#### Scenario: Sections are visually separated
- **WHEN** the settings surface is shown
- **THEN** a horizontal divider separates each of the three sections from the next

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

#### Scenario: Offsets share one row
- **WHEN** the HUD Appearance section is shown
- **THEN** the horizontal and vertical HUD offset controls appear together on a single row

## ADDED Requirements

> **Dropped (2026-07-26 playtest, submission 2026-07-26T22-24-24):** an earlier ADDED requirement here —
> "A clamped numeric field surfaces its valid range as feedback" (a red range line beneath the field) — was
> removed at the tester's request. Silent clamp-on-blur was judged sufficient; the range line was unwanted
> visual noise. The clamp behavior itself (clamp on blur, not per-keystroke) is retained above; only the
> feedback-text affordance is dropped.

### Requirement: The settings surface paints a default window background
The standalone settings window SHALL paint the active LibGUI theme's default surface color behind the
settings form, so the form's inputs sit on an opaque window panel rather than floating on a fully
transparent frame. The window SHALL continue to follow the player's global LibGUI theme (it is not forced
into Scribe's pixel-art theme).

#### Scenario: The form sits on a painted panel
- **WHEN** the standalone settings window is opened
- **THEN** the theme's default surface color is painted behind the form so the inputs read as being on a
  solid window panel, not on a transparent background

#### Scenario: The background follows the global theme
- **WHEN** the player's global LibGUI theme differs from Scribe's pixel-art theme
- **THEN** the painted background uses the global theme's surface color, consistent with the window's frame
