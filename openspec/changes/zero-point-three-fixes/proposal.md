## Why

The 0.3 tablet playtest surfaced three defects. (1) The Clockmaker's Notebook **schematic** recipe never
works and never shows in the handbook — root cause: the recipe is authored `width: 4`, but the crafting
grid is 3×3 (no 4-wide grid recipe exists anywhere in the game), so it can neither be placed nor listed
as a "Created by" entry. This also hides the vanilla dual-recipe display the player expected (a schematic
path plus the trait-gated path with a `* Requires Tinkerer trait` asterisk, like the Sling page), which
the handbook renders automatically once both recipes are valid. (2) The only ways to rehydrate a hard
tablet are passive — dropping it in water or swimming while holding it — with no deliberate, discoverable
gesture. Players expect the vanilla metal-quench affordance (crouch + right-click a water container).
(3) A hand-fired or hardened (read-only) clay tablet renders its GUI backdrop uniformly semi-transparent.
Root cause (measured, not theorized): LibGUI reuses ONE `SKPaint` (`PaintingContext.SharedPaint`) across
every draw op AND across frames, and its textured-box draw op (`DrawMaskedBox`, which paints our clay
backdrop) reuses that paint's color WITHOUT setting it — so the backdrop bitmap is modulated by whatever
color the previous frame's LAST draw op left. On the read-only tablet the last box painted each frame is
the always-on scrollbar track (theme-default alpha 0.1), so the next frame's backdrop draws at ~alpha 0.1.
The editor and the tabbed Lectern/Notebook views paint an OPAQUE footer button last, so they never leak —
which is exactly why only the read-only tablet shows it. (The retired "tint the soft art" plan and the
authored PNGs were both ruled out by measurement: every state's PNG is ~97% opaque and no tint path runs.)

This change is the first entry in a `zero-point-three-fixes` bucket for the 0.3 pass; further fixes will
land here as they're triaged.

## What Changes

- **Fix the schematic recipe grid fit.** Repack the Clockmaker's Notebook schematic recipe from an
  unusable `4×1` (`"BGMS"`) into a `2×2` layout so it registers as a valid grid recipe — making it both
  craftable and listed in the handbook. No ingredient/output change (Notebook + temporal gear + metal
  parts + reusable schematic); only the grid shape moves.
- **Restore the Sling-style dual-recipe handbook display.** With both recipes valid, the vanilla handbook
  (`addCreatedByInfo`) auto-renders both as **two separate "Created by" grids** — the trait-gated 3×1 with
  its `* Requires Tinkerer trait` asterisk and the schematic 2×2 with none. No custom handbook code; the
  fix is entirely the recipe validity above. (Recipes intentionally left ungrouped — no shared
  `recipegroup` — so both paths show side by side rather than cycling.)
- **Add a crouch + right-click quench rehydration path.** Extend `ItemScribeTablet.OnHeldInteractStart`
  so that crouch (ShiftKey) + right-click while aimed at a water-holding liquid container softens a hard
  tablet back to wet, reusing the existing `Soften` / `CarryStackData` variant-swap machinery — only the
  trigger is new. This is **additive**: the existing drop-in-water (`OnGroundIdle`) and swim-while-holding
  (`OnHeldIdle`) paths remain. Must intercept only when actually aimed at a water container so it doesn't
  swallow the existing crouch-right-click ground-storage placement.
- **Fix the read-only tablet's transparent backdrop.** Wrap the themed backdrop `Container` in a tiny
  transparent single-child render widget (`ScribeResetPaintColor`) that forces `PaintingContext.SharedPaint`
  opaque-white immediately before the backdrop paints, so the backdrop's `DrawMaskedBox` always modulates
  the clay art by opaque white regardless of what the previous frame's last op left. The fix lives in Scribe
  code because the leaky draw ops are in the vendored `Gui.dll` (can't edit). Frame-order-independent and
  view-agnostic; draws nothing of its own, so views that already rendered correctly are unchanged.
- **Raise the clay-tablet recipe cost from 8 to 12 clay.** Each of the three clay tablet grid recipes
  (`clay-red`/`clay-blue`/`clay-fire`) currently uses a `3×2` layout with clay in 4 cells at quantity 2 =
  8 clay. Repack the clay into a `2×3` block (6 cells × quantity 2 = 12 clay), keeping the same knife +
  stick and the same single-tablet output. No change to the wax recipe (no clay). This makes a clay tablet
  cost a bit more raw clay, matching the intended material budget.

## Capabilities

### New Capabilities
<!-- none: the fixes modify behavior specified in existing capabilities, or tune values the canon spec
     leaves unspecified (the clay-tablet clay cost — clay-wax-tablet-item says only "clay + sticks style",
     not a quantity, so the 8→12 bump needs no spec delta). -->

### Modified Capabilities
- `clockmaker-notebook-schematic`: the schematic craft path SHALL use a grid layout that fits the 3×3
  crafting grid (so the recipe is craftable), and the Clockmaker's Notebook handbook entry SHALL show
  both the schematic recipe and the trait-gated recipe as distinct "Created by" grids (the trait recipe
  marked with the vanilla `* Requires <trait> trait` asterisk).
- `tablet-clay-hardening`: a hard clay tablet SHALL also rehydrate to wet via a deliberate crouch +
  right-click gesture aimed at a water-filled liquid container, in addition to the existing passive
  water-exposure paths.
- `gui-backdrop`: a themed-mode textured backdrop SHALL render at its authored opacity independent of
  what any prior frame drew (a new ADDED requirement guarding against the shared-paint cross-frame color
  leak that faded the read-only tablet backdrop).

## Impact

- **Code:** `src/Mod/ItemScribeTablet.cs` (`OnHeldInteractStart` gains the quench branch; reuses existing
  `Soften`/`CarryStackData`/`ResolveMaterialState`). Possibly a splash/sizzle sound + `PreventDefault`
  handling. `src/Mod/assets/scribe/recipes/grid/scribeclockmakernotebook-schematic.json` (grid reshape).
  `src/Mod/assets/scribe/recipes/grid/scribetablet-clay.json` (clay 8→12 via a 2×3 clay block, all three
  color variants). Possibly `src/Mod/assets/scribe/lang/en.json` (a world-interaction hint for the quench).
  `src/Mod/ScribeBackdropPaintReset.cs` (NEW — the `ScribeResetPaintColor` shared-paint reset wrapper) and
  `src/Mod/ScribeDialogBase.Layout.cs` (`WrapBackdrop` wraps the backdrop `Container` in it).
- **APIs:** vanilla `BlockLiquidContainerBase.GetContent(pos)` / `WaterTightContainableProps` /
  `ICoolingMedium` to detect a water container; `blockSel.Position` + `world.BlockAccessor.GetBlockEntity`.
  Vanilla `requiresTrait` grid-recipe field + the handbook's automatic `addCreatedByInfo` rendering (no
  custom handbook code). LibGUI `SingleChildWidget`/`RenderProxyBox`/`PaintingContext.SharedPaint` for the
  backdrop paint-reset wrapper. No new mod dependency.
- **Archive order:** the target capabilities (`clockmaker-notebook-schematic`, `tablet-clay-hardening`,
  `gui-backdrop`) — the first two — are currently defined in **unarchived sibling changes**
  (`add-clockmaker-notebook-schematic`, `add-tablet-firing-mechanic`/`wire-tablet-clay-art-and-variants`).
  This change's deltas MODIFY requirements those changes introduce, so it must archive AFTER them — and its
  delta headers must match the canon wording those changes establish on archive (see the archive-order
  header-drift lesson). The `gui-backdrop` delta is a self-contained ADDED requirement against already-canon
  `gui-backdrop`, so it has no such ordering constraint.
- **Testing:** in-game — schematic becomes craftable (2×2) and both recipes render in the handbook with
  the trait asterisk on the trait one; crouch + right-click a bucket/barrel of water softens a hard tablet
  while an empty or non-water container does nothing and the ground-storage placement still works; a
  hand-fired or hardened tablet opens with a fully OPAQUE backdrop (no semi-transparent see-through), while
  the wet editor and the tabbed Lectern/Notebook views look unchanged.
