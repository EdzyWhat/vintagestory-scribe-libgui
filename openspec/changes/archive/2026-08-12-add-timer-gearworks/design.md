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

### D8 — Escape wheel: always visible, procedural, steel; engages via a rebuild-stable slide (playtest-2)

The escape wheel (the large regulator behind the temporal pair) is **always rendered** regardless of timer state — never latched in from hidden. This is the fix for the first playtest's "wheel disappears then re-slides + half-tick" fire glitch: `RefreshTimerView` calls `ForceRebuild` on every status transition, and every stock `Animated*` widget **snaps** on a fresh mount (it only tweens across a *reconcile* — VSAPI-NOTES §LibGUI), so a hidden→visible latch re-fired on each remount. An always-on wheel just repaints in place. Motion, not presence, tracks state: stationary at a resting angle when Idle, turning with the monotonic clock when Running, frozen-in-place + shudder when Fired.

The wheel is generated procedurally (`ScribeGearTexture.GreatWheel`, pure SkiaSharp, deterministic seed, cached/disposed on the backdrop-cache path) in the small gear's blocky/uneven/negative-space spirit but a **metallic steel** palette (sampled from the small gear ≈ `#605050`/`#606050`/`#505040`), **flat filled with no strokes**, and **many small teeth** (proportional to the small gear so they mesh cleanly). It turns **opposite** the primary gear. Tooth count is a single constant shared between the generator and `EscapeTeeth` so one tick = one tooth.

**Start-slide (D8a):** the Idle→Running slide-into-place must survive the `ForceRebuild` remount, so — like the rotation tick (D4) — its progress is derived from a **monotonic timestamp captured at the transition** (or a host-owned resumed controller à la the collapse/create registry), NOT a `State` flag that resets on remount. A plain `AnimatedSlide` gated on a bool snaps for the same reason the old latch did.

### D9 — Uniform scale from Pixel Art Size, pegged at 540 = 100% (playtest-2)

Every gearworks dimension is multiplied by `PixelArtSize / 540f` (`ScribePlayerSettings.PixelArtSize`, 300–1000, default 600), so 540 renders the reference sizing and the mechanism grows/shrinks proportionally with the setting. This replaces the previous ad-hoc `scale` (font-scale-derived) for the gearworks specifically — the gears are pixel-art-like ornament, so they track the pixel-art knob, not the text knob.

### D10 — Remove the residual fade with ScribeResetPaintColor (playtest-2)

All gear components render washed-out because a textured `Container` paints via `DrawMaskedBox`, the one LibGUI op that reuses `SharedPaint.Color` **without setting it** — so the bitmaps are modulated by whatever (often semi-transparent) color the previous frame's last op left. Wrap the gearworks subtree in `ScribeResetPaintColor` (sets the shared color to opaque white immediately before the children paint), the identical fix the dialog backdrops already use.

### D11 — Phase-align the tick to the real second (playtest-2)

One tooth-step should land per real second, beginning on the second boundary. The idle dwell + the two-part snap/settle rotation should sum to exactly `TickPeriodMs = 1000`. Reduce the perceived phase error from the 50 ms poll by phase-aligning to `ElapsedMilliseconds % TickPeriodMs` rather than only polling for an index change (the index derivation is already rebuild-stable per D4; this tightens *when* within the second the snap begins).

### D12 — Material identity narrows to the ONE teal gear; glow is exclusive to it (playtest-3)

