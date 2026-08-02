## Context

Proposal C (`add-tablet-dialog`) lands the bespoke `GuiDialogScribeTablet` and declares four backdrop
slots (`clay`, `fired`, `wax`, `spare`) that all point at the shared `scribe-lectern.png` placeholder
— a purely additive use of the existing per-item `gui-backdrop` mechanism. This followup replaces that
placeholder with backdrops that respect Vintage Story's actual clay materials.

Two hard prerequisites, discovered by reading the current code, are **in scope** here (not assumed
away):

1. **The tablet stores no clay-type or fired data today.** `scribetablet.json` declares a single
   variant axis `material: [clay, wax]` (Proposal B deferred firing as a non-goal). So the dialog has
   nothing on the stack to key a per-type/per-fired backdrop off of. This change must first decide how
   the tablet records clay type and fired appearance, and how that survives crafting + persistence.

2. **The backdrop renderer stretches ONE full-page texture to fill the whole 1024×1160 dialog.**
   `ScribeBackdropSpec` holds a single `AssetLocation`; `ScribeDialogBase.WrapBackdrop` paints it with
   LibGUI `BoxStyle.Texture` stretch-to-fill (a `Container` at `layout.W × layout.H`). The three
   shipped backdrops are crisp illustrated full-page frames at 1024×1160 (aspect 0.883). Vanilla
   pottery textures are small ~32px **tiling material swatches**. Stretching a 32px swatch over a
   1024px page → blurry upscaled color-fill with no page frame, visually inconsistent with the
   notebook/lectern art. So "just use VS pottery textures" as-is is placeholder-grade — real new
   rendering work is required.

Constraints carried from the plan and prior proposals:
- `src/Core/` must never reference the VS API. Nothing here needs a Core change — clay-type/fired are
  game-side stack attributes.
- No new mod dependencies, no new network packets. Clay-type/fired ride the existing
  `ScribeNotebookSaveMessage` / pickup flow as stack attributes, exactly like the docId.
- Persistence follows the vanilla Sign discipline the mod already uses (stack attributes, MarkDirty,
  server-authoritative).
- **Sequencing:** this change MODIFIES requirements that Proposal C introduces (`tablet-dialog`) or
  last modified (`clay-wax-tablet-item`). Those deltas do not reach `openspec/specs/` until C is
  archived, so this change is authored against C's change-dir spec text and must be **applied after C
  archives** — otherwise its MODIFIED headers won't locate their targets (the archive-order drift trap
  in MEMORY.md).

## Goals / Non-Goals

**Goals:**
- Record a `clayType` (red/blue/fire) and a `fired` (soft/fired) appearance value on the tablet stack,
  set at craft from the clay ingredient, preserved across close/reopen and drop/pickup.
- Expand the tablet dialog's backdrop from 2 placeholder slots to 7 material-respecting backdrops
  (6 clay = 3 types × soft/fired, plus 1 wax) selected from that stack state.
- Source the 6 clay backdrops from verified vanilla pottery textures and use a beeswax swatch as the
  wax placeholder.
- Render a small tiling swatch crisply (native-resolution tile, optional shared page-frame overlay)
  rather than blurrily stretching it, so the tablet reads as intentional chrome.

**Non-Goals:**
- The soft→fired **firing gameplay mechanic** (transformation, archive-on-fire). This change records a
  fired *appearance* only; nothing here actually fires a tablet. Whether the `fired` bit is ever set to
  `true` by real gameplay is an Open Question, not built here.
- Authoring seven bespoke illustrated full-page backdrop PNGs (the user's "revisit whether to make a
  custom frame" is a later polish pass; this change ships the interim material-swatch look, possibly
  under one shared frame overlay).
- Water damage, wax-wipe, carry-forward, stylus gate — all still deferred.
- Any `src/Core/` change or new network packet.

## Decisions

### 1. Record clay-type and fired as ItemStack attributes, set at craft — NOT new variant axes
The tablet records `clayType` ("red" | "blue" | "fire") and `fired` (bool) as **stack attributes**
(`itemstack.Attributes`), written at craft time in `OnCreatedByCrafting` from the clay ingredient
consumed, and read by the dialog to pick a backdrop. They ride the existing save/pickup flow (the same
way the docId already persists on the stack), so no new packet and no persistence invention.

