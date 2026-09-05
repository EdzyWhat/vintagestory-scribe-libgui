## Context

See `proposal.md` for the "why." Three pieces of prior history and current infrastructure shape
this design directly:

1. **ConfigLib was already tried here and abandoned.** `add-gui-inspect-overlay` added an
   `assets/scribe/config/configlib-patches.json` manifest with an `"integer"`-typed setting
   (`InspectOverlayMode`) that crashed ConfigLib's entire Dear ImGui "Mod Settings" window on
   open — persistently, across relaunches, until the on-disk value was hand-reset
   (`VSAPI-NOTES.md`, "Symptom: adding a setting to `configlib-patches.json` makes ConfigLib's
   ENTIRE 'Mod Settings' window fail to open"; commit `252186c`). The manifest was later stripped
   entirely (`7046929`, `38f9c2f`) and its settings moved onto Scribe's own native
   `ScribePlayerSettings` UI. `src/Mod/lib/configlib.dll` and `Mod.csproj`'s
   `<Reference Include="configlib">` block are dead leftovers from that attempt — no manifest
   file exists today and no C# in this codebase calls into ConfigLib.
2. **ConfigKit's settings window has no ImGui at all** — it's rebuilt on the game's own Cairo GUI
   (`reference/configkit/docs/CHANGES-FROM-CONFIGLIB.md`, "Rebuilt: The settings window"). The
   specific failure mode above cannot occur in ConfigKit's draw path because that path doesn't
   exist.
3. **The engine already gives every mod a zero-dependency local config file.**
   `ScribeModSystem` loads/stores `ScribeGearTuning` and `ScribePlayerSettings` via
   `api.LoadModConfig<T>(filename)` / `capi.StoreModConfig(obj, filename)`
   (`ScribeModSystem.cs:363,366`), which the engine transparently serializes to/from
   `GamePaths.ModConfig` (`<DataPath>/ModConfig/<filename>`) — the exact folder ConfigKit/ConfigLib
   write their own managed files into (per `reference/configkit/docs/CONFIG-FORMAT.md`: "Settings
   are written to `ModConfig/<yourmod>.yaml` (or the `file` you named)"). This is why the two
   settings-worthy subsystems here don't need any ConfigKit API binding: point a `configlib-patches.json`
   manifest's `"file"` field at the same filename Scribe already loads with `LoadModConfig<T>`, and
   ConfigKit (if installed) becomes a GUI for editing a file Scribe already owns.

## Goals / Non-Goals

**Goals:**
- Make the ambient light sampler's and particle emitter's already-acknowledged "still tunable"
  constants editable without a rebuild, with zero new mod dependency (no `<Reference>`, no
  `IsModEnabled` check — this is not a soft dependency, it's a plain file read that a config
  library can optionally also write to).
- Reuse the existing `LoadModConfig<T>`/`StoreModConfig` + POCO pattern
  (`ScribeGearTuning`/`ScribePlayerSettings`) rather than inventing a new config mechanism.
- Preserve every current default exactly, so installing neither ConfigKit nor ConfigLib, and
  never touching the generated file, produces identical behavior to today.

**Non-Goals:**
- Not binding ConfigKit's or ConfigLib's assembly/API. Neither `RegisterManagedConfig` nor
  `GetSetting` is called anywhere; this change works identically whether ConfigKit, ConfigLib,
  both, or neither is installed, because Scribe never asks which one (if any) is present.
- Not touching `worldconfig.json`'s 3 server-admin settings or `ScribePlayerSettings` — see
  proposal.md's Out-of-scope note.
- Not cleaning up the dead `configlib.dll` vendoring in `Mod.csproj`/`src/Mod/lib/` — unrelated to
  this change (it's inert either way) and left for a separate cleanup.
- Not verifying ConfigKit's actual in-game rendering of these 8 settings as part of writing this
  design — that's a task-level verification step (see tasks.md), not a design decision, since the
  point of this design is that Scribe's own behavior doesn't depend on the answer.

## Decisions

**Config-library-agnostic, zero-binding integration via the `file` override.**
`configlib-patches.json` declares `settings` only (no `patches` block) with
`"file": "scribe-visual-tuning.json"`. Per `CONFIG-FORMAT.md`, omitting `patches` is exactly the
documented shape for "a mod that reads the settings itself." Scribe reads that same filename with
`api.LoadModConfig<ScribeVisualTuning>("scribe-visual-tuning.json")` — no different from how it
already reads `scribe-gear-tuning.json`. If ConfigKit or ConfigLib is present, it manages that
file (GUI, live reload, validation); if neither is present, the file either doesn't exist (fall
back to `new ScribeVisualTuning()` defaults) or was hand-edited by the author directly — both
already-supported paths for `LoadModConfig<T>`. Rejected alternative: binding ConfigKit's
`RegisterManagedConfig`/`GetSetting` API (as the server-admin settings would need, if ever
attempted) — unnecessary complexity here since neither subsystem needs live client/server sync;
each client already reads its own local file for its own purely-cosmetic rendering.

**One combined POCO (`ScribeVisualTuning`), not two.** Both subsystems are client-side rendering
tuning with no relationship to each other functionally, but the user's stated motivation for
ConfigKit was "going to ONE place for the changes" — a single file/settings panel covering both,
rather than fragmenting into `scribe-ambient-tuning.json` + `scribe-particle-tuning.json`. Follows
the flat-POCO style of `ScribeGearTuning` (plain public properties, no serialization attributes —
the engine's `LoadModConfig<T>` handles it transparently, confirmed by that file's contents).

**`ScribeAssignmentParticleEmitter` becomes an instance, not a `static class`.** Its 4 tunables
stop being `const`, so they can no longer live on a static class without becoming mutable global
state shared across every caller. Converting to an instance built once (holding a
`ScribeVisualTuning` reference or its 4 relevant fields) and passed to the existing call sites
(`BlockEntityInbox.cs`, `BlockEntityScribeWritingStation.cs`, `ScribeModSystem.Delivery.cs`) is the
smallest change that preserves "one place to read the tuning" without introducing a second static
mutable field pattern. `ScribeAmbientLightSampler` already takes constructor-injected dependencies
(`capi`, `ScribePlayerSettings`) at its one call site (`ScribeDialogBase.cs:469`), so adding
`ScribeVisualTuning` there is consistent with its existing shape — no structural change needed.

**Load once at startup, not live-reloaded.** `ScribeGearTuning`/`ScribePlayerSettings` are also
loaded once (at mod startup / dialog construction) rather than watched for live changes. Matching
that precedent: `ScribeVisualTuning` is loaded once via `api.LoadModConfig<T>` and handed to both
consumers at construction. A config-library GUI edit takes effect on next client restart/relaunch,
same as `ScribeGearTuning` today — not a regression, since neither existing config is live-reloaded
either.

**Property naming: PascalCase C# properties, matching JSON keys via the engine's default
(case-insensitive) binding.** `ScribeGearTuning`'s properties (`SmallGearOverlapX`, etc.) are
PascalCase and the file round-trips correctly today, so the engine's `LoadModConfig<T>`/
`StoreModConfig` already tolerates whatever casing convention is used. The `configlib-patches.json`
manifest's `code` values should still be verified in-game to write JSON keys the POCO actually
binds to (see tasks.md's verification step) rather than assumed from this design.

## Risks / Trade-offs

- **[Risk] Unverified whether ConfigKit's/ConfigLib's `"file"`-override JSON output actually
  round-trips with `LoadModConfig<T>`'s deserialization (key casing, numeric formatting).** →
  Mitigation: tasks.md includes an explicit in-game step — install ConfigKit, change a setting via
  its GUI, confirm `ScribeVisualTuning`'s next load reflects it — before considering the change
  done.
- **[Risk] `ScribeAssignmentParticleEmitter`'s conversion from `static class` to an instance
  touches every existing call site.** → Mitigation: the 4 tunables are the only state added; all
  other methods stay structurally identical, just no longer `static`. Low blast radius, covered by
  existing manual particle-effect playtest steps.
- **[Trade-off] These 8 settings are technically visible/editable by any player who installs
  ConfigKit or ConfigLib, not just the author**, since there is no access gate (no `IsModEnabled`
  check exists to gate on, and gating would require the very API binding this design avoids).
  Accepted: both subsystems are purely cosmetic and client-local, so a curious player tuning their
  own particle field or ambient tint harms no one but themselves — same risk profile as
  `ScribePlayerSettings`' existing player-facing knobs.
- **[Trade-off] No live reload.** A config-library edit needs a relaunch to take effect, same
  limitation `ScribeGearTuning`/`ScribePlayerSettings` already have. Acceptable for author-tuning
  use (not a moment-to-moment adjustment workflow); revisit only if that changes.

## Migration Plan

1. Add `ScribeVisualTuning` (POCO, 8 fields, defaults matching current constants) and a
   `VisualTuningConfigFileName` constant alongside the existing Hud/GearTuning ones in
   `ScribeModSystem.cs`.
2. Load it once at startup (`api.LoadModConfig<ScribeVisualTuning>(...) ?? new ScribeVisualTuning()`),
   matching the existing Hud/GearTuning load call shape.
3. Convert `ScribeAssignmentParticleEmitter` to an instance holding the 4 particle tunables; update
   its 3 call sites. Add the `ScribeVisualTuning` parameter to `ScribeAmbientLightSampler`'s
   existing constructor; remove the 4 `const` fields it replaces.
4. Add `assets/scribe/config/configlib-patches.json` declaring all 8 settings, `"file":
   "scribe-visual-tuning.json"`, no `patches` block.
5. In-game verification: confirm defaults produce unchanged behavior with the manifest present but
   neither config library installed; then (if ConfigKit is available to install locally) confirm a
   GUI-driven edit round-trips into `ScribeVisualTuning` on next load.

No player-facing migration is needed: every setting keeps its existing default, and nothing is
removed from any existing surface.
