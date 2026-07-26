## ADDED Requirements

### Requirement: Completing a pinned task from the HUD has a brief undoable window with animated feedback
When a player completes (checks off) a pinned task from the HUD, the system SHALL hold the completion for
a brief window before it takes effect on the server, during which the player MAY undo it by unchecking the
task; an undo within the window SHALL leave the task and its pin exactly as they were, with no completion
having been applied. All completion policies SHALL share the same window duration. The system SHALL give
animated feedback during the window that reflects the pending outcome: a completion under a policy that
removes the task or its pin SHALL visibly fade the affected row, and a completion under a policy that
keeps-and-sinks the task SHALL visibly settle the row toward its sunk position. The task's checkbox SHALL
remain operable throughout the window so the undo is always available.

#### Scenario: Undo within the window applies no completion
- **WHEN** a player checks off a pinned task on the HUD and unchecks it before the window elapses
- **THEN** no completion is applied — the task's done-state and the player's pin are unchanged, and no
  removal or sink occurs

#### Scenario: Completion applies after the window
- **WHEN** a player checks off a pinned task on the HUD and does not undo before the window elapses
- **THEN** the completion is applied under the player's current policy (sink, keep, unpin, or delete)

#### Scenario: The window gives animated feedback
- **WHEN** a completion is pending within its window
- **THEN** the row animates to preview the outcome (a fade for unpin/delete, a settle toward the bottom
  for sink), while its checkbox stays operable for undo

## MODIFIED Requirements

### Requirement: Player preferences are client-local and cross-world
The system SHALL maintain a small set of per-player display/behavior preferences — including at least a
**completion policy** (what happens to a task and its pin when the task is completed), the maximum
number of pinned tasks shown on the HUD, the HUD's screen anchor, its horizontal and vertical offsets,
its row width, a collapsed-HUD flag, a **HUD font-size scale**, and a **window font-size scale** —
stored **client-locally** for the player (not per world) so the same preferences apply across all of
that player's worlds, and persisted across sessions. All of these preferences SHALL be held in a
**single** client-local preference store; the mod SHALL NOT split them across more than one client
configuration file. These preferences SHALL NOT be synchronized to or authoritative on the server; they
are personal preferences with no shared-world effect. The completion policy SHALL be one of: *sink*
(the completed task stays pinned and is de-prioritized on the HUD), *keep* (the completed task stays
pinned and keeps its place — not de-prioritized), *unpin* (completion removes the pin), or *delete*
(completion deletes the underlying task). The font-size scales SHALL be multipliers that default to
`1.0` (no change), SHALL each be snapped to a discrete notch at 5% granularity within their range (i.e.
one of `0.80, 0.85, 0.90, … , 1.20`, shown as a percent), and SHALL be applied on top of the game's
global GUI scale rather than replacing it. The horizontal and vertical HUD offsets SHALL be interpreted
as nudges *relative to* the anchor's built-in pre-baked offset (so a stored `0` leaves the HUD at the
anchor's sensible default position, e.g. clear of the default top-right minimap), not as absolute
positions. Preferences SHALL default to the *sink* policy, a maximum HUD row count of 3, and a
font-size scale of `1.0`. On read, the system SHALL clamp each numeric preference (the maximum HUD rows, the row width, the
horizontal and vertical offsets to ±300 pixels, and both font-size scales) to its sane range (snapping
each font-size scale to its nearest allowed 5% notch) and treat an unrecognized
completion-policy or HUD-anchor value as its default, so a hand-edited or corrupted preference file
cannot produce an invalid state. The preference store SHALL leave room for additional personal
preferences without a format break.

#### Scenario: Preferences default and persist across sessions
- **WHEN** a player who has never changed a preference plays, and later restarts the game
- **THEN** their preferences read as the *sink* completion policy, a maximum HUD row count of 3, and
  font-size scales of `1.0`, and any change they made persists across the restart

#### Scenario: Preferences are shared across a player's worlds
- **WHEN** a player changes a preference while in one world and then joins a different world
- **THEN** the changed preference applies in the second world (the preference is client-local, not
  per-world)

#### Scenario: All preferences live in one client store
- **WHEN** a player changes any Scribe preference, including a font-size scale
- **THEN** the change is written to the single client-local preference store, and no separate client
  configuration file holds a competing copy of that preference

#### Scenario: A corrupted preference value is normalized on read
- **WHEN** the stored preferences carry an out-of-range numeric value (maximum HUD rows, row width, an
  offset beyond ±300, or a font-size scale outside `0.80`–`1.20`) or an unrecognized completion policy or
  HUD anchor
- **THEN** each out-of-range numeric value is clamped to its allowed range (each font-size scale snapped
  to its nearest 5% notch) and each unrecognized enumerated value falls back to its default (the
  completion policy to *sink*, the HUD anchor to its default corner)
