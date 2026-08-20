## ADDED Requirements

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
