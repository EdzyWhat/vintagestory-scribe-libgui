## 1. Sound asset

- [x] 1.1 Convert the source clip to a mono Ogg Vorbis with the gain baked in. **Re-baked
      2026-08-20 to +16.9 dB (7×) = the "alarm volume 140" level the user requested** (was +14 dB /
      5×; 140/20 = 7× → 20·log10(7) = 16.9 dB). This ffmpeg build lacks `libvorbis`, so used
      `oggenc`: `ffmpeg -i ~/Downloads/stamp.m4a -ac 1 -af "volume=16.9dB" -f wav /tmp/x.wav &&
      oggenc -q 5 -o src/Mod/assets/scribe/sounds/stamp.ogg /tmp/x.wav`. Verified: 1 channel,
      vorbis, 48 kHz, max_volume −14.4 dBFS (source −31 dBFS + 16.9, Vorbis-lossy), no clipping.
      Runtime mapping unchanged (`Volume = TimerAlarmVolume/100f`) — slider 100 now hits the
      alarm-140-equivalent; 0 still silences.

## 2. Trigger seam in ScribeStamp

- [x] 2.1 Added `Action? onDescend = null` ctor param + `public Action? OnDescend { get; }` to
      `ScribeStamp`, documented as fired once at the descend-complete/contact frame.
- [x] 2.2 Added `private bool descendFired;`; `OnValueChanged` now fires `Widget.OnDescend` once
      when `value >= DescendEnd && !descendFired`. Hoisted `DescendEnd` to an `internal const
      float` on `ScribeStampState` so `Build` and `OnValueChanged` share the one value.
- [x] 2.3 `InitState` pre-sets `descendFired = true` when `controller.Value >= DescendEnd` or
      the controller is already `Completed`, so a late remount that skipped the descend is silent.

## 3. Play the sound from the Scriptorium dialog

- [x] 3.1 Added private `PlayStampSound()` one-shot in `GuiDialogScribeScriptorium` (LoadSound
      of `scribe:sounds/stamp`, `DisposeOnFinish`, `SoundType = Sound`, non-positional). Null return
      logs one warning and no-ops. **Volume unmapped 2026-08-20**: the alarm-slider mapping (calibration
      harness, Decision 0) was replaced with a fixed `Volume = 1f` now that the level is baked into the
      ogg. `SoundType.Sound` (base-game "Sound Effects" tie) retained.
- [x] 3.2 `BuildStampOverlay` passes `onDescend: PlayStampSound`. All three modes route through
      `PlayStamp` → the same overlay (`StampCopy`, import via `PlayWatcherStamp`/import handler,
      export at line 794), so one wiring covers copy + import + export.

## 4. Build + tests

- [x] 4.1 `dotnet build src/Mod/Mod.csproj -c Debug` — 0 warnings / 0 errors.
- [x] 4.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — 463 pass; the only 7 failures are
      the pre-existing `ScribeBrightnessCurve` illumination-floor drift (fails on clean `main`,
      Core untouched here).
- [x] 4.3 `bash build/restage.sh Debug` — 138 files staged, `stamp.ogg` confirmed present;
      client verified not running first.

## 5. In-game verification (playtest gate)

- [x] 5.1 Single-player: perform a Transcribe copy → the stamp sound plays once, exactly as the
      wooden stamp lands (the contact/press beat), not on fade-in or lift. Repeat for import and
      export → each plays its own single stamp sound.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.2 Volume (calibration RESOLVED 2026-08-20 → the "alarm-140" level, baked into `stamp.ogg` at
      +16.9 dB, runtime `Volume` fixed at `1f`, unmapped from the alarm slider). Remaining check: the
      stamp plays at the confirmed level and the Timer alarm slider **no longer** affects it, while the
      base-game "Sound Effects" slider **still** scales it (0 there silences the stamp).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.3 Multiplayer: two clients on one shared Scriptorium. Watcher on the same
      Transcribe/import-export view hears the stamp at the contact frame; watcher on a different
      tab (or with the dialog closed) hears nothing.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).

## 6. Docs

- [x] 6.1 If the `SoundParams.Volume` `[0,1]` clamp vs. `SetVolume` raw-to-OpenAL distinction,
      or the "bake gain into the file rather than runtime >1" tactic, proves a non-obvious fact,
      add a one-line note to `VSAPI-NOTES.md` so it is not re-derived.
  - Done 2026-08-21: the decompiled clamp/gain distinction WAS non-obvious, so added a
    "Sound playback volume" section to `VSAPI-NOTES.md` (clamp vs. raw-gain + bake-gain-into-file
    takeaway). Condition met, note recorded.