*Alternative considered — add `clay-type` and `fired` VS **variant axes** to `scribetablet.json`
(`material × clayType × fired`).* Rejected as the primary mechanism: it multiplies the item into
2×3×2 = 12 registered variants (many nonsensical, e.g. wax × fireclay × fired), needs 12 recipe
outputs, and coupling the fired axis to the item type edges toward the deferred firing mechanic (a
fired variant implies a firing transformation). Attributes keep the item as its existing
`material: [clay, wax]` shape and record appearance data orthogonally. *(If in-hand/inventory model
tinting per clay type is later wanted, a `clayType` variant axis could be added then; out of scope
here — the backdrop only needs the attribute.)*

*Default when absent:* older stacks, creative-inventory stacks, and handbook renders carry no
attribute — default to `clayType = "red"`, `fired = false` so every tablet resolves to a valid
backdrop.

### 2. Seven named backdrop specs selected by (material, clayType, fired)
Replace Proposal C's four placeholder slots with seven specs in `ScribeBackdrops`:
`ClayRedSoft`, `ClayRedFired`, `ClayBlueSoft`, `ClayBlueFired`, `ClayFireSoft`, `ClayFireFired`, and
`Wax`. `GuiDialogScribeTablet` selects one via a small switch: `material == wax` → `Wax`; else key on
`clayType` + `fired`. The selection lives in one place in the tablet dialog (mirroring C's single
`UseCuneiform` branch discipline).

### 3. Backdrop art: verified vanilla textures now, tint the fired set, wax stays placeholder
Verified against the installed 1.22.6 assets:
- Unfired clay swatches (per type): `game:block/soil/redclay.png`, `game:block/soil/blueclay.png`,
  `game:block/soil/fireclay.png` — the raw-clay item's own `block/soil/{type}clay` texture base
  (`itemtypes/resource/clay.json`, variant `type: [blue, red, fire]`); also mirrored at
  `block/clay/{type}clay.png`. All 32×32.
- Fired ceramic: `game:block/clay/aged-ceramic1.png` (the texture the tablet item already remaps) or
  `block/clay/ceramic.png`. 32×32.
- Wax placeholder: `game:item/resource/beeswax.png`. 32×32.

**Finding:** vanilla fired ceramic is NOT color-keyed by source clay — blue/red/fire clay all fire to
the same generic ceramic textures. So the three *fired* backdrops would be visually identical if they
just point at `aged-ceramic1`. To honor "3 clay types respected even when fired," the fired specs
carry a per-type **tint color** (a `Vector4` applied to the swatch) approximating each clay's fired
hue, so red/blue/fire remain distinguishable. This is a small addition to the spec record (see
Decision 4). Wax remains an explicit placeholder swatch until real diptych art exists.

### 4. Extend `ScribeBackdropSpec` + `WrapBackdrop` for tiling and an optional frame — not stretch-only
`ScribeBackdropSpec` today is `record ScribeBackdropSpec(AssetLocation Texture)` rendered by
stretch-to-fill. Extend it (additively, so the existing full-page specs are unchanged) with optional
fields, e.g. `bool Tile`, `Vector4? Tint`, and `AssetLocation? FrameOverlay`. `WrapBackdrop` gains a
branch: when `Tile` is set, paint the swatch at its native pixel size repeated across `W × H`
(LibGUI `BoxStyle` tiling / repeat) with the optional `Tint`, and when `FrameOverlay` is set,
composite a shared illustrated page-frame PNG on top so the tablet still reads as a framed page rather
than a bare texture field. The existing lectern/notebook/clockmaker specs leave the new fields null and
take the identical stretch-to-fill path (verified byte-identical, the same discipline C used for its
layout seam).

*Interim vs target split:*
- **Interim (this change):** tiled + tinted vanilla swatches, optionally under one shared frame
  overlay. Crisp, material-respecting, no bespoke per-type art.
- **Target (deferred):** seven authored full-page illustrated backdrops (clay-pillow / wax-diptych),
  a straight file-path swap per spec with `Tile`/`Tint` cleared — no further renderer change.

