## Context

The clay tablet's wet→hard→fired life-cycle is implemented (`add-tablet-firing-mechanic`, in flight)
with `hard` and `fired` as **stack attributes** and all states sharing one placeholder mesh. We now have
authored art — a custom `item/tablet-clay` model and nine per-color/per-state textures (red/blue/fire ×
soft/hard/fired) named `{color}{state}.png` (e.g. `bf` = blue-fired). Vintage Story item icons resolve
textures by **variant code**, not by stack attribute, so surfacing state-specific art forces each holdable
state to be its own variant. `fired` was already slated to become a variant; this change extends that to
`hard` as well, making the `material` axis fully describe (clay color × life-cycle state).

This change is entangled with two in-flight changes that own the affected specs: `add-tablet-firing-mechanic`
(owns `tablet-clay-hardening`, `tablet-firing`, and the state parts of `clay-wax-tablet-item`) and
`add-tablet-clay-type-backdrops` (owns the "fired appearance is a stack attribute" requirement). Both must
archive before this one so the deltas land on real spec text (archive-order drift trap, MEMORY.md).

## Goals / Non-Goals

**Goals:**
- Render the authored `item/tablet-clay` model with the correct per-color, per-state texture for every clay
  tablet a player can hold.
- Make `hard` a registered variant (like `fired`), so all three states are discoverable in handbook/creative.
- Keep the existing soft variant codes (`clay-red/blue/fire`) and `wax` unchanged — no world-migration.
- Derive editability / hard / fired from the variant code at a single seam, so the dialog, backdrop, and
  policy stay consistent.

**Non-Goals:**
- No wax art (wax keeps the placeholder until its own model, built separately from the oak-beam reference).
- No new crafting recipes for hard/fired (they are reached only by hardening/firing).
- No change to persistence, packets, or the document codec.
- No change to the read-only `UneditableTablet` Core policy or any Core code.

## Decisions

### Decision 1 — Variant list: explicit 10-state suffix list, not a state cross-product

The `material` axis becomes:
`[clay-red, clay-blue, clay-fire, clay-red-hard, clay-blue-hard, clay-fire-hard, clay-red-fired,
clay-blue-fired, clay-fire-fired, wax]`.

Soft keeps the bare code; hard/fired add `-hard`/`-fired` suffixes.

- **Why not a `claycolor × state` cross-product** (`states: [red,blue,fire] × [soft,hard,fired]`)? It would
  rename `clay-red` → `clay-red-soft`, breaking every saved item and every existing lang key, and it would
  generate `wax-hard`/`wax-fired` that need `skipVariants`. The explicit suffix list keeps soft codes stable
  and needs no skip rules.
- **Alternative — runtime mesh override** (keep hard an attribute, swap the mesh in `OnBeforeRender` keyed on
  the attribute): rejected. It would show the art but leave hard undiscoverable in handbook/creative (the
  user's explicit requirement, per the raw→cooked-meat precedent), and adds a cached `MeshRef` render path
  for no gain over a variant.

### Decision 2 — State derived from the variant code, at one seam

`ItemScribeTablet` gains a single resolver that maps a stack's `material` variant to a state enum
(`Wet`/`Hard`/`Fired`) by suffix (`-fired` → Fired, `-hard` → Hard, else soft/Wet; unrecognized → Wet-red).
`ReadHard`/`ReadFired`/`IsEditable` delegate to it. Everything downstream — `OpenTabletDialog`, the dialog's
`_state`, `ScribeBackdrops.StateOf`/`ForTablet`, and `TabletHost.Policy` — keeps reading from that resolver,
so no caller changes shape; only the *source* of the state flips from attribute-read to variant-parse.

- **Why not read the attributes still?** Once state is carried by the variant, an attribute would be a second
  source of truth that can disagree with the icon. Deriving from the variant makes the item's appearance and
  its behavior provably consistent.

### Decision 3 — `OnTransitionNow`/`DoSmelt` stop setting attributes; the variant swap carries state

The `Harden` `transitionedStack` already targets `clay-<color>-hard`, and the firepit `smeltedStack` targets
`clay-<color>-fired`, so the native transform produces the correct variant. The overrides now only copy the
document forward (their existing null-guarded document carry) and DROP the `hard = true` / `fired = true`
attribute writes — the variant already encodes state. Rehydration swaps `-hard` back to the soft variant
(building the soft stack explicitly) instead of clearing an attribute.

### Decision 4 — Firing uses `smeltingType: "cook"` (firepit yes, pit kiln no)

Confirmed against the DLLs: `CollectibleObject.CanSmelt` rejects only `Fire`-type items outside a kiln, so
`cook` fires in a firepit; and `BlockEntityGroundStorage.OnTryCreateKiln` only forms a kiln over
`SmeltingType == Fire (4)` items, so `cook` auto-blocks the pit kiln with no guard code. This satisfies
"firepit only; block the kiln" (which otherwise blanks the document, since `OnFired` drops attributes and
can't be hooked without Harmony) with a one-word JSON value.

### Decision 5 — Texture wiring: `texturesByType` feeds the shape's `ff` + `writing` keys

The `tablet-clay` shape uses two texture keys: `ff` (the tablet body, spanning the `default`/`ff` faces) and
`writing` (the engraved overlay). The itemtype supplies both per variant via `texturesByType`: each variant's
`ff` → its `scribe:items/{color}{state}` PNG, `writing` → the shared `scribe:items/writing`. Wax's entry keeps
the placeholder shape+texture.

## Risks / Trade-offs

- **[Archive-order drift]** These deltas MODIFY/RENAME requirements owned by two unarchived changes → if this
  change is applied before them, `openspec validate`/archive can't locate the target headers. **Mitigation:**
  task 0 gates on both predecessors being archived; headers here are copied verbatim from their current
  delta text.
- **[Existing hard/fired items in a test world carry the old attributes]** A tablet that hardened under the
  attribute implementation is still the soft variant with `hard = true`. **Mitigation:** dev worlds are
  disposable; the variant resolver treats an unrecognized/soft variant as Wet, so such a stack simply reads
  as editable again rather than crashing — acceptable for pre-release. No migration code.
- **[Wax visual mismatch]** Wax stays on the placeholder while clay gets custom art, so the set looks
  half-finished. **Mitigation:** documented Non-Goal; wax art is tracked separately.
- **[Six new lang keys]** Missing handbook/creative names show as raw keys. **Mitigation:** add all six
  `-hard`/`-fired` display names in the same pass; verify in creative search.

## Migration Plan

1. Archive `add-tablet-clay-type-backdrops`, then `add-tablet-firing-mechanic` (order per their own specs).
2. Apply this change: itemtype JSON (variant list, shape, textures, transition/smelt stacks), then the
   `ItemScribeTablet` state-resolver flip, then lang.
3. Rollback: revert the itemtype variant-list edit and the resolver; the placeholder mesh and attribute path
   still function, so a partial revert degrades to the prior behavior rather than breaking.

## Open Questions

- None blocking. Wax's eventual model/texture is out of scope and tracked separately.
