## 0. Sequencing precondition

- [ ] 0.1 Apply/archive AFTER `add-tablet-clay-type-backdrops` AND `add-tablet-firing-mechanic` are archived,
  so the requirements this change MODIFIES/RENAMES exist in `openspec/specs/`. Headers here were copied
  verbatim from those changes' pending deltas (archive-order drift trap, MEMORY.md). Run
  `openspec validate wire-tablet-clay-art-and-variants` after both predecessors archive and reconcile any
  header drift before applying.

## 1. Variant list + model/texture wiring (scribetablet.json)

- [x] 1.1 Expand the `material` variant list to the explicit 10 states: keep `clay-red`, `clay-blue`,
  `clay-fire`, `wax`; add `clay-red-hard`, `clay-blue-hard`, `clay-fire-hard`, `clay-red-fired`,
  `clay-blue-fired`, `clay-fire-fired`. (No `skipVariants`, no rename of the soft codes.)
- [x] 1.2 Point `shapeByType` for all nine clay variants at `scribe:item/tablet-clay`; leave `wax` on the
  placeholder `game:block/clutter/tablet-clay1`.
- [x] 1.3 Replace the single `textures` block with `texturesByType`: for each clay variant map shape key
  `ff` → `scribe:items/{color}{state}` (rs/rh/rf, bs/bh/bf, fs/fh/ff) and `writing` →
  `scribe:items/writing`; `wax` keeps the placeholder `ceramic`/`writing` mapping. Confirm the shape's two
  texture keys (`ff`, `writing`) are the ones fed.
- [x] 1.4 Repoint `transitionablePropsByType`: declare `Harden` ONLY on the three SOFT clay variants, with
  `transitionedStack` → `scribe:scribetablet-clay-<color>-hard` (was → itself). No Harden on `-hard`,
  `-fired`, or `wax`.
- [x] 1.5 Repoint `combustiblePropsByType`: declare firing on the SOFT and `-hard` clay variants (six),
  `smeltedStack` → `scribe:scribetablet-clay-<color>-fired`, and change `smeltingType` `"fire"` → `"cook"`
  (firepit-fires + auto-blocks the pit kiln). No combustible props on `-fired` or `wax`.

## 2. State derived from the variant code (ItemScribeTablet.cs)

- [x] 2.1 Add a single private state resolver mapping a stack's `material` variant to `TabletState` by
  suffix: `-fired` → Fired, `-hard` → Hard, else Wet; unrecognized/absent → Wet. Read the variant off the
  stack (`stack.Collectible`/`Item.Variant["material"]` — not `this.Variant`, since a helper takes an
  arbitrary stack).
- [x] 2.2 Reimplement `ReadFired`/`ReadHard`/`IsEditable` in terms of the resolver (Fired/Hard/soft), and
  drop the `FiredAttributeKey`/`HardAttributeKey` reads. Keep the public signatures so callers are untouched.
- [x] 2.3 `OnTransitionNow`: keep the null-guarded document carry onto the hardened output; REMOVE the
  `SetBool(HardAttributeKey, true)` write (the `-hard` variant now carries state). Verify the output stack is
  the `-hard` variant the transition built.
- [x] 2.4 `DoSmelt`: keep the pre-smelt document capture + carry onto the fired output; REMOVE the
  `SetBool(FiredAttributeKey, true)` write. Verify the output is the `-fired` variant.
- [x] 2.5 `Soften`: swap the `-hard` stack to the SOFT variant of the same clay color (build the soft
  itemstack via the resolver/variant, transferring the document) and reset the `transitionstate` timer,
  instead of removing a `hard` attribute. No-op on a `-fired` or already-soft stack. Keep it
  server-authoritative and cheap.
  - `Soften` now RETURNS the replacement soft stack (or null) rather than mutating in place, since the item
    code changes: it builds `new ItemStack(world.GetItem(CodeWithVariant("material", <base color>)))`, carries
    the document via `CarryStackData`, and copies NO `transitionstate` (so the tick re-seeds the timer from
    now — supersedes the old "remove the transitionstate attribute" mechanic). The two callers (`OnGroundIdle`,
    `OnHeldIdle`) assign the returned stack back (`entityItem.Itemstack = …` / `slot.Itemstack = …`) then sync.
