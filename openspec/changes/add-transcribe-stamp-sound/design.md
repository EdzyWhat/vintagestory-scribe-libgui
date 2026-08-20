## Context

The Transcribe flourish (`ScribeStamp`, mounted by `GuiDialogScribeScriptorium`) is a
paint-only rubber-stamp animation with a clear descend → press → lift → fade arc. Its
"contact" beat is at normalized time `DescendEnd = 0.24f` — the frame the stamp finishes
translating down and the "COPIED"/"IMPORTED"/"EXPORTED" imprint snaps in. Today the beat
is silent.

The user supplied a source clip at `~/Downloads/stamp.m4a`. Measured with ffmpeg:
**stereo, 48 kHz, 0.683 s, peak −31.0 dBFS, mean −49.4 dBFS** — a short, quiet thump.

Two engine facts (decompiled from the shipped DLLs, `ilspycmd`) constrain the volume path:

- `Vintagestory.API.Client.SoundParams.Volume` **hard-clamps its setter to `[0f, 1f]`** —
  assigning >1 stores exactly 1. So a "5× via `Volume`" is impossible.
- `LoadedSoundNative.SetVolume(float val)` stores the clamped value into
  `soundParams.Volume` but passes the **raw** `val` to OpenAL as `AL_GAIN = val *
  GlobalVolume`. OpenAL does honor gain >1, but amplifying past unity risks
  implementation-dependent clipping/distortion against the source's headroom.

Existing precedent: `ScribeAlarmSound` loads `scribe:sounds/alarm/clockbell` via
`capi.World.LoadSound(new SoundParams(...) { SoundType = Sound, RelativePosition = true,
Position = (0,0,0) })` and drives volume with `SetVolume`, reading
`MySettings.TimerAlarmVolume / 100f` (see `ScribeModSystem.Timer.cs:80`).

## Goals / Non-Goals

**Goals:**
- A single stamp sound at the contact frame for copy / import / export flourishes.
- Watcher hears it iff the flourish is visible to them — with no new network or tab-state
  plumbing.
- Volume reuses `TimerAlarmVolume`; source loudness lands near slider 20 %, 5× headroom to
  100 %; all applied gain stays within the safe `[0f, 1f]` range.
- Mono.

