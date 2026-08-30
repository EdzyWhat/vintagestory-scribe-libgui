## Why

A ModDB user (Applesauce_) reported that their Notebook's History never records Temporal Storms
or Deaths despite the notebook genuinely being on their person, on a real modded multiplayer
server. Root-cause investigation (recorded in memory, and summarized in design.md) traced this to
`CarriedInventoryClasses` in `ScribeModSystem.History.cs` — a hardcoded allow-list of four vanilla
inventory `ClassName`s (`hotbar`/`backpack`/`character`/`mouse`). A decompiled survey of the
popular player-storage mod landscape found that xSkills' "Strong Back" survival ability
permanently adds its own custom inventory (`ClassName = "xskillshotbar"`) directly into the
player's `PlayerInventoryManager.Inventories` — invisible to the allow-list, so a Notebook stored
there silently never records history. No other mechanism among 12 surveyed mods reproduces this,
but the allow-list is structurally blind to *any* future mod that does the same thing.

Separately, the CarryOn mod family (also surveyed) lets a player carry a container block (e.g. a
chest) on their back/hands/shoulder. That block's contents are frozen into a raw attribute blob
rather than a live inventory, so a Notebook stored inside a carried container is invisible to
Scribe's carried-notebook scan by a completely different mechanism — not a wrong `ClassName`, but
no live inventory to scan at all. The mod author wants this covered too, since players carrying a
back-worn chest with a Notebook inside it is a real, common CarryOn use pattern.

## What Changes

- Replace the `CarriedInventoryClasses` allow-list with a type check
  (`inv is InventoryBasePlayer`) plus a short denylist of exactly the two vanilla inventory
  classes that are player-owned but must still be excluded: `creative` (writing history mutates
  the infinite creative-tab template — the original bug fixed by `c36e1cb`) and `ground`
  (transient staging, not on-person). This makes any current or future mod-added
  `InventoryBasePlayer`-derived inventory (e.g. xSkills' Strong Back bag) visible to
  `FindCarriedNotebooks` automatically, without needing to know its `ClassName` in advance.
- **BREAKING (behavior change, not API):** `craftinggrid` moves from excluded to included — a
  Notebook sitting in the 3×3 crafting grid now counts as "carried" and will record Death/PvpKill/
  TemporalStorm history, whereas previously it did not.
- Add a new, optional detection path for Notebooks stored inside a block currently carried via the
  CarryOn mod family (CarryOnLib's public `ICarryManager` API). This path is only active when
  CarryOn is installed and enabled (`IsModEnabled` check, consistent with the mod's existing soft-
  dependency pattern for Immersive Lanterns/ConfigLib) and only runs on the same rare events as
  today (player death, PvP kill, temporal storm rising edge) — never per-tick.
- Requires an explicit decision on *how* Scribe invokes CarryOnLib's API without taking a hard
  mod dependency (reflection-only vs. a non-shipped compile-time reference) — see design.md; this
  is a new category of soft integration for this mod (deeper than the existing "detect a side
  effect via `IsModEnabled`" pattern used for Immersive Lanterns) and needs explicit sign-off.

## Capabilities

### New Capabilities
(none — this extends the existing `notebook-history` capability's carried-scope definition)

### Modified Capabilities
- `notebook-history`: the "carried on person" scope referenced by the Death, PvpKill, and
  TemporalStorm requirements changes from a `ClassName` allow-list to a type-based check
  (`InventoryBasePlayer` minus `creative`/`ground`), which now includes `craftinggrid` and any
  mod-added player-owned inventory (e.g. xSkills' Strong Back). It also gains a new detection path
  for Notebooks inside a container block carried via the CarryOn mod family, active only when that
  mod is installed.

## Impact

- `src/Mod/ScribeModSystem.History.cs` — `CarriedInventoryClasses`/`FindCarriedNotebooks` redesign;
  new CarryOn-aware scan path added alongside it.
- No `Core` changes — this is entirely in the game-facing adapter layer (`Core` never referenced
  vanilla or mod inventory types).
- No new hard mod dependency. If the CarryOn integration proceeds via a non-shipped compile-time
  reference to `CarryOnLib.dll` (see design.md open question), that reference itself needs the
  same review a `ConfigLib`-style soft dependency would get.
- Behavior-visible to players: notebooks in the crafting grid, in any mod-added bonus player
  inventory, and (if the CarryOn path ships) inside a carried container, now participate in
  automatic history recording where they previously did not.
