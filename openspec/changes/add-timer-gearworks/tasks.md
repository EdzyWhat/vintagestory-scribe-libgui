# Tasks — add-timer-gearworks

> Milestone order (per proposal): **M1 = gears working** first, then **M2 = glass + fire-shudder + tuning**.
> View-layer only — no `src/Core/` changes and no new Core tests, but `verify.sh` must stay green.

## 1. Assets & attribution (prep)

- [ ] 1.1 Copy Gearlock Firearms' gear geometry/texture from the mod zip into a scratch working area (permitted reuse), and record the ModDB reuse-permission note + link (`https://mods.vintagestory.at/show/mod/60282`) in this change folder.
- [ ] 1.2 Re-skin the gear texture to the temporal-teal palette using `reference-temporalgear.png` / vanilla `temporalgear.png` as the reference; export the gear PNG(s) into `src/Mod/assets/scribe/textures/` (e.g. `gui/gear-temporal-large.png`, `gui/gear-temporal-small.png`).
- [ ] 1.3 Extend `CREDITS` to attribute JeanPierre / Gearlock Firearms for the reused/derived gear assets (JeanPierre is already credited for Wanderer's Sketchbook — add the Gearlock line).

## 2. M1 — Gears working (motion, mesh, material)

- [ ] 2.1 Add a self-loaded bitmap helper for the gear texture(s) — `capi.Assets.TryGet(loc, loadAsset:true)` → `SKBitmap.Decode`, loaded once and cached (per VSAPI-NOTES §LibGUI: `Image`/`SkiaAssetLoader` silently fail post-startup).
- [ ] 2.2 Create the gearworks widget (`src/Mod/`): a self-ticking `StatefulWidget` (following `ScribeCountdownText` / `ScribeTimerIcon`) that renders a `Stack` of gears, each a `Container { BoxStyle.Texture = bmp }` wrapped in `AnimatedRotation`.
- [ ] 2.3 Derive the tick phase from `capi.World.ElapsedMilliseconds` (`toothIndex = floor(elapsedMs / tickPeriodMs)`); each gear targets `toothIndex × stepAngle × ratio` with alternating direction so adjacent gears counter-rotate and mesh (Gearlock single-driver trick). Confirm the motion is **rebuild-stable** (no jump when the tab `ForceRebuild`s).
- [ ] 2.4 Skin the gears with the teal texture and apply the Skia **emissive glow** (additive brightness + soft halo, per the `CuneiformGlow` precedent); cache the glow layer / wrap static parts in `RepaintBoundary`.
- [ ] 2.5 Insert the gearworks **above** the content in `GuiDialogClockmakerNotebook.BuildTimerContent`, decoupled from timer state (shown when Idle, Running, and Fired). Keep it in its own vertical region and non-hit-testing so it cannot swallow control input (D7).
- [ ] 2.6 In-game trial: pick the easing curve (`EaseOutBack` / `EaseOutElastic` / `EaseInOutBack`) and the gear count/sizes for the best spring-wound feel; record the choice in `design.md` Open Questions.
- [ ] 2.7 Verify M1 spec scenarios in-game: gears shown with no timer set; keep running across Idle↔Running↔Fired; not shown on other tabs; mesh + counter-rotate without teeth drift; snap-and-settle motion; teal + glowing; timer controls remain fully usable.

## 3. M2 — Glass framing, fire-shudder, final tuning

- [ ] 3.1 Add the **behind-glass** framing over the gear region — a translucent, clipped/framed overlay that reads as looking into the mechanism through glass; non-hit-testing.
- [ ] 3.2 Implement **fire → shudder → lock**: observe `modSystem.MyTimer?.Status` (subscribe to `MyTimerChanged`); on entering Fired, run a brief shudder via `AnimatedRotation` then freeze `toothIndex`; on leaving Fired (cleared / auto-disappeared / new timer) resume normal ticking. Ensure the blinking `00:00` is unaffected.
- [ ] 3.3 Final positioning/sizing pass (placement was draft) and glass look (authored frame texture vs. pure Skia-drawn frame + translucent fill) — resolve `design.md` Open Questions.
- [ ] 3.4 Verify M2 spec scenarios in-game: mechanism appears behind glass; gears shudder then lock on fire; gears resume after the fired timer is cleared.

## 4. Docs & verification

- [ ] 4.1 Add `VSAPI-NOTES.md` §LibGUI notes: rotating a self-loaded raster in the widget tree; the evaluated-and-rejected real-3D `ItemStackRenderer` / `ItemStackDisplay` path (works, wrong shape/motion); the Gearlock raw-GL `Render2DTexture` fallback.
- [ ] 4.2 Add a `CHANGELOG.md` `[Unreleased]` entry for the Timer-tab gearworks.
- [ ] 4.3 Run `verify.sh` (Core + Atlas) green and restage; full Timer-tab playtest on macOS (idle/running/fired); record verdicts in `TESTING.md`.
- [ ] 4.4 `openspec validate add-timer-gearworks --strict` passes.