**Non-Goals:**
- No new volume setting or settings-UI change.
- No envelope/breathing/fade shaping (that is the alarm's job; a stamp is a one-shot).
- No 3D/positional audio — this is a flat GUI cue.
- No change to the animation timing or visuals.

## Decisions

### Decision 0 — The alarm-volume mapping is an expedient calibration harness, not the final design

> **RESOLVED 2026-08-20 — harness retired.** The in-game sweep landed on the "alarm-volume-140"
> level; that loudness was baked into `stamp.ogg` (+16.9 dB, Decision 1) and the runtime volume is
> now **fixed at `1f`**, unmapped from `TimerAlarmVolume`. The base-game "Sound Effects" tie
> (`SoundType.Sound`) is kept — it was never the mapping to remove. The rest of this decision is
> retained as the record of why the harness existed.

Reusing `TimerAlarmVolume` is a **temporary** measure whose sole purpose is to let the user
sweep the stamp loudness quickly in-game and discover the right absolute level. It is
explicitly meant to be **unmapped later**: once the in-game calibration lands on a
comfortable slider position, that finding is captured as a concrete number (the resulting
linear gain / dBFS) and folded into a dedicated stamp-volume default, at which point the
stamp stops borrowing the alarm slider. Two consequences for this change:

- The in-game task records the chosen slider % and the derived absolute gain durably (in
  `TESTING.md` and/or a note), so the information survives to inform the future dedicated
  setting — the whole point of the harness is to "take back" that value.
- The trigger and playback code (Decisions 2–3) are written so the volume source is a
  single expression (`modSystem.MySettings.TimerAlarmVolume / 100f`); swapping it for a
  future dedicated setting is a one-line change that touches neither the trigger nor the
  asset.

### Decision 1 — Bake the gain into the file; keep runtime gain in [0,1]

Convert `stamp.m4a` → **mono** Ogg Vorbis with a fixed gain baked in, written to
`src/Mod/assets/scribe/sounds/stamp.ogg`. Rationale, from the −31 dBFS measurement:

- We want the play-time gain to be a plain linear multiplier in `[0,1]` (safe, no OpenAL
  amplification gamble) while still lifting the source to the loudness the user wants at the
  top of the alarm slider.
- **Revised target (per the user, 2026-08-20): "set the stamp to its level as if Alarm
  volume were 140."** The slider hard-caps at 100 (`Volume` clamps to `[0,1]`), so 140 % can
  only be reached by baking the extra gain into the file. Against the original "natural at
  slider 20" anchor, alarm-140 = `140/20 = 7×` the source = **+16.9 dB** (`20·log10(7)`).
- So bake **+16.9 dB** into the file: `−31 + 16.9 ≈ −14.1 dBFS` peak (measured −14.4 dBFS
  after Vorbis-lossy encode). Then:
  - slider 100 → `Volume 1.00` → playback peak ≈ −14 dBFS = the **alarm-140-equivalent**
    level the user asked for ✓
  - the slider stays proportional below that (0 still silences; the old "natural at 20 %"
    reference shifts down to ≈ slider 14 %, expected — the ceiling moved up).
  - Peak stays ≈ −14 dBFS even at max slider → **no clipping**, no reliance on OpenAL gain >1.
- The runtime mapping (`Volume = TimerAlarmVolume/100f`) is **unchanged** — this is purely a
  louder re-bake of the asset, so the calibration harness (Decision 0) and "0 silences" both
  survive.
- Conversion command (this ffmpeg build lacks `libvorbis`; decode to WAV then `oggenc`):
  `ffmpeg -i ~/Downloads/stamp.m4a -ac 1 -af "volume=16.9dB" -f wav /tmp/x.wav && oggenc -q 5
  -o src/Mod/assets/scribe/sounds/stamp.ogg /tmp/x.wav`

*(Prior bake was +14 dB = 5× / slider-100. Superseded by the alarm-140 request above.)*

**Alternative considered — `SetVolume(TimerAlarmVolume/20f)` for a runtime 5× via OpenAL.**
Rejected: relies on OpenAL honoring gain up to 5.0 (implementation-dependent) and would
clip badly if the source were ever re-normalized louder; harder for the author to reason
about than a plain 0–1 slider against a pre-normalized file.

### Decision 2 — Fire from inside `ScribeStamp` at `DescendEnd`; the widget's own mount gates the watcher

Add an `Action? onDescend` ctor param to `ScribeStamp`. In `ScribeStampState`, track a
`bool descendFired`; in `OnValueChanged`, when `t` first reaches `DescendEnd (0.24)` and
`!descendFired`, set the flag and invoke `onDescend`. In `InitState`, if the controller is
**already** at/after `DescendEnd` (or `Completed`), pre-set `descendFired = true` so a late
remount that skips the descend stays silent (mirrors the existing `OnEnd`-on-already-
Completed guard).

This satisfies the watcher requirement for free: a `ScribeStamp` is only ever constructed
by `BuildStampOverlay` when its slot matches `stampTargetSlot` **and** the Transcribe /
import-export view is the active body. A watcher on another tab never mounts the widget, so
`onDescend` never runs for them — no extra tab-state check or per-watcher network gate. The
actor and any on-view watcher run the identical path.

**Alternative considered — play the sound in `PlayStamp` / on the network receive.** Rejected:
that fires regardless of whether the flourish is visible, so a watcher on the Settings tab
(or with the view closed) would hear a phantom stamp — the exact case the user excluded.

### Decision 3 — One-shot playback owned by the dialog, self-disposing

`BuildStampOverlay` passes `onDescend: PlayStampSound`. `PlayStampSound()` does a
fire-and-forget one-shot:

```
capi.World.LoadSound(new SoundParams(new AssetLocation("scribe:sounds/stamp"))
{
    ShouldLoop       = false,
    DisposeOnFinish  = true,                 // engine cleans up the ~0.68s clip
    SoundType        = EnumSoundType.Sound,  // routes through base-game "Sound Effects" slider (matches alarm)
    RelativePosition = true,
    Position         = new Vec3f(0, 0, 0),   // non-positional, flat 2D cue
    Volume           = 1f,                   // level baked into stamp.ogg; unmapped 2026-08-20 (Decision 0)
})?.Start();
```

`DisposeOnFinish = true` removes the need for the tick/dispose bookkeeping the looping alarm
carries. A null return (missing asset) logs one warning and no-ops. Overlapping stamps each
spawn an independent one-shot; that is acceptable and self-cleaning.

The volume read mirrors `ScribeModSystem.Timer.cs:80` exactly (`MySettings.TimerAlarmVolume
/ 100f`); the `SoundParams.Volume` setter clamps to `[0,1]` anyway, so out-of-range settings
are inherently safe.

## Risks / Trade-offs

- **[Overlapping one-shots on rapid re-copy]** → Each play is short (0.68 s) and OpenAL
  mixes independent sources; `DisposeOnFinish` reclaims them. No pooling needed.
- **[Source is quiet (−31 dBFS)]** → By design the +14 dB bake lifts the reference so slider
  20 % ≈ source level and the default slider (65) plays at a moderate −21 dBFS. Final feel is
  a playtest calibration (§ in-game task); if too quiet/loud the fix is a one-line ffmpeg
  re-bake, not a code change.
- **[Reusing the alarm slider couples two unrelated sounds]** → Explicitly requested as an
  expedient calibration harness ("map … to the alarm sound level … for now"), to be unmapped
  once the right absolute level is learned (Decision 0). The volume source is a single
  expression, so splitting out a dedicated setting later is a one-line change that touches
  neither the trigger nor the asset.
- **[The base-game Sound Effects slider also scales it]** → Intended, via `SoundType =
  EnumSoundType.Sound` (same as the alarm). Note this stacks with the (temporary) alarm-slider
  mapping — the effective loudness is `TimerAlarmVolume/100 × SoundEffectsLevel × bakedGain`,
  which is the correct base-game behavior and matches the alarm's own stacking.
- **[`onDescend` misses the crossing if a frame jumps past 0.24]** → The check is `t >=
  DescendEnd` with a fire-once flag, not equality, so a hitched frame still fires exactly once.

## Migration Plan

Additive and non-load-bearing. New asset + two small code edits; no persistence, network,
or settings-schema change. Rollback = delete the asset and revert the two files; the
flourish returns to silent.

## Open Questions

- Final loudness calibration (does slider ~20 % feel "natural" in-game, is the default 65
  comfortable?) — resolved by the in-game playtest task, adjustable via ffmpeg re-bake.
