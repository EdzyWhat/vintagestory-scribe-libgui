# Tasks — add-timer-gearworks

> Milestone order (per proposal): **M1 = gears working** first, then **M2 = glass + fire-shudder + tuning**.
> View-layer only — no `src/Core/` changes and no new Core tests, but `verify.sh` must stay green.

## 1. Assets & attribution (prep)

- [x] 1.1 Copy Gearlock Firearms' gear geometry/texture from the mod zip into a scratch working area (permitted reuse), and record the ModDB reuse-permission note + link (`https://mods.vintagestory.at/show/mod/60282`) in this change folder. *(Recorded in `gearlock-reuse-permission.md`.)*
- [x] 1.2 Re-skin the gear texture to the temporal-teal palette using `reference-temporalgear.png` / vanilla `temporalgear.png` as the reference; export the gear PNG(s) into `src/Mod/assets/scribe/textures/` (e.g. `gui/gear-temporal-large.png`, `gui/gear-temporal-small.png`). *(Teal `gui/gear-temporal-large.png` + steel small gear shipped; escape wheel is procedural, not a PNG.)*
- [x] 1.3 Extend `CREDITS` to attribute JeanPierre / Gearlock Firearms for the reused/derived gear assets (JeanPierre is already credited for Wanderer's Sketchbook — add the Gearlock line).

## 2. M1 — Gears working (motion, mesh, material)

- [x] 2.1 Add a self-loaded bitmap helper for the gear texture(s) — `capi.Assets.TryGet(loc, loadAsset:true)` → `SKBitmap.Decode`, loaded once and cached (per VSAPI-NOTES §LibGUI: `Image`/`SkiaAssetLoader` silently fail post-startup). *(`ScribeModSystem.GetGuiTextureBitmap`, reusing the backdrop cache/dispose path.)*
- [x] 2.2 Create the gearworks widget (`src/Mod/`): a self-ticking `StatefulWidget` (following `ScribeCountdownText` / `ScribeTimerIcon`) that renders a `Stack` of gears, each a `Container { BoxStyle.Texture = bmp }` wrapped in `AnimatedRotation`. *(`ScribeGearworks`.)*
- [x] 2.3 Derive the tick phase from `capi.World.ElapsedMilliseconds` (`toothIndex = floor(elapsedMs / tickPeriodMs)`); each gear targets `toothIndex × stepAngle × ratio` with alternating direction so adjacent gears counter-rotate and mesh (Gearlock single-driver trick). Confirm the motion is **rebuild-stable** (no jump when the tab `ForceRebuild`s). *(`TickPeriodMs = 1000` so one tooth-step ≈ one real second per author request; ReferenceTeeth=11 to match placeholder art.)*
- [x] 2.4 Skin the gears with the teal texture and apply the Skia **emissive glow** (additive brightness + soft halo, per the `CuneiformGlow` precedent); cache the glow layer / wrap static parts in `RepaintBoundary`. **SUPERSEDED by D18/task 7.5: the glow was tried (task 6.1) and REMOVED — it didn't read well in-game.** Only the teal *texture* carries the temporal identity now; no glow halo on any gear. The teal-skin narrows to the single central gear (D12).
- [x] 2.5 Insert the gearworks **above** the content in `GuiDialogClockmakerNotebook.BuildTimerContent`, decoupled from timer state (shown when Idle, Running, and Fired). Keep it in its own vertical region and non-hit-testing so it cannot swallow control input (D7). *(Divider is the topmost item; gearworks in a centering Row above `Expanded(content)`.)*
- [x] 2.6 In-game trial: pick the easing curve (`EaseOutBack` / `EaseOutElastic` / `EaseInOutBack`) and the gear count/sizes for the best spring-wound feel; record the choice in `design.md` Open Questions. *(Resolved through the playtest-1..4 refinement batches — final layout/counts/sizes are the baked `.geartune` defaults, gear count settled at the symmetric 4-gear train per D15.)*
- [x] 2.7 Verify M1 spec scenarios in-game: gears shown with no timer set; keep running across Idle↔Running↔Fired; not shown on other tabs; mesh + counter-rotate without teeth drift; snap-and-settle motion; teal + glowing; timer controls remain fully usable. *(Playtest 2026-08-11: `c61c1776` "Gears run, decoupled" + `a788ec71` "Teal-only, no glow" both PASS. "teal + glowing" reads as teal-only per D18 — no glow ships.)*

## 3. M2 — Glass framing, fire-shudder, final tuning

- [x] 3.1 Add the **behind-glass** framing over the gear region — a translucent, clipped/framed overlay that reads as looking into the mechanism through glass; non-hit-testing. **DONE mechanically, awaiting playtest.** Setting = authored **backdrop** (behind the gears) + **border-trim** frame (`gearworks-backdrop.png` / `gearworks-border.png`). Glass = a very-nearly-transparent **glass pane drawn IN FRONT** of the gears (`gearworks-glass.png`, topmost child INSIDE the HardEdge clip via `TrimGlassAsset`), derived from vanilla clear glass (`glass/plain.png`, its cool-gray tint minus the leaded dark border) at ~6% alpha with a soft diagonal sheen. Non-hit-testing (plain textured Container).
- [x] 3.2 Implement **fire → shudder → lock** — DONE mechanically, awaiting playtest confirm. Added a third gear, the **escape wheel** (regulator), reading `Widget.Status` (threaded from `MyTimer?.Status`): Idle = stationary at tooth 0; Running = rotates with the monotonic clock; Fired = frozen at the captured `_lockedIndex` + a brief decaying shudder. **Two corrections from the first playtest:** (a) the shudder is now an **instant** `Transform.Rotate` (about center), NOT `AnimatedRotation` — the 520ms settle tween was smoothing the 70ms jolts away, so the shudder read as invisible; (b) the wheel is now **always visible** (a resting peek when Idle, sliding to a larger live peek when engaged) instead of latching in from hidden — the old hidden→visible `AnimatedSlide` re-triggered on every `RefreshTimerView` → `ForceRebuild` remount (stock `Animated*` widgets snap on a fresh mount, VSAPI-NOTES §LibGUI), producing the "disappears then re-slides + half-tick" fire glitch. Blinking `00:00` unaffected (separate `ScribeBlinkText`). *(Note: the live-peek slide is an instant snap for the same remount reason; a true tween would need the parked reconcile conversion.)*
- [x] 3.3 Final positioning/sizing pass (placement was draft) and glass look (authored frame texture vs. pure Skia-drawn frame + translucent fill) — resolve `design.md` Open Questions. **Positioning/sizing DONE** (baked `.geartune` defaults + authored backdrop/border art). **Glass look:** decided to use an authored frame + a still-TODO translucent front overlay (see 3.1); the frame-texture vs. Skia-drawn question is resolved in favor of authored art.
- [x] 3.4 Verify M2 spec scenarios in-game: mechanism appears behind glass; gears shudder then lock on fire; gears resume after the fired timer is cleared. *(Playtest 2026-08-11: `8026a2f6` "Shudder-and-lock on fire" PASS ("Works.") and `daeb6761` glass overlay PASS — both halves confirmed.)*

- [x] 3.5 **Procedural great wheel** (new, author request "look into procedurally generating the big gear … keep the small gear's visual style"). `ScribeGearTexture.GreatWheel()` draws a many-toothed (26) wheel in SkiaSharp: blocky flat-topped teeth, a quantized brown-steel palette with per-element shade jitter (uneven colouring), narrow spokes leaving open sectors, and a bored transparent hub (negative space, not a solid disc). Deterministic (fixed seed → stable across launches), cached + disposed via `GetProceduralGreatWheel` on the backdrop-cache path. Used as the escape wheel (falls back to the reused small-gear PNG server-side). **Placeholder to judge/tune in-game** — knobs (tooth count, spoke count, palette, radii) are all in one file; swapping to hand-drawn art is a one-line call-site change. If the generator's tooth count changes, update `EscapeTeeth` to match.

## 5. Playtest-2 refinements (2026-08-11)

> Second-playtest feedback batch (screenshots `~/Desktop/idle.png`, `~/Desktop/active.png`). All view-layer;
> no Core changes. Grouped so nothing is lost — see the spec delta requirements added alongside.

- [x] 5.1 **Fire must LOCK in place, not rewind.** DONE mechanically, awaiting playtest. Root cause: `AnimatedRotation` seeds Begin==End on a fresh mount and only tweens across a reconcile (confirmed in `ImplicitlyAnimatedWidget` source), and the Running→Fired transition remounts the widget via `ForceRebuild` — so any Fired angle that wasn't exactly the last running angle made the fresh mount SNAP there (the rewind). Fixed by deriving the lock angle from `Widget.FireLockMs` (host-stamped at the fire instant) through the same clock formula the running wheel uses, so it freezes precisely where it was. Shudder plays on top, then it stays put.
- [x] 5.2 **Small gear fixed position.** Pinned `smallTop` to the wheel's LIVE-peek position and made it independent of state — the small gear no longer drops when Idle; the wheel slides under it.
- [x] 5.3 **No strokes on the great wheel.** Removed all `SKPaintStyle.Stroke` draws from `ScribeGearTexture` — flat filled shapes only.
- [x] 5.4 **Remove the residual fade.** Wrapped the gearworks in `ScribeResetPaintColor` in `BuildTimerContent` (the `DrawMaskedBox`/`SharedPaint.Color` leak — same fix as the dialog backdrops).
- [x] 5.5 **Smaller, more, slightly-spaced great-wheel teeth.** Generator now `Teeth = 44` with a shorter tooth length (rRoot 0.40→0.42) and smaller half-angle (0.62→0.52 → more gap). `EscapeTeeth = ScribeGearTexture.Teeth` so they can't drift.
- [x] 5.6 **Great wheel spins the opposite direction.** Flipped the escape angle sign (was `-escapeBase`, now `+escapeBase`).
- [x] 5.7 **Scale the whole gearworks with `PixelArtSize`.** Gearworks `scale = ClampPixelArtSize(PixelArtSize) / 540f` (was the text/window font scale). 540 = 100%.
- [x] 5.8 **Animated slide-in on Start.** DONE mechanically, awaiting playtest. Rebuild-stable slide: peek interpolates resting→live over `EngageSlideMs` (420) with `EaseOutBack`, progress derived from host-owned `Widget.EngageStartMs` (survives the remount that delivers Running); the self-tick keeps repainting while `EngageSlideProgress() < 1`. Not a State flag, so it glides rather than snapping.
- [x] 5.9 **Tick precisely on the real second.** Tightened: `PollMs` 50→16 (≈one-frame phase error) so the snap begins right on the second boundary; index still derived from `ElapsedMilliseconds / 1000` (rebuild-stable, D4). Confirm the felt cadence in-game.
- [x] 5.10 **Recolor the great wheel toward steel.** Palette replaced with gray-steel shades sampled from the small gear PNG (`#605050`/`#606050`/`#505040`/`#706060`/`#504040`/`#707060`); per-element random pick retained.

## 6. Playtest-3 refinements (2026-08-11)

> Third-playtest feedback batch. All view-layer; no Core changes. Grouped so nothing is lost —
> see the spec delta requirements amended alongside (material identity, fire-retract, escape-wheel layout).

- [x] 6.1 **Only the temporal gear is teal + glowing.** Do NOT re-skin the pair or any other gear to teal
      (supersedes the old plan in 1.2/2.4 to teal-skin all gears): the single already-teal
      `gear-temporal-large.png` is the ONLY teal element, and the emissive glow applies to it ALONE. The two
      flanking small gears and the procedural escape wheel stay steel and unlit. Apply the glow via the
      `CuneiformGlowMask.ForSigma` blur primitive (a soft bright halo painted behind the teal gear), in a
      small `RenderProxyBox`-style wrapper; cache the mask (never dispose per-frame, per the CuneiformGlow
      discipline).
- [x] 6.2 **Reduce great-wheel tooth height by 30%.** In `ScribeGearTexture`, bring `rOuter` in toward
      `rRoot` so tooth length (rOuter−rRoot) drops ~30% from its current value (0.48→~0.462 with rRoot 0.42),
      keeping tooth count/spacing.
- [x] 6.3 **Cheap 3D depth ("shadows") on the gears — in ADDITION to the teal glow, never instead of it.**
      Not `BoxStyle.BoxShadows` — that shadows the square box rect, not the round gear silhouette. Instead
      draw an offset, dark, semi-transparent DUPLICATE of each gear's own texture directly beneath it in the
      Stack (a `ScribeTintBox`-style wrapper that forces `SharedPaint.Color` to a dark translucent value, the
      colored twin of `ScribeResetPaintColor`, so the duplicate silhouette reads as a cast shadow). Every
      shadow MUST stay inside the existing `HardEdge` clip — no spill outside the region box.
- [x] 6.4 **2px diagnostic border on the clip box.** Draw a 2px border around the clip region so its bounds
      are visible during tuning. DIAGNOSTIC — flag for likely removal before ship (not a spec requirement).
- [x] 6.5 **On completion, retract the escape wheel to idle.** When the timer enters Fired, immediately
      animate the escape (great) wheel's POSITION back OUT to its resting/idle peek, reusing the same
      timestamp-derived slide mechanism as the Start slide-in but driven by the host-owned `_fireLockMs`
      (engage = slide toward live over `EngageStartMs`; fire = slide back toward resting over `FireLockMs`).
      This is a position/translation change only and is independent of the rotation LOCK (angle still freezes
      at the fire instant per 5.1) and the shudder — all three play together on Fired.
- [x] 6.6 **Stroke on the great wheel's circular parts only.** Bring back a stroke in `ScribeGearTexture`,
      but ONLY around the circular portions (rim outer/inner edge + hub), NOT the spokes or teeth. Solid,
      slightly darker gray than the fill palette. (Reverses part of 5.3, but scoped to the round edges — the
      teeth/spoke outlines that read as "drawn-on lines" stay strokeless.)
- [x] 6.7 **Symmetric layout: centered teal gear flanked by two smalls, teal lower.** Restructure the Build
      layout from (teal-left + one small-right) to: the temporal/teal gear horizontally CENTERED, with TWO
      small steel gears — one on each side — meshing with it; the teal gear positioned LOWER than the two
      smalls so that when it (and the escape wheel) slide into place it dramatically overlaps the great wheel.
      Keep both small gears counter-rotating correctly against the centered teal driver.
- [x] 6.8 In-game trial of the full Playtest-3 set: teal-only glow reads; shadows give depth and never spill
      the clip; great wheel teeth shorter with circular-only stroke; escape wheel retracts on fire while the
      pair locks + shudders; symmetric two-small layout with the teal gear overlapping on slide-in. Record
      verdicts in `TESTING.md`. *(Playtest 2026-08-11: `a60e6537` "Depth + tooth stroke" + `10d4b71f` "Slide-in / retract + symmetry" both PASS ("Works."). This confirms the mechanical `[~]` set 6.1–6.7 (glow reversed to teal-only per D18/7.5).)*

## 7. Playtest-4 refinements (2026-08-11)

> Fourth-playtest feedback batch. View-layer only; no Core changes EXCEPT the two client-settings the tick-tock
> sound reads (mute + a possible enable). See the spec delta requirements amended alongside (tick-tock sound;
> opacity; depth tuning; layout mesh; glow removal).

- [x] 7.1 **Tighten the mesh — gears must visibly touch.** Current spacing is too wide to be credible. Bring the
      two small gears more IN-LINE with the central temporal gear (raise `smallTop` toward `largeTop`) and reduce
      the horizontal gap (increase `toothKiss`) so the small-gear teeth overlap the teal gear's teeth rather than
      floating beside them. Re-tune per the now-25%-larger gears (7.4).
- [x] 7.2 **Sharper shadows.** Halve the shadow blur AND the offset (`ShadowBlur` 2→1, `ShadowOffset` 3→1.5) so
      the cast shadow reads as a crisp silhouette close under each gear, not a soft smear.
- [x] 7.3 **Lighter shadows on the three steel gears; teal keeps its.** The teal gear's shadow keeps its current
      opacity; the other three gears (two smalls + escape wheel) drop shadow alpha to 2/3 of current (0x70 →
      ~0x4B). Needs a per-gear shadow tint (two `ScribeGearEffect` shadow colours, not one shared constant).
- [x] 7.4 **All gears 25% larger, same bounding box.** Multiply each gear's SIZE by 1.25 (`largeSize`,
      `smallSize`, `escapeSize`) while keeping `regionW`/`regionH` unchanged, then re-tune positions (7.1) so the
      bigger gears still fit and mesh within the same clipped region.
