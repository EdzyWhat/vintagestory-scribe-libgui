## ADDED Requirements

### Requirement: Alarm sound plays when the timer fires
When the Clockmaker Notebook timer transitions to the Fired state the mod SHALL start playing the
mechanical alarm clock sound (`scribe:alarm/clockbell`) on the client that owns the timer.
The sound SHALL play exactly once with no looping. The sound SHALL use `EnumSoundType.Ambient` so it
plays at a fixed volume unaffected by the player's world position.

#### Scenario: Sound starts on fire
- **WHEN** the client receives a timer-state update with status Fired and the timer was previously Running
- **THEN** the alarm sound begins playing immediately from the start of the clip

#### Scenario: No sound for already-fired timer on relog
- **WHEN** a player relogs and the server pushes a Fired timer state whose `FiredElapsedSeconds` is
  already ≥ 26 seconds (past the clip's natural end)
- **THEN** no new alarm sound is started

#### Scenario: Sound is ambient
- **WHEN** the alarm is playing and the player moves to a different location
- **THEN** the alarm volume does not change due to distance

### Requirement: Alarm volume ramps up over one second using easeInCubic
For the first second after the alarm starts the volume SHALL be scaled by an easeInCubic curve
(`t³` where `t` goes from 0 to 1 over 1000 ms), starting from silence and reaching the nominal
volume at t = 1. The ramp SHALL be driven by a per-frame tick listener, not VS's built-in FadeIn.

#### Scenario: Volume at start of ramp
- **WHEN** the alarm has just started (t ≈ 0)
- **THEN** the volume is effectively silent (near zero)

#### Scenario: Volume at end of ramp
- **WHEN** one second has elapsed since the alarm started
- **THEN** the volume has reached the nominal volume level

### Requirement: Alarm volume breathes with a sine wave during steady play
After the one-second ramp-up, the alarm SHALL modulate its volume with a sine wave:
`volume = nominalVolume × (1.0 + 0.1 × sin(2π × t / 3.0))`, where `t` is the elapsed seconds
since ramp-up completed. The period SHALL be 3 seconds and the amplitude SHALL be ±10% of
`nominalVolume`. The modulated volume SHALL never exceed 1.0.

#### Scenario: Breathing cycle
- **WHEN** the alarm has been ringing in steady state for 3 seconds
- **THEN** the volume has completed one full sine cycle (peaked above and dipped below nominal)

#### Scenario: Volume ceiling respected
- **WHEN** the sine wave is at its positive peak (+10%)
- **THEN** the resulting volume is at most 1.0 (nominalVolume × 1.1 ≤ 1.0 by constraint on nominalVolume)

### Requirement: Nominal volume is calibrated to a gentle reminder level
The alarm's nominal volume (`ScribeAlarmSound.NominalVolume`) SHALL be set such that the peak
breathing volume (nominalVolume × 1.1) roughly matches the perceived loudness of the bear growl
(`survival:creature/bear/growl1`) heard from approximately 10 blocks away. The value SHALL be
exposed in the `.geartune` tuning window for in-session calibration. The placeholder value before
calibration is `0.65f`.

#### Scenario: Alarm is not overwhelming
- **WHEN** the alarm is playing in steady state at default nominal volume
- **THEN** the sound is clearly audible but not startlingly loud relative to normal game ambience

### Requirement: Alarm fades out with easeInOutSine when dismissed early
When the player dismisses the alarm before the clip ends, the volume SHALL fade out using an
easeInOutSine curve (`(1 − cos(π×t)) / 2`, inverted to go from current volume to silence) over
500 ms. The `ILoadedSound` SHALL be stopped and disposed when the fade completes.

#### Scenario: Fade starts on dismiss
- **WHEN** the player dismisses the alarm (HUD click or Stop Timer)
- **THEN** the volume begins fading out smoothly (slow start, fast middle, slow end)

#### Scenario: Sound stops after fade
- **WHEN** 500 ms have elapsed since the dismiss fade began
- **THEN** the sound has fully stopped and the ILoadedSound has been disposed

### Requirement: Alarm ends naturally when the clip finishes
If the player has not dismissed the alarm by the time the ~26.4s clip reaches its natural end,
the sound SHALL stop on its own. No looping, no restart. The ticking section at the end of the
clip SHALL play through to silence.

#### Scenario: Natural clip end
- **WHEN** approximately 26.4 seconds have elapsed since the alarm started with no dismissal
- **THEN** the sound stops naturally and the ILoadedSound is disposed

### Requirement: Attribution is included in the mod
The mod SHALL credit Freesound #78562 (author: klankbibliotheek, CC BY 4.0) in `modinfo.json`
and in a `CREDITS.txt` file at the mod root.

#### Scenario: Credits present
- **WHEN** a player reads the mod's description on the mod DB or opens CREDITS.txt
- **THEN** the Freesound attribution is present with the author name, sound ID, and license
