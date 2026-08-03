## 0. Sequencing precondition

- [x] 0.1 Confirm Proposal C (`add-tablet-dialog`) is IMPLEMENTED and ARCHIVED so its `tablet-dialog`
  and `clay-wax-tablet-item` deltas live in `openspec/specs/`. — Confirmed: C archived; both target specs
  present under `openspec/specs/`.
- [x] 0.2 Resolve the Open Questions in `design.md` with the user before coding. — Q1: leave `fired`
  false this round (no gameplay sets it; fired specs creative/future-only). Q2: per-type tint on the
  fired specs. Q3: user authored **full-page** clay art (3 PNGs), so the interim swatch/frame-overlay is
  moot — soft clay uses the existing stretch path. Q4: three separate clay recipes.

## 1. Clay type is a VS variant; only `fired` is a stack attribute

- [x] 1.1 REVISED 2026-08-02 (reverses the original attribute approach — see design Decision 1): clay type
  is now a real **VS variant**, not a stack attribute. `scribetablet.json`'s `material` axis enumerates
  composite states `[clay-red, clay-blue, clay-fire, wax]` (the single-group composite idiom vanilla uses
  for `fishfillet`), producing four discrete registered items each with its own handbook page + recipe. The
  earlier `output.attributes.clayType` mechanism is removed. Reason: attributes never yield discrete
  handbook/creative entries (VS lists by variant), which the user requires as a base expectation.
- [x] 1.2 Removed the `ReadClayType` helper and `clayType`/`ClayTypeAttributeKey` (dead now — type lives in
  the variant). `ReadFired(stack)` remains (fired is still an attribute, defaulting false). No new packet.
- [x] 1.3 `fired` rides the existing save/pickup flow; clay type rides the item code itself (variant). No
  new persistence code, no `src/Core/` change. (In-game persistence check: task 6.5.)

## 2. Recipes output the three discrete clay-tablet items

- [x] 2.1 `scribetablet-clay.json` is THREE recipes (red/blue/fire), each outputting its discrete variant
  code `scribe:scribetablet-clay-{type}` (no `output.attributes` — the type is the item). Wax recipe
  unchanged (`scribetablet-wax`).
- [x] 2.1a RECIPE SHAPE (2026-08-02, user direction): each clay recipe is pattern `KCC,SCC` (width 3,
  height 2): a knife top-left, a stick mid-left, and a 2×2 clay block at `quantity: 2` per cell =
  **8 `game:clay-{type}` + 1 `game:stick` + 1 knife**. The knife is
  `{ tags: ["tool-knife"], isTool: true, toolDurabilityCost: 3 }` — NOT consumed, loses 3 durability per
  craft (vanilla `bed.json` precedent). Any knife material works. Replaced the earlier thin `1 clay + 1
  stick` recipe (reported not discoverable).
- [x] 2.2 `scribetablet.json` `material` axis = `[clay-red, clay-blue, clay-fire, wax]`; `shapeByType`
  updated to `*-clay-red`/`*-clay-blue`/`*-clay-fire`/`*-wax`; four `item-scribetablet-*` lang name/desc
  keys ("Red/Blue/Fire Clay Tablet", "Wax Tablet").
- [ ] 2.3 In-game: confirm FOUR discrete tablet entries appear in the handbook and creative (Red/Blue/Fire
  Clay Tablet + Wax Tablet), each clay type craftable via its own recipe, and each opens its matching
  backdrop. (Unblocks 6.2; supersedes the earlier "one collapsed entry" symptom.)

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

- [x] 5.1 Selection lives in ONE place: `ScribeBackdrops.ForTablet(material, fired)`, called from
  `ItemScribeTablet.OpenTabletDialog` with the item's `Variant["material"]` (`clay-red`/`clay-blue`/
  `clay-fire`/`wax`) + read `fired` attribute; wax → `Wax`, else keyed on the clay material variant ×
  fired, defaulting to `ClayRedSoft` for an unknown/absent material. REVISED 2026-08-02: takes the
  material variant, not a separate `clayType` argument (clay type is now the variant — Decision 1).

## 6. Verification

- [x] 6.1 `dotnet build` clean (0 errors, 3 pre-existing warnings); `dotnet test` Core suite 255/255 (no
  Core change).
- [ ] 6.2 In-game: obtain each clay tablet — from creative (now three discrete items) OR by crafting from
  red/blue/fire clay — and confirm each opens with its own distinct, crisp clay-type backdrop.
- [ ] 6.3 In-game: reach a fired clay tablet via creative (fired=true is not craftable this round) and
  confirm the fired-ceramic tint reads distinctly per clay type; tune tint values if needed.
- [x] 6.4 SUPERSEDED by the variant pivot (2026-08-02): the earlier playtest confirmed the wax + a single
  collapsed creative "Clay Tablet" (red+soft default) rendered without error — but clay type is now a
  variant, so creative offers three discrete typed clay items instead of one attribute-less stack. The
  fallback path (unknown/absent material → red + soft) still exists in `ForTablet` for legacy stacks; the
  live "four discrete entries render correctly" check folds into task 2.3 / 6.2.
- [ ] 6.5 In-game: confirm clayType/fired persist across close/reopen and drop/pickup.
- [ ] 6.6 In-game: confirm the Lectern and both Notebooks are visually unchanged (renderer seam did not
  disturb the full-page backdrops).
- [ ] 6.7 Atlas/integration: the local pre-push gate stages the `gui` dep and exercises the tablet open
  path; keep synthetic player names ≤16 chars.
