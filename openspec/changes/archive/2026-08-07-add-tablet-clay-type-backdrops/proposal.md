## Why

Proposal C (`add-tablet-dialog`) ships the bespoke `GuiDialogScribeTablet` with only **two**
backdrop slots keyed to the tablet item's single existing `material: [clay, wax]` variant axis, and
even those point at the shared `scribe-lectern.png` placeholder. The user wants the tablet dialog to
respect Vintage Story's actual clay materials: VS has three clay types (red, blue, fire[tan]) and a
fired/unfired distinction, so a clay tablet's editing surface should look like the clay it was made
from. That is **seven** distinct backdrops — 3 clay types × 2 fired-states, plus one wax — instead of
two placeholders.

This is a deliberate followup that **sequences AFTER Proposal C archives**: it modifies the
`tablet-dialog` backdrop requirement that C introduces, so C's spec must land in `openspec/specs/`
first.

## What Changes

- **Make each clay type a discrete tablet item (REVISED 2026-08-02 — see design Decision 1).** The item
  declared only `material: [clay, wax]` (firing deferred by Proposal B). This change expands the
  `material` variant axis to the composite states `[clay-red, clay-blue, clay-fire, wax]`, so red/blue/fire
  clay tablets are **three discrete registered items** (`scribetablet-clay-red/blue/fire`), each with its
  own handbook page and recipe — a VS base expectation the user requires. `fired` (soft/fired) remains a
  stack **attribute** (appearance only; nothing fires a tablet this round). *(Originally clay type was to
  be a stack attribute set at craft; that produced a single collapsed handbook/creative entry because VS
  lists by variant, so it was reversed to a variant. The tablet is unreleased → no save migration.)*
- **Select one of seven backdrops from the item + fired state.** `ScribeBackdrops.ForTablet(material,
  fired)` picks the backdrop from the `material` variant + `fired`: 6 clay backdrops (3 types × soft/fired)
  + 1 wax. An unknown/absent material defaults to red + soft so legacy/handbook stacks still resolve.
- **Source the backdrop art from vanilla VS pottery textures** (verified codes below) rather than the
  `scribe-lectern.png` placeholder: unfired uses the per-type clay swatches, fired uses the ceramic
  swatch, wax uses the beeswax swatch as an explicit placeholder.
- **Extend the backdrop renderer** so a spec can render a small tiling material swatch crisply at
  native resolution (optionally under a shared illustrated page-frame overlay) instead of only
  stretching one full-page texture to fill. Vanilla pottery textures are ~32px tiling swatches;
  stretch-to-fill would upscale them to a blurry, frameless color-fill that clashes with the crisp
  1024×1160 illustrated notebook/lectern pages. See design for the alternatives and the interim/target
  split.

## Capabilities

### New Capabilities

<!-- None — this change expands existing capabilities rather than introducing a new one. -->

### Modified Capabilities

- `tablet-dialog`: the "Tablet dialog uses its own theme and a placeholder backdrop" requirement
  changes from **two** slots pointing at a shared placeholder to **seven** distinct backdrops selected
  from the tablet's `material` variant + `fired` state.
- `clay-wax-tablet-item`: the clay tablet becomes **three discrete items** (a `material` variant per clay
  type: `clay-red`/`clay-blue`/`clay-fire`, plus `wax`), each with its own handbook entry and recipe; the
  tablet also **records** a `fired` appearance attribute (preserved across persistence and drop/pickup)
  without adding the firing gameplay mechanic.
- `gui-backdrop`: gains a rendering mode so a backdrop spec can tile a small material swatch at native
  resolution and/or composite a shared page-frame overlay, not only stretch a single full-page texture
  to fill.

## Impact

- **Modified code:** `src/Mod/ScribeBackdrop.cs` (7 specs + `ForTablet(material, fired)` keyed on the
  variant), `src/Mod/ScribeDialogBase.Layout.cs` (`WrapBackdrop` passes the spec), `src/Mod/ItemScribeTablet.cs`
  (`ReadFired` only — `ReadClayType`/`clayType` attribute removed; select backdrop from `Variant["material"]`),
  `src/Mod/assets/scribe/itemtypes/scribetablet.json` (`material` axis → the four composite states +
  `shapeByType`), the grid recipes (output the discrete variant codes), and `lang/en.json` (four
  `item-scribetablet-*` name/desc keys).
- **Recipe (revised 2026-08-02):** `scribetablet-clay.json` is three shaped recipes (red/blue/fire),
  each `KCC,SCC` (3×2): 8 clay (`game:clay-{type}` ×2 per `C` cell) + 1 `game:stick` + 1 knife
  (`tags: ["tool-knife"]`, `isTool: true`, `toolDurabilityCost: 3` — not consumed, wears durability).
  Replaces the earlier thin `1 clay + 1 stick` recipe, which was reported not discoverable in-game and
  blocked testing the per-type backdrops.
- **New assets (small):** possibly one shared page-frame overlay PNG (if the framed rendering path is
  taken); no bespoke full-page art this round — the six clay backdrops pull vanilla textures and wax is
  a placeholder swatch.
- **Verified VS asset codes (1.22.6):** unfired per-type clay swatches
  `game:block/soil/blueclay.png`, `game:block/soil/redclay.png`, `game:block/soil/fireclay.png`
  (the raw-clay item's own `block/soil/{type}clay` texture base; also mirrored at
  `block/clay/{type}clay.png`), all 32×32; fired ceramic `game:block/clay/aged-ceramic1.png`
  (the texture the tablet item already uses; also `block/clay/ceramic.png`), 32×32; wax placeholder
  `game:item/resource/beeswax.png`, 32×32. Clay-type variant axis confirmed at
  `itemtypes/resource/clay.json` → `type: [blue, red, fire]`.
- **Finding to resolve (see Open Questions in design):** vanilla fired ceramic is NOT color-keyed by
  source clay type — blue/red/fire clay all fire to the same generic `aged-ceramic*`/`ceramic`
  textures. So "3 clay types × fired" cannot be visually distinguished from stock fired textures
  without tinting; the three fired backdrops would otherwise be identical.
- **No new mod dependencies, no new network packets, no `src/Core/` change** — clay-type/fired are
  stack attributes carried by the existing save/pickup flow; Core stays game-API-free.
- **Out of scope (deferred):** the soft→fired firing gameplay mechanic (and archive-on-fire), water
  damage, wax-wipe, carry-forward — all remain deferred tablet non-goals; and authoring seven bespoke
  full-page backdrop PNGs (the user's "revisit ... maybe a custom frame" polish).
