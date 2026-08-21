# transcribe-copy-paste Specification

## Purpose
TBD - created by archiving change add-transcribe-copy-paste. Update Purpose after archive.
## Requirements
### Requirement: Copy a document between the two Transcribe slots

The Transcribe view SHALL provide an Original (source) slot and a Duplicate (target) slot and a copy
action that duplicates the Original item's stored `ScribeDocument` onto the target item, using the
`ScribeDocumentAttributes` serialization path. The Original item SHALL NOT be consumed or modified by
the copy. The copy SHALL be server-authoritative: the client sends a copy request; the server performs
the duplication and syncs the result back.

#### Scenario: Copy onto an empty target

- **WHEN** the Original slot holds a Scribe item with a document and the Duplicate slot holds a Scribe item with no contents
- **AND** the player triggers the copy action
- **THEN** the Duplicate item receives a copy of the Original's document (its tasks, notes, and other blocks)
- **AND** the Original item is unchanged
- **AND** the Duplicate slot's summary card updates to show the copied contents

#### Scenario: Copy action is unavailable until both slots are filled

- **WHEN** either the Original or the Duplicate slot is empty
- **THEN** the copy action is disabled and an explainer indicates a Scribe item is needed in each slot

#### Scenario: Copied document is independent of the source

- **WHEN** a document has been copied from the Original to the Duplicate item
- **AND** the player later edits either item's document
- **THEN** the edit affects only that item; the two documents do not share state

### Requirement: Overwrite confirmation when the target already has contents

When the Duplicate slot's item already contains tasks, the copy action SHALL require an explicit
second confirmation before overwriting, and SHALL surface how many tasks would be replaced. When the
target has no contents, no confirmation SHALL be required.

#### Scenario: First press on a non-empty target asks to confirm

- **WHEN** the Duplicate item has N existing tasks and the player triggers the copy action
- **THEN** the action does not yet copy; it changes to a distinct confirm state indicating "overwrite N tasks"
- **AND** a second trigger of the action performs the copy, replacing the target's document with the Original's

#### Scenario: Empty target copies without confirmation

- **WHEN** the Duplicate item has no contents and the player triggers the copy action
- **THEN** the copy proceeds immediately with no confirmation step

#### Scenario: Confirmation resets if the slots change

- **WHEN** the copy action is in its "confirm overwrite" state
- **AND** the contents of either slot change before the confirming press
- **THEN** the action returns to its initial (unconfirmed) state

### Requirement: Wax-seal stamp affordance and press animation

The copy action SHALL be presented as a wax-seal / stamp affordance. Triggering it SHALL play a
button-triggered press animation (a 2D seal pressing down with scale, slight rotation, and fade) that
leaves a brief imprint on the Duplicate slot before the copied summary card appears. The animation SHALL
be a non-load-bearing flourish: if the animation is disabled or removed, the copy SHALL still function
identically. The stamp SHALL be implemented as a reusable animation component so it can later be reused
elsewhere.

#### Scenario: Stamp animation plays on copy

- **WHEN** the player triggers a successful copy
- **THEN** the seal plays its press animation and leaves a brief imprint on the Duplicate slot
- **AND** the copied summary card is shown when the animation settles

#### Scenario: Copy works without the animation

- **WHEN** the stamp animation is unavailable
- **THEN** the copy still completes and the Duplicate slot still reflects the copied contents

### Requirement: Transcribe stamp plays an audible contact cue

The Transcribe stamp flourish (copy, import, and export) SHALL play a short stamp sound
effect once per play, at the frame the wooden stamp finishes translating down onto the
slot (the descend-complete / contact beat), so the motion has an audible landing.

The cue SHALL be non-load-bearing: if the sound asset is missing or cannot be loaded, the
flourish and the underlying copy/import/export SHALL be unaffected (the play simply
produces no sound, mirroring the existing missing-bitmap tolerance).

#### Scenario: Sound fires at the contact frame, once
- **WHEN** a Transcribe stamp flourish plays and its animation reaches the descend-complete frame
- **THEN** the stamp sound plays exactly once for that play
- **AND** the sound does not repeat during the press, lift, or fade phases of the same play

#### Scenario: A fresh play on re-copy re-triggers the sound
- **WHEN** a second copy/import/export triggers a new flourish before or after the first has ended
- **THEN** the new play fires its own stamp sound at its own descend-complete frame

#### Scenario: Missing sound asset does not break the flourish
- **WHEN** the stamp sound asset cannot be loaded
- **THEN** the flourish still animates and the copy/import/export still completes
- **AND** a single warning is logged rather than an error surfaced to the player

### Requirement: Stamp sound plays only when the flourish is visible to the local viewer

The stamp sound SHALL play on a given client only when that client actually mounts and
plays the flourish animation. It SHALL be triggered from within the animation widget at
the contact frame rather than from the network/copy dispatch, so a multiplayer watcher
hears the cue exactly when the flourish is on their screen and never when it is not.

#### Scenario: Watcher on the same view hears the stamp
- **WHEN** another player performs a copy/import/export on a shared Scriptorium
- **AND** the local watcher is viewing the same Transcribe / import-export view where the
  flourish mounts and plays
- **THEN** the watcher hears the stamp sound at the flourish's contact frame

#### Scenario: Watcher not seeing the animation hears nothing
- **WHEN** another player performs a copy/import/export on a shared Scriptorium
- **AND** the local watcher is not on the view that mounts the flourish (so no stamp
  animation plays for them)
- **THEN** the watcher hears no stamp sound

#### Scenario: A late mount that skips the descend is silent
- **WHEN** the flourish's animation is already past the descend-complete frame at the moment
  its widget mounts (a late remount)
- **THEN** no stamp sound plays for that mount (the contact beat was not shown)

### Requirement: Stamp volume follows the timer alarm volume setting

The stamp sound's play-time volume SHALL be driven by the existing timer alarm volume
setting (`ScribePlayerSettings.TimerAlarmVolume`, 0–100), with no new setting introduced.
The applied gain SHALL remain within the engine's safe `[0f, 1f]` range at all slider
positions; the loudness calibration (source clip's own level at ~20 %, up to 5× at 100 %)
SHALL be realized by the fixed gain baked into the mono sound asset, not by any runtime
gain above 1.

#### Scenario: Volume tracks the slider
- **WHEN** the player sets the timer alarm volume to a value V (0–100)
- **THEN** the next stamp sound plays at gain V/100 (clamped to [0,1])

#### Scenario: Muted setting silences the stamp
- **WHEN** the timer alarm volume is 0
- **THEN** the stamp sound is silent

