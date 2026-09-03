## ADDED Requirements

### Requirement: A control opens LibGUI's own theme picker
The settings surface's Window Appearance section SHALL include a labeled button that, when
activated, runs LibGUI's `.ui settings` client command, opening LibGUI's own theme-picker dialog.
This surfaces a LibGUI capability that is otherwise reachable only by a player who already knows
the hidden chat command exists. The control SHALL provide localized helptext like every other
setting on the surface.

#### Scenario: The button opens LibGUI's theme picker
- **WHEN** a player activates the theme-picker button in Scribe Settings' Window Appearance
  section
- **THEN** LibGUI's own theme-picker dialog opens, the same as if the player had typed
  `.ui settings` themselves

#### Scenario: The control is labeled and localized
- **WHEN** the theme-picker button renders its label and helptext
- **THEN** both strings are resolved through the localization assets rather than hardcoded
  literals