- [x] 2.6 `OpenTabletDialog`: replace `StateOf(ReadHard, ReadFired)` with the resolver's state for the
  stack, and pass `Variant["material"]` + that state to `ForTablet(...)` and the dialog ctor as before.
  - **Deviation:** passes the resolver's RESOLVED BASE material (e.g. `clay-red`), NOT `Variant["material"]`
    (which now carries the `-hard`/`-fired` suffix). `ForTablet`/theme/glow key off the base clay color, so
    the suffix would miss every switch arm and fall back to red. One `ResolveMaterialState` call yields both
    the base material and the state.

## 3. State-source flip in the resolve seam (backdrop / dialog / policy)

- [x] 3.1 `ScribeBackdrops`: keep `ForTablet(material, state)` as-is, but confirm `StateOf` is no longer the
  attribute-based `(hard, fired)` entry point for the tablet (the item now supplies the resolved state).
  Leave the 9-way clay×state backdrop switch intact — it already keys off `(material, state)`.
- [x] 3.2 Confirm `GuiDialogScribeTablet` (`_state`, `IsEditable`, `EmptyHintLangKey`, `ReadViewIsReadOnly`)
  and `TabletHost.Policy` need NO change: they consume the state/editability the item resolves, so the
  attribute→variant flip is transparent to them. Spot-check by reading each.

## 4. Lang + discoverability

- [x] 4.1 Add display names for the six new variants (`-hard`/`-fired` × red/blue/fire) to `lang/en.json`,
  following the existing tablet name convention, so handbook/creative show real names not raw keys.
- [x] 4.2 Per-variant handbook text (user-requested): give the soft/wax entry a life-cycle overview section
  (wet → hard → fired and how to move between states); give `-hard` and `-fired` their OWN handbook sections
  via `attributesByType` — hard tells the player to dunk it in water (edit again) OR fire it (make permanent)
  and what each means; fired says the writing is permanent and water won't soften it. Confirm NO crafting
  recipe is added for the `-hard`/`-fired` variants (reached only via hardening/firing).
  - Each `attributesByType` entry is SELF-CONTAINED (repeats `groundStorageTransform`): a matched ByType
    entry REPLACES the base `attributes` value (verified against vanilla `seraph/head.json`, which keeps
    universal keys in base `attributes` and only per-variant fields in `attributesByType`). Soft/wax match no
    pattern and fall back to the base handbook block. Added lang keys: `handbook-scribetablet-states-*`
    (soft overview), `-hard-*`, `-fired-*`.
  - **9 pages, 3 texts:** each registered clay variant (color×state) is its own item, so VS auto-generates
    one handbook page per variant (9 total); the two wildcard `attributesByType` patterns (`*-hard`,
    `*-fired`) + base share exactly 3 text bodies across those 9 pages (soft-shared, hard-shared,
    fired-shared) per the user's requirement. The 3 shared bodies are COLOR-AGNOSTIC: the earlier hardcoded
    `red` `<a href=handbook://…-red-hard/-red-fired>` cross-links were REMOVED (they were wrong on blue/fire
    pages and redundant). Verified in `CollectibleBehaviorHandbookTextAndExtraInfo` (VSSurvivalMod.dll) that
    the handbook auto-renders color-correct clickable navigation from each page's own props: a "Processes
    into" section from `transitionableProperties` (soft → its own `-hard`), a smelt section from
    `SmeltedStack` (soft/hard → its own `-fired`), and `addCreatedByInfo` (fired ← same-color soft/hard) —
    so no hand-authored per-color link is needed.

## 5. Verification

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean; `dotnet test` — Core suite still green (no Core
  changes expected; the `UneditableTablet` policy is untouched).
- [ ] 5.2 In-game: pull each of the nine clay variants from Creative; confirm each renders the
  `item/tablet-clay` model with its own color+state texture (red/blue/fire × soft/hard/fired all distinct),
  and wax still shows the placeholder.
- [ ] 5.3 In-game: let a soft clay tablet (with tasks) harden; confirm it becomes the `-hard` variant,
  keeps its document, opens read-only, and shows the hard backdrop.
- [ ] 5.4 In-game: fire a soft AND a hard clay tablet in a firepit; confirm each becomes the `-fired`
  variant of the same color, keeps its document, opens read-only, shows the fired backdrop; confirm a pit
  kiln will NOT form over a clay tablet.
- [ ] 5.5 In-game: rehydrate a `-hard` tablet (drop in water; enter water holding it); confirm it returns
  to the soft variant, editable, document intact, dry-out timer reset.
- [ ] 5.6 In-game: confirm the six `-hard`/`-fired` variants each appear in the handbook and creative
  search with correct names, and none has a crafting recipe.
- [ ] 5.7 Restage (`bash build/restage.sh Debug`) before playtesting so the game loads current DLLs/assets.
