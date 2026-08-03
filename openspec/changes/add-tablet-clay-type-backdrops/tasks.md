## 0. Sequencing precondition

- [x] 0.1 Confirm Proposal C (`add-tablet-dialog`) is IMPLEMENTED and ARCHIVED so its `tablet-dialog`
  and `clay-wax-tablet-item` deltas live in `openspec/specs/`. — Confirmed: C archived; both target specs
  present under `openspec/specs/`.
- [x] 0.2 Resolve the Open Questions in `design.md` with the user before coding. — Q1: leave `fired`
  false this round (no gameplay sets it; fired specs creative/future-only). Q2: per-type tint on the
  fired specs. Q3: user authored **full-page** clay art (3 PNGs), so the interim swatch/frame-overlay is
  moot — soft clay uses the existing stretch path. Q4: three separate clay recipes.

## 1. Record clay-type and fired on the tablet stack

- [x] 1.1 Record `clayType` (`red`/`blue`/`fire`) at craft. — DEVIATION (cleaner than sniffing the
  ingredient in `OnCreatedByCrafting`): set declaratively via each clay recipe's `output.attributes`
  (`clayType: "..."`), verified against `CraftingRecipeIngredient.Attributes`/`ResolvedAttributes` and
  vanilla `lantern.json` precedent. `fired` is left absent (defaults false per Q1); wax carries no
  `clayType`.
- [x] 1.2 Add read helpers `ItemScribeTablet.ReadClayType(stack)` / `ReadFired(stack)` that default an
  absent value to red + soft, reusing the stack-attribute discipline (`Attributes.GetString/GetBool`); no
  new packet.
- [x] 1.3 Attributes ride the existing save/pickup flow (same stack as the docId) — no new persistence
  code. No `src/Core/` change. (In-game persistence check: task 6.5.)

## 2. Update the tablet recipe to carry clay type at craft

- [x] 2.1 Q4: `scribetablet-clay.json` is now THREE recipes (red/blue/fire), each consuming
  `game:clay-{type}` and writing `output.attributes.clayType`. Wax recipe unchanged.
- [x] 2.2 `scribetablet.json` still declares only `material: [clay, wax]` (clayType/fired are attributes,
  not variant axes).

## 3. Seven backdrop specs

- [x] 3.1 Replaced Proposal C's placeholder tablet slots with seven specs in `ScribeBackdrops`:
  `ClayRedSoft`, `ClayBlueSoft`, `ClayFireSoft`, `ClayRedFired`, `ClayBlueFired`, `ClayFireFired`, `Wax`.
- [x] 3.2 DEVIATION from the vanilla-swatch plan (superseded by the user's authored art): the three soft
  specs point at authored full-page PNGs `scribe-clay-tablet-{red,blue,fire}.png` (1024×1160, same shape
  as the other pages → existing stretch path). The three fired specs REUSE that art under a per-type tint
  (Q2). `Wax` reuses the fire-clay art as an interim placeholder (no beeswax swatch needed).

## 4. Extend the backdrop renderer

- [x] 4.1 Added an optional `Vector4? Tint` field to `ScribeBackdropSpec` (existing full-page specs leave
  it null). DEVIATION: dropped the planned `bool Tile` / `AssetLocation? FrameOverlay` — see 4.2.
- [x] 4.2 DEVIATION (forced by source-read): LibGUI's `BoxStyle.Texture` path (`DrawMaskedBox` →
  `DrawBitmap(texture, rect)`) ONLY stretches to fill and exposes no tint, and `BoxStyle` lives in the
  read-only `gui` dep — so tiling/frame-overlay could not be built there and, with full-page authored art,
  were unnecessary. Instead, `ScribeModSystem.GetBackdropBitmap(spec)` bakes the tint into a cached SKBitmap
  copy via `SKColorFilter.CreateBlendMode(..., Modulate)` (the same primitive LibGUI's `RenderIcon` uses)
  and feeds it through the UNCHANGED stretch path. `WrapBackdrop` now passes the spec, not the texture.
- [x] 4.3 Q2: per-type ceramic tints applied to `ClayRedFired`/`ClayBlueFired`/`ClayFireFired` so the three
  fired tablets stay distinguishable (values eyeballed; tune in-game — task 6.3).
- [x] 4.4 (Q3) SUPERSEDED: no shared page-frame overlay authored — the user supplied full-page framed art,
  so each backdrop is already a framed page. No `FrameOverlay` field exists.
- [ ] 4.5 Diff-review + in-game smoke test that the Lectern and both Notebook backdrops render unchanged
  (they pass a null-tint spec, so `GetBackdropBitmap` returns the same decoded bitmap as before). — code
  path verified null-tint = identical bitmap; in-game confirm is task 6.6.

## 5. Select the backdrop in the tablet dialog

- [x] 5.1 Selection lives in ONE place: `ScribeBackdrops.ForTablet(material, clayType, fired)`, called from
  `ItemScribeTablet.OpenTabletDialog` with the stack's read attributes; wax → `Wax`, else keyed on
  clayType × fired, defaulting to `ClayRedSoft` when attributes are absent.

## 6. Verification

- [x] 6.1 `dotnet build` clean (0 errors, 3 pre-existing warnings); `dotnet test` Core suite 255/255 (no
  Core change).
- [ ] 6.2 In-game: craft clay tablets from red, blue, and fire clay; confirm each opens with a distinct,
  crisp clay-type backdrop.
- [ ] 6.3 In-game: reach a fired clay tablet via creative (fired=true is not craftable this round) and
  confirm the fired-ceramic tint reads distinctly per clay type; tune tint values if needed.
- [ ] 6.4 In-game: confirm a wax tablet shows the wax placeholder backdrop, and a legacy/creative clay
  tablet with no attributes falls back to red + soft without error.
- [ ] 6.5 In-game: confirm clayType/fired persist across close/reopen and drop/pickup.
- [ ] 6.6 In-game: confirm the Lectern and both Notebooks are visually unchanged (renderer seam did not
  disturb the full-page backdrops).
- [ ] 6.7 Atlas/integration: the local pre-push gate stages the `gui` dep and exercises the tablet open
  path; keep synthetic player names ≤16 chars.
