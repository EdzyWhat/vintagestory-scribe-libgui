## ADDED Requirements

### Requirement: Lectern dialog offers a settings view in its central region
The Lectern dialog SHALL offer a settings view as a third selectable state of its central content
region, alongside the read and editor views, reachable from a gear control in the dialog chrome that is
present in both the read and editor views. Switching to the settings view SHALL replace the read/editor
content while leaving the dialog's chrome in place, and the view SHALL provide a way to return to the
previously shown read or editor content.

#### Scenario: Gear switches the central region to settings
- **WHEN** a player activates the gear control in an open Lectern dialog
- **THEN** the dialog's read/editor content is replaced by the settings view and the dialog chrome remains

#### Scenario: Leaving settings returns to the prior view
- **WHEN** the settings view is shown and the player leaves it
- **THEN** the dialog returns to the read or editor view that was shown before

## MODIFIED Requirements

### Requirement: Lectern row sizing is sourced from client configuration
The LibGUI lectern dialog SHALL source its task-row font size from the player's single client-local
preference store (the same store that holds the other per-player preferences), applying the window
font-size scale to a built-in base font size. The remaining task-row layout values (per-row vertical and
horizontal padding, checkbox size, checkbox-to-text gap, and the editor field's internal padding) SHALL
be built-in constants rather than user configuration. The dialog SHALL derive its row sizing from the
live preference store each time it builds its content, so that a font-size change made in the settings
view takes effect on the open dialog. When the preference store has never been written, the dialog SHALL
fall back to a font-size scale of `1.0` (the base sizes) without error.

#### Scenario: Changing the font scale in settings applies to the open dialog
- **WHEN** the player changes the window font-size scale in the settings view
- **THEN** the lectern's rows re-render at the new size without the dialog being closed and reopened

#### Scenario: Unset preferences fall back to base sizing
- **WHEN** the player has never changed a font-size preference and opens the lectern
- **THEN** the lectern opens normally using the base font size (scale `1.0`), with no error

### Requirement: Row sizing scales through a single factor
The lectern's scalable row-sizing values SHALL be derived by multiplying a built-in base font size (and
any font-derived spacing) by the player's single window font-size scale factor, applied at one place
before the values reach the row widgets. With the scale factor at its default of `1.0`, the rendered
sizes SHALL equal the base values (a no-op). Any fixed control-centering offsets that depend on the font
size SHALL be computed from the measured text/control heights at the current scale rather than from
constants tuned to a single font size, so the checkbox and grip stay centered on a row at any scale.

#### Scenario: Default scale reproduces the base sizes
- **WHEN** the window font-size scale factor is at its default value of `1.0`
- **THEN** each row's font size and scalable spacing equal the base values

#### Scenario: A non-default scale multiplies the sizes uniformly
- **WHEN** the window font-size scale factor is set to a value other than `1.0`
- **THEN** the scalable row-sizing values are multiplied by that factor for both the read and editor
  views, and the checkbox and grip remain vertically centered on a single-line row

## REMOVED Requirements

### Requirement: Row-sizing config is exposable via ConfigLib without a hard dependency
**Reason**: Row-layout values (other than the font-size scale) are no longer user configuration — they
become built-in constants — and the font-size scale is exposed through the in-mod settings view instead
of ConfigLib. ConfigLib's in-game panel is rejected as the settings surface because it is broken on
Apple Silicon (the development machine), which is the motivation for the in-mod settings view. The
`ScribeClientConfig` client file that this requirement's ConfigLib manifest read is retired.
**Migration**: Font sizing is now set through the in-mod settings view, which writes the consolidated
client-local preference store. An existing `scribe-client-config.json` file is ignored (unknown keys are
tolerated on load); a player re-enters a non-default font size once through the settings view.
