## Why

The Clockmaker's Notebook **Timer tab** works but reads as a plain form (label field + H/M/S steppers + a countdown + Stop button). The Clockmaker fantasy — a tinkerer who builds *timepieces* — is barely expressed on the one screen that is literally about time. An ambient, always-running **clockwork gear-train**, glimpsed as if **behind glass** at the top of the Timer tab, makes the passage of time tangible and thematic. It is pure presentation: no change to timer behavior, the Core model, the codec, or the network — so the payoff is high and the risk is contained to the view layer.

## What Changes

- Add an **ambient interlocking gear-train** to the Timer tab, positioned **above the countdown/form region**, that runs **continuously whenever the tab is open** — decoupled from whether a timer is set, running, or fired (a "peek into the machinery behind the page").
- **Author round toothed gear art** *inspired by* the vanilla temporal gear (its apparent **12-tooth** read → a clean 30°/tooth step), **starting from the Gearlock Firearms mod's gear assets** (its author licenses them for reuse/modification), then modifying/extending as needed. This is an authored recreation, **not** a render of the real `game:gear-temporal` item.
- **Carry over the temporal gear's material identity**: skin the authored gears with the temporal gear's **teal metallic texture** (`temporalgear.png`, already mirrored in-repo as `reference-temporalgear.png`) and give them an **emissive / self-illuminated glow** so they read as *temporal* gears rather than plain cogs — rendered in Skia (additive/bloom brightness + a soft halo), reusing the mod's existing in-GUI glow precedent (`cuneiform-contrast-glow` / `CuneiformGlow`), not the game's per-face `glow`/world-light mechanism (which only applies to real 3D item/block meshes).
- Gears **mesh**: adjacent gears counter-rotate, their per-tooth angular steps related by tooth-count ratio and hand-tuned positioning so painted teeth interlock (the Gearlock technique).
- Motion is a **spring-wound tick**: discrete per-tooth steps with an easing curve that snaps-and-settles (trialed in-game among `EaseOutBack` / `EaseOutElastic` / `EaseInOutBack`).
- Present the train **behind semi-visible glass** — a framed, slightly translucent window over the mechanism.
- On timer **fire**, the mechanism **shudders then locks** (pairing with the existing blinking `00:00`); it resumes ticking once the timer is cleared/reset.
- **Milestone order (explicit):** land the **gears working first** (spin/tick/mesh, decoupled from timer state), *then* the glass framing and any supporting visual chrome as a follow-on milestone.
- Credit **JeanPierre (Gearlock Firearms)** for the reused/derived assets and record the reuse permission.

**Non-goals:** no change to timer countdown/fire/clear behavior, the `src/Core/` model, the document codec, or any network packet; **no real 3D item render** (evaluated and set aside — the real temporal gear is an irregular lattice that cannot visually mesh or tick per tooth); **no HUD gear** (this change is Timer-tab-only); no new *code* mod dependency (Gearlock assets are copied/derived, not referenced at runtime).

## Capabilities

### New Capabilities
- `timer-gearworks`: the ambient clockwork gear-train presentation on the Clockmaker's Notebook Timer tab — continuous meshing per-tooth tick motion, behind-glass framing, and a fire→shudder→lock reaction, all decoupled from timer state.

### Modified Capabilities
<!-- None. Timer behavior is unchanged: `timer-lifecycle`'s fired/clear requirements are OBSERVED
     by the shudder reaction, not altered. No existing spec's requirements change. -->

## Impact

- **Code (`src/Mod/`):** `GuiDialogClockmakerNotebook.cs` — insert the gearworks above the Timer-tab content (`BuildTimerContent`); a new self-ticking gearworks widget (following the `ScribeTimerIcon` / self-ticking `StatefulWidget` precedent). No `src/Core/` changes; no new tests in `tests/Core.Tests` (view-only feature).
- **Rendering:** LibGUI Skia widget tree — a self-loaded gear raster in a `Container { BoxStyle.Texture }` wrapped in `AnimatedRotation` (the crash-safe rotation the mod already uses), layered with `Stack`/`Positioned`, clipped/tinted for the glass. Raw-GL (`capi.Render.Render2DTexture` + `GlRotate`, the Gearlock pattern) recorded as the documented fallback if the Skia path underperforms. Mechanism finalized in `design.md`.
- **Assets:** new authored/derived gear textures under `assets/scribe/textures/` (Gearlock gear geometry re-skinned with the temporal gear's teal `temporalgear.png` material), plus an optional glass-frame texture; use the **self-load bitmap** pattern (VSAPI-NOTES §LibGUI: `Image`/`SkiaAssetLoader` silently fail post-startup — load via `capi.Assets.TryGet(loc, loadAsset:true)`). Emissive glow is a **Skia render effect** (additive brightness + soft halo, per the `CuneiformGlow` precedent), not baked into the texture.
- **Docs:** CREDITS (extend the existing JeanPierre credit to Gearlock Firearms + note the reuse permission); `VSAPI-NOTES.md` §LibGUI (record the rotating-raster / self-load findings and the evaluated-and-rejected 3D `ItemStackRenderer` path); `CHANGELOG.md` `[Unreleased]`.
- **Dependencies:** none new. `game 1.22.x`, hard `gui 3.1.0` unchanged.
