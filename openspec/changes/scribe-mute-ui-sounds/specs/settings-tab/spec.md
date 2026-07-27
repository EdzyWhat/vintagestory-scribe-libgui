## ADDED Requirements

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
