# Lectern GUI / layout polish (near-term cluster)

> **Status:** mostly delivered (2026-07-30). Item-by-item:
> - **1 Placement facing** — SHIPPED v0.1.0
> - **2 "Edit" → "Task Editor" relabel** — SHIPPED 2026-07-30
> - **3 Damped left-gutter scaling** — DEFERRED (minor; `ControlSize` scales linearly in `ScribeRowStyle.cs`; no OpenSpec change yet)
> - **4 Side rail** — SHIPPED (the LibGUI right-col nav column IS the side rail)
> - **5 Fold switch-view into collapse toggle** — OBSOLETE (shelved; nav column handles view switching cleanly)
> - **6 Skeuomorphic collapse control** — SHIPPED (nav buttons with themed SVG icons + color states satisfy this intent)
> - **7 Lectern block shape + notebook PNG** — DEFERRED (art-gated; GUI backdrop delivered the aesthetic direction)
> - **8 Icon-font audit** — SHIPPED (`add-custom-svg-row-icons`, archived 2026-07-21)

## Summary

A grab-bag of near-term lectern GUI/layout polish items surfaced during playtesting, merged
into one spec because they all touch the same three files (`GuiDialogScribeLectern.cs`,
`ScribeClientConfig.cs`, `assets/scribe/lang/en.json`) and one block file
(`BlockScribeLectern.cs` / `blocktypes/lectern.json`). Two of them are trivial and standalone
(relabel the Edit button; orient the lectern to the placing player). The rest are a
progressive re-think of the editor's control chrome — move the option bar into a **side rail**,
fold the read/edit switch into the collapse toggle, make that toggle a **skeuomorphic
bookmark/ribbon**, and narrow the icon gutters at large text size. Two are model/art items
(loose-leaf-paper-and-quill lectern shape; an icon-font audit) that produce follow-up swaps
rather than code on their own.

Merges these ROADMAP.md items:
- "Lectern faces a fixed direction when placed; should face the placing player."
- "Relabel the editor's 'Edit' button to 'Edit Tasks.'"
- "Narrower icon columns at large text size."
- "Move the editor's option bar into a side rail."
- "Fold the 'Switch to Read/Edit' button into the collapse/expand toggle."
- "Skeuomorphic collapse/expand control."
- "Lectern model polish: loose-leaf paper + a quill/pen."
- "Icon-font audit session."

**Guardrail note:** every item here lives in `src/Mod/` (game-API GUI/block code) or in
assets. Nothing touches `src/Core/`. No new dependencies. The one item with persistence
implications (block orientation) follows the vanilla variant-group pattern, which stores
facing in the block code — no new synced state, so the Sign-pattern constraint is not engaged.

---

## VS API hooks

