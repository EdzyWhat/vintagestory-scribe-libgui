## Context

The pinned-task HUD (`HudScribePins`, `src/Mod/HudScribePins.cs`) is built on LibGUI's retained-mode
widget framework — text is drawn by `Gui.Widgets.Basic.Text` widgets styled by a `TextStyle` struct
via Skia, with no `CairoFont` and no direct glyph shader anywhere in `src/`. This change makes the
HUD react to temporal instability. Two research findings from decompiling the shipped DLLs are the
foundation:

1. **Vanilla's "crazed" storm text is not a shader.** It is Zalgo-style Unicode combining-mark
   injection into the string content, done by
   `EntityBehaviorTemporalStabilityAffected.destabilizeText(string, float)` (VSSurvivalMod.dll,
   `private`). It walks the string and, per character, with probability = strength, appends one
   random mark from a fixed 23-element array of combining code points (U+0300-range). It renders
   through the ordinary font path. The GUI shader `gui.vsh` contains no warp include, so geometric
   wobble is impossible on the HUD without writing a custom GUI shader — explicitly out of scope.

2. **Storm/stability state is fully public and already read server-side.** The mod already resolves
   `SystemTemporalStability` in `ScribeModSystem.OnStormTick` (server). Client-side reads:
   - Storm active + tier: `capi.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData` →
     `.nowStormActive` (bool) and `.nextStormStrength` (`EnumTempStormStrength` Light/Medium/Heavy).
     `StormData` is synced to the client on the `"temporalstability"` channel.
   - Personal stability: `capi.World.Player.Entity.WatchedAttributes.GetDouble("temporalStability",
     1.0)` — the 0..1 value backing the behavior's public `OwnStability`.

The HUD already has `capi` (via `GuiBase`), a per-frame `OnRenderGUI(float)` hook, a 250 ms
`OnTick`, and a 1 Hz `OnTimerDisplayTick` that calls `ForceRebuild()`; `ForceRebuild()` fully
recreates the widget tree, so smooth per-frame animation uses self-ticking `StatefulWidget` wrappers
(e.g. the existing `ScribeFadeText`). The title string is chosen in exactly one place —
`HudPinsContent.BuildHeader` → `Lang.Get("scribe:scribe-hud-title")`.

## Goals / Non-Goals

**Goals:**
- Corrupt all HUD text via combining-mark injection, faithful to vanilla, on two triggers.
- Variable strength: storm tier → vanilla glitch strengths (≈0.53 / 0.67 / 0.90); low stability →
  linear ramp 0.0 at 0.50 to 1.0 at 0.10; effective = max of the two.
- Storm-only title swap to "Survive the Storm", steady for the storm.
- Organic re-randomization on a 0–5 s cadence.
- A client-local settings toggle (default on) that fully disables the effect.
- Keep the corruption logic in `src/Core` as a pure, unit-tested helper.

**Non-Goals:**
- No screen/world shader wobble on the HUD (vanilla doesn't do it; `gui.vsh` has no warp).
- No server changes and no new network messages — all reads are client-local synced state.
- No change to pinned-task data or ordering; corruption is presentation-only.
- Not calling vanilla's `destabilizeText` (it's `private`) — we reproduce it.

## Decisions

**1. Corruption logic lives in `src/Core` as a pure helper.**
Add e.g. `ScribeTextCorruptor` (or a static method) in `src/Core` that takes `(string text, double
strength, int seed)` and returns the mark-injected string, reproducing vanilla's algorithm (per-char
probability = strength, random pick from the fixed combining-mark set). Passing an explicit seed
keeps it deterministic and unit-testable with no VS API and no reliance on `Math.Random`. _Rationale:_
honors the Core/Mod split (`src/Core` must not reference the VS API) and makes the "0 strength = no
change", "1.0 = every char marked", and stability-across-a-fixed-seed behaviors testable in xUnit.
_Alternative rejected:_ implementing it in the Mod layer — untestable without a game install.

**2. Compute an effective strength each refresh in `HudScribePins`.**
A private helper reads the two triggers off `capi` and returns `(double strength, bool stormActive)`:
- `stormStrength` from `nextStormStrength` when `nowStormActive`: Light→0.53, Medium→0.67, Heavy→0.90
  (constants; not scaled). 0 when no storm.
