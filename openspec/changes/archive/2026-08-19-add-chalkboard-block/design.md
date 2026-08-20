## Context

Scribe's placed document surfaces already share a three-layer base:
`BlockScribeWritingStation` → `BlockScribeLectern` (block), `BlockEntityScribeWritingStation`
→ `BlockEntityScribeLectern` (block entity), and `ScribeDialogBase` → the per-surface dialog.
The Lectern and Scriptorium are BOTH thin subclasses of these — the Lectern block is 15
lines, its block entity 26 lines, its dialog 38 lines. All shared behavior (placement,
lock, persistence, guestbook, autosave, all task kinds) lives in the base. The chalkboard
is designed to be a fourth thin subclass, cosmetic-only.

The art is already committed (`eb44b45`): a `.bbmodel`, four block textures
(chalk/slate/wood-h/wood-v), and a 128×145 GUI background (`scribe-chalkboard.png`). But
the committed `chalkboard.json` was cloned from the Scriptorium: it carries Scriptorium
comments, references `class: "BlockChalkboard"` / `entityclass: "Chalkboard"` (classes that
don't exist yet, so the block can't register), has a texture-dict key typo (`clate` vs the
`slate` the shape needs), Scriptorium-sized collision/selection boxes, and Scriptorium
handbook sections.

## Goals / Non-Goals

**Goals:**
- A working chalkboard block whose document layer is a Lectern, using the thin-subclass
  pattern; the only base-class change is two additive placement seams (D6).
- Four cosmetic deltas (model, block textures, dialog theme, GUI background) + one behavioral
  delta (wall-mount placement, painting idiom).
- Fix the malformed committed assets so the block registers and renders textured.

