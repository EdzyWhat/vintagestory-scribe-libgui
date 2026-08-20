## 1. Asset prep (manual — author does this before coding)

- [x] 1.1 Download Freesound #78562 (joedeshon — alarm_clock_ringing_01.wav, CC BY 4.0) from freesound.org/s/78562.
- [x] 1.2 Normalize +0.9 dB (peak was −1.9 dBFS → target −1 dBFS), convert to mono 44.1 kHz OGG Vorbis q6 via ffmpeg→oggenc pipeline. Duration 26.44s, ticking tail intact.
- [x] 1.3 Placed at `assets/scribe/sounds/alarm/clockbell.ogg`. afinfo confirms 1ch 44100 Hz vorb, 26.44s.

## 2. Attribution

- [x] 2.1 Add a `CREDITS.txt` at the mod root crediting: Freesound #78562 "Old Mechanical Wind-Up Alarm Clock" by klankbibliotheek, licensed CC BY 4.0 (freesound.org/s/78562).
- [x] 2.2 Append the attribution to the `description` field in `modinfo.json` (a short credit line: "Alarm sound: freesound.org/s/78562 by klankbibliotheek, CC BY 4.0").

## 3. ScribeAlarmSound controller

- [x] 3.1 Create `src/Mod/ScribeAlarmSound.cs`. Define a `Phase` enum: `RampUp`, `Breathing`, `FadingOut`, `Done`.
- [x] 3.2 Add fields: `ILoadedSound _sound`, `long _tickListenerId`, `Phase _phase`, `float _elapsed`, `float _fadeStart` (volume at fade-out onset).
- [x] 3.3 Add a `const float NominalVolume = 0.65f` and `const float BreathPeriod = 3f` (placeholder — calibrated in §6). NominalVolume lives in ScribeGearTuning.AlarmNominalVolume (live-tunable) rather than as a const; default 0.65f.
- [x] 3.4 Constructor: load the sound via `capi.World.LoadSound(new SoundParams { Location = new AssetLocation("scribe:alarm/clockbell"), ShouldLoop = false, DisposeOnFinish = false, SoundType = EnumSoundType.Ambient, Volume = 0 })`, then call `_sound.Start()`. Register the tick listener.
- [x] 3.5 Implement the tick method. Accumulate `_elapsed += dt`.
- [x] 3.6 Add `public void Dismiss()` method: if phase is `RampUp` or `Breathing`, record `_fadeStart` = current volume (re-derive from the last-computed value), record `_fadeElapsedAtStart = _elapsed`, transition to `FadingOut`.
- [x] 3.7 Add `public bool IsDone => _phase == Phase.Done` for the `ScribeModSystem` to poll.
- [x] 3.8 Add `public void Dispose()`: stop and dispose `_sound` if not already disposed; unregister tick listener.

## 4. ScribeModSystem integration

- [x] 4.1 In `ScribeModSystem` (or `ScribeModSystem.Timer.cs`), add `ScribeAlarmSound? _activeAlarm`.
- [x] 4.2 In `OnClientReceivedTimerState`: after updating `MyTimer`, check if the new status is `Fired` and previous status was not `Fired` (i.e., this is the fire transition). If so, and `capi != null`, and `MyTimer.FiredElapsedSeconds < 26.0`, create `_activeAlarm = new ScribeAlarmSound(capi)`.
- [x] 4.3 Add `public void DismissAlarm()`: calls `_activeAlarm?.Dismiss()`. The alarm object self-manages its fade + disposal via its own tick; `ScribeModSystem` holds the reference until `Dispose()`.
- [x] 4.4 In `StopClientSide` / dispose: call `_activeAlarm?.Dispose(); _activeAlarm = null`.

## 5. Dismiss wiring

- [x] 5.1 In `HudScribePins.cs`, locate the fired-timer-row click handler that sends the clear packet. Add `modSystem.DismissAlarm()` at the same site.
- [x] 5.2 In `GuiDialogClockmakerNotebook.cs`, locate the Stop Timer button handler. Add `modSystem.DismissAlarm()` at the same site.

## 6. Build, calibrate, verify

- [x] 6.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [x] 6.2 `dotnet test tests/Core.Tests` — 440/440 passed.
- [x] 6.3 `bash build/restage.sh Debug` — restaged 2026-08-18.
- [x] 6.4 In-game calibration: volume moved to Scribe Settings (Timer section) as "Alarm Volume (0–100)", default 65. Live — changes take effect immediately while the alarm is playing. No manual const tuning needed.
- [x] 6.5 Manually verified in-game:
  - Timer fires → alarm starts (ramp up, then breathing rhythm). ✓
  - Dismiss early (HUD click) → fade-out, then silence. ✓
  - Dismiss early (Stop Timer in dialog) → same. ✓
  - Alarm Volume setting responds live while alarm is playing. ✓
  - Scribe Settings "Timer" section displays correctly with paired controls. ✓
- [x] 6.6 `openspec validate add-clockmaker-alarm-sound` passes. Playtest verdicts recorded above.
