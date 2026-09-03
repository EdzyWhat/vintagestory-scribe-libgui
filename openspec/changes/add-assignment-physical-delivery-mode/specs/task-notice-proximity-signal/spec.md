## ADDED Requirements

### Requirement: A periodic proximity scan surfaces an at-rest Task Notice to its recipient
The system SHALL periodically (every 2-5 seconds, reusing the existing storm-tick heartbeat idiom)
scan, for each online player with at least one outstanding sealed Task Notice addressed to them, a
10-15 block radius around that player's current position for a Scribe-tagged item stack — whether
dropped in the world or sitting inside any block entity's inventory — whose recorded recipient
matches them. When found, the system SHALL spawn the same existing ambient particle/badge effect
already used for pending Desk assignments at that block or entity's position, visible only to that
player's client.

#### Scenario: A dropped notice signals its recipient
- **WHEN** a player with an outstanding sealed Task Notice addressed to them walks within the scan
  radius of that notice lying dropped in the world
- **THEN** the existing ambient particle/badge effect spawns at the notice's position, visible only
  to that player

#### Scenario: A notice left in any container signals its recipient
- **WHEN** a player with an outstanding sealed Task Notice addressed to them walks within the scan
  radius of a block entity (e.g. a chest) whose inventory contains that notice
- **THEN** the same ambient particle/badge effect spawns at that block's position for them, with no
  special-casing for the block's type

#### Scenario: The scan is not visible to other players
- **WHEN** the proximity scan finds a notice addressed to one player
- **THEN** no particle/badge effect is spawned for any other player, regardless of their proximity
  to the same notice

### Requirement: The proximity scan is gated by chunk movement to stay cheap for stationary players
The system SHALL skip a given player's proximity scan on any tick where that player has not
crossed into a new chunk since their last scan, so a stationary player incurs no repeated scan
cost between chunk boundaries.

#### Scenario: A stationary player's scan is skipped on repeat ticks
- **WHEN** a player has not moved into a new chunk since their last proximity scan
- **THEN** the next scheduled scan for that player is skipped entirely