- [x] 7.5 **Remove the temporal gear's outer glow.** It isn't working — delete the `ScribeGearEffect` GLOW layer
      behind the teal gear (task 6.1's glow). Keep the shadow-depth effect (D13) — this only removes the halo
      (reverses D12's glow half / task 6.1's glow). `ScribeGearEffect` stays (shadows still use it).
- [x] 7.6 **All gears fully opaque.** ROOT-CAUSED (read from vendored `Gui.dll`): the gears aren't transparent
      in the art (PNG bodies measured at alpha 255) — `PaintingContext.DrawMaskedBox` (the textured-`Container`
      path) does `DrawBitmap(texture, SharedPaint)` setting only `FilterQuality`, so each gear is modulated by
      whatever `Color`/`ColorFilter`/`BlendMode` the PREVIOUS op left (`DrawBox` sets the shared colour to its
      fill and never restores it; the diagnostic border + shadow/glow effects are such ops). The one top-level
      `ScribeResetPaintColor` can't help — many ops paint between it and each gear. Fix: reset opaque white +
      clear `ColorFilter`/`ImageFilter` (and restore an opaque `BlendMode`) IMMEDIATELY before EACH gear's own
      paint — extend `ScribeResetPaintColor` (or wrap every plain gear in it), so no stale state can modulate it.
- [x] 7.7 **Faint tick-tock sound on the Timer page.** One tick per real second (phase-aligned to the same
      monotonic second boundary as the gear tick, D11), alternating between TWO variations for a tick-tock
      cadence (interim: vanilla `game:sounds/tick.ogg` at two pitches — a higher "tick" and a lower "tock" — to
      avoid shipping/attributing new audio; a dedicated pair of samples is a later nicety). Plays via
      `capi.World.LoadSound(new SoundParams { SoundType = EnumSoundType.Sound, RelativePosition = true, ... })`
      so it rides the base-game **Effect** volume slider. Faint (low `Volume`). Only while the Timer tab is open
      (start on tab-open/InitState, dispose on close/Dispose). MUTED by the existing "Mute Scribe UI sounds"
      preference (`ScribePlayerSettings.MuteUiSounds`) — gate playback on it, matching `GetUiSoundPlayer`.
- [x] 7.8 **Remove the 2px diagnostic border** (task 6.4) once positioning is confirmed in this pass — it was
      always flagged as diagnostic, not shippable.
- [x] 7.9 In-game trial of the full Playtest-4 set: gears mesh credibly and are 25% larger in the same box;
      shadows sharp + steel-gear shadows lighter than the teal's; no teal halo; all gears fully opaque; faint
      tick-tock once/sec on the Effect slider, muted by the Scribe-UI-sound toggle. Record verdicts in
      `TESTING.md`. *(Playtest 2026-08-11: `ff234ef1` "Sized, sharp, opaque" + `47b99a48` "Tick-tock sound" both PASS ("Works."). This confirms the mechanical `[~]` set 7.1–7.8.)*

## 4. Docs & verification

- [x] 4.1 Add `VSAPI-NOTES.md` §LibGUI notes: rotating a self-loaded raster in the widget tree; the evaluated-and-rejected real-3D `ItemStackRenderer` / `ItemStackDisplay` path (works, wrong shape/motion); the Gearlock raw-GL `Render2DTexture` fallback. *(Added under §LibGUI, 2026-08-11.)*
- [x] 4.2 Add a `CHANGELOG.md` `[Unreleased]` entry for the Timer-tab gearworks. *(Added under `### Added`.)*
- [x] 4.3 Run `verify.sh` (Core + Atlas) green and restage; full Timer-tab playtest on macOS (idle/running/fired); record verdicts in `TESTING.md`. *(2026-08-11: `verify.sh Debug` green — Core 317/317, Atlas 25/25, restaged. Timer-tab playtest pass recorded in `TESTING.md` (2.7 / 3.4 / 6.8 / 7.9 + glass all PASS).)*
- [x] 4.4 `openspec validate add-timer-gearworks --strict` passes. *(Green 2026-08-11.)*
