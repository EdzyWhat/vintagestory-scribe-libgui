# lectern-access-mode Specification

## Purpose
TBD - created by archiving change fix-transient-lectern-editor-lock. Update Purpose after archive.
## Requirements
### Requirement: A lectern has a persisted access mode distinct from the editor lock
Each lectern SHALL carry a persisted, server-authoritative access mode with two values:
`Public` (the default) and `Private`. The access mode SHALL be persisted to the world save
and synced to clients through the same server-authoritative round-trip used for the
lectern's other synced state, and SHALL be distinct from the transient single-editor lock
(the lock guards concurrent editing within a session; the access mode is a durable
permission on the lectern). A newly placed lectern SHALL default to `Public`, and an
existing lectern loaded from a save that predates this field SHALL be treated as `Public`.

#### Scenario: New lectern defaults to Public
- **WHEN** a lectern block entity is created or loaded from a save with no recorded access mode
- **THEN** its access mode is `Public`

#### Scenario: Access mode persists and syncs
- **WHEN** a lectern's access mode is set on the server and the block entity's save data is written
  and re-synced
- **THEN** the value survives save/reload and is mirrored to connected clients

### Requirement: Private access mode is reserved and not player-settable in this release
The `Private` access mode SHALL be defined and plumbed (persisted and synced) but SHALL NOT
be exposed through any player-facing control in this release — there SHALL be no GUI toggle,
command, or interaction that changes a lectern's access mode away from `Public`. The
mechanism is reserved for a future change that will surface it; until then every lectern
behaves as `Public` and the transient editor lock is the only gate on editing.

#### Scenario: No player-facing control changes the access mode
- **WHEN** a player interacts with a lectern through any shipped control (dialog, block interaction,
  or command)
- **THEN** there is no affordance that sets the access mode to `Private`, and the lectern remains
  `Public`

#### Scenario: Reserved Private mode does not affect default behavior
- **WHEN** the mod ships with the access-mode mechanism present but no way to leave `Public`
- **THEN** lectern editing behaves exactly as the transient-lock rules describe, with no read-only
  gating applied