- `stabilityStrength = Clamp((0.50 - stability) / (0.50 - 0.10), 0, 1)`.
- `effective = Max(stormStrength, stabilityStrength)`; `stormActive` drives only the title swap.
Resolve `SystemTemporalStability` once and null-check it (graceful no-op when absent).

**3. Thread the signal into `HudPinsContent`, corrupt at render.**
Add ctor fields to `HudPinsContent` (mirroring the existing `collapsed`/`leftAligned` flags),
populated in `HudScribePins.Build()`: `corruptionStrength`, `stormActive`, and a `corruptionSeed`.
Every user-visible string built in `HudPinsContent` (title, row text, "+N more", timer label +
countdown) is passed through the Core corruptor with the current strength/seed before being handed
to its `Text` widget. Title selection in `BuildHeader` chooses `scribe-hud-title-storm` when
`stormActive`, else `scribe-hud-title`, then corrupts the chosen string. _Trade-off:_ corrupting at
build time (not per frame) means the marks only change on rebuild — which is exactly what the 0–5 s
re-randomization drives (decision 4). Icons/textures (chevron glyph, gear) are left untouched.

**4. Re-randomization via a randomized-interval rebuild, edge-detected.**
Reuse the existing tick machinery rather than adding per-frame work. On a recurring tick, when a
trigger is active, if the current time has passed the next scheduled re-scramble, advance
`corruptionSeed`, pick a new random interval in [0 s, 5 s], and `ForceRebuild()`. Also edge-detect
trigger on/off and storm active/inactive transitions to rebuild immediately (so the title swap and
first corruption are prompt). _Rationale:_ matches the established `OnTimerDisplayTick`
`ForceRebuild()` pattern and the `_stormWasActive` edge-detect already in `OnStormTick`. Since
`Date.now`-style calls are fine in the Mod layer (unlike Core), use `capi.World.ElapsedMilliseconds`
for scheduling. _Alternative considered:_ a self-ticking `StatefulWidget` corruptor per text element
(like `ScribeFadeText`) — cleaner animation but more surface; deferred unless per-frame shimmer is
wanted later. The 0–5 s cadence does not need frame-accurate animation.

**5. Settings toggle: client-local, follows the existing preference pattern.**
Add one boolean preference (default true) alongside the other client-local display/behavior prefs,
surfaced in the settings tab with a label + helptext lang key, writing through immediately (matching
"Setting a control writes through immediately with live preview"). `HudScribePins` reads it in the
strength computation: when off, force `effective = 0` and `stormActive = false` so both corruption
and title swap are suppressed, and rebuild on the toggle so it applies live.

**6. Strength constants sourced from vanilla, not invented.**
The Light/Medium/Heavy values (≈0.53 / 0.67 / 0.90) are vanilla's own `stormGlitchStrength` bases
from `SystemTemporalStability` (confirmed by decompilation), chosen (user-directed) so a storm's HUD
corruption reads at the same intensity the game's own chat corruption would.

## Risks / Trade-offs

- [Combining marks could break LibGUI/Skia text layout — width measurement, wrapping, or the glyph
  atlas may mishandle stacked diacritics] → Verify in-game across title, wrapping row text, and the
  timer; if layout misbehaves, cap the marks-per-character or restrict to a subset that renders
  cleanly. This is the primary feasibility unknown and gets an explicit test task.
- [`ForceRebuild()` on the re-scramble cadence adds churn] → Cadence is 0–5 s and only while a
  trigger is active; negligible next to the existing 1 Hz timer rebuild. Confirm no interaction with
  the timer rebuild or collapse animation.
- [Reading `WatchedAttributes` / `SystemTemporalStability` on a non-survival server] → Null-check the
  system and default stability to 1.0; effect simply never triggers.
- [Corruption reduces legibility by design] → Mitigated by the default-on toggle; the title swap
  keeps the storm message readable enough at lower strengths, and low-stability starts near 0.
- [Fixed combining-mark set may look wrong in the bundled fonts] → Reuse vanilla's exact code-point
  set (it renders fine in the game's font pipeline); verify against Scribe's bundled fonts.

## Migration Plan

Purely additive and presentation-only. No data migration, no network/protocol change, no server
change. The new setting defaults on; rollback is reverting the change. No stored-state compatibility
concern.

## Open Questions

- Does LibGUI's Skia text path render stacked combining marks faithfully, or does it need a
  per-character mark cap? (Resolved by the in-game verification task.)
- Exact placement/label of the new settings control within the Appearance section — finalize when
  wiring the control.
