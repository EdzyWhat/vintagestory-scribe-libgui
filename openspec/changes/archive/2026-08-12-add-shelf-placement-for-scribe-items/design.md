## Context

Scribe documents can currently sit in the world as a placed Lectern block or as held
Notebooks/Tablets dropped into vanilla ground-storage piles (via `GroundStorable`, tuned in
the just-completed ground-storage transform work). Players who keep vanilla books on shelves
reasonably expect to shelve a Notebook or clay Tablet the same way.

Investigation of the shipped game DLLs (`VSSurvivalMod.dll`, decompiled with `ilspycmd`)
established that Vintage Story has **three distinct storage surfaces**, each with its own
opt-in attribute, but all reading one shared transform key:

| Surface | Block entity / behavior | Opt-in attribute | Transform key |
|---|---|---|---|
| General shelf | `BlockEntityShelf` (`entityClass: "Shelf"`) | `shelvable` (string layout, bool `true`→Quadrants, or `IShelvable`) | `onshelfTransform` |
| Bookshelf | `BlockEntityBookshelf` (`entityClass: "Bookshelf"`) | `bookshelveable: true` (bool) | `onshelfTransform` |
| Cabinet | `Display` behavior → `BEBehaviorDisplay` | `displayable.<type>` block with a `size` (falls back to `shelvable`+`DefaultItemSize`) | `onshelfTransform` (then `onDisplayTransform`) |

Vanilla `book.json` declares **all three** (`shelvable: true`, `bookshelveable: true`, and a
`displayable.shelf` block) plus a `groundStorageTransform` — it is the canonical
"everywhere-placeable" opt-in and the exact template this change follows.

Key source facts (from decompilation):
- `BlockEntityShelf.GetShelvableLayout(stack)` returns a layout if `attributes["shelvable"]`
  parses to a known `EnumShelvableLayout` (`Quadrants`/`Halves`/`SingleCenter`), or if it is
  bool `true` (→ `Quadrants`), or if the collectible implements `IShelvable`.
- `BlockEntityBookshelf` accepts a stack iff `attributes["bookshelveable"].AsBool(false)`.
- `BlockBehaviorDisplay.GetDisplayableAttributes(slot, displayType)` reads
  `attributes["displayable"][displayType]` (cabinet `displayType` defaults to `"shelf"`); if
  that's absent but `shelvable` is true it synthesizes a `DefaultItemSize` entry. `TryPut`
  then enforces the declared `size` fits the placement surface, erroring with
  `shelfhelp-toolarge-error` otherwise.
- All three surfaces use `AttributeTransformCode = "onshelfTransform"` for item positioning,
  so a single transform serves every surface.

## Goals / Non-Goals

**Goals:**
- Make Notebook, Clockmaker's Notebook, and all Tablet variants placeable on general shelves,
  bookshelves, and cabinets, matching vanilla books.
- Preserve document identity (docId in stack attributes) across shelve → retrieve.
- Keep it JSON-only: no C#, no `src/Core/` touch, no new dependency.

**Non-Goals:**
- No custom Scribe storage furniture (that's a separate roadmap idea, not this).
- No behavior change to ground storage, the Lectern, or the documents' own editing rules.
- No `IShelvable`/`IDisplayableProps` C# interface implementation — the JSON attribute path is
  sufficient and simpler. (Interface path is the escape hatch only if a per-stack decision
  were ever needed, e.g. layout varying by tablet state; it isn't.)
- Pixel-perfect transforms are not settled in this document — they're an in-game tuning task,
  same as the ground-storage offset just completed.

## Decisions

**Decision 1 — JSON attributes over a C# interface.** Add `shelvable`, `bookshelveable`, and
`displayable.shelf` attributes to each item JSON rather than implementing `IShelvable` /
`IDisplayableProps` in the item C# classes.
- *Why:* the vanilla block entities already read these attributes and do all placement,
  rendering, inventory, and sync work. Books do exactly this. Adding an interface would be
  strictly more code for no behavioral gain, and cuts against the project's "clear,
  conventional" preference.
- *Alternative considered:* implement `IShelvable.GetShelvableType(stack)` to vary layout by
  tablet state — rejected; all these items are small and want the same `Quadrants` layout, so
  a static attribute is correct.

**Decision 2 — `shelvable: "Quadrants"`.** Use the `Quadrants` layout (4-per-shelf-section),
matching books and the items' existing `GroundStorable` `layout: "Quadrants"`.
- *Why:* Notebooks and Tablets are book-sized; Quadrants is the vanilla convention for that
  footprint and keeps the ground-storage and shelf mental models identical.

**Decision 3 — reuse / derive `onshelfTransform` from the tuned ground transform.** Seed each
item's `onshelfTransform` from its already-tuned `groundStorageTransform` and refine in-game.
- *Why:* both place the same mesh flat on a horizontal surface at similar scale, so the ground
  transform is the closest known-good starting point. Books use *different* values for the two
  (ground vs shelf), so expect to tune — this is a starting seed, not a final value.

**Decision 4 — `displayable.shelf` size copied from `book.json`.** Start cabinet `size` from
the vanilla book (`{ width, height, length }` in the `displayable.shelf` block) and adjust if
a Tablet/Notebook reads too large/small in a cabinet.
- *Why:* the cabinet enforces size-fits-surface; the book size is a proven fit for a
  book-sized object, so it's the safe seed.

**Decision 5 — apply to every tablet variant branch.** The tablet's `attributesByType` splits
into base / `*-hard` / `*-fired` / `*-wax`. Add the shelf attributes to the shared base
`attributes` block if inheritance covers all branches; otherwise add to each branch. Verify
which by testing one wet and one non-base (e.g. `*-wax`) variant in-game.
- *Why:* `attributesByType` overrides can shadow the base block; a hard/fired/wax tablet must
  be shelvable too, not just the wet base.

## Risks / Trade-offs

- **[`attributesByType` shadows the base `attributes` block]** → If a variant's branch
  replaces rather than merges the base attributes, only the base wet clay tablet would be
  shelvable. Mitigation: test a `*-wax` and a `*-hard` tablet on all three surfaces; if
  shadowed, duplicate the four attributes into each of the four branches (they already
  duplicate `groundStorageTransform` per branch, so this is the established pattern in the
  file).
- **[Transforms look wrong / clip]** → cosmetic only, never a crash. Mitigation: in-game tuning
  pass, identical method to the ground-storage fix (adjust translation/rotation, break-and-
  replace to re-tesselate, screenshot-compare).
- **[Cabinet size too large → placement silently refused]** → the game shows the
  `shelfhelp-toolarge-error` notice, so it's visible, not silent-fail. Mitigation: seed size
  from `book.json`; shrink if rejected.
- **[Save-compat]** → none. Additive attributes; a stack in a ground pile or hand is
  unaffected, and shelf inventory is standard vanilla Sign-pattern persistence.

## Migration Plan

No migration. Additive item attributes take effect on next game load for all existing and new
items. Rollback = revert the three JSON files. Existing worlds need no touch.

## Open Questions

- Final `onshelfTransform` per item (settled in-game; seeded from the ground transform).
- Final cabinet `displayable.shelf` `size` per item (seeded from `book.json`).
- Whether the wax tablet's distinct mesh needs a different transform than the clay tablets
  (likely minor; confirm during the tuning pass).
