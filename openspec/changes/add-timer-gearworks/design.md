# Design — add-timer-gearworks

## Context

The Clockmaker's Notebook Timer tab (`GuiDialogClockmakerNotebook.BuildTimerContent`) currently shows a set-timer form (Idle), a self-ticking `ScribeCountdownText` + Stop (Running), or a blinking `00:00` + Stop (Fired). We want an ambient clockwork **gear-train** above that region — a thematic "peek into the mechanism" — running continuously and decoupled from timer state, with a fire→shudder→lock reaction. See `proposal.md` for motivation and `specs/timer-gearworks/spec.md` for the observable requirements.

Three research digs (decompiling the game DLLs, the `gui` LibGUI source, and the Gearlock Firearms mod) established the constraints this design rests on:

- **The real `game:gear-temporal` item is an irregular clockwork *lattice*, not a toothed cog.** Its vanilla spin is a *continuous* rotation about its own axis (`ItemTemporalGear.InGuiIdle`: `Rotation.Y = (elapsedMs/50) mod 360`). It cannot visually *mesh* with neighbors or *tick per tooth*.
- **LibGUI *can* render the real 3D item** cheaply — `ItemStackDisplay` + `ItemStackRenderer` (an `IPreSkiaRenderer` in the `gui` dep) composite a real `ItemStack` into the Skia canvas via an offscreen FBO, macOS-safe. This was evaluated and **set aside**: it renders the wrong shape with the wrong motion for an interlocking, ticking clockwork.
- **Gearlock Firearms renders its "gear system" as flat 2D texture quads** rotated with a GL matrix in `OnRenderGUI` (`Render2DTexture` + `GlPushMatrix/GlRotate/GlTranslate`), faking mesh via one driver scalar × per-gear `(sign, ratio)` constants and hand-tuned positioning. Its author (**JeanPierre**, already credited in this mod for Wanderer's Sketchbook) licenses the assets for reuse/modification.
- **The mod already has the primitives** for a Skia-native version: `AnimatedRotation` (crash-safe rotation — `ScribeTimerIcon` uses it precisely to dodge a "zero-size Skia matrix NaN/GPU crash"), self-ticking `StatefulWidget`s (`ScribeCountdownText`, `ScribeBlinkText`), an in-GUI glow precedent (`CuneiformGlow` / `cuneiform-contrast-glow`), and the self-load-bitmap pattern for post-startup PNGs (VSAPI-NOTES §LibGUI: `Image`/`SkiaAssetLoader` silently fail after startup).

## Goals / Non-Goals

**Goals:**
- An ambient, always-running, interlocking toothed gear-train on the Timer tab, decoupled from timer state.
- Spring-wound **per-tooth tick** motion; adjacent gears counter-rotate and stay meshed.
- Temporal-gear **material identity**: teal texture + emissive glow.
- **Behind-glass** framing.
- **Fire → shudder → lock**, resuming on clear/reset.
- Stay entirely in the view layer: no Core/codec/network/model change; no new *code* dependency.
- Ship in two milestones: **M1 gears working** first, **M2 glass + fire-shudder + tuning** after.

**Non-Goals:**
- Rendering the real 3D `game:gear-temporal` item (wrong shape/motion — see Context).
- A HUD gear (Timer-tab-only this change).
- Any change to timer countdown/fire/clear semantics (`timer-lifecycle` is observed, not modified).
- Sound design for the tick/shudder (a possible later nicety; out of scope here).

## Decisions

### D1 — Authored 2D toothed gears, inspired by the temporal gear (not the real item)

Build authored round toothed gears rather than rendering `game:gear-temporal`. **Why:** the real item is a lattice that can neither mesh nor tick, and the whole point is an interlocking clockwork. **Alternatives considered:** (a) real 3D via `ItemStackDisplay` — faithful to the item but not to the *mechanism* the user wants; (b) hybrid real-hub + authored-teeth — deferred as more art + code for marginal gain. Authored gears also restore full control over tick, mesh, easing, and shudder.

Start from **Gearlock's gear geometry** (permitted reuse), re-skinned to the temporal gear's teal `temporalgear.png` material (mirrored in-repo as `reference-temporalgear.png`). Primary gear follows the temporal gear's apparent **12 teeth → 30°/tooth**; secondary gear(s) sized relative to it.

### D2 — Render in the LibGUI Skia widget tree, not raw GL

Implement the gears as **Skia widgets**: a self-loaded gear PNG in a `Container { BoxStyle.Texture = bmp }`, wrapped in `AnimatedRotation`, composed with `Stack` + `Positioned` for the meshed layout, tinted/clipped for glow and glass. **Why over raw GL:** it stays in-framework, is macOS-safe, and reuses the exact rotation primitive `ScribeTimerIcon` already relies on — the mod currently makes **zero** raw-`capi.Render` calls, and this avoids introducing the first hand-rolled GL surface to maintain. **Alternative (documented fallback):** the Gearlock raw-GL pattern (`Render2DTexture` + `GlRotate` inside `ScribeDialogBase.OnRenderGUI`). Only adopt it if the Skia path shows a visual/perf problem a spike can't resolve.

### D3 — Mesh via a single driver, per-gear (sign, ratio) — the Gearlock trick

Drive the whole train from **one monotonic phase** (see D4). Each gear's angle = `direction × ratio × phase`, where `ratio = referenceTeeth / thisGearTeeth` and `direction` alternates down the chain. Positions are hand-tuned (like Gearlock's `+82,-82` offset) so painted teeth interlock. No physics/teeth-collision solver — the mesh is a faked constant, tuned in-game.