*Alternatives considered:*
- *(a) Accept blurry stretched swatches, no renderer change.* Rejected as the shipped look — it clashes
  badly with the crisp illustrated notebook/lectern pages the mod already ships; acceptable only as a
  throwaway first build, not the deliverable.
- *(b) Author seven full-page PNGs now.* Rejected for this round — it is the deferred art-polish goal
  and blocks the functional clay-type plumbing on illustration work. The renderer extension lets the
  art land later as a pure asset swap.
- *(c) Tile without a frame.* Viable and simplest; the frame overlay is the "maybe a custom frame
  around it" the user floated. Kept as an optional field so the frame is a follow-on decision, not a
  hard prerequisite.

### 5. Verify codes now; finalize any unresolved paths during implementation
The texture codes above are verified against the installed game. If a chosen texture (e.g. the exact
`aged-ceramic*` variant or the frame overlay source) needs tuning in-game, treat it as a
finalize-during-implementation detail (Proposal B did the same for ingredient codes) — flagged in
tasks, not blocking the proposal.

## Risks / Trade-offs

- **Fired ceramic isn't color-differentiated in vanilla** → the three fired backdrops need per-type
  tinting (Decision 3) or they're identical; tint values are eyeballed and tuned in-game.
- **Tiling a material swatch reads as "field of clay," not a page** → mitigate with the optional shared
  frame overlay (Decision 4) and by tuning tile scale; fall back to accepting an untinted tile if the
  frame work slips.
- **Recording `fired` risks scope-creeping into the deferred firing mechanic** → strictly an appearance
  attribute; no gameplay sets it true this round (see Open Questions). Guard the language in specs so a
  reviewer doesn't read it as shipping firing.
- **Sequencing / archive-order drift** → this change's MODIFIED headers target requirements C
  introduces; if applied before C archives, the deltas won't locate their target (MEMORY.md trap).
  Mitigate by gating apply on C's archive and matching C's exact requirement header text.
- **Default-attribute stacks** → creative/handbook/legacy stacks have no `clayType`/`fired`; the
  red+soft default (Decision 1) keeps them valid rather than crashing backdrop selection.
- **Renderer seam disturbs incumbents** → the three shipped full-page backdrops must render
  byte-identically; mitigate with default-null fields and a diff review + in-game Lectern/Notebook
  smoke test, exactly as C did for its layout seam.

## Migration Plan

- No data migration: tablets crafted before this change simply have no `clayType`/`fired` attribute and
  fall back to red + soft. Nothing rewrites existing stacks.
- Deploy after Proposal C is implemented AND archived (so `tablet-dialog` / `clay-wax-tablet-item`
  deltas exist in `openspec/specs/`). Rollback is a straight revert — clearing the new specs restores
  C's four-placeholder behavior; leftover stack attributes are simply ignored by the older code.

## Open Questions

1. **Does anything ever set `fired = true`?** This change records a fired *appearance* but does not add
   the firing mechanic (deferred). Options: (a) leave `fired` always false for now so only the 3 soft
   clay backdrops + wax are reachable in real play, shipping the 3 fired backdrops as
   craft-via-creative/future-only; (b) allow crafting a pre-fired tablet from fired-clay/ceramic
   ingredients so `fired = true` is reachable without a firing transform; (c) wait and wire `fired`
   when the firing mechanic is un-deferred. Needs a user decision — flagged, not chosen.
2. **Tint vs identical fired art:** is per-type tinting of the shared ceramic swatch acceptable as the
   interim look for the fired backdrops, or should all fired tablets share ONE ceramic backdrop
   (dropping "clay type respected once fired") until bespoke art exists?
3. **Frame overlay:** ship the tiled swatch bare, or author the one shared page-frame overlay PNG this
   round? (The user floated "maybe just a custom frame around it" as a maybe.)
4. **Where clay type is captured at craft:** the current clay recipe uses `game:clay-blue`
   specifically (Proposal B finalized blue). To support all three types the recipe must accept any
   `clay-*` and copy the used type onto the output — confirm the recipe/`OnCreatedByCrafting` approach
   (wildcard ingredient + attribute copy) vs three separate clay recipes.
