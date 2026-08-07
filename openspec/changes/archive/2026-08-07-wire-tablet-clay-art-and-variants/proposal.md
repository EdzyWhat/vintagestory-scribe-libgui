## Why

The clay tablet's three life-cycle states (wet → hard → fired) currently all render the same
placeholder vanilla clutter mesh (`game:block/clutter/tablet-clay1`) with a generic aged-ceramic
texture, and only `fired` is a discoverable item — `hard` is a transient stack attribute the
handbook and Creative inventory can't enumerate. We now have authored art: a custom tablet model
(`item/tablet-clay`) and nine per-color/per-state textures (red/blue/fire × soft/hard/fired). To
surface that art, every state a player can hold must be its own item variant, because item icons
select textures by **variant code**, not by stack attribute.

## What Changes

- **Promote `hard` from a stack attribute to a registered variant**, alongside `fired`. The
  tablet `material` axis grows from `[clay-red, clay-blue, clay-fire, wax]` to a 10-state list:
  each clay color gains `-hard` and `-fired` siblings; existing wet codes and `wax` are unchanged
  (no world-migration of already-placed items). **BREAKING** relative to the still-in-flight
  `add-tablet-firing-mechanic`: this reverses that change's "hard is a stack attribute" decision.
- **Wire the authored art**: `shapeByType` → the new `scribe:item/tablet-clay` model for all clay
  variants; `texturesByType` maps each variant's body texture to its color+state PNG
  (`rs/rh/rf`, `bs/bh/bf`, `fs/fh/ff`) plus the shared `writing` overlay. Wax stays on the
  placeholder until its own model lands.
- **Re-point the transition/firing stacks at variants**: `Harden`'s `transitionedStack` →
  the `-hard` variant (was → itself); firepit `smeltedStack` for both the wet and `-hard` inputs
  → the `-fired` variant.
- **Derive tablet state (editable / hard / fired) from the variant code**, not from `hard`/`fired`
  stack attributes — in `ItemScribeTablet`, the dialog, the backdrop resolver, and the policy
  selector. `OnTransitionNow`/`DoSmelt` stop *setting* those attributes (the variant swap now
  carries state) and only carry the document forward.
- All three clay states appear in the handbook and Creative inventory (fired discoverability now
  extends to hard), matching the raw→cooked-meat precedent.

## Capabilities

### New Capabilities
<!-- none — this refines existing tablet capabilities rather than introducing a new one -->

### Modified Capabilities
- `clay-wax-tablet-item`: the `material` variant axis expands to include per-clay-color `-hard`
  and `-fired` states; clay variants render a custom `item/tablet-clay` model with per-color,
  per-state textures instead of the shared placeholder clutter mesh.
- `tablet-clay-hardening`: the hardened tablet is a registered `-hard` variant rather than a
  `hard` stack attribute; the `Harden` transition targets that variant, and read-only/editability
  is derived from the variant code.
- `tablet-firing`: the firepit `smeltedStack` targets the `-fired` variant from both the wet and
  `-hard` inputs; fired state is derived from the variant code.

## Impact

- **Assets**: `src/Mod/assets/scribe/itemtypes/scribetablet.json` (variant list, `shapeByType`,
  `texturesByType`, transition/combustible stacks); new `shapes/item/tablet-clay.json` model; nine
  `textures/items/{r,b,f}{s,h,f}.png` + `writing.png`.
- **Code**: `src/Mod/ItemScribeTablet.cs` (state derived from variant, not attribute),
  `GuiDialogScribeTablet.cs`, `ScribeBackdrop.cs`, `TabletHost.cs` — the state-resolution seam.
  No Core changes; the read-only `UneditableTablet` policy is unaffected.
- **Lang**: handbook/creative names for the six new variants in `lang/en.json`.
- **Sequencing**: this change modifies the `tablet-clay-hardening` and `tablet-firing` specs owned
  by the in-flight `add-tablet-firing-mechanic` change, so it MUST be applied/archived AFTER that
  change (archive-order drift trap). Its deltas are authored against that change's post-archive
  spec text.
- **No new dependencies**; no persistence/packet changes (documents still ride the ItemStack via
  the existing `ScribeNotebookSaveMessage`).
