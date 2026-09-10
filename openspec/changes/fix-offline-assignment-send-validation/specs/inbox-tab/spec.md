## MODIFIED Requirements

### Requirement: Inbox-capable blocks show an ambient particle when the player has an unseen assignment
Every Inbox-capable block (Assignment Desk, standalone Inbox, and any Lectern, Scriptorium, or
Chalkboard exposing the Inbox nav button) SHALL emit an ambient particle effect, visible only to
the local client, when the viewing player has at least one New (unseen) assignment that has NOT
been delivered to them as a physical Task Notice item, and is within a configurable detection
radius (**12 blocks** by default) of that block. An assignment the player already holds a sealed
or opened Task Notice item for — i.e. one that has been received into their inventory — SHALL NOT
count toward this trigger, even while it remains otherwise unseen (not yet opened in an Inbox); the
player already has an in-hand signal for that assignment and does not need a second, world-space
one pointing them at a nearby block. This exclusion applies only to the block particle — it does
not change what counts as "unseen" for any other purpose (e.g. the Inbox nav-button shimmer, or the
Inbox tab's own unseen-count display). The check SHALL be client-side and player-local; it SHALL
NOT reveal another player's unseen assignments. The particle field SHALL spawn from the block's
vertical midpoint (not from above its top face), SHALL rise to roughly two-thirds of the vertical
distance it covered before this requirement's range/position values changed while its total
particle lifetime is unchanged (particles travel more slowly, not for a shorter time), and SHALL
spawn at a configurable reduced rate (**0.6×** the per-tick particle count used before this
requirement changed, by default).

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

#### Scenario: No particle once a Task Notice has already been received into inventory
- **WHEN** a player has an unseen assignment, but that assignment was delivered as a Task Notice
  item and it has already been received into their inventory
- **THEN** no Inbox-capable block emits the ambient particle for that assignment, even though the
  player has not yet opened an Inbox to mark it seen, and even while within the detection radius of
  a nearby block

#### Scenario: A different, still-undelivered unseen assignment keeps particling normally
- **WHEN** a player is carrying an already-received Task Notice for one assignment (no particle,
  per the scenario above) AND separately has a second, unseen Local-Inboxes-delivered assignment
- **THEN** nearby Inbox-capable blocks still emit the ambient particle for that player, driven by
  the second assignment
