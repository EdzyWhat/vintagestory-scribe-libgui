## ADDED Requirements

### Requirement: The HUD lists the player's own pinned tasks
The system SHALL render an on-screen HUD listing the current client player's own pinned tasks,
sourced from that player's synced pin set. Each rendered row SHALL show the pinned task's last-known
text and a completed/not-completed indicator, using the last-known snapshot so a pin whose source
document is unresolvable (unloaded chunk, broken block) is still shown. The row SHALL be presented
with the task's text legible over the game world (for example via a text glow/outline) and SHALL NOT
require a manual removal control. A player SHALL only ever see their own pins on the HUD.

#### Scenario: A pinned task appears on the HUD
- **WHEN** the player pins a task and the server pushes the updated pin set
- **THEN** the HUD shows a row for that task with its text and completion indicator

#### Scenario: An unresolvable pin still renders from its snapshot
- **WHEN** a pinned task's source document is in an unloaded chunk or its block is broken
- **THEN** the HUD still shows the task from its last-known text/done snapshot

#### Scenario: The HUD shows only the current player's pins
- **WHEN** another player pins tasks in a shared/multiplayer world
- **THEN** those pins do not appear on this player's HUD

### Requirement: The HUD orders pins automatically, sinking completed tasks
The system SHALL present the HUD rows in a deterministic order: the player's pin order, with completed
tasks ordered below not-completed tasks. When a task is completed from the HUD, the system SHALL keep
its row briefly in place (a short undo window in which the player can revert the completion) before
re-ordering it to the bottom, and SHALL visually de-emphasize a completed task's row (for example by
muting its text). The HUD SHALL NOT offer manual reordering; manual ordering is provided elsewhere (a
later in-document surface).

#### Scenario: A completed task sinks to the bottom
- **WHEN** the player completes a pinned task from the HUD and its completion is retained (the pin is
  not removed by the completion policy)
- **THEN** the row is de-emphasized and, after a brief undo window, moves below the not-completed rows

#### Scenario: Completion can be undone within the window
- **WHEN** the player re-toggles a just-completed row's control within the undo window
- **THEN** the task returns to not-completed and to its prior position

### Requirement: The HUD refreshes when the pin set changes
The system SHALL update the HUD to reflect the current pin set whenever a fresh pin set or settings
push arrives, without requiring the player to reopen anything.

#### Scenario: Completing a task updates the HUD live
- **WHEN** the player completes one of their pinned tasks (from the HUD or their own lectern edit) and
  the server re-pushes their pin set
- **THEN** the HUD reflects the change (the row updates its completion indicator, or is removed if the
  completion policy cleared the pin or deleted the task) without a manual refresh

### Requirement: The HUD is bounded by a configurable maximum row count
The system SHALL display at most the player's configured maximum number of HUD rows (defaulting to 3).
When the player has more pins than the maximum, the system SHALL show the first maximum-count pins and
indicate that additional pins exist rather than growing without bound.

#### Scenario: Pins beyond the maximum are summarized
- **WHEN** the player has more pinned tasks than their configured maximum HUD rows
- **THEN** the HUD shows exactly the maximum number of rows and indicates that further pins exist

#### Scenario: Changing the maximum is honored
- **WHEN** the player's maximum-HUD-rows setting changes and is synced
- **THEN** the HUD shows up to the new maximum on its next refresh

### Requirement: The HUD's screen position is configurable
The system SHALL anchor the HUD to one of seven screen positions — top-left, top-middle, top-right,
middle-left, middle-right, bottom-left, bottom-right — defaulting to top-right, as a client-local
per-player preference. Each anchor SHALL support a configurable pixel X/Y offset so the HUD can be
nudged clear of other on-screen overlays (the minimap, coordinate overlay, and block-info overlay).
The default top-right anchor SHALL be pre-offset far enough to the left that the HUD does not render
underneath the default top-right minimap. The HUD's task-row area SHALL be a fixed width. Selecting the
anchor and offsets from an in-mod settings UI is out of scope for this change (the values are
config-editable now; the UI is a later change).

#### Scenario: The default position clears the minimap
- **WHEN** the player has pins and has not changed the HUD position preference
- **THEN** the HUD renders anchored top-right, offset left of the default minimap, not beneath it

#### Scenario: Changing the anchor is honored
- **WHEN** the player changes the HUD anchor preference (e.g. to bottom-right) and reloads
- **THEN** the HUD renders at the new anchor on its next show

#### Scenario: An offset nudges the HUD clear of an overlay
- **WHEN** the player sets a nonzero X/Y offset for the active anchor
- **THEN** the HUD is displaced by that offset from the anchored corner/edge

### Requirement: The HUD auto-shows when there are pins and is collapsible
The system SHALL show the HUD automatically whenever the player has at least one pin, and SHALL hide it
entirely when the player has zero pins. Separately from that automatic hidden-at-zero behavior, the
system SHALL let the player **collapse** the HUD — minimizing it to a compact affordance that remains
visible and can re-expand it — rather than hiding it outright. Collapse SHALL be toggleable from two
entry points: a rebindable hotkey and an on-HUD collapse control. The collapsed preference SHALL
persist across sessions (as a client-local preference), and the collapse/expand transition SHALL be
animated.

#### Scenario: HUD appears with the first pin and disappears at zero
- **WHEN** the player pins their first task
- **THEN** the HUD becomes visible; and **WHEN** the player later has no pins, the HUD hides

#### Scenario: Collapsing minimizes rather than hiding
- **WHEN** the player collapses the HUD while it has pins
- **THEN** the task rows are hidden but a compact affordance remains on screen from which the player
  can re-expand the HUD

#### Scenario: Both the hotkey and the on-HUD control toggle collapse
- **WHEN** the player presses the HUD toggle hotkey, or activates the on-HUD collapse control
- **THEN** the HUD collapses (or expands if already collapsed) and the collapsed preference is recorded

#### Scenario: The collapse preference persists across sessions
- **WHEN** the player collapses the HUD and later restarts the game
- **THEN** the HUD's collapsed/expanded state matches what the player last set

### Requirement: HUD rows complete by stable identity under the player's policy
The system SHALL let the player complete a task directly from its HUD row, addressed by
`(DocId, TaskId)` so no block resolution is required, using the same identity-addressed completion the
read view uses. Completion SHALL record the completed state in the player's own pin store (so it works
even when the source is unresolvable) and write through to the source document when it is resolvable.
The outcome SHALL follow the player's completion policy: *sink* keeps the row (see the ordering
requirement), *unpin* removes the pin and the row leaves the HUD, *delete* removes the underlying task
and the row leaves the HUD. The HUD SHALL NOT provide a separate manual unpin control; removal is a
consequence of the policy.

#### Scenario: Completing a task under the sink policy
- **WHEN** the player activates the completion control on a HUD row and their policy is *sink*
- **THEN** the task is marked complete by identity and the row remains, de-emphasized and re-ordered
  to the bottom after the undo window

#### Scenario: Completing a task under the unpin policy
- **WHEN** the player activates the completion control on a HUD row and their policy is *unpin*
- **THEN** the task is marked complete by identity, the player's pin is removed, and the row leaves the
  HUD

#### Scenario: Completing a task under the delete policy
- **WHEN** the player activates the completion control on a HUD row and their policy is *delete*
- **THEN** the underlying task is deleted and the row leaves the HUD
