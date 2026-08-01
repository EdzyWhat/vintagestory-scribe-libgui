## Why

Temporal storms are Vintage Story's signature dread moment, but Scribe's pinned-task HUD sits
inertly through them. Making the HUD *react* — corrupting its own text the way the game corrupts
chat, and swapping its title to a call to action — turns a utility overlay into part of the
storm's atmosphere, reinforcing "drop everything and survive" exactly when the player needs that
push. Research into the base game confirms the effect is cheap and faithful to reproduce: the
vanilla "crazed" text is not a shader but Unicode combining-mark injection into the string, and
storm/stability state is fully public API.

## What Changes

- **Corrupt all HUD text during instability.** When triggered, every text string the HUD draws
  (title, task rows, "+N more", timer label/countdown) is passed through a Zalgo-style combining-
  diacritic injector — the same mechanism vanilla uses for storm chat — so it renders crazed and
  hallucinatory through the normal font path. No shader work.
- **Two independent triggers, variable strength (not binary):**
  - **Temporal storm:** corruption strength keyed to storm tier using vanilla's own glitch
    strengths — **~0.53 (Light) / ~0.67 (Medium) / ~0.90 (Heavy)** via `EnumTempStormStrength`.
  - **Low personal stability:** ramps from **0% at 50% stability to 100% at 10% stability**
    (linear); above 50% it is off.
  - When both apply, the HUD uses the **stronger** of the two strengths.
- **Storm-only title swap.** During an active temporal storm the HUD title "Pinned" is replaced by
  "Survive the Storm", steady for the storm's duration, reverting when it ends. The low-stability
  trigger does **not** change the title (it only drives corruption).
- **Organic re-randomization.** The corrupted rendering re-scrambles on a randomized **2–8 second**
  interval while a trigger is active, so it writhes rather than sitting static.
- **Player-facing toggle.** A new Scribe setting (default on) disables the entire effect — title
  swap and corruption — for players who rely on HUD legibility or are motion-sensitive.

## Capabilities

### New Capabilities
- `hud-storm-corruption`: instability-driven corruption of the pinned-task HUD text (dual trigger,
  variable strength, organic re-randomization) plus the storm-only title swap, gated by a settings
  toggle.

### Modified Capabilities
- `pinned-task-hud`: the HUD SHALL read client-side temporal-storm and personal-stability state and
  render corrupted title/row/timer text while a trigger is active; the title SHALL swap to a storm
  call-to-action during an active storm.
- `settings-tab`: a new client-local setting SHALL toggle the storm-corruption effect (default on).

## Impact

- **Code (src/Mod, client-side):** `HudScribePins.cs` / `HudPinsContent` (thread a storm/stability
  signal into the widget tree; swap the title; wrap rendered strings through the corruptor; add a
  randomized re-scramble tick). Read state via
  `capi.ModLoader.GetModSystem<SystemTemporalStability>()` (`.StormData.nowStormActive`,
  `.StormData.nextStormStrength`) and `capi.World.Player.Entity.WatchedAttributes.GetDouble(
  "temporalStability")`. Settings plumbing for the new toggle.
- **Core (src/Core):** a small, pure, unit-testable text-corruption helper (combining-mark
  injector) with no VS API dependency — reproducing vanilla's `destabilizeText` (which is `private`
  and not callable).
- **Assets:** new lang key `scribe-hud-title-storm` ("Survive the Storm") and the settings
  label/help lang keys in `lang/en.json`.
- **Dependencies:** none added. `SystemTemporalStability` (from `VSSurvivalMod`) is already
  referenced and already read server-side; this adds a client-side read. The effect degrades
  gracefully to "never triggers" if that system is absent (non-survival servers).
- **Non-goals:** no world/screen shader wobble on the HUD (vanilla's `gui.vsh` has no warp; that
  effect is world-geometry only and out of scope).
