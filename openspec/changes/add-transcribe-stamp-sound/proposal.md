## Why

The Scriptorium's Transcribe flourish (the wooden rubber-stamp that descends onto the
Duplicate / import-export slot on a copy, import, or export) is currently silent. A
stamping motion that lands without a sound reads as unfinished — the "press" beat is the
natural moment for an audible thump. Adding a short stamp sound at the contact frame
completes the feedback loop and matches the polish already given to the animation itself.

## What Changes

- Add a short stamp sound effect, played once when the wooden stamp finishes translating
  down onto the slot (the contact / press beat), for copy, import, and export flourishes.
- Convert the provided source clip (`~/Downloads/stamp.m4a`, stereo) to a **mono** Ogg
  Vorbis asset under `assets/scribe/sounds/`, baking a fixed +14 dB (5×) gain into the
  file so the play-time gain stays within the safe `[0f, 1f]` range while still offering
  5× loudness headroom (see design for the measurement behind this number).
- Play the sound **only when the animation is actually seen**: fire it from inside the
  `ScribeStamp` widget at the descend-complete frame, so a multiplayer watcher hears it
  exactly when (and only when) the flourish mounts and plays on their screen — i.e. when
  they are on the same Transcribe/import-export view. A watcher on a different tab never
  mounts the widget and so hears nothing, with no extra tab-state or network logic.
- Map the stamp volume to the existing **Timer alarm volume** setting
  (`ScribePlayerSettings.TimerAlarmVolume`, 0–100) — no new setting. Calibrated (via the
  baked file gain) so the source clip's own loudness lands at slider ≈ 20 %, leaving 5×
  headroom up to slider 100 %.

## Capabilities

### New Capabilities

<!-- none: this extends the existing Transcribe flourish rather than introducing a new capability -->

### Modified Capabilities

- `transcribe-copy-paste`: the copy/import/export stamp flourish gains an audible stamp
  cue at the contact frame, gated so it plays only when the flourish is visible to the
  local viewer (covering the multiplayer-watcher case), with volume driven by the timer
  alarm volume setting.

## Impact

- **Assets**: new `src/Mod/assets/scribe/sounds/stamp.ogg` (mono, +14 dB baked). One-time
  ffmpeg conversion from `~/Downloads/stamp.m4a`.
- **Code**:
  - `src/Mod/ScribeStamp.cs` — add an `onDescend` callback fired once when the animation
    crosses `DescendEnd` (the descend-complete frame), guarded against a late remount
    that skips the descend.
  - `src/Mod/GuiDialogScribeScriptorium.cs` — wire `onDescend` in `BuildStampOverlay` to a
    one-shot sound play (`capi.World.LoadSound(...).Start()` with `DisposeOnFinish`),
    reading `modSystem.MySettings.TimerAlarmVolume / 100f` for the volume (mirrors
    `ScribeModSystem.Timer.cs`'s alarm-volume read).
- **Settings**: none added. Reuses `TimerAlarmVolume`.
- **Dependencies**: none. Uses the same `capi.World.LoadSound` / `ILoadedSound` path as
  `ScribeAlarmSound`.
- **Non-load-bearing**: the sound is pure feedback; a missing asset or null `LoadSound`
  logs a warning and no-ops, exactly like the stamp bitmap and the alarm.
