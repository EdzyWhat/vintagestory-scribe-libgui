## MODIFIED Requirements

### Requirement: Inbox-capable blocks show an ambient particle when the player has an unseen assignment
Every Inbox-capable block (Assignment Desk, standalone Inbox, and any Lectern, Scriptorium, or
Chalkboard exposing the Inbox nav button) SHALL emit an ambient particle effect, visible only to
the local client, when the viewing player has at least one New (unseen) assignment and is within
a configurable detection radius (**12 blocks** by default) of that block. The check SHALL be
client-side and player-local; it SHALL NOT reveal another player's unseen assignments. The
particle field SHALL spawn from the block's vertical midpoint (not from above its top face), SHALL
rise to roughly two-thirds of the vertical distance it covered before this requirement's
range/position values changed while its total particle lifetime is unchanged (particles travel
more slowly, not for a shorter time), and SHALL spawn at a configurable reduced rate (**0.6×** the
per-tick particle count used before this requirement changed, by default).

#### Scenario: A nearby block particles when the player has a new assignment

- **WHEN** a player with an unseen assignment walks within the configured detection radius
  (12 blocks by default) of any Inbox-capable block
- **THEN** that block emits the ambient particle effect for that player's client only, spawning
  from around the block's vertical midpoint

#### Scenario: No particle once every assignment is seen or resolved

- **WHEN** the player has no assignment in the New (unseen) state
- **THEN** no Inbox-capable block emits the particle effect for that player

#### Scenario: A player outside 12 blocks sees no particle

- **WHEN** a player with an unseen assignment is farther than the configured detection radius
  (12 blocks by default) from every Inbox-capable block
- **THEN** no particle is emitted for them until they come within that range

#### Scenario: A configured detection radius or density is honored

- **WHEN** the visual-tuning config sets the detection radius or the per-tick count multiplier to
  a value different from the default
- **THEN** the particle effect uses the configured detection radius and/or spawn density instead
  of the 12-block/0.6× defaults
