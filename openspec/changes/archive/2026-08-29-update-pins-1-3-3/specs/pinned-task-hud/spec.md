## MODIFIED Requirements

### Requirement: The HUD refreshes when the pin set changes
The system SHALL update the HUD to reflect the current pin set whenever a fresh pin set or settings
push arrives, without requiring the player to reopen anything. This refresh SHALL NOT be deferred by
the game being paused — a pin added, removed, or changed while the game is paused (for example,
singleplayer auto-pause while the Handbook is open) SHALL become visible on the HUD immediately, with
no dependence on the player unpausing or closing whatever paused the game.

#### Scenario: Completing a task updates the HUD live
- **WHEN** the player completes one of their pinned tasks (from the HUD or their own lectern edit) and
  the server re-pushes their pin set
- **THEN** the HUD reflects the change (the row updates its completion indicator, or is removed if the
  completion policy cleared the pin or deleted the task) without a manual refresh

#### Scenario: A pin added while paused appears immediately
- **WHEN** the player pins a task from an open Notebook/Lectern while the game is paused
- **THEN** the HUD shows the new pin row without waiting for the player to unpause
