## Context

Crafting the Clockmaker's Notebook (`scribe:scribeclockmakernotebook`) is gated to the vanilla
`tinkerer` trait via a single grid recipe (`recipes/grid/scribeclockmakernotebook.json`,
`requiresTrait: "tinkerer"`). The only escape hatch is the operator-only, world-wide
`scribeClockmakerRequiresTrait false` worldconfig lever (read in
`ScribeModSystem.ServerLifecycle.cs`). This change adds a per-player, in-world path: a trader-sold
schematic that unlocks the craft for whoever holds it.

The closest vanilla precedent — confirmed by decompiling `VSSurvivalMod.dll` and reading the
shipped assets — is the **Glider Schematic** (`game:schematic-glider`):
- It is a plain paper item (`itemtypes/utility/schematic.json`, `maxstacksize: 1`, `GroundStorable`,
  no custom C# class) used purely as a recipe ingredient — no knowledge/recipe-unlock code exists.
- Its "reusable blueprint" behavior comes entirely from the recipe: the glider recipe references it
  with **`consume: false`**, so it stays in the grid while the other ingredients are consumed. It is
  **not** `isTool` (no durability) and **not** a `returnedStack`. The obsolete
  `attributes.noConsumeOnCrafting` flag on the itemtype is superseded by recipe-level `consume`.

Trader wares are **not** inline in the trader entitytype. Each trader loads a shared tradelist via
`tradePropsFile: "config/tradelists/trader-{type}"`; the file's root is a `TradeProperties` object
with a `selling.list` array of `TradeItem` entries. So the non-invasive way to add a ware is a JSON
patch appending to that array — not editing the entitytype. The Commodities and Treasure Hunter
tradelists are `game:config/tradelists/trader-commodities.json` and `trader-treasurehunter.json`;
each is shared across all gender/climate variants of its type.

## Goals / Non-Goals

**Goals:**
- A trait-free crafting path for the Clockmaker's Notebook, keyed to holding a schematic item.
- Reuse vanilla mechanics (`consume: false`, tradelist JSON patch) — no new C# behavior if avoidable.
- Leave the existing trait-gated recipe and the `scribeClockmakerRequiresTrait` lever untouched.
- Sell the schematic through the Commodities and Treasure Hunter traders, priced as a rare find.

**Non-Goals:**
- A persistent per-player "recipe unlocked" knowledge flag (we use held-item gating, like the glider).
- Changing the Clockmaker's head start (they still craft with no schematic).
- Selling it through other trader types, or guaranteeing it appears in every trader's stock.
- Any `src/Core/` change — this is assets + optional thin item plumbing only.

## Decisions

### Decision 1: Reusable blueprint via `consume: false`, not code
Model the schematic exactly on the Glider Schematic: a second grid recipe for the Clockmaker's
Notebook that includes the schematic ingredient with `consume: false`, and omits `requiresTrait`.
- **Why:** It is the canonical vanilla pattern, needs zero custom code, and "reusable" falls out for
  free. One purchase → unlimited crafts.
- **Alternatives considered:** (a) `isTool: true` — gives the schematic durability we don't want and
  would eventually destroy it. (b) `returnedStack` — hands back a fresh stack, more moving parts than
  `consume: false`. (c) A real per-player knowledge unlock — requires custom persistence and packet
  work for no player-visible benefit over held-item gating.

### Decision 2: Second recipe file, existing recipe untouched
Add a new recipe (either a second entry in the existing `scribeclockmakernotebook.json` array or a
new sibling file — implementer's choice; a separate file reads more clearly) that outputs the
Clockmaker's Notebook, requires the schematic, and has **no** `requiresTrait`. The current
trait-gated recipe stays byte-for-byte.
- **Why:** Additive and low-risk — Clockmakers keep their no-schematic path; the worldconfig lever is
  unaffected; both recipes coexist because VS matches any recipe whose pattern and ingredients are
  satisfied.
- **Carryover is already covered:** `notebook-craft-carryover` specifies the crafted Clockmaker's
  Notebook carries over the source Notebook's document + history for *any* recipe, so the new recipe
  inherits that behavior with no spec change. The new recipe must keep the same output code and the
  Notebook ingredient so the carryover handler fires identically.

### Decision 3: Schematic is a plain item — no custom C# class (pending confirmation in tasks)
Define `scribe:clockmakerschematic` as a bare itemtype (`maxstacksize: 1`, `GroundStorable`,
`creativeinventory`, a handbook `extraSections` block), mirroring the vanilla schematic. It needs no
`class:` because it carries no behavior beyond being a recipe ingredient.
- **Why:** Least code; matches the glider precedent. If a future need arises (e.g. a tooltip hint) an
  item class can be added later.
- **Art:** New paper-blueprint texture + shape. Simplest is a flat 2D item texture (`textures/item/…`)
  with no custom shape, unlike the notebooks' 3D `item/notebook` shape — a schematic reads fine as a
  flat rolled-paper icon. Final art is a task.

### Decision 4: Trader availability via JSON patch on the two tradelists
Add `src/Mod/assets/scribe/patches/trader-clockmakerschematic.json` with two `add` ops, each
appending one `TradeItem` to `game:config/tradelists/trader-{commodities,treasurehunter}.json` at
path `/selling/list/-`.
- **Ware entry:** `{ code: "scribe:clockmakerschematic", type: "item", stacksize: 1,
  stock: {avg:1, var:0}, price: {avg: <N>, var: <M>} }` in temporal gears. Rare + single-stock,
  modeled on the treasure-hunter's `locatormap-treasures`/gem entries.
- **Why patch, not entitytype edit:** Wares live in the tradelist, shared across all variants of a
  type; a patch appends without overwriting existing wares and automatically hits every
  gender/climate variant.
- **Probabilistic appearance:** only `maxItems` of the shuffled list show per trader (8 for
  commodities, 14 for treasurehunter). A single appended entry appears with probability
  `maxItems/listLen`. We accept probabilistic appearance (a rare schematic *should* be a lucky find);
  we do **not** bump `maxItems` (that would reshape the whole trader). This is a deliberate trade-off,
  called out below.

## Risks / Trade-offs

- **[Probabilistic stock]** With one entry among ~26 (commodities) / ~43 (treasurehunter), the
  schematic won't appear at every trader. → Intended (rare find). If playtesting shows it's *too*
  rare, the fallback is to raise its odds by adding it to more trader types or nudging `maxItems`,
  decided after in-game observation — not up front.
- **[Two recipes for one output]** Both recipes could theoretically be satisfiable at once (a
  Clockmaker who also holds a schematic). → Harmless: VS just matches one; output is identical. No
  duplication or conflict.
- **[Vanilla tradelist path/domain drift]** The patch targets `game:config/tradelists/…`; if a future
  VS version renames or restructures those files the patch silently no-ops. → Low risk (stable vanilla
  paths); a load-time log-check during playtest confirms the ware resolved. Patch failure degrades
  gracefully (schematic simply isn't sold; craft path via creative/other still works).
- **[Price balance]** Gear price is a guess until playtested. → Ship a reasonable value, tune from
  feedback; it's a one-number asset edit.
- **[Already-spawned traders]** Existing traders keep their rolled stock until their restock tick
  (~24h) or a fresh spawn. → Expected vanilla behavior; note it in testing so it's not read as "patch
  didn't work."

## Open Questions

- **Gear price + variance** for the ware entry — pick a starting value in tasks (proposed: `avg 12,
  var 3`, matching treasure-hunter rare items) and tune in playtest.
- **Item art**: flat 2D rolled-paper icon vs. a 3D shape. Proposed flat 2D for simplicity; confirm
  when creating the texture.
- **Recipe pattern/ingredients** for the schematic recipe: reuse the trait recipe's `Notebook + gear
  + metal-parts` set plus the schematic, or a lighter set? Proposed: keep the same ingredient set (so
  the two paths cost the same materials, differing only by trait-vs-schematic) and add the schematic
  with `consume: false`. Confirm in tasks.
