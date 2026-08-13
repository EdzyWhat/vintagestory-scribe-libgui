## Why

Scribe documents already sit in the world two ways — a placed Lectern block, and held
Notebooks/Tablets that drop into vanilla ground-storage piles (up to 4 per block). But the
natural home for a small stack of books and tablets is furniture: shelves, bookshelves, and
cabinets. Vanilla `book.json` opts into all three surfaces, so a player who keeps books on a
shelf reasonably expects to shelve a Scribe Notebook or clay Tablet the same way — and today
they can't. This is a small, purely additive polish item that makes the documents feel like
first-class in-world objects instead of ground-only clutter.

## What Changes

- Notebooks (`scribenotebook`), Clockmaker's Notebooks (`scribeclockmakernotebook`), and all
  Tablet variants (`scribetablet`, clay + wax) become **placeable on general shelves,
  bookshelves, and cabinets** — the same three surfaces vanilla books accept.
- This is a **JSON-only, item-attribute opt-in** — no C# and no `src/Core/` change. Each item
  gets three attributes mirroring `book.json`:
  - `shelvable` — accepted by general shelves (`BlockEntityShelf`); value is a layout
    (`"Quadrants"` for the small tablets/books, or bool `true` → Quadrants).
  - `bookshelveable: true` — accepted by bookshelves (`BlockEntityBookshelf`).
  - `displayable.shelf` (a `size` + optional transform) — accepted by cabinets' `Display`
    behavior (`BEBehaviorDisplay`); the size must fit the placement surface or the game
    rejects it with a "too large" notice.
  - an `onshelfTransform` (position/rotation/origin) so each item sits correctly on the
    surface — all three storage systems read this one transform key.
- The document's identity is preserved across shelving: shelves store the full `ItemStack`
  (including the Scribe docId attribute), so a shelved-then-retrieved document reopens intact.
  No new persistence path — this rides the vanilla shelf inventory, itself Sign-pattern-based.
- Per-item `onshelfTransform` values (and the `displayable.shelf` size) will need in-game
  tuning, exactly like the ground-storage transform tuning just completed — the proposal
  ships the opt-in; the exact offsets are settled in playtest.

## Capabilities

### New Capabilities
- `scribe-item-shelf-placement`: Scribe document items (Notebook, Clockmaker's Notebook, and
  every Tablet variant) can be placed on and retrieved from vanilla general shelves,
  bookshelves, and cabinets, with their document identity preserved and a per-item on-surface
  transform positioning them correctly.

### Modified Capabilities
<!-- None. The two item specs (notebook-item, clay-wax-tablet-item) describe the documents'
     own behavior; shelf placement is a new cross-cutting world-interaction capability that
     doesn't change either item's existing requirements. -->

## Impact

- **Assets (only):** `src/Mod/assets/scribe/itemtypes/scribenotebook.json`,
  `scribeclockmakernotebook.json`, `scribetablet.json` — add `shelvable`, `bookshelveable`,
  `displayable.shelf`, and `onshelfTransform` attributes. The tablet's four `attributesByType`
  branches (base / `*-hard` / `*-fired` / `*-wax`) each need the attributes so every variant
  is shelvable.
- **No C# / no `src/Core/`** — the vanilla shelf/bookshelf/cabinet block entities already do
  all the work; this is opt-in metadata.
- **No new dependencies** — vanilla `VintagestoryAPI` behaviors only.
- **Save-compat:** additive and safe. Existing worlds/items gain the ability with no
  migration; a document already in a ground pile or hand is unaffected.
- **Docs:** wiki Items/Tablets pages and the handbook can note shelf placement; not release-
  gating.
