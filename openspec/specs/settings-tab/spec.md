# settings-tab

## Purpose

TBD - created via spec sync from change `add-settings-tab`. This capability covers Scribe's in-mod
per-player settings surface: a gear-opened, host-agnostic settings widget that embeds into a host
dialog's central content region (or the HUD), groups preferences into Behavior and Appearance sections,
exposes one labeled input control per preference, writes through immediately with live preview, and
surfaces localized helptext.

## Requirements

### Requirement: A gear control opens the settings surface
The system SHALL provide a gear control that opens Scribe's per-player settings surface. The gear
SHALL be present in the Lectern dialog's chrome (available from both the read and editor views) and on
the pinned-task HUD adjacent to its collapse control. Activating the gear SHALL show the settings
surface; the surface SHALL offer a way to dismiss it and return to the previously shown content.

#### Scenario: Gear opens settings from the Lectern
- **WHEN** a player activates the gear control in an open Lectern dialog
- **THEN** the dialog's central content is replaced by the settings surface

#### Scenario: Gear opens settings from the HUD
- **WHEN** a player activates the gear control on the pinned-task HUD
- **THEN** the settings surface is shown

#### Scenario: Dismissing settings returns to prior content
- **WHEN** the settings surface is shown and the player dismisses it
- **THEN** the previously shown content is restored

### Requirement: Settings replace a host dialog's central content region
When opened from a block or item dialog, the settings surface SHALL be rendered as a swap of that
dialog's central content region, not as a separate window, and SHALL be implemented as a host-agnostic
widget so different host dialogs (of differing sizes) can embed the same settings surface. Entering the
settings surface from an editing context that holds an edit lock SHALL first commit any pending edit and
release the lock, so the settings surface is shown lock-free.

#### Scenario: Settings reuse the host dialog's central region
- **WHEN** the settings surface is opened from a host block/item dialog
- **THEN** it occupies that dialog's central content region and the dialog's own chrome remains

#### Scenario: Entering settings from the editor releases the edit lock
- **WHEN** a player opens the settings surface while editing a Lectern document under the edit lock
- **THEN** the pending edit is committed and the edit lock is released before the settings surface is shown

### Requirement: Settings are grouped into Behavior and Appearance sections
The settings surface SHALL present its controls in two labeled sections: a Behavior section and an
Appearance section. The Behavior section SHALL contain the completion policy. The Appearance section
SHALL contain the HUD anchor, HUD maximum rows, HUD row width, HUD horizontal and vertical offsets, the
HUD font-size scale, and the window font-size scale. The collapsed-HUD flag SHALL be presented as a
control on the surface as an alternative to the HUD's own collapse control and hotkey.

#### Scenario: Controls appear under their section
- **WHEN** the settings surface is shown
- **THEN** the completion-policy control appears under Behavior, and the HUD position/size and
  font-scale controls appear under Appearance

### Requirement: Each preference maps to a labeled input control
The settings surface SHALL expose one input control per preference: the completion policy and the HUD
anchor SHALL each use a dropdown of their allowed values; the HUD maximum rows, HUD row width, HUD
horizontal and vertical offsets, and the HUD and window font-size scales SHALL each use a **numeric-entry
control** (not a slider) constrained to its allowed range and stepping by a sensible increment, so that
adjusting a value never captures the scroll wheel or otherwise interferes with scrolling the form; the
font-size scales SHALL be entered as a percent; the HUD horizontal and vertical offsets SHALL be laid out
together on one row; the collapsed-HUD flag SHALL use a checkbox. A numeric-entry control SHALL reflect
the clamped/normalized value after a write (an out-of-range entry SHALL settle onto the clamped value).
Every control SHALL be labeled, and the enumerated options of the dropdowns SHALL be shown by
human-readable, localized labels.

#### Scenario: Enum preferences are chosen from dropdowns
- **WHEN** a player opens the completion-policy or HUD-anchor control
- **THEN** it presents the allowed values as a dropdown of localized labels

#### Scenario: Numeric preferences use numeric-entry controls
- **WHEN** a player adjusts the HUD rows, row width, an offset, or a font-size scale
- **THEN** the value is changed through a numeric-entry control (not a slider), and doing so does not
  capture the scroll wheel or scroll the form

#### Scenario: Offsets share one row
- **WHEN** the Appearance section is shown
- **THEN** the horizontal and vertical HUD offset controls appear together on a single row

### Requirement: Setting a control writes through immediately with live preview
Changing any control on the settings surface SHALL immediately write the new value to the player's
client-local preferences and persist it, with no separate apply or confirm step. The change SHALL take
effect live: a change to a HUD-affecting preference SHALL update the HUD without reopening it, and a
change to the window font-size scale SHALL update an open host dialog without reopening it. Values SHALL
be normalized/clamped on write, so a control can never persist an out-of-range value.

#### Scenario: A HUD preference updates the HUD live
- **WHEN** a player changes the HUD anchor, rows, width, offsets, or HUD font scale on the settings surface
- **THEN** the pinned-task HUD reflects the change immediately, without being closed and reopened

#### Scenario: The window font scale updates the open dialog live
- **WHEN** a player changes the window font-size scale while a Lectern dialog is open
- **THEN** the dialog's text re-renders at the new scale without the dialog being closed and reopened

#### Scenario: The settings form re-scales itself live
- **WHEN** a player changes the window font-size scale on the settings surface
- **THEN** the settings form's own text and checkboxes re-render at the new scale without reopening

#### Scenario: No separate apply step
- **WHEN** a player changes a control and then dismisses the settings surface
- **THEN** the change was already persisted at the moment it was made, with no apply or confirm action required

### Requirement: Each control provides localized helptext
Each setting SHALL provide descriptive helptext, surfaced on demand (for example as a tooltip), and all
labels, section titles, enum option labels, and helptext strings SHALL be drawn from the mod's
localization assets so they can be translated.

#### Scenario: Helptext is available per setting
- **WHEN** a player requests help for a setting (for example by hovering it)
- **THEN** localized descriptive text for that setting is shown

#### Scenario: All settings text is localizable
- **WHEN** the settings surface renders any label, section title, option, or helptext
- **THEN** the string is resolved through the localization assets rather than a hardcoded literal