| Hook | Used by | Confirmed |
|------|---------|-----------|
| `variantgroups` + `loadFromProperties: "abstract/horizontalorientation"` (north/east/south/west, angle 0/90/180/270) | Lectern facing | `assets/survival/worldproperties/abstract/horizontalorientation.json` (read from install); `wood/toolrack.json`, `wood/chest.json` use it |
| `Block.SuggestedHVOrientation(IPlayer byPlayer, BlockSelection blockSel)` → returns the H/V facing to place with | Lectern facing (code path) | `VintagestoryAPI.xml`: "Returns a horizontal and vertical orientation which should be used for oriented blocks like stairs during placement." |
| `BlockFacing.HorizontalFromYaw(float)` | Lectern facing (manual path, if not using variant group) | `VintagestoryAPI.xml` member present |
| `shapebytype` / `rotateY` per variant, `selectionbox.rotateYByType` | Lectern facing (rotates shape + selection box per facing) | `wood/toolrack.json` lines 21-48 |
| `Lang.Get("scribe:<key>")` (domain-prefixed) | Edit-button relabel | VSAPI-NOTES.md "Localization" — every call site already uses the `scribe:` prefix |
| `GuiComposerHelpers.AddIconButton(composer, icon, onToggle, bounds, key)` — `icon` is a built-in glyph name | Side rail buttons, icon-font audit | `VintagestoryAPI.xml`; already used in `ComposeEditorView`/`ScribeBlockRowCell` |
| `IconUtil.DrawIcon(ctx, code, x, y, w, h, rgba[])` + `Gui.DrawSvg(svgAsset, …)` | Icon-font audit (what's drawable, and the custom-SVG escape hatch) | Decompiled `IconUtil` (`ilspycmd`) — full built-in glyph list below |
| `GuiComposer.BeginChildElements(bounds)` with a left/side column of fixed bounds | Side rail | Same pattern the current control stack uses; the Survival Handbook (`GuiDialogHandbook`) docks its category list similarly |
| `ScribeHoverIconButton` (existing mod element, base `GuiElementToggleButton`) | Side rail toggles that persist state | `ScribeBlockRowCell.cs`; needs `toggleable: true` for stateful toggles (VSAPI-NOTES.md) |

### Built-in icon-font glyph list (for the audit item)

Confirmed by decompiling `Vintagestory.API.Client.IconUtil` (the switch in `DrawIcon`). These
are every `code` string `AddIconButton` / `IconUtil.DrawIcon` can render without a custom SVG:

```
none, line, ring, dice, cursor, lake, tree, redo, undo, cape, belt, mask, left, right,
brush (a.k.a. paintbrush), airbrush, raiselower, growshrink, erode, floodfill, trousers,
necklace, pullover, handheld, shirt, boots, medal, repeat, import, eraser, select, gloves,
basket, bracers, offhand, hat, ring, plus, move, height, width, quality, size, x, y,
apple, health, menuicon,
wpBee, wpCave, wpCircle, wpCross, wpHome, wpLadder, wpPick, wpPlayer, wpRocks, wpRuins,
wpSpiral, wpStar1, wpStar2, wpTrader, wpVessel
```

Currently in use in the lectern GUI:
- **Add Task** toolbar → `"plus"`
- **Pin** row icon → `"wpCircle"` (a waypoint circle; not obviously a "pin")
- **Delete** row icon → `"eraser"`
- **Drag handle** → the literal string `"::"` drawn as text (`ScribeDragHandleElement`, base
  `GuiElementStaticText`), NOT an icon-font glyph — there is no built-in reorder/grip glyph.
- **Checkbox** → custom Cairo draw in `ScribeRowElement.DrawCheckboxGlyph`, not an icon-font
  glyph.
- **Collapse / Expand** → text button (`Lang.Get("scribe:scribe-gui-collapse"/"-expand")`),
  no icon.
- **Switch view** → text button, no icon.

**Custom-SVG escape hatch:** `IconUtil.DrawIcon` falls through to `Gui.DrawSvg(svgAsset, …)`
for a code that isn't a built-in — so a mod-provided SVG asset can supply a proper pin/grip/
bookmark glyph if none of the built-ins fit. This is the lead for any glyph the audit decides
to change but the built-in set can't cover.

---

## C# data structures

No `Core` changes anywhere in this cluster. The only new state is client-side config knobs and
one lang key; the block-facing item adds no synced state (facing rides the variant group).

### `ScribeClientConfig` additions / changes

```csharp
// --- Sub-linear icon-gutter scaling (Narrower icon columns item) ---
// Instead of scaling gutter widths straight by TextSizeScale, scale by a dampened factor.
// A gutter at scale s gets width = base * (1 + GutterScaleDamping * (s - 1)), clamped to
// [base, base * GutterScaleMaxFactor]. Damping < 1 makes gutters grow slower than text;
// 0 pins them at base width. See the formula section below.
public double GutterScaleDamping = 0.35;   // 0 = fixed-width gutters, 1 = today's linear
public double GutterScaleMaxFactor = 1.5;  // hard cap: a gutter never exceeds 1.5x its base

// --- Side rail (Side rail item) ---
public double SideRailWidth = 44;          // width of the docked rail column
public double SideRailGap = 8;             // gap between rail and the row-list content
public double SideRailButtonSize = 36;     // square rail button
public double SideRailButtonSpacing = 42;  // vertical stride between rail buttons

// --- Skeuomorphic collapse control (Skeuomorphic toggle item) ---
// If the toggle becomes a custom-drawn ribbon/bookmark element, these tune it. Mirrors the
// ruling-color knobs already in this config so the on-disk JSON stays flat/hand-editable.
public double BookmarkRibbonWidth = 28;
public double BookmarkRibbonHeight = 64;
public AssetLocation? BookmarkTexture = null; // null = Cairo-draw; set = skeuomorphic image
```

`ReadListWidth`/`EditorListWidth` are **collapsed into one width by S2** (see
`lectern-edit-in-place-rows` proposal) — the side-rail item assumes that single width already
exists and lays the rail out beside it. Don't re-collapse them here.

### Sub-linear gutter-width formula (the one non-trivial bit of math)

Today (`ScribeBlockRowCell.TextWidth` / `Compose`, `RowTextLayout.For`):
- `toggleWidth = ToggleWidth * TextSizeScale` (linear — checkbox scales with text, correct)
- `DragHandleWidth`, `PinWidth`, `DeleteWidth` are used **flat** (no scale) today. Re-read of
  the code: the *checkbox/toggle* column scales linearly; the drag/pin/delete gutters are
  already fixed-width. So the playtest complaint ("icon columns take too much space at large
  text") is really that **the gutters stay fixed while the text area shrinks relative to the
  now-larger text** — the gutters feel disproportionately wide because everything *else* grew.

Two candidate fixes (pick in Open Questions):

- **(A) Keep gutters fixed (already true), do nothing to them; instead grow the shared list
  width with text size** so the text column keeps a constant *proportion*. Simplest, but
  widens the whole dialog.
- **(B) Scale gutters sub-linearly and *upward* only when a design decides they should track
  text at all** — but that's the opposite of the complaint. The complaint wants them
  *narrower relative to text*, i.e. to **shrink** the gutter's share as text grows.

The intended formula, applied at each gutter's use site (a single helper
`ScaledGutter(double baseWidth)` on the dialog, mirroring `ScaledRowSpacing`):

```csharp
// Gutters grow slower than text (damping < 1) and never exceed a cap. At scale 1.0 this is
// exactly baseWidth; at large scale it's a fraction of the linear growth, so the gutter's
// share of the row shrinks relative to the (fully-scaled) text.
private double ScaledGutter(double baseWidth)
{
    double s = clientConfig.TextSizeScale;
    double factor = 1 + clientConfig.GutterScaleDamping * (s - 1);
    factor = System.Math.Clamp(factor, 1, clientConfig.GutterScaleMaxFactor);
    return baseWidth * factor;
}
```

Then `ScribeBlockRowCell.TextWidth` / `RowTextLayout.For` take the *already-scaled* gutter
widths from the dialog rather than reading raw config fields. **Care:** the icon SVG renderer
crashes if the row/icon box goes below ~15px (see `MinRowHeight` doc comment) — the damped
gutter must still be wide enough for its icon; clamp the *lower* bound at the icon's minimum,
not at 0.

---

## Implementation spec

### 1. Orient the lectern to the placing player — **small**, standalone-now

**File:** `assets/scribe/blocktypes/lectern.json` + possibly `BlockScribeLectern.cs`.

Two viable approaches (recommend the first — it's the pure-vanilla idiom):

**(A) Variant-group approach (recommended).** Add
`variantgroups: [{ code: "side", loadFromProperties: "abstract/horizontalorientation" }]`,
convert `shape` → `shapebytype` with a `rotateY` per facing (0/90/180/270), and add
`selectionbox.rotateYByType` / `collisionbox.rotateYByType`. The engine's default placement
already picks the variant matching `SuggestedHVOrientation` for a block that has a
horizontalorientation variant group, so the book ends up facing the player with no code
change — *if* `BlockScribeLectern` doesn't override placement. Verify: `BlockScribeLectern`
currently has no `TryPlaceBlock`/`DoPlaceBlock` override, so the base handles it. **Watch-outs:**
(i) the block code changes from `scribelectern` to `scribelectern-north` etc. — creative-
inventory entries, any hardcoded `AssetLocation("scribe:scribelectern")`, the recipe output,
and the block-entity registration must all account for the variant suffix; (ii) confirm the
current single `guiTransform` still reads right for all four rotations (toolrack sets one; the
clutter lectern uses a single `guiTf` per shape). (iii) `SuggestedHVOrientation` returns the
direction the player is facing — vanilla oriented blocks like chests face *toward* the player,
so confirm the book opens toward the player and flip the rotateY mapping by 180° if it opens
away.

**(B) Code approach (fallback).** Override `TryPlaceBlock`/`DoPlaceBlock` in
`BlockScribeLectern`, call `SuggestedHVOrientation(byPlayer, blockSel)`, and place a facing-
specific block or set a `BlockFacing` attribute the shape reads. More code, and needs its own
persisted facing — avoid unless the variant-group route has a blocker.

**Why not copy vanilla clutter's `WrenchOrientable`:** the clutter block orients via the
`BlockClutter` class + `WrenchOrientable` behavior (wrench-rotated after placement, arbitrary
angle), which is heavier than needed and not player-facing on placement. The toolrack/chest
`horizontalorientation` variant group is the right, minimal precedent. (Confirmed: clutter.json
uses `class: BlockClutter` + `WrenchOrientable`; toolrack/chest use the variant group.)

**Effort:** small (mostly JSON; the variant-suffix ripple is the only risk).

### 2. Relabel "Edit" → "Edit Tasks" — **trivial**, standalone-now

**File:** `assets/scribe/lang/en.json`, key `scribe-gui-switch-to-editor` (currently
`"Edit"`). Change value to `"Edit Tasks"`. That key is the read-view button label
(`ComposeReadView` → `AddSmallButton(Lang.Get("scribe:scribe-gui-switch-to-editor"), …)`).

Note there is a *separate* interaction-help key `blockhelp-scribelectern-edit` (also `"Edit"`,
the shift+right-click tooltip). Decide whether that should also become "Edit Tasks" (Open
Question) — the roadmap item names only the button. **Effort:** trivial.

### 3. Narrower icon columns at large text size — **small**, rides S2

**Files:** `GuiDialogScribeLectern.cs` (new `ScaledGutter` helper), `ScribeBlockRowCell.cs`
(`TextWidth`, `Compose`), `RowTextLayout.cs` (`For`), `ScribeClientConfig.cs` (two knobs).

Apply the sub-linear formula above to `DragHandleWidth`/`PinWidth`/`DeleteWidth` at their use
sites. Because S2 (`lectern-edit-in-place-rows`) is already rewriting `ComposeEditorView` and
the row layout onto the shared `ScribeRowElement`/`RowTextLayout`, this should land *inside or
right after* S2 so it edits the reworked layout code once, not the soon-to-be-deleted editor
path. **Effort:** small.

### 4. Move the option bar into a side rail — **medium**, rides/after S2

**File:** `GuiDialogScribeLectern.cs` (`ComposeEditorView`, and `ComposeReadView` for the
switch button), `ScribeClientConfig.cs` (rail knobs).

Today the editor stacks, below the row list: text-size label+slider, collapse toggle, the
icon toolbar (Add Task), and the switch-mode button — each on its own `ControlRowGap` row,
eating vertical space that competes with the document. Move these into a **fixed-width column
docked to the left (or right) of the row list**, laid out top-to-bottom inside its own
`BeginChildElements(railBounds)`, the way `GuiDialogHandbook` docks its category tabs beside
the content pane. The row-list clip/scrollbar bounds shift right by `SideRailWidth + SideRailGap`.

Design decisions to settle first (Open Questions): which side; whether the text-size slider
(a horizontal control) fits a narrow vertical rail or becomes a small stepper/popout; whether
Add Task stays in the rail or moves to a per-list "+" affordance. This is the item most worth
a quick mock/confirm before coding. **Effort:** medium.

### 5. Fold the switch-view button into the collapse/expand toggle — **small**, after item 4

**File:** `GuiDialogScribeLectern.cs`.

Once the side rail (item 4) makes the chrome layout concrete, merge the two controls into one.
Options: a single control cycling Read → Edit-collapsed → Edit-expanded; or the collapse toggle
appears only in editor mode and doubles as "collapse the rail / go back to read." Needs the
side rail's shape decided first. Removes one control row. **Effort:** small (after item 4).

### 6. Skeuomorphic collapse/expand control — **medium**, after item 4/5

**Files:** new `ScribeBookmarkToggleElement.cs` (custom `GuiElement`, Cairo-drawn like
`ScribeRowElement`), `GuiDialogScribeLectern.cs`, `ScribeClientConfig.cs` (ribbon knobs).

Replace the plain collapse/expand `AddSmallButton` with a tactile bookmark/ribbon element
that reads as part of the writing panel. Model it on `ScribeRowElement`: bake a Cairo texture
in `ComposeElements` (or blit a `BookmarkTexture` image if set), draw in
`RenderInteractiveElements`, handle the click in `OnMouseUpOnElement`. A ribbon that visually
tucks into/out of the page communicates collapsed/expanded state skeuomorphically. This
naturally hosts item 5's merged switch-view state (a ribbon with two rest positions). Purely
presentational; no data-model change. Best done together with items 4-5 as one "editor chrome
redesign" change. **Effort:** medium.

### 7. Lectern model polish — loose-leaf paper + quill — **medium**, standalone (art)

**File:** `assets/scribe/blocktypes/lectern.json` `shape` (and a new shape asset under
`assets/scribe/shapes/block/`).

Today the block reuses the vanilla `game:block/clutter/bookshelves/lecturn-book-open` shape.
Swap it for a shape reading as "loose-leaf paper + a quill/pen on a stand" so it signals
"edit paper here" rather than "a book." Needs an actual model asset (authored in VS Model
Creator or hand-edited JSON), not just code. If item 1 (orientation) uses `shapebytype`, this
shape becomes the base each rotation points at. **Effort:** medium (art-gated). Standalone but
best sequenced after item 1 so the shape is authored once against the final variant structure.

### 8. Icon-font audit session — **decision session**, standalone-now (produces follow-ups)

Not a code task — a presentation/decision session. Deliverable: walk the user through the
built-in glyph list above and the current usage table, and decide per-icon whether to change.
Concrete candidates to raise:
- **Pin** is `"wpCircle"` (a map-waypoint circle) — reads as a dot, not a pin. No built-in pin
  glyph exists; options are a different built-in (`wpStar1`/`wpHome`?) or a custom SVG.
- **Drag handle** is the text string `"::"`, not an icon — a custom grip SVG would read better;
  no built-in grip glyph.
- **Delete** is `"eraser"` — thematically on-brand for a writing tool (keep?) vs. a
  conventional "x"/`wpCross`.
- **Add Task** `"plus"` — fine, likely keep.
Output: a short list of small follow-up swap tasks. **Effort:** the session itself is trivial;
the follow-ups are trivial-to-small — but note the custom-SVG ones are **register-icon +
repoint**, not a one-line code-string change (see the corrected mechanism note below).

#### Audit decisions (session held 2026-07-21)

Per-icon verdicts:
- **Add Task `"plus"` → KEEP.** Universal "add" glyph, reads correctly, no change.
- **Pin `"wpCircle"` → custom pushpin SVG.** No built-in pin glyph exists; `wpCircle` reads as
  a dot. Author a pushpin SVG and register it (see mechanism note below). (Resolves #6.)
- **Drag handle `"::"` (text) → custom grip SVG.** No built-in grip glyph. Author the
  conventional six-dot (⠿) grip SVG and replace the `ScribeDragHandleElement` text with an
  icon button. (Resolves #6.)
- **Delete `"eraser"` → custom close SVG.** Convention wins over the writing-tool pun; the
  eraser risks reading as "clear the text" rather than "remove this row." **Superseding update
  2026-07-21:** rather than the built-in font `"x"`, author a **custom close glyph** so all four
  row/control icons share one hand-drawn family (matched line quality/ink) instead of mixing a
  font glyph among three custom SVGs. See `scribe-icon-svgs.md`. (Built-in `"wpCross"` — itself a
  `CustomIcons` vector cross, confirmed in decompile — remains the zero-art fallback if the custom
  set is deferred.)
- **"Edit" → "Edit Tasks" relabel → custom edit/pencil SVG (icon-only).** No built-in
  pencil/edit glyph; go icon-only with a custom quill/pencil SVG to match the chrome-redesign
  direction. The words "Edit Tasks" survive only as the lang string behind the tooltip/hover.
  (This is the decision ROADMAP Open decision #2 was blocked on — now unblocked.)

**Tooltip invariant (session decision 2026-07-21): every icon gets a hover tooltip.** This mod
already tooltips its icons via the built-in `composer.AddHoverText(langText, CairoFont.
WhiteSmallText(), (int)config.HoverTextWidth, bounds.FlatCopy())` — pin (`ScribeBlockRowCell.cs:226`),
delete (`:236`), and every toolbar option (`GuiDialogScribeLectern.cs:801`, off `option.LangKey`)
already have one. Treat "every icon has an `AddHoverText` driven off a `scribe:` lang key" as a
standing invariant for all the swaps below, not a separate work item: the cost is one line per
icon and matches the existing pattern. This matters most for the icons going **icon-only**
(Edit, drag-handle grip) — the tooltip is where their words survive. Caveats: hover tooltips
are **mouse-only** (a controller/keyboard player never sees them, so the glyph itself must still
read well — the tooltip complements the glyph choice, it doesn't replace it); and the drag
handle is a custom `ScribeDragHandleElement`, so its tooltip is a sibling `AddHoverText` on the
same bounds (as pin/delete do), not a property on the element.

**Mechanism (corrected — decompiled 2026-07-21; the earlier "`Gui.DrawSvg` fallthrough" claim
was wrong).** `IconUtil.DrawIconInt` has **no default case**: an unknown code draws *nothing,
silently*. A custom SVG must be **registered at client init** into `capi.Gui.Icons.CustomIcons`
via `SvgIconSource(new AssetLocation("scribe:icons/…"))`; the button's code string then resolves
to that entry (`GuiElementToggleButton` → `Icons.DrawIcon`, confirmed). Tinting is **resolved**:
`DrawSvg` flood-recolors the SVG with the button's `Font.Color`, so author each glyph **single
flat color** and let the button supply the ink (hover recolor is free). Full details + the one
remaining unknown (exact `capi.Assets.TryGet` path for the asset location) live in
`scribe-icon-svgs.md`.

**The custom SVG art is specced separately in `docs/specs/scribe-icon-svgs.md`** — per-glyph
geometry for the four icons, plus a **hand-inked aesthetic** direction (irregular quill strokes,
thick/thin, imperfect joins — matching VS's handmade-with-primitive-tools fiction, NOT a clean
geometric icon font), sized for ~15px→40px legibility. **All four row/control icons are custom**
(including close/delete, task #1) so they share one hand-drawn family rather than mixing in a
built-in font glyph.

**Follow-up swap tasks** (each is **register icon + repoint button + keep/add tooltip**, not a
one-line string swap; the custom-SVG tasks share the one-time `CustomIcons` registration setup —
prove the asset path with one icon first, per `scribe-icon-svgs.md`):

0. **Drag-handle tooltip** (no asset, no swap): add a sibling `AddHoverText` on the drag-handle
   bounds at `ScribeBlockRowCell.cs:176`/`:181` with a new `scribe:scribe-gui-reorder` lang key.
   The one existing icon with no tooltip today; closes the coverage gap independently of the
   grip swap (#3). Trivial. Rides the S2 window (touches `ScribeBlockRowCell`), no other dep.
1. **Delete → custom close SVG**: author `close.svg`, register it in `CustomIcons`, repoint
   `ScribeBlockRowCell.cs:231` `"eraser"` → the close code. Existing `scribe-gui-delete` tooltip
   (`:236`) stays. Small. (Zero-art fallback: repoint to built-in `"wpCross"` instead — no asset,
   no registration — if the custom set is deferred.)
2. **Pin → pushpin SVG**: author `pin.svg`, register it, repoint `ScribeBlockRowCell.cs:221`
   `"wpCircle"` → the pin code. Existing `scribe-gui-pin` tooltip (`:226`) stays. Small. **Do this
   one first of the custom-SVG set** — it carries the one-time `CustomIcons` registration + asset-
   path proving-out (a wrong path draws nothing, silently).
3. **Drag handle → grip SVG**: author `grip.svg`, register it, replace the `"::"`
   `ScribeDragHandleElement` at `ScribeBlockRowCell.cs:176` with an icon button on the grip code.
   Keep (or add, if #0 hasn't landed) the `scribe-gui-reorder` tooltip. Small.
4. **Edit relabel → pen SVG**: author `edit.svg`, register it, convert the read-view "Edit"
   `AddSmallButton` (`GuiDialogScribeLectern.cs:571`) to an icon button on the edit code, and
   add an `AddHoverText` on its bounds using the same `scribe-gui-switch-to-editor` key (value
   "Edit Tasks") the button used to display — that lang string now lives on only in the tooltip.
   Small.

Sequencing: these prefer to land after S2's layout settles (they touch `ScribeBlockRowCell` /
the read-view compose, which S2 is reworking). The **`wpCross` fallback for delete** (#1's
no-asset variant) is the only one with no asset/registration dependency, so it could go anytime;
the custom-SVG variants all wait on the one-time registration setup landing with #2.

---

## Dependencies & sequencing

**Standalone, do-now (no dependency on the row-list rework / S2):**
- **Item 2** (relabel Edit → Edit Tasks) — one-line lang change.
- **Item 1** (orient to placing player) — block JSON/definition; independent of the GUI rework.
- **Item 8** (icon-font audit) — a decision session; can happen anytime, though its follow-ups
  may prefer to land after S2's layout settles.
- **Item 7** (model swap) — art-gated but independent of the GUI internals; best sequenced
  after item 1 if item 1 adopts `shapebytype`.

**Rides S2 (`lectern-edit-in-place-rows`) or lands right after it:**
- **Item 3** (sub-linear gutters) — S2 rewrites the editor row layout and unifies the list
  width; editing the reworked `RowTextLayout`/`ComposeEditorView` once (post-S2) avoids
  touching soon-deleted code. S2 also collapses `ReadListWidth`/`EditorListWidth` into the one
  width this item's proportion reasoning assumes.

**A grouped follow-on change after S2 ("editor chrome redesign"):**
- **Item 4** (side rail) → **Item 5** (fold switch into toggle) → **Item 6** (skeuomorphic
  ribbon). These are a dependent chain: item 5 needs item 4's layout decided; item 6's ribbon
  is the natural host for item 5's merged state. Propose them as one OpenSpec change once S2
  has merged and the single-width editor exists.

**Suggested split into OpenSpec changes:**
1. A tiny "lectern quick polish" change: items 1 + 2 (+ optionally kick off item 8). Shippable
   now.
2. Item 3 folded into S2's follow-up (or S2 itself if scope allows).
3. "Editor chrome redesign": items 4 + 5 + 6, after S2.
4. Item 7 as its own art-gated change when a model is authored.

---

## Open questions

1. **Do the trivial items (1 relabel, 2 orientation) get pulled out as a standalone quick change
   NOW**, ahead of the row-list rework finishing — or batched with the chrome redesign later?
   (They have no dependency on S2 and would improve every playtest immediately.)
2. **Side rail: left or right of the row list?** The Survival Handbook docks its tabs on the
   left; a right rail keeps the reading eye on the page's left margin. Also: does the
   horizontal text-size slider survive in a narrow vertical rail, or become a stepper/popout?
3. **Should `blockhelp-scribelectern-edit` (the shift+right-click tooltip, also "Edit") change
   to "Edit Tasks" too**, or only the in-GUI button? (Roadmap names only the button.)
   **RESOLVED 2026-07-21: yes, change it to "Edit Tasks" too.** With the in-GUI control going
   icon-only (see item 8's audit decisions), the world-interaction tooltip becomes the primary
   place the words appear, so it should carry the fuller wording for consistency.
4. **Gutter narrowing — formula (B, damped scaling) vs. approach (A, grow list width to keep
   text proportion)?** The recommendation is the damped `ScaledGutter` formula, but if S2's
   unified width already feels roomy, growing the width may be simpler. Which does the user
   prefer visually?
5. **Orientation: variant-group (recommended, code `scribelectern` → `scribelectern-north`
   etc.) is a breaking block-code change** — acceptable given early development (no shipped
   saves to migrate), or is a code-only facing approach preferred to keep the block code stable?
6. **Pin/drag-handle glyphs:** accept a custom SVG asset for a proper pin + grip (best fit), or
   stay within the built-in set (`wpCircle`, `"::"`) to avoid authoring art? (Feeds item 8.)
   **RESOLVED 2026-07-21: custom SVG for both** — a pushpin SVG for the pin and a six-dot grip
   SVG for the drag handle (no built-in covers either). See item 8's audit decisions and
   follow-up tasks #2 and #3.
