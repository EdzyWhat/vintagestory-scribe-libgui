## Context

`respect-local-illumination` (shipped, playtest-pending) shades every `ScribeDialogBase` surface by
the light reaching the player. Its `ScribeAmbientLightSampler.Sample(dt)` folds three inputs —
block-grid light, ambient/sky, and the player's **held** light — into one combined `(brightness,
RGB tint)`, runs brightness through the author's non-linear curve, then eases the whole result
toward its target with frame-rate-independent exponential smoothing (`τ = 0.2s`, ~400ms glide) in
`Smooth(...)` before quantizing.

**Immersive Lanterns (IL), decompiled at v0.4.1, is a `GetLightHsv` Postfix, not a private system.**
`ModSystemImmersiveLanterns.SetupFlickerPatching()` Harmony-patches
`CollectibleObject.GetLightHsv(IBlockAccessor, BlockPos, ItemStack)` (and `BlockLantern`'s
override) with a Postfix (`FlickerGetLightHsvPatch.Postfix`) that:

- Runs **only when `pos == null`** (returns early if `pos != null`) — i.e. it flickers **held /
  inventory** items, not placed blocks. Our `TryHeldLight` calls `GetLightHsv(blockAccessor, null,
  stack)` with `pos == null`, so **we already receive the flickered V for free**; placed-grid
  queries pass a real `pos` and are untouched.
- Modifies **V (brightness index) only**, never H/S — a pure brightness flicker, no hue shift.
- Applies a sine flicker between a min/max factor: **torch** `0.75..1.0` over a randomized
  100–300ms cycle; **lantern/candle/lamp** `0.75..1.0` over a 500–1000ms cycle (item classified by
  its code path containing `lantern`/`candle`/`lamp`). All amplitudes/cadences come from VS
  `ClientSettings` (`flickeringlights-*` keys) and are tunable in IL's own config dialog — so the
  flicker the player sees is *their* configured flicker.

The consequence: our sampler already ingests IL's flicker; the **only** thing erasing it is our
own 400ms low-pass filter, which heavily attenuates a 100–300ms torch flicker and partially eats a
500–1000ms lantern one. This change is therefore small: stop smoothing the held-brightness term
when IL is active, and IL's flicker reappears exactly as the player configured it — with no
dependency, reflection, or flicker-matching code on our side.

## Goals / Non-Goals

**Goals:**
- When IL is active and a held light dominates the GUI, the page brightness flickers in step with
  IL's held-light flicker, matching the player's IL settings.
- Preserve the 400ms glide for *environmental* brightness changes (sun↔shade, day/night, weather)
  even while a held flicker passes through.
- Zero behavioral or performance change for players without IL — a static scene keeps LibGUI's
  paint cache fully valid.
- Soft, optional integration: no hard dependency, no new package, graceful no-op when IL is absent.

**Non-Goals:**
- Reading IL's settings, curve, or internal state; matching its flicker in code. We reproduce it
  by *not filtering* the value we already sample.
- Synthesizing a flicker of our own when IL is absent.
- Placed-block lantern flicker (IL itself excludes it; we mirror that scope).
- Any change to the brightness curve, tint math, config floor, tint mechanism, persistence, sync,
  or document model.
- Supporting flicker mods other than IL by name in this change (the detection is a single mod id;
  generalizing to other flicker mods is a later concern).

## Decisions

### D1 — Split the held-brightness term out of the combined shade, unsmooth only that term

Today `Sample()` collapses grid/sky/held into one `blockLuma`/`rawBrightness` then smooths the
single result. To let held flicker through while environment stays smoothed, separate the
brightness into two components before `Smooth()`:

- **environment brightness** — from grid light + sun/sky/weather (as today);
- **held brightness** — the curve-mapped contribution from `TryHeldLight` when it dominates.

Smooth the environment component with the existing `τ = 0.2s`. The held component, when the
pass-through is active, is applied with **no smoothing** (adopt target directly) or a negligible τ,
then the two are combined by the same MAX rule already used (VS light is max-based). The reported
brightness = `max(smoothedEnvironment, heldFlickerBrightness)`. Because the held term is what
carries IL's flicker, passing it through unsmoothed reproduces the flicker; because environment is
still eased, walking sun↔shade still glides.

**Alternative rejected — global τ switch (drop smoothing entirely when IL active):** simplest, but
it would also un-smooth genuine environmental transitions, reintroducing the exact stepping the
400ms glide was added to fix. The split keeps both behaviors correct at once.

**Alternative rejected — separate hue pass-through:** unnecessary. IL flickers V only, so only the
brightness term needs the exception; the tint keeps its existing smoothed treatment.

### D2 — Gate on `IsModEnabled("immersivelanterns")`, evaluated once and cached

Detect IL via `capi.ModLoader.IsModEnabled("immersivelanterns")` (the modid from IL's
`modinfo.json`). Evaluate once (at sampler construction or first sample) and cache a `bool` — mod
enablement does not change mid-session. When false, the sampler takes exactly today's path: held
term smoothed with the environment, no split, no extra re-records. This keeps the feature free for
the vast majority of players and makes "no IL → no change" structurally guaranteed rather than a
runtime coincidence.

**Alternative rejected — always pass held flicker through (no gate):** without IL there is no
flicker on `GetLightHsv`, so a held steady light would simply be unsmoothed — harmless visually,
but it would defeat the paint-cache for a held-light-dominated static scene for no benefit. The
gate avoids that cost for non-IL players.

**Alternative rejected — detect by sampling variance (watch for a wiggling V):** fragile and
laggy (needs history to distinguish flicker from movement) and would trigger on non-IL noise. The
explicit modid check is deterministic and cheap.

### D3 — Accept bounded extra paint-cache re-records while flicker is visible; measure, then cap if needed

Letting the held brightness flicker through means the quantized `Shade` changes roughly every frame
*while a flickering held light dominates an open Scribe GUI*, so `ScribeGlobalTint` re-records the
`SKPicture` at flicker cadence. This is the deliberate, bounded cost of the feature. It is confined
to: IL enabled **and** a Scribe GUI open **and** held light dominant. Vanilla players never pay it.

The cost MUST be measured in-game on the pixel-art parchment backdrops (the same surfaces D3 of
`respect-local-illumination` flags). If it hitches, the fallback — still passing a *visible* flicker
through — is to coarsen the held-brightness quantization or cap the re-record cadence (e.g. update
the flicker no more than every N ms), trading flicker fidelity for cache stability. A LibGUI fork
remains off the table.

## Risks / Trade-offs

- **[Paint-cache re-record at flicker cadence may hitch on parchment backdrops]** → Bounded to
  IL-on + GUI-open + held-dominant; measure in-game (tasks). Fallback is coarser held-brightness
  quantization / a cadence cap, still visibly flickering — never a wider or LibGUI change.
- **[Flicker could read as distracting while reading]** → It matches what the player already sees on
  their held light in the world and chose via IL's settings; if it proves annoying, the natural
  home for an opt-out is a future `ScribePlayerSettings` toggle, not this change. Noted, not built.
- **[IL changes its patch shape in a future version]** → We depend only on the *observable* result
  (a flickering `GetLightHsv` for held items) and on the stable modid, not on IL's internals, so a
  patch-shape change still works as long as IL keeps flickering held `GetLightHsv`. If IL stops
  patching that method, we simply see a steady value and smooth it — graceful degradation, no
  breakage.
- **[Only IL is recognized]** → Other flicker mods that patch `GetLightHsv` for held items would
  work too if we generalized the gate; scoped to IL by name here to keep the change tight.

## Open Questions

- **Exact held-term smoothing:** true zero (adopt target every frame) vs. a small τ (e.g. 20–30ms)
  to take the hardest edge off aliasing without visibly lagging the flicker. Decide from the
  in-game feel during the playtest; either is a one-constant change.
- **Whether to expose a player opt-out toggle** (`ScribePlayerSettings`) now or defer until someone
  reports the flicker as distracting. Leaning defer (keep this change tight), revisit on feedback.
