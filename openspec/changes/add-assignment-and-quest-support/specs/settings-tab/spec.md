## ADDED Requirements

### Requirement: Quest Accept and Completion policies are Settings pickers, gated on VS Quest
When VS Quest is installed and enabled, the Behavior section of Scribe Settings SHALL include two
independent dropdowns — Quest Accept Policy and Quest Completion Policy — each offering Always,
Never, and Prompt, each defaulting to Prompt, each with localized helptext describing what it
governs (per the `quest-auto-detect` capability). When VS Quest is not installed, neither row
SHALL appear.

#### Scenario: Quest policy rows appear only with VS Quest installed
- **WHEN** a player with vsquest installed opens Scribe Settings
- **THEN** the Behavior section shows the Quest Accept Policy and Quest Completion Policy
  dropdowns, both defaulting to Prompt

#### Scenario: Quest policy rows are absent without VS Quest
- **WHEN** a player without vsquest installed opens Scribe Settings
- **THEN** neither Quest policy row appears anywhere in the settings surface

#### Scenario: The two policies are set independently
- **WHEN** the player changes Quest Accept Policy to Always
- **THEN** Quest Completion Policy is unchanged and continues to write through independently
