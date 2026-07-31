## ADDED Requirements

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
