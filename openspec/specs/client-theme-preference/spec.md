# client-theme-preference

## Purpose

This capability covers Scribe's
client-local pixel-art-display preference: a per-player, client-only setting that selects the Lectern's
theme (a light pixel-art theme when on, the player's global LibGUI theme when off), persists across
restarts, is never synchronized to the server or other players, governs only the Lectern (not the HUD or
settings window), and propagates to an open Lectern live without a restart.

## Requirements

### Requirement: A client-local pixel-art-display preference exists, defaults on, and persists

The system SHALL provide a per-player `PixelArtDisplay` preference that selects the Lectern's theme. The
preference SHALL default to **on** (pixel-art / light theme). It SHALL be stored as part of the player's
client-local preferences and SHALL persist across game restarts. The preference SHALL be client-local
only: it MUST NOT be synchronized to the server or to any other player, and it MUST NOT be carried by any
block, block entity, document, or pin data. It SHALL be represented as pure data with no dependency on
the game API, so it remains unit-testable outside a running game.

#### Scenario: Default is on for a fresh profile
- **WHEN** a player runs Scribe with no prior saved preference for pixel-art display
- **THEN** the preference reads as on (the Lectern's light theme is active)

#### Scenario: The preference survives a restart
- **WHEN** a player sets the pixel-art-display preference and later restarts the game
- **THEN** the preference reads back at the value they last set, loaded from the client-local
  preferences store

#### Scenario: The preference is never sent to the server or other players
- **WHEN** a player changes the pixel-art-display preference
- **THEN** no network message conveying it is sent, the server holds no copy of it, and no other
  player's Scribe surfaces are affected by the change

### Requirement: Pixel-art mode renders the Lectern in a light theme

When the pixel-art-display preference is on, the Lectern dialog (its read and editor views, and any
future view hosted within it) SHALL render using a light theme in which body text is dark and the
surface/background roles are light, so text reads as dark ink on a light page. The light theme SHALL be
applied to the Lectern so that all its body content that resolves its colors from the active theme
(rows, multiline text fields, and buttons) recolors accordingly.

#### Scenario: Lectern renders light when pixel-art mode is on
- **WHEN** pixel-art mode is on and a player opens the Lectern dialog
- **THEN** its body content renders with dark text on light surfaces

### Requirement: The Lectern follows the player's global theme when pixel-art mode is off

When the pixel-art-display preference is off, the Lectern SHALL render using the player's global LibGUI
theme (the theme LibGUI loads from the player's `libgui.json` — the stock dark default unless the player
set their own) with plain flat panels and no illustrated background. In this mode the mod SHALL be fully
usable with zero art assets present: the Lectern may not fail, render illegibly, or depend on a missing
texture. This fallback is mandatory and SHALL come from the inherited global LibGUI theme rather than any
Scribe-authored asset.

#### Scenario: Off uses the player's global theme
- **WHEN** pixel-art mode is off and a player opens the Lectern
- **THEN** it renders with the player's global LibGUI theme and plain flat panels, with no illustrated
  background

#### Scenario: The mod is usable with no art
- **WHEN** pixel-art mode is off and no Scribe GUI art assets are installed
- **THEN** the Lectern still opens and remains fully legible and usable, depending on no missing texture

### Requirement: The HUD and settings window are not governed by the preference

The pinned-task HUD and the standalone settings window SHALL NOT be affected by the pixel-art-display
preference. Both SHALL always render using the player's global LibGUI theme regardless of the
preference's value; neither SHALL be wrapped in Scribe's light theme. Toggling the preference SHALL leave
the HUD's and the settings window's appearance unchanged.

#### Scenario: The HUD never changes with the toggle
- **WHEN** a player toggles pixel-art mode on or off while the pinned-task HUD is shown
- **THEN** the HUD's appearance does not change — it stays on the player's global theme in both states

#### Scenario: The settings window never changes with the toggle
- **WHEN** a player toggles pixel-art mode on or off with the settings window open
- **THEN** the settings window's appearance does not change — it stays on the player's global theme in
  both states

### Requirement: The Lectern title bar follows the active mode explicitly

Because a `WindowFrame` title bar does not inherit the per-dialog theme wrap automatically (it reads the
default theme at construction), the system SHALL set the Lectern's title-bar color and title text color
explicitly, derived from the active theme's color scheme, so the title bar and its text are legible
whether pixel-art mode is on or off (they do not stay stuck on the global default while the body is
light).

#### Scenario: Title bar matches the active mode
- **WHEN** the Lectern is shown in either pixel-art or global-theme mode
- **THEN** its title bar and title text are colored from the active mode's scheme and remain legible

### Requirement: A single settings window is reachable from both the Lectern and the HUD

There SHALL be exactly one Scribe settings surface: a standalone settings window. It SHALL be openable
from both the Lectern's gear control and the pinned-task HUD's gear control, and both SHALL open the same
window. There SHALL NOT be a separate in-Lectern settings tab. Opening the settings window from the
Lectern SHALL NOT disturb an in-progress edit or the editor lock held by the Lectern behind it.

#### Scenario: Both gears open the one window
- **WHEN** a player clicks the gear in the Lectern, and separately the gear on the HUD
- **THEN** each opens the same standalone settings window (there is no in-Lectern settings tab)

#### Scenario: Opening settings from the Lectern preserves the edit
- **WHEN** a player is editing in the Lectern and opens the settings window from its gear
- **THEN** the in-progress edit and the editor lock are preserved (the settings window opens over the
  Lectern rather than replacing its content)

### Requirement: A change to the preference propagates to the open Lectern without a restart

Changing the pixel-art-display preference SHALL take effect immediately on a currently open Lectern
without closing and reopening it and without restarting the game. Toggling the preference SHALL be the
single control that flips the Lectern's theme; there SHALL NOT be a separate per-surface theme control.

#### Scenario: Toggling relights the open Lectern live
- **WHEN** a player toggles pixel-art mode while the Lectern is open
- **THEN** the Lectern switches between the light theme and the player's global theme live, with no
  reopen and no restart

#### Scenario: One toggle governs the Lectern theme
- **WHEN** a player changes the pixel-art-display preference
- **THEN** the Lectern's theme flips, driven by that single preference, with no other per-surface theme
  setting involved
