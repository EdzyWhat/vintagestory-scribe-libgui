## Why

Vintage Story's progression is built around crafting chains — players frequently need to remember
to make N of something, and making it means gathering the right ingredients in the right amounts. A
plain Item Tracker covers "have N of X," but it can't answer the question a crafting goal actually
raises: *"what, and how much, do I need to gather to build these?"* A Crafting Task should own the
output goal **and** spell out its ingredient shopping list, so the whole plan lives in one place and
self-updates as the player collects materials.

## What Changes

- **Adds a `Craft` task kind** to the document model: a composite generator bound to a specific grid
  recipe. The `Craft` row tracks the output item (target count via a stepper, like a Tracker), and
  it **auto-generates one child Item Tracker per recipe ingredient** at the quantity the batch
  requires (ingredient qty × crafts-needed, rounded up). Children are ordinary `Tracker` rows placed
  directly below the parent at indent depth 1.
- **Adds a general "subtask" (depth-1) capability** as a prerequisite: rows can render one level of
  indentation, and the drag grip gains a tap gesture that toggles a row's depth between 0 and 1.
  This is an orthogonal, kind-agnostic capability (any row can be a subtask); Crafting Tasks are its
  first consumer. Depth already round-trips through all three codecs, so no persistence change is
  needed.
- **Creates Crafting Tasks from the Handbook**, not from a bare footer click. An item's Handbook
  page gains an "Add Crafting Task" link **per grid recipe variant** (a single link when the item
  has exactly one grid recipe), reusing the existing append-only Harmony postfix that already injects
  "Add Tracker"/"Add Link". Each link is labeled by its distinguishing ingredient (with a
  "Recipe N" fallback). Items with no grid recipe show no crafting link.
- **Self-heals loosely**: whenever the parent's target changes (or on document open), the `Craft`
  row re-derives the ingredient list and updates the quantities of the contiguous depth-1 rows it
  owns, matching them by item code. It creates missing ingredient rows and updates counts, but
  **never auto-deletes** the player's rows and only ever manages one level deep.
- **Reuses Tracker carried-count progress** for every count-tracked row (parent output + ingredient
  children): progress follows the viewer's carried inventory, and the existing per-player Tracker
  Completion setting (complete / delete / nothing) applies unchanged.
- **v1 deferrals**: liquid ingredients (declared via `liquidContainerProps`/`requiresLitres`) are
  emitted as a non-counting note rather than litre-counted; cumulative crafted-event tracking
  (`OnItemCrafted`) stays deferred — carried-count is the correct first signal.

## Capabilities

### New Capabilities
- `task-subtasks`: A general, kind-agnostic depth-1 indentation capability. Rows can sit at depth 0
  or depth 1; depth-1 rows render indented beneath the row above them; the drag grip's tap gesture
  toggles a row's depth (distinct from press-and-hold to reorder). One level deep only.
- `craft-task`: The `Craft` block kind — a recipe-bound composite generator that owns an output goal
  and auto-generates/maintains its ingredient Item Tracker subtasks, plus its Handbook creation
  links and loose self-heal behavior.

### Modified Capabilities
_(none — no existing spec requirements change; `task-subtasks` and `craft-task` are both new)_

## Impact

- **`src/Core/`**: `ScribeBlockKind` gains `Craft = 4` (appended, never renumbered). `ScribeBlock`
  gains a recipe-binding field (a recipe signature string) so a `Craft` row remembers which variant
  it generates from; it reuses the existing Tracker fields (`TargetItemCode`, `TargetQuantity`,
  `CurrentQuantity`) and the existing `Depth` field (no new depth field, no codec format change).
  A Core-side ingredient-list model and ceil batch math (kept API-free) support the generator.
- **`src/Mod/`**: `ScribeHandbookPatch` appends the per-variant "Add Crafting Task" link(s);
  `ScribeModSystem.Handbook` gains an `AddCraftFromHandbook(itemCode, recipeSignature)` entry point
  and a recipe-probe/generator that expands a chosen grid recipe into ingredient rows.
  `ScribeAddKinds` registers the `Craft` kind (Handbook-only, `RequiresItemContext`).
  `ScribeTrackerCounter.TryResolveIngredient` is extended to resolve wildcard/family codes so
  ingredient children with `linen-*`-style codes count families; `{var}`-bound ingredients resolve
  to their concrete bound code. Row rendering gains the `Craft` label/icon and depth-1 indent; the
  grip GestureDetector gains an `onTap` depth toggle. The carried-count scan gates on a broader
  "count-tracked" predicate so `Craft` parents update like Trackers.
- **`lang/en.json`**: strings for the crafting link(s), the `Craft` row label, and the liquid note.
- **`tests/Core.Tests`**: unit tests for the `Craft` codec round-trip, recipe-signature persistence,
  depth round-trip, ceil batch math, and ingredient-list derivation (all Core, no game install).
- No new mod dependencies (Harmony ships with the base game), no network-packet schema changes, no
  block entity schema changes, no persistence format changes beyond the appended `Kind` value and
  the new recipe-signature string field.
