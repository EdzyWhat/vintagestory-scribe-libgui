## 1. ScribeVisualTuning config surface

- [x] 1.1 Add `src/Mod/ScribeVisualTuning.cs`: a plain POCO (no serialization attributes, matching
  `ScribeGearTuning`'s style) with 8 public properties — `BrightnessSteps` (int, default 32),
  `HueSteps` (int, default 16), `TintStrength` (float, default `2f/3f`), `SmoothingTau` (float,
  default `0.2f`), `DetectionRadius` (double, default `12.0`), `RainbowRatio` (float, default
  `0.5f`), `CountMultiplier` (float, default `0.6f`), `SeedBurstMultiplier` (float, default
  `3.5f`) — and verify it compiles with `dotnet build`.
- [x] 1.2 Add a `VisualTuningConfigFileName = "scribe-visual-tuning.json"` constant next to the
  existing `HudConfigFileName`/`GearTuningConfigFileName` in `ScribeModSystem.cs`, and load it once
  at startup via `api.LoadModConfig<ScribeVisualTuning>(VisualTuningConfigFileName) ?? new
  ScribeVisualTuning()`, matching the existing Hud/GearTuning load call shape. Verify by logging
  (or breakpointing) the loaded defaults on a fresh client with no config file present and
  confirming they match the constants above.

## 2. Wire tuning into the two consumers

- [x] 2.1 Add a `ScribeVisualTuning` parameter to `ScribeAmbientLightSampler`'s constructor,
  replacing its 4 `const` fields (`BrightnessSteps`, `HueSteps`, `TintStrength`, `SmoothingTau`)
  with instance fields read from it. Update its one call site (`ScribeDialogBase.cs:469`). Verify
  `dotnet build` succeeds and the ambient-illumination playtest steps (brightness/hue
  smoothing) still behave identically with default tuning values.
- [x] 2.2 Convert `ScribeAssignmentParticleEmitter` from a `static class` to an instance holding
  the 4 particle tunables (`DetectionRadius`, `RainbowRatio`, `CountMultiplier`,
  `SeedBurstMultiplier`) sourced from `ScribeVisualTuning`, keeping `SpawnAt`'s existing signatures
  and logic otherwise unchanged. Update its 3 call sites (`BlockEntityInbox.cs`,
  `BlockEntityScribeWritingStation.cs`, `ScribeModSystem.Delivery.cs`) to use the shared instance.
  Verify `dotnet build` succeeds and the unseen-assignment ambient particle effect still spawns
  in-game at an Inbox-capable block with default tuning values.

## 3. Config-library manifest

- [x] 3.1 Add `assets/scribe/config/configlib-patches.json` declaring all 8 settings (grouped by
  `integer`/`float` type, per `CONFIG-FORMAT.md`'s object form) with `"file":
  "scribe-visual-tuning.json"` and no `patches` block. Verify the JSON is well-formed (`python3 -m
  json.tool` or equivalent) and that its `code` values exactly match `ScribeVisualTuning`'s
  property names.
- [ ] 3.2 With no config-library mod installed, confirm in-game that the mod behaves identically
  to before this change (ambient illumination smoothing and particle detection/density unchanged)
  — verifies the manifest's mere presence has no effect without a config library.

## 4. In-game config-library verification

- [ ] 4.1 Install ConfigKit locally (already unpacked at
  `reference/ConfigKitInvestigations/configkit_1.2.0/`) and confirm its settings screen renders
  all 8 values with no crash (the specific failure mode that broke ConfigLib's ImGui panel for an
  integer setting).
- [ ] 4.2 Change one integer setting (e.g. `DetectionRadius` or `BrightnessSteps`) and one float
  setting via ConfigKit's GUI, restart the client, and confirm `ScribeVisualTuning`'s next load
  reflects the written value (observable via the corresponding rendering behavior — e.g. a changed
  particle detection range).
