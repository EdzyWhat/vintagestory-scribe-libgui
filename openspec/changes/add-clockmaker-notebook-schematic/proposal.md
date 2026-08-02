## Why

Crafting the Clockmaker's Notebook is gated to the vanilla `tinkerer` trait (the Clockmaker
class). Non-Clockmaker players can only get it if a server operator globally lifts the gate with
`scribeClockmakerRequiresTrait false` — an all-or-nothing, operator-only lever. There is no
per-player, in-world way for a non-Clockmaker to earn the recipe. A trader-sold **Clockmaker's
Notebook Schematic** gives them one: a rare purchase that unlocks the craft for that player,
without weakening the Clockmaker's head start (they still craft it with no schematic).

## What Changes

- Add a new **Clockmaker's Notebook Schematic** item (`scribe:clockmakerschematic`) — a paper
  blueprint modeled on the vanilla Glider Schematic (stacksize 1, ground-storable, its own
  texture and handbook entry).
- Add a **second grid recipe** for the Clockmaker's Notebook that requires the schematic and has
  **no** `requiresTrait`, so any player holding the schematic can craft it. The schematic is a
  **reusable blueprint** — it uses `consume: false`, so it stays in the grid and one purchase
  enables unlimited crafts (the vanilla Glider Schematic pattern).
- The existing trait-gated recipe is **unchanged** — Clockmakers keep their no-schematic path,
  and the `scribeClockmakerRequiresTrait` worldconfig lever is untouched.
- Add the schematic to the **Commodities** and **Treasure Hunter** trader ware pools via a JSON
  patch on their shared tradelists (`config/tradelists/trader-commodities.json` and
  `trader-treasurehunter.json`), priced as a rare, single-stock item in temporal gears.
- Add lang + handbook entries for the schematic, and update the Clockmaker's Notebook handbook
  craft text to mention the schematic path alongside the trait path.

## Capabilities

### New Capabilities
- `clockmaker-notebook-schematic`: A trader-sold schematic item that grants a non-Clockmaker,
  trait-free crafting path for the Clockmaker's Notebook — the item itself, its reusable-blueprint
  recipe behavior, and its availability in the two trader ware pools.

### Modified Capabilities
<!-- None. The existing trait-gated recipe, its worldconfig lever, and the craft-carryover
     behavior are all unaffected; this change is purely additive (a second recipe + a new item +
     trader wares). notebook-craft-carryover already covers "the crafted Clockmaker's Notebook
     carries over the source document/history" for ANY recipe, so it needs no delta. -->

## Impact

- **Assets (`src/Mod/assets/scribe/`)**: new `itemtypes/clockmakerschematic.json`, its texture(s)
  and shape, a second recipe file (or a second entry in `recipes/grid/scribeclockmakernotebook.json`),
  a new `patches/` file targeting the two vanilla tradelists, and lang/handbook strings in
  `lang/en.json`.
- **Code (`src/Mod/`)**: likely an `Item` class for the schematic only if it needs behavior beyond
  a plain paper item (the Glider Schematic is a plain item — a bare itemtype may suffice; TBD in
  design). No `src/Core/` changes. No new mod dependencies — vanilla `game` domain assets only.
- **Trader inventories**: patched tradelists are shared across all gender/climate variants of each
  trader type; already-spawned traders pick up the ware on their next restock. Appearance is
  probabilistic (only `maxItems` of the shuffled list show per trader) — a design decision to
  keep or override.
- **Docs**: CHANGELOG entry; ROADMAP touch if it tracks this. No breaking changes; existing worlds
  and saved Clockmaker's Notebooks are unaffected.
