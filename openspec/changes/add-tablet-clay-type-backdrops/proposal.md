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

- **Record clay-type and fired-state on the tablet stack.** The item has no such data today — it
  declares only `material: [clay, wax]` (firing was deferred as a non-goal of Proposal B). This
  change records a `clayType` (red/blue/fire) and a `fired` (soft/fired) value on the tablet
  `ItemStack` as attributes, set at craft time from the clay ingredient used and preserved across
  persistence and drop/pickup — the same stack-attribute discipline the existing docId uses. This is
  the appearance-record ONLY; it does not add the soft→fired firing gameplay mechanic (still deferred).
- **Select one of seven backdrops from that stack state.** `GuiDialogScribeTablet` picks its backdrop
  from `material` + `clayType` + `fired`: 6 clay backdrops (3 types × soft/fired) + 1 wax. Absent
  attributes default to red + soft so older/handbook stacks still resolve.
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
  from the tablet stack's `material` / `clayType` / `fired` state.
- `clay-wax-tablet-item`: gains a requirement that the tablet **records** its clay type and
  fired-state on the stack, set at craft from the clay ingredient and preserved across persistence and
  drop/pickup — without adding the firing gameplay mechanic.
- `gui-backdrop`: gains a rendering mode so a backdrop spec can tile a small material swatch at native
  resolution and/or composite a shared page-frame overlay, not only stretch a single full-page texture
  to fill.

## Impact

- **Modified code:** `src/Mod/ScribeBackdrop.cs` (7 specs + optional tiling/frame fields on
  `ScribeBackdropSpec`), `src/Mod/ScribeDialogBase.Layout.cs` (`WrapBackdrop` composites tile + frame),
  `src/Mod/GuiDialogScribeTablet.cs` (select backdrop from stack state), `src/Mod/ItemScribeTablet.cs`
  and/or `src/Mod/TabletHost.cs` (read/write `clayType` + `fired` attributes),
  `src/Mod/assets/scribe/itemtypes/scribetablet.json` and the grid recipes (record clay type at craft).
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
