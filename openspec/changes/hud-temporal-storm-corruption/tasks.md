## 1. Core text-corruption helper

- [x] 1.1 Add a pure `ScribeTextCorruptor` in `src/Core` (no VS API): `Corrupt(string text, double
      strength, int seed)` that injects a random combining mark after each character with probability
      = strength, using vanilla's fixed combining-mark code-point set. Deterministic for a given seed.
- [x] 1.2 Unit tests (tests/Core.Tests): strength 0 returns the input unchanged; strength 1 marks
      every character; same seed → same output; different seeds → different output; empty/whitespace
      handled; base characters are preserved (marks only inserted).

## 2. Client storm/stability read + strength model

- [x] 2.1 In `HudScribePins`, add a private helper that resolves
      `capi.ModLoader.GetModSystem<SystemTemporalStability>()` (null-safe) and returns
      `(double strength, bool stormActive)`: storm tier → ≈0.53/0.67/0.90 (Light/Medium/Heavy) when
      `StormData.nowStormActive`; low-stability ramp `Clamp((0.50 - stability)/0.40, 0, 1)` from
      `WatchedAttributes.GetDouble("temporalStability", 1.0)`; effective = max of the two.
- [x] 2.2 When the temporal system is absent or the setting is off, force strength 0 and
      stormActive false (graceful no-op).

## 3. HUD rendering: corruption + title swap

- [x] 3.1 Add a lang key `scribe-hud-title-storm` = "Survive the Storm" in `lang/en.json`.
- [x] 3.2 Thread `corruptionStrength`, `stormActive`, and `corruptionSeed` into `HudPinsContent`
      (new ctor fields, populated in `HudScribePins.Build()`), mirroring existing flags.
- [x] 3.3 In `HudPinsContent.BuildHeader`, select `scribe-hud-title-storm` when `stormActive` else
      `scribe-hud-title`, then corrupt the chosen title string.
- [x] 3.4 Corrupt every other rendered string — task row text, "+N more" footer, timer label and
      countdown — through the Core corruptor with the current strength/seed. Leave icon/texture
      glyphs (chevron, gear, clock) untouched.

## 4. Organic re-randomization

- [x] 4.1 On a recurring tick, while a trigger is active, when `capi.World.ElapsedMilliseconds`
      passes the next scheduled re-scramble, advance `corruptionSeed`, pick a new random interval in
      [0, 5000] ms, and `ForceRebuild()`.
- [x] 4.2 Edge-detect trigger on/off and storm active/inactive transitions to rebuild immediately
      (prompt title swap + first corruption), mirroring the existing `_stormWasActive` pattern.

## 5. Settings toggle

- [x] 5.1 Add a client-local boolean preference (default true) for the storm-corruption effect,
      alongside the other display/behavior prefs; add label + helptext lang keys.
- [x] 5.2 Surface it as a labeled control with helptext in the settings tab (Appearance section),
      writing through immediately.
- [x] 5.3 `HudScribePins` reads the preference in the strength computation and rebuilds on toggle so
      enabling/disabling applies live.

## 6. Verification

- [x] 6.1 Build passes (`build/verify.sh`); Core tests green.
- [ ] 6.2 In-game (restage): trigger a temporal storm — confirm the title reads "Survive the Storm"
      and all HUD text corrupts at roughly the storm-tier intensity; confirm it reverts when the
      storm ends.
- [ ] 6.3 In-game: drive personal stability below 50% without a storm (e.g. rift proximity) — confirm
      corruption ramps up as stability drops toward 10%, and the title stays "Pinned".
- [ ] 6.4 In-game: confirm the re-scramble visibly shifts on a 0–5 s cadence and text layout
      (wrapping rows, title, timer) renders the combining marks without breaking measurement or
      clipping; cap marks-per-char if layout misbehaves.
- [ ] 6.5 In-game: toggle the setting off mid-storm — confirm corruption and title swap stop
      immediately; toggle on — confirm they resume.
- [ ] 6.6 Cross-check against `what-to-test` / TESTING.md and record verdicts.
