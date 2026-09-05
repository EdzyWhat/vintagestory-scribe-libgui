## ADDED Requirements

### Requirement: Auto-pin-on-quest-accept is a per-player, client-local preference
The system SHALL provide a per-player, client-local preference — `AutoPinOnQuestAccept` — governing
whether accepting a quest's accept-prompt automatically pins the resulting linked task for that
player, stored alongside the player's other client-local display/behavior preferences. It SHALL
default to enabled for a player who has never changed it.

#### Scenario: Default is enabled for a new player
- **WHEN** a player who has never changed this preference accepts a quest prompt
- **THEN** the resulting linked task is pinned for that player

#### Scenario: Disabling the preference stops the auto-pin
- **WHEN** a player disables the auto-pin-on-accept preference and then accepts a quest prompt
- **THEN** the resulting linked task is linked but not pinned, matching the pre-existing behavior

### Requirement: Accepting a quest pins the resulting task when the preference is enabled
When a player accepts a quest's accept-prompt (from the HUD banner or the center-screen modal) and
their `AutoPinOnQuestAccept` preference is enabled, the system SHALL pin the resulting linked task
for that player through the same pin-add operation a manual pin uses, following the same insertion
rules (the player's Pin Insert setting, parent/child clustering). The quest SHALL be accepted and
linked regardless of whether the follow-on pin-add succeeds.

#### Scenario: Accepting pins the task
- **WHEN** a player with the preference enabled accepts a quest's accept-prompt
- **THEN** the resulting linked task appears in that player's pin set, positioned per their Pin
  Insert setting

#### Scenario: Accepting does not pin when the preference is disabled
- **WHEN** a player with the preference disabled accepts a quest's accept-prompt
- **THEN** the quest is linked as before, and no pin is added for the resulting task
