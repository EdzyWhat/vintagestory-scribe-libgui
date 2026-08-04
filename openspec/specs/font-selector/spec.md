# font-selector Specification

## Purpose
TBD - created by archiving change v1-release-checklist. Update Purpose after archive.
## Requirements
### Requirement: Player can select the font used for task text
The mod SHALL allow the player to choose a font family for task row text from a set of bundled options.
The selection SHALL be stored in `ScribePlayerSettings` (client-local, persisted in `scribe:settings:v1`)
and applied immediately to all open Lectern dialogs and the HUD without requiring a restart or reload.
The default value SHALL be the existing body font so the existing UX is unchanged for players who never
interact with the selector.

#### Scenario: Font selector appears in Settings
- **WHEN** a player opens the Scribe Settings window
- **THEN** a font selector control is visible in the Window Appearance section

#### Scenario: Changing the font updates the task list immediately
- **WHEN** a player selects a different font from the selector while a Lectern is open
- **THEN** the task rows in the open Lectern dialog re-render in the chosen font without closing or
  reopening the dialog

#### Scenario: Font choice persists across relog
- **WHEN** a player selects a font, closes the game, and relogs
- **THEN** the Lectern task rows render in the previously selected font

#### Scenario: Default value leaves existing visual unchanged
- **WHEN** a player has never interacted with the font selector
- **THEN** task rows render in the same font as before this feature was introduced

### Requirement: Scapholene is available as a bundled font option
The mod SHALL bundle the Scapholene typeface (licensed for personal and commercial use) as a selectable
option for task text. The face SHALL be registered via `FontRegistry.RegisterCustomFont` at
`StartClientSide` using the same pattern as the bundled Caudex face.

#### Scenario: Scapholene renders on Apple Silicon
- **WHEN** the player selects Scapholene on a Mac (Apple Silicon, OpenGL 4.1 + Skia)
- **THEN** task row text renders in Scapholene glyphs with no crash, log error, or garbled output

#### Scenario: Missing Scapholene asset falls back gracefully
- **WHEN** the Scapholene TTF is absent from the mod's assets directory
- **THEN** the selector still shows the option but falls back to the default font, logging a single
  warning and not crashing

### Requirement: At least 3 font options are offered
The mod SHALL offer at least 3 named font choices in the selector, including the default (existing)
body font and Scapholene. The exact lineup (up to 4 faces total) is an implementation decision
tracked in design.md open question #2.

#### Scenario: Selector shows at least 3 options
- **WHEN** a player opens the font selector control in Settings
- **THEN** at least 3 distinct named font options are listed

