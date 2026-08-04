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
The settings surface SHALL present its controls in three labeled sections separated by horizontal
dividers: a Mod Behavior section, a Window Appearance section, and a HUD Appearance section. The Mod
Behavior section SHALL contain the
completion policy and the mute-UI-sounds toggle. The Window Appearance section SHALL contain the window
font-size scale, the Pixel Art Display toggle, the Pixel Art Size, and the **task font selector**. The
HUD Appearance section SHALL contain the HUD anchor, HUD maximum rows, HUD row width, HUD horizontal
and vertical offsets, the HUD font-size scale, and the collapsed-HUD toggle.

#### Scenario: Font selector appears under Window Appearance
- **WHEN** the settings surface is shown
- **THEN** the task font selector control is present in the Window Appearance section, alongside the
  window font-size scale and Pixel Art controls

#### Scenario: Controls appear under their section
- **WHEN** the settings surface is shown
- **THEN** the completion-policy and mute-sounds controls appear under Mod Behavior; the window
  font-scale, pixel art, and font-selector controls appear under Window Appearance; and the HUD
  position/size and HUD font-scale controls appear under HUD Appearance

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

### Requirement: A preference mutes Scribe's own UI sounds
The settings surface SHALL expose a client-local boolean preference that, when enabled, suppresses the
interaction sounds (button/checkbox click sounds and similar) that Scribe's own LibGUI-based dialogs and
HUD would otherwise play. The preference SHALL default to disabled (sounds on). Enabling it SHALL affect
ONLY Scribe's own UI sounds and SHALL NOT alter the vanilla game's audio or any other mod's sounds. The
preference SHALL be client-local (a per-client display/audio preference, not server-authoritative document
state) and SHALL write through and take effect immediately like every other control on the surface, with
no separate apply or reopen step.

#### Scenario: Muting silences Scribe's UI sounds
- **WHEN** a player enables the mute-UI-sounds preference and then interacts with a Scribe dialog or HUD
  control that would normally play a sound
- **THEN** Scribe plays no sound for that interaction

#### Scenario: Unmuting restores Scribe's UI sounds
- **WHEN** the preference is disabled (its default)
- **THEN** Scribe's UI controls play their interaction sounds as before

#### Scenario: Muting is scoped to Scribe
- **WHEN** the mute-UI-sounds preference is enabled
- **THEN** the vanilla game's sounds and other mods' sounds are unaffected

#### Scenario: The mute preference writes through immediately
- **WHEN** a player toggles the mute-UI-sounds preference on the settings surface
- **THEN** the new value is persisted at the moment it is toggled and takes effect for Scribe's UI with no
  apply, confirm, or reopen step

### Requirement: The mute-UI-sounds control is paired beside the collapsed-HUD checkbox
In the Mod Behavior section, the mute-UI-sounds preference SHALL be presented as a labeled checkbox laid
out as a second column beside the collapsed-HUD checkbox, so the two checkboxes share one paired row. The
control SHALL be labeled and SHALL provide localized helptext on demand, and its label and helptext SHALL
be drawn from the localization assets.

#### Scenario: The two checkboxes share a paired row
- **WHEN** the settings surface's Mod Behavior section is shown
- **THEN** the collapsed-HUD checkbox and the mute-UI-sounds checkbox appear together on one row as two
  columns

#### Scenario: The mute control is labeled and localized
- **WHEN** the mute-UI-sounds control renders its label and helptext
- **THEN** both strings are resolved through the localization assets rather than hardcoded literals

### Requirement: A preference governs whether a fired timer auto-disappears
The settings surface SHALL expose, in the Mod Behavior section, a client-local boolean "Timer
disappears" preference that governs whether a fired Clockmaker's Notebook timer auto-clears from the
Pinned Task HUD after a short window. The preference SHALL default to enabled (a fired timer disappears
after roughly 30 seconds, preserving prior behavior). When disabled, a fired timer SHALL remain shown
until the player dismisses it (see the timer-lifecycle capability). The preference SHALL be presented as
a labeled checkbox with localized label and on-demand localized helptext, SHALL be client-local (a
per-client behavior preference, never server-synced), and SHALL write through and take effect
immediately — including for a timer that is already fired — with no separate apply or reopen step.

#### Scenario: The preference appears in Mod Behavior
- **WHEN** the settings surface's Mod Behavior section is shown
- **THEN** a labeled "Timer disappears" checkbox is presented, defaulting to enabled

#### Scenario: The preference is labeled and localized
- **WHEN** the "Timer disappears" control renders its label and helptext
- **THEN** both strings are resolved through the localization assets rather than hardcoded literals

#### Scenario: Toggling writes through immediately
- **WHEN** a player toggles the "Timer disappears" preference on the settings surface
- **THEN** the new value is persisted at the moment it is toggled and takes effect immediately, with no
  apply, confirm, or reopen step

### Requirement: A preference toggles the storm-corruption effect

The settings surface SHALL provide a labeled, localized-helptext control that toggles the temporal
storm-corruption HUD effect (both the text corruption and the storm title swap). The control SHALL
default to on. When off, the HUD SHALL never corrupt its text or swap its title regardless of storm
or stability state. The setting SHALL be client-local (a display/behavior preference), consistent
with the other Scribe client preferences, and SHALL write through immediately.

#### Scenario: Disabling the effect stops corruption immediately

- **WHEN** the player turns the storm-corruption setting off while a storm is active
- **THEN** the HUD immediately renders normal, uncorrupted text with the normal "Pinned" title

#### Scenario: Default is on

- **WHEN** a player has never changed the setting
- **THEN** the storm-corruption effect is active by default

### Requirement: A Pixel Art Size preference drives the lectern layout
The Appearance section SHALL expose a permanent "Pixel Art Size" numeric preference — the driving width `W`
of the lectern's proportional layout. It SHALL be a numeric-entry control (not a slider) that increments by
10 and is clamped to the range 300..1000 on entry and on load. Changing it SHALL rescale the open lectern
live, following the same write-through-with-live-preview behavior as the other appearance preferences.

#### Scenario: Pixel Art Size appears under Appearance
- **WHEN** the player opens the settings surface
- **THEN** the Appearance section shows a "Pixel Art Size" numeric-entry control stepping by 10, bounded to
  300..1000

#### Scenario: Changing Pixel Art Size rescales the open lectern live
- **WHEN** the player changes Pixel Art Size while a lectern is open
- **THEN** the open lectern's layout rescales to the new width immediately, with no separate apply step

#### Scenario: Pixel Art Size is clamped and persisted
- **WHEN** a value outside 300..1000 is entered, or a hand-edited config holds an out-of-range value, and
  it is loaded
- **THEN** the value is clamped to the range (and snapped to the 10-step grid), and in-range values persist
  across sessions like the other client-local preferences

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

