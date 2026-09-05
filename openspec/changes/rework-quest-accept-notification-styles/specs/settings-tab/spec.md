## MODIFIED Requirements

### Requirement: Quest Accept and Completion policies are Settings pickers, gated on an installed quest backend
When a supported quest backend (VS Quest, or Progression Framework) is installed and enabled, the
Behavior section of Scribe Settings SHALL include two independent dropdowns — Quest Accept Policy and
Quest Completion Policy. Quest Accept Policy SHALL offer four options: Always, Never, Prompt via HUD,
and Prompt via Popup, defaulting to Prompt via HUD. Quest Completion Policy SHALL offer three options
— Always, Never, and Prompt — defaulting to Prompt, unchanged from before. Each dropdown SHALL have
localized helptext describing what it governs (per the `quest-auto-detect` and
`quest-accept-notification-style` capabilities). When no supported quest backend is installed, neither
row SHALL appear.

#### Scenario: Quest policy rows appear only with a supported backend installed
- **WHEN** a player with a supported quest backend installed opens Scribe Settings
- **THEN** the Behavior section shows the Quest Accept Policy dropdown (four options, defaulting to
  Prompt via HUD) and the Quest Completion Policy dropdown (three options, defaulting to Prompt)

#### Scenario: Quest policy rows are absent without a supported backend
- **WHEN** a player with no supported quest backend installed opens Scribe Settings
- **THEN** neither Quest policy row appears anywhere in the settings surface

#### Scenario: The two policies are set independently
- **WHEN** the player changes Quest Accept Policy to Prompt via Popup
- **THEN** Quest Completion Policy is unchanged and continues to write through independently

#### Scenario: Prompt via Popup is selectable and persists
- **WHEN** the player selects Prompt via Popup for Quest Accept Policy
- **THEN** the selection persists and subsequent detected quest accepts render via the center modal
  (subject to the vanilla-dialog guard)
