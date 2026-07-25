## MODIFIED Requirements

### Requirement: Player preferences are client-local and cross-world
The system SHALL maintain a small set of per-player display/behavior preferences — including at least a
**completion policy** (what happens to a task and its pin when the task is completed), the maximum
number of pinned tasks shown on the HUD, and a collapsed-HUD flag — stored **client-locally** for the
player (not per world) so the same preferences apply across all of that player's worlds, and persisted
across sessions. These preferences SHALL NOT be synchronized to or authoritative on the server; they
are personal preferences with no shared-world effect. The completion policy SHALL be one of: *sink*
(the completed task stays pinned and is de-prioritized on the HUD), *unpin* (completion removes the
pin), or *delete* (completion deletes the underlying task). Preferences SHALL default to the *sink*
policy and a maximum HUD row count of 3. On read, the system SHALL clamp the maximum-HUD-rows value to
a sane range and treat an unrecognized completion-policy value as the *sink* default, so a hand-edited
or corrupted preference file cannot produce an invalid state. The preference store SHALL leave room for
additional personal preferences without a format break.

#### Scenario: Preferences default and persist across sessions
- **WHEN** a player who has never changed a preference plays, and later restarts the game
- **THEN** their preferences read as the *sink* completion policy and a maximum HUD row count of 3, and
  any change they made persists across the restart

#### Scenario: Preferences are shared across a player's worlds
- **WHEN** a player changes a preference while in one world and then joins a different world
- **THEN** the changed preference applies in the second world (the preference is client-local, not
  per-world)

#### Scenario: A corrupted preference value is normalized on read
- **WHEN** the stored preferences carry an out-of-range maximum-HUD-rows value or an unrecognized
  completion policy
- **THEN** the maximum is clamped to the allowed range and the policy falls back to *sink*

### Requirement: A pin is a player-owned, store-authoritative copy of a task
The system SHALL treat each pin as the pinning player's own copy of a task: the per-player pin store
SHALL be authoritative for a pinned task's completed state, and each pin SHALL carry the player's
captured text/done snapshot. A pin's text and completed state SHALL change ONLY as a result of the
pinning player's own actions — never from another player's edit to the source task. A pin SHALL
remain valid and completable even when its source document is unresolvable or its source block has
been destroyed.

#### Scenario: Another player's edit does not change my pin
- **WHEN** another player edits the text of a task that I have pinned
- **THEN** my pin's captured text is unchanged

#### Scenario: Another player's completion does not change my pin
- **WHEN** another player toggles the completed state of a task that I have pinned
- **THEN** my pin's completed state is unchanged

#### Scenario: A pin survives destruction of its source
- **WHEN** the block hosting a pinned task's source document is broken or removed
- **THEN** my pin remains in my set with its last-known snapshot and can still be completed

### Requirement: Completing a task applies the requested completion policy
The system SHALL, when a player completes one of their pinned tasks, record the completed state in that
player's pin store and apply the completion policy **carried with the completion request** (the
player's client-local preference), after validating it and treating an unrecognized value as *sink*:
under *sink* the pin is retained (marked done); under *unpin* the player's pin is removed; under
*delete* the underlying task is removed from its source document. When the source document is
resolvable, completion SHALL also write the completed (or deleted) state through to the source
document, reconciling ONLY the acting player's pin. When the source is unresolvable, the store record
alone SHALL define the outcome (the source write is skipped).

#### Scenario: Sink keeps the pin and records completion
- **WHEN** a player with the *sink* policy completes a pinned task
- **THEN** the pin is marked done in that player's store and remains pinned

#### Scenario: Unpin removes the pin
- **WHEN** a player with the *unpin* policy completes a pinned task
- **THEN** the player's pin for it is removed

#### Scenario: Delete removes the task
- **WHEN** a player with the *delete* policy completes a pinned task whose source is resolvable
- **THEN** the underlying task is deleted from its source document and the pin is removed

#### Scenario: Completing a pin whose source is gone still applies the policy
- **WHEN** a player completes a pinned task whose source document is unresolvable or destroyed
- **THEN** the completion is recorded in the player's store and the policy is applied (sink retains
  the completed pin; unpin/delete remove it) without requiring the source

### Requirement: A player's own edit reconciles only their own pins
The system SHALL, when a player edits a source document (deleting, completing, or unpinning a task
from an edit view), reconcile ONLY that acting player's matching pins, identified by the task's stable
identity. Deleting a task the player has pinned SHALL remove that player's pin; the same action by a
different player SHALL NOT affect this player's pin.

#### Scenario: Deleting my pinned task in my own edit removes my pin
- **WHEN** I delete a task I have pinned while editing its source document
- **THEN** my pin for that task is removed

#### Scenario: Another player deleting the task leaves my pin
- **WHEN** another player deletes a task that I have pinned
- **THEN** my pin for that task remains in my set (retaining its last-known snapshot)
