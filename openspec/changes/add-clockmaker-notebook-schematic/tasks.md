## 1. Schematic item

- [x] 1.1 Create `src/Mod/assets/scribe/itemtypes/clockmakerschematic.json` (code
  `clockmakerschematic`, `maxstacksize: 1`, `GroundStorable`, `creativeinventory` general+items),
  modeled on the vanilla Glider Schematic — a plain item with **no** `class:` (add one only if
  1.2 shows behavior is needed).
- [x] 1.2 Item art: reuse the vanilla Glider Schematic art directly — itemtype `shape` points at
  `game:item/utility/schematic-glider` (per user: "steal the art from the glider schematic, no need
  to make art"). No custom texture/PNG; renders as the vanilla blueprint scroll.
- [x] 1.3 Add lang entries in `src/Mod/assets/scribe/lang/en.json`: item display name
  (`item-clockmakerschematic`) + `-desc`, and handbook `extraSections` title/text
  (`handbook-clockmakerschematic-about-*`) explaining it unlocks the craft without the Clockmaker
  class. Wired the `handbook.extraSections` block in the itemtype.
- [ ] 1.4 Build + restage done (build clean, 52 files staged). **Relaunch and confirm in-game:**
  the schematic appears in Creative search, shows its name/desc + handbook page, and renders as the
  glider-blueprint scroll when held/ground-stored.

## 2. Trait-free schematic recipe

- [x] 2.1 Added `recipes/grid/scribeclockmakernotebook-schematic.json`: 4-wide `BGMS` pattern,
  outputs `scribe:scribeclockmakernotebook`, same Notebook + `gear-temporal` + `metal-parts` set,
  schematic `S` with **`consume: false`**, no `requiresTrait`. Original trait recipe untouched.
- [x] 2.2 Updated `handbook-scribeclockmakernotebook-craft-text` to add a "No Clockmaker class?"
  paragraph pointing at the schematic path, plus the schematic's own handbook page.
- [ ] 2.3 Manually test (non-Clockmaker character): with the schematic in the grid the recipe
  completes and yields the Clockmaker's Notebook; the schematic **remains** in the grid afterward
  (reusable) and the other ingredients are consumed. Craft a second one from the retained
  schematic to confirm reuse.
- [ ] 2.4 Manually test (regression): a Clockmaker (tinkerer trait) still crafts via the original
  no-schematic recipe; a non-Clockmaker with no schematic still cannot craft via the trait recipe.
- [ ] 2.5 Manually test (carryover): crafting via the schematic recipe from a Notebook that has
  tasks/notes/history carries the document + history into the result (per `notebook-craft-carryover`).

## 3. Trader availability

- [x] 3.1 Created `src/Mod/assets/scribe/patches/trader-clockmakerschematic.json` with two `add`
  ops appending the ware to `/selling/list/-` of `game:config/tradelists/trader-commodities.json`
  and `trader-treasurehunter.json`. Entry: `{ code: "scribe:clockmakerschematic", type: "item",
  stacksize: 1, stock: {avg:1, var:0}, price: {avg:12, var:3} }`. `maxItems`/other wares untouched.
- [ ] 3.2 Confirm the patch resolves at load: check the game log for patch-apply confirmation / no
  "could not find file/path" warning for the two tradelists (verify the `game:` domain and the
  `/selling/list/-` pointer are accepted).
- [ ] 3.3 Manually test in-game: spawn/visit Commodities and Treasure Hunter traders (creative
  `/entity spawn` or worldgen) until the schematic appears for sale; confirm price/stock look right
  and buying it yields a working schematic. Note appearance is probabilistic (only `maxItems` of the
  shuffled list show) and already-spawned traders won't restock immediately — spawn fresh ones.
  Tune the gear price if it feels off.
- [ ] 3.4 Confirm a non-target trader (e.g. Survival Goods or Artisan) does NOT list the schematic.

## 4. Staging, docs, and close-out

- [x] 4.1 Confirmed `build/restage.sh` picks up the new itemtype, recipe, patch, and lang additions
  (restage staged 52 files; itemtype/recipe/patch present in staged mod). `package.sh` blanket-copies
  `src/Mod/assets`, so no script edit needed.
- [x] 4.2 Added an `[Unreleased]` CHANGELOG entry (schematic item + trait-free trader-sold craft path).
- [x] 4.3 Added an `add-clockmaker-notebook-schematic` group to `TESTING.md` (7 manual-test items:
  1.4, 2.3, 2.4, 2.5, 3.2, 3.3, 3.4) via the `what-to-test` skill.
- [x] 4.4 Core suite green (197/197) and Debug build clean (no new warnings) — the 2 build warnings
  are pre-existing and unrelated to this change.
