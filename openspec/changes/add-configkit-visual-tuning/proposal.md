## Why

A handful of client-side visual/behavior constants — the ambient light sampler's quantization
and smoothing tuning (`ScribeAmbientLightSampler`) and the unseen-assignment ambient particle
effect's detection radius, color-mix, and density knobs (`ScribeAssignmentParticleEmitter`) —
are `const` fields the author has already been hand-tuning repeatedly across playtests (per their
own doc-comments: "still playtest-tunable, not final", "active tuning knob"). Today each tweak
means editing C#, rebuilding, and relaunching. ConfigLib was already tried in this codebase for
exactly this kind of author-facing tuning and abandoned: registering an `"integer"`-typed setting
made ConfigLib's Dear ImGui settings window fail to open entirely, persisting across relaunches
(commit `252186c`, `VSAPI-NOTES.md`) — a symptom of ImGui's broader unreliability on this
project's Apple Silicon dev machine. ConfigKit rebuilds that settings screen on the game's own
Cairo GUI with no ImGui at all, removing the failure mode that killed the previous attempt, so a
real in-game tuning surface is viable again.

## What Changes

- Add a `ScribeVisualTuning` POCO (8 fields: `BrightnessSteps`, `HueSteps`, `TintStrength`,
  `SmoothingTau` from the ambient light sampler; `DetectionRadius`, `RainbowRatio`,
  `CountMultiplier`, `SeedBurstMultiplier` from the particle emitter), loaded/stored via the
  engine's existing `LoadModConfig<T>`/`StoreModConfig` to `ModConfig/scribe-visual-tuning.json`
  — the exact same mechanism already used for `scribe-gear-tuning.json`/`scribe-hud-config.json`.
  Defaults match today's hardcoded values exactly, so behavior is unchanged until the file (or a
  config-library GUI writing to it) sets a different value.
- Ship `assets/scribe/config/configlib-patches.json` declaring these 8 settings with a `"file":
  "scribe-visual-tuning.json"` override and no `patches` block (Scribe reads the value itself).
  This is the same format ConfigLib and ConfigKit both read — **no assembly reference, no
  `IsModEnabled` check, no new mod dependency of any kind.** If neither is installed, Scribe reads
  its own file directly (or falls back to the code defaults if it doesn't exist yet); if either is
  installed, the player/author gets a real settings screen for free, editing the same file Scribe
  already reads.
- `ScribeAmbientLightSampler` and `ScribeAssignmentParticleEmitter` take their 8 constants as
  constructor/parameter inputs sourced from `ScribeVisualTuning` instead of `const` fields.
  `ScribeAssignmentParticleEmitter` becomes an instance (constructed once, holding the tuning
  values) rather than a `static class`, since its values are no longer compile-time constants.
- These are author-facing tuning knobs, not a new player feature: not documented in the handbook,
  not surfaced in Scribe Settings. Any player who installs ConfigKit/ConfigLib can technically see
  and edit them (harmless — purely cosmetic, client-side-only rendering behavior with no server
  authority or cross-player sync involved), but they are not advertised.
- Out of scope: the 3 existing `worldconfig.json` server-admin settings (`scribeDeliveryMode`,
  `scribeDeliveryRadius`, `scribeClockmakerRequiresTrait`) are untouched — they're genuinely
  server-authoritative, already live-tunable via `/worldconfig set`, and rely on `World.Config`'s
  free automatic client/server sync, which this zero-dependency approach can't replicate without
  either breaking that sync or requiring a full ConfigKit API binding. `ScribePlayerSettings`
  (HUD rows, font scale, illumination floor, alarm volume) is also untouched — those are
  player-facing preferences with their own tailored Settings-dialog UI already.

## Capabilities

### New Capabilities
- `visual-tuning-config`: the `ScribeVisualTuning` config surface itself — the 8 exposed knobs,
  the `LoadModConfig`/file-based read path, defaulting behavior when the file or a key is absent,
  and the config-library-agnostic manifest that lets ConfigKit or ConfigLib (or a hand-edited
  file) drive it with zero mod dependency.

### Modified Capabilities
- `gui-ambient-illumination`: the smoothing requirement currently states the transition interval
  as a fixed "~400ms"; this becomes a configurable default (`SmoothingTau`), not a spec-fixed
  constant.
- `inbox-tab`: the ambient-particle requirement currently states a fixed "12 blocks" detection
  range and a fixed "0.6×" density multiplier; these become configurable defaults
  (`DetectionRadius`, `CountMultiplier`), not spec-fixed constants.

## Impact

- New: `src/Mod/ScribeVisualTuning.cs` (POCO), `assets/scribe/config/configlib-patches.json`
  (manifest).
- Modified: `src/Mod/ScribeAmbientLightSampler.cs`, `src/Mod/ScribeAssignmentParticleEmitter.cs`
  (constants → instance fields sourced from `ScribeVisualTuning`), and their call sites
  (`ScribeDialogBase.cs:469`; `BlockEntityInbox.cs`, `BlockEntityScribeWritingStation.cs`,
  `ScribeModSystem.Delivery.cs` for the particle emitter's now-instance `SpawnAt`).
  `ScribeModSystem.cs`/`ScribeModSystem.ClientPrefs.cs` gain a `VisualTuningConfigFileName`
  constant and load/store calls alongside the existing Hud/GearTuning ones.
- No changes to `Mod.csproj`, `src/Mod/lib/`, `worldconfig.json`, `ScribePlayerSettings`, or
  `CLAUDE.md`'s dependency guardrail — this integration adds no assembly reference and no soft
  dependency of any kind.
- `src/Core/` is unaffected: both subsystems already live in `src/Mod/` and are pure
  client-rendering code.
