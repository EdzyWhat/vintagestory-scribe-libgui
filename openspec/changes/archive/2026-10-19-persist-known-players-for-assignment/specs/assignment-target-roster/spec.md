## Purpose

Ensures the Assignment Desk's target-player picker offers every player who has ever connected to
the world, not only those currently online, and keeps that list ordered and defaulted in a way
that stays useful as the roster grows.

## ADDED Requirements

### Requirement: Every player who has ever connected to the world remains assignable
The system SHALL maintain a world-scoped registry of every player who has ever connected to the
server, keyed by player UID and their most recently observed display name. The Assignment Desk's
target-player picker SHALL offer every entry in this registry, unioned with any currently online
player not yet in the registry, regardless of how a player connected (a direct join, a
singleplayer session opened to LAN, or a dedicated server) and regardless of whether they are
currently online. The registry SHALL persist across server/game restarts and SHALL survive the
world being closed and reopened.

#### Scenario: A player who joined via LAN and later disconnected stays assignable
- **WHEN** a player joins a singleplayer world that has been opened to LAN, plays, and then
  disconnects, and the world is later closed and reopened
- **THEN** that player still appears as a selectable target in the Assignment Desk's picker

#### Scenario: A player who joined a dedicated server and is now offline stays assignable
- **WHEN** a player who has previously connected to a dedicated server is not currently online
- **THEN** they still appear as a selectable target in the Assignment Desk's picker

#### Scenario: A brand-new player is assignable immediately upon joining
- **WHEN** a player who has never previously connected to this world joins it for the first time
- **THEN** they appear as a selectable target in the Assignment Desk's picker for any Assigner who
  currently has it open, without requiring a reconnect or dialog reopen

### Requirement: The target-player picker's list stays alphabetically ordered
The Assignment Desk's target-player picker SHALL present its entries sorted alphabetically
(case-insensitive) by the underlying player name, and SHALL re-establish that order whenever a
new entry is added to the registry — including the local player's self-assignment entry, which
sorts by their own player name despite its distinct display label.

#### Scenario: Newly added players are inserted in alphabetical position
- **WHEN** a player who was not previously in the registry connects and is added to it
- **THEN** the target-player picker's list reflects that addition in alphabetical order rather
  than appended at the end

#### Scenario: Self-assignment entry sorts by the player's own name
- **WHEN** the Assigner's own entry (labeled distinctly for self-assignment) is present in the
  list alongside other players
- **THEN** it is positioned according to the Assigner's own player name, not forced to the first
  or last position

### Requirement: The picker defaults to the Assigner's last-assigned target
The Assignment Desk's target-player picker SHALL default its selection to the player this
Assigner most recently sent an assignment to (successfully, on this or any prior visit to any
Assignment Desk), rather than to the first entry in the list. If the Assigner has no prior
assignment history, or their last-assigned target is no longer present in the current list, the
picker SHALL fall back to the first entry in the alphabetically-ordered list.

#### Scenario: Picker defaults to the most recently assigned player
- **WHEN** an Assigner who has previously sent an assignment to player X opens the Create
  Assignments tab again (in the same or a later session)
- **THEN** the target-player picker's selection defaults to player X

#### Scenario: Default advances after each successful send
- **WHEN** an Assigner sends an assignment to player Y after previously having defaulted to
  player X
- **THEN** the picker's default target becomes player Y for subsequent visits, replacing X

#### Scenario: Falls back to alphabetically-first when there is no usable history
- **WHEN** an Assigner has never sent an assignment, or their last-assigned target is no longer
  present in the current list
- **THEN** the picker's selection defaults to the first entry in the alphabetically-ordered list
