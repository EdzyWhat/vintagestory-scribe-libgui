## Why

The cuneiform outer glow was tuned on **wet** clay tablets, where a *dark* halo behind the dark
ink separates the text from a mid-tone backdrop. But hardened and fired tablets have visibly
**darker** backdrops, so that same dark halo sits dark-on-dark and *reduces* contrast instead of
adding it — the text reads worse, not better (playtest verdict on `00000016`). The glow currently
keys off clay **material** only and ignores the tablet's life-cycle **state**, so all three states
get the wet-tuned dark halo.

## What Changes

- Make the cuneiform glow **state-aware** in addition to material-aware: the glow lookup gains the
  tablet's `TabletState` (Wet / Hard / Fired) alongside its clay material.
- **Wet** keeps its existing per-material dark-halo seeds unchanged (already validated in-game).
- **Hard** and **Fired** each get a single **light-halo** seed — a light halo lifts dark ink off a
  dark ground (the classic polarity, already supported since the halo color is arbitrary). The seed
  is **uniform across all clay palettes** (blue / red / fire share one seed per state) and
  **distinct between Hard and Fired**.
- **Ink and theme colors are unchanged** — this is a glow-only fix; no change to text color,
  backdrop, or any theme value.
- **Wax** is untouched: it has no hardened or fired state, so its single wet-style seed still applies.
- The final light-halo alpha / color / blur values are found via the existing runtime glow dev
  command and baked (an in-game tuning gate), consistent with how the wet seeds were tuned.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `cuneiform-contrast-glow`: the per-clay-type glow parameters become per-clay-type **and
  per-tablet-state**; the requirement that glow color/polarity is authored per material is extended
  so that hardened and fired states select a distinct light-polarity halo regardless of clay color.

## Impact

- **Code (Mod only):**
  - `src/Mod/CuneiformGlow.cs` — `CuneiformGlowTable.For(material)` gains a `TabletState` parameter;
    add the Hard and Fired light-halo seeds.
  - `src/Mod/GuiDialogScribeTablet.cs` — thread the dialog's existing `_state` into the three glow
    call sites (row glow, resting title, editing title).
- **No `src/Core/` change** (glow lives entirely in the Mod layer).
- **No new dependencies, no persistence/schema change, no theme/ink change.**
- Final light-halo values are an in-game tuning gate (dev glow command → bake), not a code decision.
