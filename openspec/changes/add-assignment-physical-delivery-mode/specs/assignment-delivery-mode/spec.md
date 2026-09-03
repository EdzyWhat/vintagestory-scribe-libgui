## ADDED Requirements

### Requirement: A server admin DeliveryMode setting controls the available delivery paths
The system SHALL provide a server admin setting, `DeliveryMode`, with three values:
`AlwaysInstant` (the shipped in-range-only behavior; no notice-related UI is ever shown),
`AlwaysPhysical` (every send requires a Task Notice regardless of distance; the delivery-mode
toggle is never shown, since there is only one path), and `Hybrid` (the default; the range check
and player-facing toggle described in the requirements below). Changing this setting SHALL take
effect for all subsequent sends without requiring a server restart.

#### Scenario: AlwaysInstant hides all notice UI
- **WHEN** the server's `DeliveryMode` is `AlwaysInstant`
- **THEN** the Create Assignments tab shows no delivery-mode toggle and no Task Notice slots, and
  every send behaves exactly as the assignment system did before this change

#### Scenario: AlwaysPhysical requires a notice for every send
- **WHEN** the server's `DeliveryMode` is `AlwaysPhysical`
- **THEN** the Create Assignments tab always shows the Task Notice supply and output slots, shows
  no toggle (there is nothing to switch between), and every send consumes a blank Task Notice

#### Scenario: Hybrid enables the range check and toggle
- **WHEN** the server's `DeliveryMode` is `Hybrid`
- **THEN** the Create Assignments tab shows the delivery-mode toggle described in the range-check
  requirement below, and the Task Notice slots appear only when "Send a Notice" is selected

### Requirement: In Hybrid mode, a one-time range check at Assign time decides the toggle's default position
When `DeliveryMode` is `Hybrid`, the moment the Assigner selects a target player, the system SHALL
compare the Assignment Desk's block position to the target's current live position (if the target
is online) or their last-known position (if offline, per the logout-capture requirement below)
against an admin-configurable radius (default 200 blocks). If the target is within the radius, the
delivery-mode toggle SHALL default to "Local Inboxes"; if outside it, the toggle SHALL default to
"Send a Notice". This check SHALL run exactly once, when the target is selected, and SHALL NOT be
re-evaluated after the assignment is sent, regardless of either party's later movement or
online/offline transitions.

#### Scenario: An in-range online target defaults to Local Inboxes
- **WHEN** the Assigner selects a target who is online and within the configured radius of the
  Assignment Desk
- **THEN** the delivery-mode toggle defaults to "Local Inboxes"

#### Scenario: An out-of-range online target defaults to Send a Notice
- **WHEN** the Assigner selects a target who is online but farther than the configured radius from
  the Assignment Desk
- **THEN** the delivery-mode toggle defaults to "Send a Notice"

#### Scenario: An offline target uses their last-known position
- **WHEN** the Assigner selects a target who is currently offline
- **THEN** the range check uses that player's last-known position (captured on their most recent
  logout) rather than requiring them to be online

#### Scenario: The computed default is not re-evaluated after send
- **WHEN** an assignment has already been sent with a given delivery-mode toggle position
- **THEN** no later change in either party's position or online status alters that assignment's
  already-chosen delivery path

### Requirement: A player's last-known position is captured on logout
The system SHALL persist each player's world position at the moment they disconnect from the
server, and SHALL use that stored value as their position for the Hybrid range check whenever they
are offline at Assign time. This value SHALL be overwritten on every subsequent logout and SHALL
NOT be updated while the player remains online or offline between logouts.

#### Scenario: Position is captured on disconnect
- **WHEN** a player disconnects from the server
- **THEN** their current world position is persisted as their last-known position

#### Scenario: Last-known position is used for an offline target's range check
- **WHEN** an Assigner selects an offline target as an assignment's recipient
- **THEN** the range check in the requirement above uses that player's persisted last-known
  position, not a live position (which does not exist for an offline player)
