## 0. Sequencing precondition

- [ ] 0.1 Confirm Proposal C (`add-tablet-dialog`) is IMPLEMENTED and ARCHIVED so its `tablet-dialog`
  and `clay-wax-tablet-item` deltas live in `openspec/specs/`. This change's MODIFIED headers target
  C's requirement text — do NOT apply before C archives (archive-order drift trap, MEMORY.md).
- [ ] 0.2 Resolve the Open Questions in `design.md` with the user before coding: whether/how `fired`
  is ever set true (Q1), tint-vs-single-fired-backdrop (Q2), ship a frame overlay this round (Q3), and
  the craft-time clay-type capture approach (Q4).

## 1. Record clay-type and fired on the tablet stack

- [ ] 1.1 In `src/Mod/ItemScribeTablet.cs`, in `OnCreatedByCrafting`, read the clay ingredient's type
  and write a `clayType` (`red`/`blue`/`fire`) stack attribute on the crafted tablet; leave wax tablets
  without a `clayType`. Write a `fired` attribute per the Q1 decision (default `false`).
- [ ] 1.2 Add a small helper (on `ItemScribeTablet` or `TabletHost`) to READ `clayType`/`fired` from a
  stack, defaulting absent values to `red` + soft. Reuse the existing stack-attribute discipline the
  docId uses; do NOT add a new network packet.
- [ ] 1.3 Verify the attributes survive the existing save/pickup flow (they ride the same stack as the
  docId). Add nothing to `src/Core/`.

## 2. Update the tablet item/recipe to carry clay type at craft

- [ ] 2.1 Per the Q4 decision, update `src/Mod/assets/scribe/recipes/grid/scribetablet-clay.json` to
  accept the clay ingredient in a way that lets the crafted output record which of red/blue/fire was
  used (wildcard `clay-*` + attribute copy, or three recipes). Keep the wax recipe unchanged.
- [ ] 2.2 Confirm `scribetablet.json` still declares only `material: [clay, wax]` (clay-type/fired are
  attributes, NOT new variant axes) unless the Q1/Q4 decision explicitly calls for a variant.

## 3. Seven backdrop specs

- [ ] 3.1 In `src/Mod/ScribeBackdrop.cs`, replace Proposal C's four placeholder tablet slots with seven
  specs: `ClayRedSoft`, `ClayBlueSoft`, `ClayFireSoft`, `ClayRedFired`, `ClayBlueFired`, `ClayFireFired`,
  and `Wax`.
- [ ] 3.2 Point the soft specs at the verified vanilla per-type clay swatches
  (`game:block/soil/redclay.png`, `block/soil/blueclay.png`, `block/soil/fireclay.png`), the fired specs
  at the verified fired ceramic swatch (`game:block/clay/aged-ceramic1.png`), and `Wax` at
  `game:item/resource/beeswax.png` (placeholder). Finalize any exact texture variant in-game.

## 4. Extend the backdrop renderer

- [ ] 4.1 In `src/Mod/ScribeBackdrop.cs`, add optional fields to `ScribeBackdropSpec` (e.g. `bool Tile`,
  `Vector4? Tint`, `AssetLocation? FrameOverlay`); existing full-page specs leave them null/default.
- [ ] 4.2 In `src/Mod/ScribeDialogBase.Layout.cs` `WrapBackdrop`, add a branch: when `Tile` is set,
  paint the swatch at native resolution repeated across `W × H` (LibGUI `BoxStyle` tiling/repeat) with
  the optional `Tint`; when `FrameOverlay` is set, composite the frame PNG on top. Leave the
  stretch-to-fill path unchanged for specs with none of the new fields.
- [ ] 4.3 Per the Q2 decision, apply per-type tint to the three fired specs (since vanilla fired ceramic
  is not color-keyed by source clay) so red/blue/fire fired tablets stay distinguishable.
- [ ] 4.4 (If Q3 = yes) Author one shared page-frame overlay PNG under
  `src/Mod/assets/scribe/textures/gui/` and reference it as `FrameOverlay` on the clay/wax specs.
- [ ] 4.5 Diff-review + in-game smoke test that the Lectern and both Notebook backdrops render
  byte-identically (they take the unchanged stretch path).

## 5. Select the backdrop in the tablet dialog

- [ ] 5.1 In `src/Mod/GuiDialogScribeTablet.cs`, select one of the seven specs from the stack's
  `material` + `clayType` + `fired` (wax → `Wax`; else key clayType×fired), in ONE place, defaulting to
  `ClayRedSoft` when attributes are absent. Feed the chosen spec through the existing backdrop path.

## 6. Verification

- [ ] 6.1 `dotnet build` clean; `dotnet test` — Core suite still green (no Core change expected).
- [ ] 6.2 In-game: craft clay tablets from red, blue, and fire clay; confirm each opens with a distinct,
  crisp (non-stretched) clay-type backdrop.
- [ ] 6.3 In-game: confirm a fired-appearance clay tablet (however `fired=true` is reachable per Q1)
  shows the fired-ceramic backdrop, distinct per clay type via tint.
- [ ] 6.4 In-game: confirm a wax tablet shows the wax placeholder backdrop, and a legacy/creative clay
  tablet with no attributes falls back to red + soft without error.
- [ ] 6.5 In-game: confirm clay-type/fired persist across close/reopen and drop/pickup.
- [ ] 6.6 In-game: confirm the Lectern and both Notebooks are visually unchanged (renderer seam did not
  disturb the full-page backdrops).
- [ ] 6.7 Atlas/integration: the local pre-push gate stages the `gui` dep and exercises the tablet open
  path; keep synthetic player names ≤16 chars.
