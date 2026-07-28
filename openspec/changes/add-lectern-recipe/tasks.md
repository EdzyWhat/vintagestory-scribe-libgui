## 1. Author the recipe asset

- [x] 1.1 Create the directory `src/Mod/assets/scribe/recipes/grid/` (new; the mod has no recipes yet).
- [x] 1.2 Add `src/Mod/assets/scribe/recipes/grid/scribelectern.json` — a shaped 3×3 grid recipe with
  pattern rows `F R L` / `P P P` / `B P N` and ingredients: `F` = `game:feather`, `R` =
  `game:paper-parchment`, `L` = `game:leather-normal-plain`, `P` = `game:plank-*` (×4), `B` =
  `game:bowl-*-fired`, `N` = `game:metalnailsandstrips-*`. Set `width: 3`, `height: 3`.
- [x] 1.3 Add the ink requirement as `attributes.liquidContainerProps`: `requiresContent` =
  `{ type: "item", code: "game:dye-black" }`, `requiresLitres: 1`, `consumeContainer: true` (mirror
  `assets/survival/recipes/grid/inkandquill.json`).
- [x] 1.4 Set `output` = `{ type: "block", code: "scribe:scribelectern", quantity: 1 }` and a `name`
  for the recipe (used in logs/handbook).

## 2. Build, stage, and load-verify

- [x] 2.1 `bash build/restage.sh Debug` and fully relaunch the client (assets load at boot).
- [x] 2.2 Watch the server/game log on launch for recipe-load warnings; confirm the recipe registers
  with no "could not resolve ingredient" errors.
  - **Confirmed 2026-07-27**: server-main.log shows no recipe-resolve warnings and no `scribelectern`
    load errors; a clean load is silent in VS, and the successful survival craft (3.1) confirms the
    recipe registered.

## 3. In-game verification (manual)

- [x] 3.1 In survival, arrange the 3×3 pattern with a fired bowl of black dye (≥1 L) in the `B` slot
  → confirm the output is exactly one Lectern, the bowl is consumed, and 1 L of dye is removed.
  - **Confirmed 2026-07-27**: crafted the Lectern in survival with the intended ingredients + bowl of
    black dye.
- [x] 3.2 Confirm the recipe appears in the in-game handbook entry for the Lectern
  (`scribe:scribelectern`).
  - **Confirmed 2026-07-27**: the (empty) handbook entry for the Lectern is present.
- [x] 3.3 Negative cases: an empty bowl (or a bowl of a non-dye liquid) in the `B` slot produces no
  output; a dyed/non-plain leather in the `L` slot produces no output; confirm any wood plank and any
  metal nails are accepted.
  - **Confirmed 2026-07-27**: no craft with an empty bowl or incorrect (non-plain) leather.
