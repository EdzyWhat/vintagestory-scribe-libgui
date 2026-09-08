## MODIFIED Requirements

### Requirement: Soft auto-detection supports VS Quest and Progression Framework, each independently gated
When the VS Quest mod (`vsquest`) is installed and enabled, the system SHALL detect accept and
completion state by reading that mod's own server-synced `WatchedAttributes` keys on quest-giver
entities — no reflection is used for accept/completion detection. Only VS Quest's live
kill/place/break progress-count mirroring uses reflection, reading that mod's own open
quest-selection dialog (found by type name); that reflection path is best-effort and covers
progress counts only, never accept/completion state.

When the Progression Framework mod (`progressionframework`) is installed and enabled, the system
SHALL detect accept, completion, and per-objective progress for **player-scoped** quests by
reading that mod's own server-synced `WatchedAttributes` tree on the player's own entity — no
reflection is used for this path's detection at all.

The system SHALL ALSO detect activation and completion for Progression Framework's
**server-scoped** quests — one shared quest instance every player on the world contributes to
together, with no per-player membership concept in the source mod's own data model. Because this
shared state is not exposed via `WatchedAttributes` (it syncs over Progression Framework's own
private network channel instead), the system SHALL read it via best-effort reflection against
that mod's own `QuestSystem` ModSystem instance, mirroring the same reflection posture already
used for VS Quest's progress-count mirroring: read-only, self-disabling on failure, never
throwing.

Each of the three paths (VS Quest, Progression Framework player-scoped, Progression Framework
server-scoped) is gated independently on its own mod/scope check; any subset may be active in a
given world (`vsquest` and `progressionframework` cannot in practice both be installed at once,
but a world with Progression Framework may have only player-scoped, only server-scoped, or both
kinds of quests catalogued). The system SHALL NOT reflect into any other quest mod's dialog or
state (including Alegacy Quest Framework), and SHALL NEVER write into any backend's own state —
every read, across all three paths, is observational only.

#### Scenario: Accept-state is detected for VS Quest via its synced entity attributes
- **WHEN** a player with vsquest installed accepts a quest from a quest-giver entity
- **THEN** the system reads that quest's accept-state from the entity's synced
  `WatchedAttributes` without querying or modifying vsquest through any other path

#### Scenario: Accept-state is detected for Progression Framework via the player's own attributes
- **WHEN** a player with Progression Framework installed accepts a player-scoped quest
- **THEN** the system reads that quest's status from `progressionframework:questlog` on the
  player's own entity `WatchedAttributes`, with no reflection involved

#### Scenario: Activation is detected for a Progression Framework server-scoped quest
- **WHEN** a server-scoped Progression Framework quest's shared status transitions from
  unavailable/inactive to active (any player on the world triggering it, typically by talking to
  its quest-giver NPC)
- **THEN** the system detects that transition via its best-effort reflection read and treats it as
  that quest's activation, the same way a player-scoped accept is treated

#### Scenario: Alegacy Quest Framework is never reflected into or read from
- **WHEN** a player has Alegacy Quest Framework installed instead of (or alongside) either
  supported backend
- **THEN** the system does not attempt to read Alegacy's state under any circumstance

### Requirement: Auto-detection fails safe per backend and never blocks manual Quest Links
Any failure detecting state for a given path (a reflection error for VS Quest's progress
mirroring or for Progression Framework's server-scoped state, an unexpected attribute shape for
either `WatchedAttributes`-based path) SHALL be caught and SHALL silently disable auto-detection
**for that path only**, for the session, rather than raise a visible error or crash. A failure in
one path's detection SHALL NOT disable any other path's. Manually-created Quest Links (the
`link-task` capability's Quest Link requirement) SHALL continue to work normally regardless of
any path's auto-detection state.

#### Scenario: A reflection failure disables only VS Quest's progress mirroring
- **WHEN** vsquest's dialog fields no longer match what auto-detection expects (e.g. after a
  vsquest update)
- **THEN** VS Quest progress mirroring silently stops attempting further reads for the session,
  while VS Quest accept/completion detection and all Progression Framework detection (both
  scopes) are unaffected

#### Scenario: An attribute-shape failure in one backend doesn't disable the other
- **WHEN** Progression Framework's player-scoped `WatchedAttributes` tree shape no longer matches
  what auto-detection expects (e.g. after a Progression Framework update)
- **THEN** Progression Framework player-scoped detection silently stops for the session, while VS
  Quest detection (if active) and Progression Framework server-scoped detection continue
  unaffected

#### Scenario: A reflection failure in server-scoped detection doesn't disable player-scoped detection
- **WHEN** Progression Framework's internal `QuestSystem` shape no longer matches what the
  server-scoped reflection read expects (e.g. after a Progression Framework update)
- **THEN** Progression Framework server-scoped detection silently stops for the session, while
  Progression Framework player-scoped detection (which uses `WatchedAttributes`, not reflection)
  continues unaffected

#### Scenario: Manually-created Quest Links are unaffected by any detection failure
- **WHEN** any path's auto-detection has failed and disabled itself for the session
- **THEN** existing and new manually-created Quest Links for any backend/scope continue to work
  normally

## ADDED Requirements

### Requirement: A server-scoped Quest Link tracks the shared quest, not a personal copy
A Quest Link created for a Progression Framework server-scoped quest SHALL display the same
shared status and objective progress every player on the world sees for that quest — matching
what Progression Framework's own Quest Log shows — rather than any player-specific state. Linking
a server-scoped quest from two different players' documents SHALL show identical live progress on
both, since there is only one underlying quest instance.

#### Scenario: Two players' Links to the same server-scoped quest show identical progress
- **WHEN** two different players each have a Quest Link to the same server-scoped quest code
- **THEN** both Links display the same shared objective progress, updating together as either
  player (or any other player on the world) contributes toward it

#### Scenario: A server-scoped quest's completion is shared, not personal
- **WHEN** a server-scoped quest's shared status transitions to completed
- **THEN** every player with a Quest Link to that quest code sees it as completed, regardless of
  which player's contribution finished it

### Requirement: Server-scoped quests are included in the Progression Framework catalog
The Progression Framework catalog SHALL include server-scoped quests alongside player-scoped
ones, available in the Quest Link picker on the same terms (title/description resolved the same
way, no additional gating beyond the existing per-backend `IsModEnabled` visibility rule). A
server-scoped quest's catalog entry SHALL be indistinguishable in the picker from a player-scoped
one except for the live progress it later displays once linked.

#### Scenario: A server-scoped quest appears in the Quest Link picker
- **WHEN** Progression Framework is installed and its catalog includes a server-scoped quest
- **THEN** that quest appears in the Quest Link picker exactly like any player-scoped catalog
  entry, selectable regardless of whether its shared status is currently active
