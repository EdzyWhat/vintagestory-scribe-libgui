## Purpose

Lets the author adjust the in-world collision/selection box of a placed Scribe writing-station
block live, while standing next to it, instead of editing JSON and rebuilding/relaunching to see
each change.

## ADDED Requirements

### Requirement: Five independently tunable box targets
The system SHALL expose exactly five independently tunable box targets: the Inbox block's ground
placement, the Inbox block's wall-mounted placement, the Scriptorium block, the Assignment Desk
block, and the Chalkboard block. Adjusting one target's box SHALL NOT affect any other target's
box.

#### Scenario: Tuning the wall-mounted Inbox does not affect the ground Inbox
- **WHEN** the author changes the wall-mounted Inbox's box values
- **THEN** a separately placed ground-mode Inbox's box is unchanged

#### Scenario: Tuning the Scriptorium does not affect the Assignment Desk
- **WHEN** the author changes the Scriptorium's box values
- **THEN** a placed Assignment Desk's box is unchanged

#### Scenario: Tuning the Chalkboard does not affect the Assignment Desk
- **WHEN** the author changes the Chalkboard's box values
- **THEN** a placed Assignment Desk's box is unchanged

### Requirement: The Chalkboard's collision box is never tunable, only its selection box is
For every other tunable target, the same tuned box drives both the collision box and the
selection box. The Chalkboard is the sole exception: it SHALL have no collision box regardless of
its tuned values (matching its existing walk-through, painting-style placement), while its
selection box SHALL be tunable exactly like every other target's box.

#### Scenario: A tuned Chalkboard stays walk-through
- **WHEN** the author sets any of the Chalkboard's six tuned values, including values that would
  otherwise describe a full-cell box
- **THEN** a placed Chalkboard still has no collision box (a player can walk through it)

#### Scenario: A tuned Chalkboard's selection box changes
- **WHEN** the author changes the Chalkboard's box values
- **THEN** a placed Chalkboard's selection (click-target) box reflects the new values

### Requirement: Each axis is clamped to the range 0 to 2
Every one of a target's six box values (`x1,y1,z1,x2,y2,z2`) SHALL be clamped to the inclusive
range `[0, 2]`, regardless of whether it was set via the tuning window or a hand-edited config
file.

#### Scenario: A value entered above the maximum is clamped
- **WHEN** the author sets a box value above 2
- **THEN** the effective value used for the block's collision/selection box is 2

#### Scenario: A value entered below the minimum is clamped
- **WHEN** the author sets a box value below 0
- **THEN** the effective value used for the block's collision/selection box is 0

### Requirement: A change applies to the live world with no rebuild or relaunch
Changing a target's box value SHALL immediately change the collision and selection box of every
currently placed block of that target in the world, without requiring a client restart or a mod
rebuild.

#### Scenario: A live box change is immediately reflected in-world
- **WHEN** the author changes one of the Scriptorium's box values while a Scriptorium is placed
  in the world
- **THEN** that Scriptorium's collision and selection box reflect the new values on the very next
  interaction (e.g. a subsequent look-at or placement/collision check), with no client restart

### Requirement: An untouched configuration matches today's shipped boxes exactly
When no tuning configuration has ever been saved, every target's box SHALL equal that block's
current shipped collision/selection box values exactly (the Inbox-Wall's default SHALL equal the
ground Inbox's current shared box) — so introducing this capability changes nothing observable
until the author actively tunes a value.

#### Scenario: Fresh install behaves identically to before this capability existed
- **WHEN** a client with no `scribe-box-tuning` configuration file places an Inbox, an Inbox-Wall,
  a Scriptorium, an Assignment Desk, or a Chalkboard
- **THEN** each block's collision and selection box matches its pre-existing shipped value

### Requirement: The rotation behavior of ground-placed boxes is unaffected
A ground-placed target's box SHALL continue to rotate to face the placing player exactly as it did
before this capability existed; only the source dimensions being rotated become tunable, not the
rotation mechanism itself.

#### Scenario: A tuned ground-placed box still rotates with the block's facing
- **WHEN** the author tunes the Scriptorium's box values, then places a Scriptorium facing a
  different direction than a previously placed one
- **THEN** each placed Scriptorium's box is rotated to match its own facing, using the same tuned
  dimensions

### Requirement: Tuning values persist across client sessions
A saved tuning value SHALL survive a client restart, applying to every subsequently loaded world
without needing to be re-entered.

#### Scenario: A tuned value survives a restart
- **WHEN** the author tunes a box value, then restarts the client and rejoins a world
- **THEN** the previously tuned value is still in effect

### Requirement: The tuning surface is author-only, not player-facing
This capability SHALL NOT be documented in the in-game handbook, SHALL NOT appear in the Scribe
Settings dialog, and SHALL have no effect on any player who never invokes it.

#### Scenario: A player who never tunes anything sees no trace of this capability
- **WHEN** a player who has never adjusted any box value opens the handbook or Scribe Settings
- **THEN** no mention of block box tuning appears in either
