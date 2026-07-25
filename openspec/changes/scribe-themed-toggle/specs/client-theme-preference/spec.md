## ADDED Requirements

### Requirement: A client-local themed-mode preference exists, defaults on, and persists

The system SHALL provide a per-player `ThemedBackgrounds` preference that selects Scribe's GUI theme.
The preference SHALL default to **on** (themed). It SHALL be stored as part of the player's client-local
preferences and SHALL persist across game restarts. The preference SHALL be client-local only: it MUST
NOT be synchronized to the server or to any other player, and it MUST NOT be carried by any block, block
entity, document, or pin data. It SHALL be represented as pure data with no dependency on the game API,
so it remains unit-testable outside a running game.

#### Scenario: Default is themed on a fresh profile
- **WHEN** a player runs Scribe with no prior saved preference for themed mode
- **THEN** the themed-mode preference reads as on (the light theme is active)

#### Scenario: The preference survives a restart
- **WHEN** a player sets the themed-mode preference and later restarts the game
- **THEN** the preference reads back at the value they last set, loaded from the client-local
  preferences store

#### Scenario: The preference is never sent to the server or other players
- **WHEN** a player changes the themed-mode preference
- **THEN** no network message conveying it is sent, the server holds no copy of it, and no other
  player's Scribe surfaces are affected by the change

### Requirement: Themed mode renders Scribe surfaces in a light theme

When the themed-mode preference is on, every Scribe GUI surface — the Lectern dialog, the pinned-task
HUD, and the settings surface — SHALL render using a light theme in which body text is dark and the
surface/background roles are light, so text reads as dark ink on a light page. The light theme SHALL be
applied per surface so that all body content that resolves its colors from the active theme (rows,
multiline text fields, buttons, and the settings form) recolors accordingly.

#### Scenario: Lectern renders light when themed
- **WHEN** themed mode is on and a player opens the Lectern dialog
- **THEN** its body content renders with dark text on light surfaces

#### Scenario: HUD and settings surfaces render light when themed
- **WHEN** themed mode is on and the pinned-task HUD and the settings surface are shown
- **THEN** each renders with dark text on light surfaces, consistent with the Lectern

### Requirement: Fallback mode renders the stock dark LibGUI theme with no art dependency

When the themed-mode preference is off, every Scribe GUI surface SHALL render using the stock LibGUI
default theme (dark parchment surfaces with light text) with plain flat panels and no illustrated
background. In this mode the mod SHALL be fully usable with zero art assets present: no Scribe GUI
surface may fail, render illegibly, or depend on a missing texture when themed mode is off. This
fallback is mandatory and SHALL come from the inherited LibGUI default theme rather than any Scribe-
authored asset.

#### Scenario: Fallback uses the stock dark theme
- **WHEN** themed mode is off and a player opens any Scribe GUI surface
- **THEN** it renders with the stock LibGUI default (dark) theme and plain flat panels, with no
  illustrated background

#### Scenario: The mod is usable with no art
- **WHEN** themed mode is off and no Scribe GUI art assets are installed
- **THEN** every Scribe GUI surface still opens and remains fully legible and usable, depending on no
  missing texture

### Requirement: The title bar and HUD glow halo follow the active mode explicitly

Because a surface's title bar and its glow/halo effects do not inherit the per-surface theme wrap
automatically, the system SHALL set them explicitly so they match the active mode. Each themed
`WindowFrame` SHALL be given a title-bar color and title text color derived from the active theme's
color scheme, so the title bar and its text are legible in both light and fallback modes. The pinned-
task HUD's glow/halo behind its text SHALL be conditioned on the active mode — a light halo in themed
(light) mode and the dark halo in fallback mode — so HUD text stays legible either way.

#### Scenario: Title bar matches the active mode
- **WHEN** a Scribe dialog with a framed title is shown in either themed or fallback mode
- **THEN** its title bar and title text are colored from the active mode's scheme and remain legible
  (they do not stay stuck on the fallback default while the body is light)

#### Scenario: HUD halo inverts with the mode
- **WHEN** the pinned-task HUD is shown in themed (light) mode
- **THEN** its text halo is light (inverted from the fallback's dark halo), keeping the HUD text legible

### Requirement: A change to the preference propagates to all open surfaces without a restart

Changing the themed-mode preference SHALL take effect immediately across every currently open Scribe
GUI surface — the Lectern dialog, the pinned-task HUD, and the settings surface — without closing and
reopening any of them and without restarting the game. Toggling the preference SHALL be the single
control that flips Scribe's themed mode; there SHALL NOT be a separate per-surface theme control.

#### Scenario: Toggling relights every open surface live
- **WHEN** a player toggles themed mode while the Lectern, the HUD, and the settings surface are all
  open
- **THEN** all three switch between the light theme and the dark fallback together, live, with no
  surface needing to be reopened and with no restart

#### Scenario: One toggle governs the whole themed mode
- **WHEN** a player changes the themed-mode preference
- **THEN** the theme flips for all Scribe surfaces at once, driven by that single preference, with no
  other per-surface theme setting involved