Supersedes D1/D5's implicit "all gears are temporal/teal + glowing." Only the single `gear-temporal-large.png`
(already teal) carries the temporal-gear identity: it is the **only** teal element and the **only** glowing
one. The two flanking small gears and the procedural escape wheel are plain **steel** regulators and stay
unlit. **Why:** the author wants the temporal gear to read as the special, energised heart of a mechanism
built from ordinary steel cogs — a uniform teal-glow field flattens that contrast. The glow reuses the
`CuneiformGlowMask.ForSigma` cached-blur primitive (D5's Skia-halo approach) but painted behind that one gear
only, in a small paint-wrapper widget; the mask is cached and never disposed per-frame (CuneiformGlow
discipline).

### D13 — Depth via an offset dark silhouette twin, not BoxStyle.BoxShadows (playtest-3)

For cheap 3D depth on every gear (in ADDITION to D12's teal glow, not instead of it), draw an offset, dark,
semi-transparent **duplicate of each gear's own texture** directly beneath it in the `Stack`. **Why not
`BoxStyle.BoxShadows`:** that primitive shadows the widget's square box rect, so a round gear would cast a
rectangular shadow. Painting the gear's own bitmap again — shifted a few px down-right and modulated to a
dark translucent color (a `ScribeTintBox`, the colored twin of `ScribeResetPaintColor` that forces
`SharedPaint.Color` instead of resetting it to white) — casts a silhouette-accurate shadow for one extra
textured draw per gear. All shadow draws sit **inside** the existing `HardEdge` clip, so nothing spills the
region box (a hard requirement from the author).

### D14 — Escape wheel retracts to idle on fire, reusing the slide mechanism (playtest-3)

On Fired the escape (great) wheel **slides back out** to its resting/idle peek, the mirror of the Start
slide-in (D8a). Same rebuild-stable mechanism: the retract progress is derived from the host-owned
`_fireLockMs` timestamp (which survives the `ForceRebuild` remount), not a `State` flag. Engage interpolates
peek resting→live over `EngageStartMs`; fire interpolates peek live→resting over `FireLockMs`. This is a
**position/translation** change only — orthogonal to the rotation **lock** (angle still freezes at the fire
instant, D6/5.1) and the **shudder**; all three play together when the timer completes, so the mechanism
visibly disengages and withdraws as it stops.

### D15 — Symmetric layout: centered teal driver flanked by two steel gears, dropped low (playtest-3)

Restructure the pair layout (was teal-left + one small-right) to a **symmetric** train: the teal temporal gear
is horizontally **centered** and acts as the driver, with **two** small steel gears meshing against it — one
on each side, counter-rotating. The teal gear sits **lower** than the two smalls so that, as it and the escape
wheel slide into their live positions, the teal gear dramatically **overlaps** the great wheel behind it.
**Why:** symmetry reads as a deliberate, balanced mechanism rather than a lopsided pair, and the low-centered
driver makes the slide-in overlap the visual payoff of Start. Positions remain hand-tuned Skia constants (D3),
all scaled by `PixelArtSize/540` (D9).

### D16 — Per-gear paint-state reset makes every gear fully opaque (playtest-4)

The gears looked semi-transparent even though the PNG bodies are fully opaque (alpha 255, measured). Reading
the vendored `Gui.dll`: `PaintingContext.DrawMaskedBox` — the path a textured `Container` (a gear) takes —
does `DrawBitmap(texture, SharedPaint)` and sets only `FilterQuality`; it never sets `Color`, `ColorFilter`,
or `BlendMode`. So each gear is modulated by whatever the PREVIOUS draw op left on the one shared `SKPaint`
(`DrawBox` sets the shared colour to its fill and never restores it; the diagnostic-border Container and the
shadow/glow `ScribeGearEffect`s are exactly such ops). The single top-level `ScribeResetPaintColor` (D10)
can't fix this because many ops paint between it and each gear. **Decision:** reset the shared paint to opaque
white and clear `ColorFilter`/`ImageFilter` (restoring an opaque `BlendMode`) **immediately before EACH gear's
own paint** — either by extending `ScribeResetPaintColor` to also clear the filters or by wrapping every plain
(non-effect) gear in it. This supersedes relying on the one outer reset for the gearworks. (The `SrcIn` tint
in `ScribeGearEffect` deliberately still sets a colour filter — that's the shadow/glow silhouette; only the
PLAIN gears must paint through a cleared paint.)

### D17 — Depth tuning: sharper shadows, lighter on steel gears (playtest-4)

Shadow blur and offset are halved (`ShadowBlur` 2→1, `ShadowOffset` 3→1.5 px, both still ×scale) so the cast
shadow reads as a crisp near-contact silhouette rather than a soft smear. Shadow opacity is now **per-gear**:
the teal temporal gear keeps the original alpha (0x70), the three steel gears (two smalls + escape wheel) drop
to 2/3 (~0x4B) — the temporal gear is the visual focus and earns a heavier shadow. Requires two shadow tints
rather than one shared constant.

### D18 — Remove the temporal gear's outer glow (playtest-4)

Reverses the glow half of D12 (task 6.1): the Skia halo behind the teal gear didn't read well in-game, so the
GLOW `ScribeGearEffect` layer is removed. The temporal gear's identity now rests on its teal texture + the
(retained) cast-shadow depth alone. `ScribeGearEffect` itself stays — the shadow effect still uses it; only
the glow instance is deleted. The material-identity spec requirement is relaxed from "teal + emissive glow" to
"teal (distinct from the steel gears)".

### D19 — Faint tick-tock sound on the Effect channel, mutable, Timer-tab-scoped (playtest-4)

A faint clockwork tick-tock plays while the Timer tab is open: one beat per real second, phase-aligned to the
same monotonic second boundary as the gear tick (D11), alternating between two variations (a higher "tick" and
a lower "tock") for the tick-tock cadence. **Effect channel:** played via `capi.World.LoadSound(new SoundParams
{ SoundType = EnumSoundType.Sound, RelativePosition = true })` so it's routed through the base-game **Effect**
(Sound) volume slider, not Music. **Mutable:** gated on `ScribePlayerSettings.MuteUiSounds` (the same "Mute
Scribe UI sounds" preference that swaps `GetUiSoundPlayer` to the silent player) — no sound when muted.
**Scoped:** the loop starts when the Timer tab opens (widget `InitState`) and is disposed when it closes
(`Dispose`), so it never plays on other tabs or when the dialog is shut. **Interim asset:** vanilla
`game:sounds/tick.ogg` at two pitches, to avoid shipping and attributing new audio; a dedicated pair of samples
is a possible later nicety (this resolves the D6 "should the tick play a sound?" open question for the tick
cadence; the shudder stays silent for now).

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
- **Start-slide mechanism** (D8a): monotonic-timestamp-derived slide vs. a resumed host-owned controller — pick the lighter one that survives `ForceRebuild`; resolve while implementing task 5.8.
- **Final great-wheel tooth count / spacing** (task 5.5) — tune in-game so teeth visually mesh with the small gear; keep `EscapeTeeth` synced.