**Non-Goals:**
- No drawing/stroke input (that is the separate ROADMAP v6 "drawable chalkboard" concept).
- No new task kinds, persistence format, or sync mechanism; no document-layer divergence.
- No change to the player's global theme preference or to any other surface; no change to the
  Lectern/Scriptorium placement (the new seams default to today's behavior).
- No `src/Core/` change; no new dependency (`HorizontalAttachable` is a vanilla behavior).

## Decisions

### D1: Subclass the shared writing-station base, mirroring the Lectern exactly

Add `BlockScribeChalkboard : BlockScribeWritingStation` overriding only `InteractionsCacheKey`,
`OpenHintLangCode`, `EditHintLangCode`; and `BlockEntityScribeChalkboard :
BlockEntityScribeWritingStation` overriding only `PageBackdrop`, `PageAspect`,
`DefaultDocumentTitleKey`, `MeshCacheKeyPrefix`, and `CreateDialog`. This is the same seam
the Lectern uses — no base edits.

*Alternative rejected:* making the chalkboard a `variant` of the Lectern blocktype (one
block, texture/shape-by-variant). Rejected because the dialog theme + backdrop differ, which
are C#-side (block-entity/dialog) concerns, not JSON variant knobs — a distinct
block-entity + dialog subclass is cleaner and matches how the Scriptorium already differs.

### D2: GUI background via a new `ScribeBackdrops.ChalkboardPage` spec

Adding a full-page backdrop is, by the `ScribeBackdrops` doc-comment, "only a new spec here
plus its PNG." The chalkboard block entity overrides `PageBackdrop => ChalkboardPage` and
`PageAspect => 145f/128f` (the committed PNG is 128×145). The stretch-to-fill `WrapBackdrop`
path is unchanged.

### D3: Distinct dialog theme via the existing `ResolveTheme` override — scoped, not global

`ScribeDialogBase.ResolveTheme(bool pixelArt)` is already `protected virtual`. The chalkboard
dialog overrides it to return a new `ScribeTheme.Chalkboard` `ThemeData` (dark-slate surface,
chalk-light text/on-surface roles). Because the theme is resolved per-dialog, the player's
global Light/Default preference and every other surface are untouched — satisfying the
"scoped, cosmetic-only" requirement.

*Known nuance (see Risks):* the dialog reads `ScribeTheme.For(MySettings.PixelArtDisplay)`
directly in ~10 call sites (title/nav button colors in `Layout`/`Guestbook`/`ViewSwitching`)
that bypass `ResolveTheme`. The wrapped content tree gets the chalkboard theme; those
directly-resolved chrome colors do not, unless we also route them through an overridable
member. Decision: START by overriding `ResolveTheme` only (smallest change, themes the body
+ rows), then evaluate in-game whether the button chrome looks acceptable against the slate
background before deciding whether to widen the seam. Widening (a `protected virtual
ThemeData ActiveTheme` the direct call sites read) is a fast follow if needed and is called
out as task 4.x.

### D4: Register the class names the committed JSON already references

`chalkboard.json` names `class: "BlockChalkboard"` and `entityclass: "Chalkboard"`. Register
exactly those strings in `ScribeModSystem` (`RegisterBlockClass("BlockChalkboard", …)`,
`RegisterBlockEntityClass("Chalkboard", …)`) rather than renaming the JSON, so the committed
art needs no code-name churn.

### D6: Wall-mount via the vanilla painting idiom + two placement seams on the base

The chalkboard hangs on a wall like a vanilla painting (user decision). Vanilla paintings do
this declaratively: the `HorizontalAttachable` block behavior + a `side` variant group
(`abstract/horizontalorientation` → north/east/south/west) with `rotateYByType` on both the
shape and the selection box. `HorizontalAttachable.TryPlaceBlock` picks the `-<side>` variant
from the clicked wall face, sets `EnumHandling.PreventDefault`, and calls that variant's
`CanPlaceBlock` + `DoPlaceBlock`.

The blocker: the chalkboard's `CanPlaceBlock` is inherited from `BlockScribeWritingStation`,
which REQUIRES a solid floor cell below (it is documented floor-only furniture) and would
reject every wall. And its `TryPlaceBlock` applies a player-facing `MeshAngleRad` rotation
that is wrong for a wall block (orientation comes from the `side` variant instead). C# has no
`base.base`, so the chalkboard can't reach `Block`'s implementations directly.

Solution: refactor the base's placement into two `protected virtual` seams, both defaulting
to today's behavior:
- `protected virtual bool RequiresSolidGround => true;` — guards the below-floor check in
  `CanPlaceBlock`. Chalkboard overrides to `false`.
- `protected virtual bool OrientTowardPlayerOnPlace => true;` — guards the `MeshAngleRad`
  block in `TryPlaceBlock`. Chalkboard overrides to `false` (the `side` variant +
  `rotateYByType` supply visual orientation; `MeshAngleRad` is never set, so the base's
  `RotatedBox` stays null and `GetCollision/SelectionBoxes` fall back to the JSON boxes —
  which carry the painting's `rotateYByType`, exactly right).

The Lectern and Scriptorium inherit the `true` defaults, so their placement is byte-identical.
Drops/pick still flow through the base (which stamps the document onto whatever stack the
`HorizontalAttachable` drop path returns — the normalized `-north` variant — so break/pick →
re-place preserves the document and ids just like the Lectern).

*Alternative rejected:* re-implementing wall attachment in C# on the chalkboard block.
Rejected — `HorizontalAttachable` is the shipped, tested vanilla path and keeps the block's
code to config + two flag overrides.

### D5: Fix the malformed JSON in place, matching Lectern semantics + painting characteristics

Strip the Scriptorium comments; fix the `clate`→`slate` texture key; reconcile the `.bbmodel`
texture keys with the blocktype `textures` dict (drop leftover Scriptorium keys glass/material/
lining/etc. or map them, so no face renders untextured); replace the borrowed Scriptorium
handbook sections with chalkboard copy. Adopt the painting's wall characteristics (per D6 +
user request to copy the collision model and several blocktype characteristics): a `side`
variant group, `HorizontalAttachable` behavior, `rotateYByType` on the shape + selection box,
a thin (or `null`) collision box, `replaceable`/`rainPermeable`/`materialDensity`, and
painting-style `guiTransform`/`groundTransform`/`tpHandTransform` so the held item reads as a
wall hanging. Right-size the selection box to the chalkboard model's actual depth against the
wall (the committed box is the free-standing Scriptorium's).

## Risks / Trade-offs

- **Theme chrome bleed (D3):** nav/title buttons resolve theme colors outside `ResolveTheme`.
  → Mitigation: ship the body theme first, inspect in-game, widen the seam only if the chrome
  reads poorly. Low risk — worst case is Lectern-colored buttons on a slate body for one
  iteration.
- **`.bbmodel` ↔ blocktype key mismatch:** the committed model carries mixed
  Scriptorium/chalkboard texture keys; a missed key renders an untextured face.
  → Mitigation: enumerate the model's keys and the blocktype dict, reconcile 1:1, verify
  in-game (a restage smoke test) before marking done.
- **Naming collision with ROADMAP's v6 "drawable chalkboard":** two different concepts share
  the name. → Mitigation: this proposal explicitly scopes to the cosmetic variant; the v6
  drawable board remains a separate future change. Resolve the naming in the Open Question
  below before release copy is written.
- **Wall-vs-floor form factor:** the committed model/boxes came from the free-standing
  Scriptorium. → Mitigation: right-size boxes to the actual model; if the art is intended to
  be wall-mounted (orientable), that is a placement-behavior change and would exceed
  "cosmetic-only" — flagged as an Open Question, defaulting to free-standing like the Lectern.

## Migration Plan

Additive only — a new block code. No world migration. Rollback = don't register the classes
(the block silently won't load, exactly as today).

## Open Questions

1. **Name:** keep "chalkboard" (colliding with the ROADMAP v6 drawable board) or rename this
   cosmetic variant (e.g. "slate board", "task board")? Default: keep the committed name,
   note the collision, revisit before release.
2. **Placement:** RESOLVED — wall-mounted like a vanilla painting (user decision), via the
   `HorizontalAttachable` + `side`-variant idiom (D6). This adds one behavioral dimension
   beyond the cosmetic four; the document layer stays identical.
3. **Guestbook nav button:** include it (the Lectern does) or omit for the chalkboard?
   Default: include, for full Lectern parity.
4. **Recipe:** what does the chalkboard cost to craft? Needs a recipe design (out of the
   pure-code path; a JSON + lang entry). Default: mirror a simple plank/pigment recipe.
