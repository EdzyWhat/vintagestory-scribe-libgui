## ADDED Requirements

### Requirement: The HUD reflects temporal instability

The pinned-task HUD SHALL read client-side temporal state each refresh and reflect it visually: it
SHALL corrupt its rendered text while an instability trigger (active temporal storm, or personal
stability below 0.50) is present, and SHALL display the storm call-to-action title while a storm is
active. This reflection SHALL be read-only with respect to game state — the HUD SHALL NOT modify
storm or stability values. When the effect setting is disabled, the HUD SHALL render exactly as it
did before this capability existed.

#### Scenario: HUD reacts to an active storm

- **WHEN** a temporal storm begins while the HUD is visible
- **THEN** the HUD title swaps to the storm call-to-action and its text renders corrupted, with no
  change to the underlying pinned-task data

#### Scenario: HUD returns to normal after the storm

- **WHEN** the storm ends and personal stability is at or above 0.50
- **THEN** the HUD title reverts to "Pinned" and all text renders normally