### D4 — Tick derived from a monotonic clock, spring-settled by AnimatedRotation (rebuild-safe)

The discrete tick's **phase is derived from `capi.World.ElapsedMilliseconds`**, not accumulated in widget state: `toothIndex = floor(elapsedMs / tickPeriodMs)`, and each gear targets `toothIndex × stepAngle × ratio`. `AnimatedRotation` then tweens toward each new target with a snap-settle **curve**, giving the spring-wound feel. **Why derive from a monotonic clock:** the Timer tab calls `ForceRebuild` on status transitions (`RefreshTimerView`), which remounts the subtree; a phase accumulated in `State` could reset and make the gears jump. A monotonic-derived target is **rebuild-stable** — the gears resume at the correct angle no matter how often the tab rebuilds. The widget is still a self-ticking `StatefulWidget` (a lightweight game-tick listener calls `SetState` to recompute `toothIndex`), following the `ScribeCountdownText`/`ScribeTimerIcon` precedent so it repaints without the host rebuilding each frame.

**Easing:** trial `EaseOutBack`, `EaseOutElastic`, and `EaseInOutBack` in-game during M1 and pick the best spring-wound feel; default to `EaseOutBack` (the same family as `ScribeTimerIcon`'s `EaseInOutBack`). Prefer `AnimatedRotation` over raw `Transform.Rotate` to avoid the documented zero-size Skia-matrix NaN crash.

### D5 — Emissive glow as a Skia effect (reuse CuneiformGlow), not the game glow

The game's per-face `glow: 35` + world `lightHsv` only apply to real 3D meshes, which we're not using. Fake emissive in Skia: an **additive/brightened draw + a soft halo** (blur behind the gear), following the `CuneiformGlow` precedent already shipping in this mod's GUI. Cache the glow layer / wrap static neighbors in `RepaintBoundary` so per-frame rotation doesn't re-blur the whole subtree.

### D6 — Fire → shudder → lock via observing TimerStatus

The gearworks widget observes `modSystem.MyTimer?.Status` (and subscribes to `MyTimerChanged`, like the dialog already does). On entering **Fired**: run a brief **shudder** (a few rapid low-amplitude oscillations via `AnimatedRotation`) then set a `_locked` flag that freezes `toothIndex` advance. On leaving Fired (cleared / auto-disappeared / new timer): clear `_locked` and resume. This is the *only* coupling between timer state and the gears; the continuous tick is otherwise state-independent (D4).

### D7 — Layout: gears occupy their own region above the content, non-interactive

The gear-train sits in a **dedicated vertical region above** the form/countdown (not overlaying the controls), so it structurally cannot swallow control input. The glass overlay and gears are **non-hit-testing** paint layers (drawn, not interactive). Positioning is explicitly **draft/movable** (user's note) — final placement and sizing are tuned in M2.

## Risks / Trade-offs

- **`ForceRebuild` resets animation phase** → Mitigated by D4 (monotonic-derived target; phase never lives only in `State`).
- **Zero-size Skia matrix NaN/GPU crash at first layout** (documented for `Transform.Rotate`) → Use `AnimatedRotation`; guard against zero-size before rotating.
- **Self-loaded PNG fails post-startup** (`Image`/`SkiaAssetLoader` silently no-op) → Use `capi.Assets.TryGet(loc, loadAsset:true)` → `SKBitmap.Decode`, load once and cache; render via `Container { BoxStyle.Texture }`.
- **Faked mesh looks wrong (teeth drift/overlap/gap)** → Hand-tune ratios + offsets in-game (Gearlock precedent); accept that this is visual tuning, not simulation.
- **Glow overdraw / per-frame blur cost** → Small textures, cache the glow layer, `RepaintBoundary` around static parts; a few small rasters per frame is negligible (Gearlock ships heavier).
- **Glass overlay swallowing clicks** → D7 keeps gears/glass out of the controls' layout band and non-hit-testing; verify against the spec's "controls remain fully usable" scenario.
- **Asset-licensing/attribution** → Record JeanPierre/Gearlock reuse permission and extend CREDITS before shipping; assets are copied/derived, never a runtime dependency.

## Migration Plan

Purely additive **view-layer** feature — no savegame, codec, network, or Core change, so **no data migration** and no save-compat gate. Rollback = revert the widget insertion in `BuildTimerContent`, the new gearworks widget file, and the added assets; nothing else references them. Ship behind the normal build; verify in-game via the Timer tab (idle/running/fired) on macOS.

## Open Questions

- **Gear count & sizes** (2 vs 3 gears; diameters/tooth counts of the secondaries) — trial in M1.
- **Final easing curve** among the three candidates — trial in M1.
- **Glass rendering**: authored frame texture vs. a pure Skia-drawn frame + translucent fill — decide in M2.
- **Should the shudder (or each tick) play a subtle sound?** Out of scope now; note as a possible follow-on nicety.
- **Exact temporal-teal recolor** of the Gearlock gear texture — art pass in M1 (reuse `reference-temporalgear.png` palette).
