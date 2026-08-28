## MODIFIED Requirements

### Requirement: A Craft task loosely self-heals its ingredient subtasks
When a Craft task's `TargetQuantity` is changed from the editor's inline stepper, the task SHALL
re-derive its ingredient list and reconcile **only** the contiguous run of `Depth` 1 rows directly
below it, matching them to ingredients by item code (order among those rows SHALL NOT matter). For
each matched child Tracker it SHALL update `TargetQuantity` to the new batch amount. It SHALL **not**
create a child for a missing ingredient, SHALL **not** run on document or editor open, and SHALL
**not** run on complete, delete, unindent, or reorder. It SHALL **never auto-delete** rows
(player-added or player-edited rows survive), and it SHALL manage exactly **one level deep**. The
first non-depth-1 row ends the owned run. Handbook **creation** still generates children once (see
auto-generate); that is not heal.

#### Scenario: Raising the target rescales existing ingredient subtasks
- **WHEN** the player increases a Craft task's `TargetQuantity` so crafts-needed doubles
- **THEN** each owned ingredient child's `TargetQuantity` is doubled in place, preserving its carried progress

#### Scenario: A deleted ingredient is not recreated when the target changes
- **WHEN** the player deletes one ingredient child and then edits the parent's target
- **THEN** that ingredient is not recreated; remaining children in the owned run are only rescaled

#### Scenario: Opening the editor does not recreate children
- **WHEN** the player opens the editor on a Craft parent whose owned run is empty
- **THEN** no ingredient children are created

#### Scenario: Self-heal never deletes and never nests deeper
- **WHEN** the stepper reconcile runs against a Craft task whose depth-1 run contains extra player-added rows
- **THEN** the player-added rows are preserved, no row is auto-deleted, and no depth-2 row is created

### Requirement: A Craft task auto-generates one ingredient subtask per recipe ingredient
On creation, a `Craft` task SHALL generate one child **`Tracker`** block per counting ingredient of
its bound recipe, placed contiguously directly below the parent at `Depth` 1 (subtasks, per the
`task-subtasks` capability). Each child's `TargetItemCode` SHALL be the ingredient's item code and
its `TargetQuantity` SHALL be **ingredient quantity × crafts-needed**, where crafts-needed is
`ceil(TargetQuantity ÷ recipe output quantity)`. Children are ordinary Tracker rows: they render,
count carried inventory, and complete exactly like any Item Tracker. The parent `Craft` row itself
tracks the output item's carried count. Cells that are tools (`IsTool`), not consumed (`Consume` is
false), or tag-only with no usable item code (including the default `*:*` code) SHALL NOT become
children.

#### Scenario: Ingredients are generated at batch quantity
- **WHEN** a Craft task targets 16 of an output whose recipe yields 4 per craft and consumes 2 of ingredient A per craft
- **THEN** crafts-needed is 4 (ceil(16 ÷ 4)) and a child Tracker for ingredient A is generated with `TargetQuantity` 8 (2 × 4)

#### Scenario: Output quantity greater than one rounds crafts up
- **WHEN** a Craft task targets 5 of an output whose recipe yields 4 per craft
- **THEN** crafts-needed is 2 (ceil(5 ÷ 4)) and every ingredient child is scaled by 2

#### Scenario: Each ingredient becomes its own subtask row
- **WHEN** a recipe has three distinct counting ingredients
- **THEN** three `Tracker` children are created at `Depth` 1 directly below the `Craft` parent, one per ingredient

#### Scenario: Debarked-log tools are omitted
- **WHEN** a Crafting Task is created for a debarked oak log (recipe cells: tag-only axe, tag-only hammer, oak log)
- **THEN** the only generated ingredient child is the oak log; no wildcard/`*:*` Tracker (e.g. “Pocketsun (any variant)”) is created

### Requirement: Craft tasks are created only from Handbook grid-recipe links
A `Craft` task SHALL be creatable **only** from an item's Handbook page, via an injected crafting
link, and never from a bare editor footer click (the `Craft` kind requires item context and
no-ops on a null code). An item's Handbook page SHALL show **one crafting link per grid recipe
variant**, collapsing to a single link when the item has exactly one grid recipe, and showing **no
crafting link** when the item has no grid recipe. On the Handbook the single-recipe label SHALL be
**Add ingredients** and a multi-recipe label SHALL be **Add ingredients ({0})** with the
distinguishing ingredient (or "Recipe N"). The editor Add picker SHALL keep the existing
"Add Crafting Task" label. Variants SHALL be grouped by recipe group, wildcard-material fan-out
SHALL be collapsed to one link, and disabled recipes and pure tool ingredients SHALL be filtered
out. Clicking a link SHALL create the `Craft` task (and generate its ingredient subtasks) on the
resolved Scribe surface.

#### Scenario: A single-recipe item shows one crafting link
- **WHEN** an item has exactly one enabled grid recipe
- **THEN** its Handbook page shows one "Add ingredients" link that creates a Craft task bound to that recipe

#### Scenario: A multi-recipe item shows one link per variant
- **WHEN** an item has more than one distinct grid recipe variant (after grouping and wildcard collapse)
- **THEN** its Handbook page shows one labeled crafting link per variant, each labeled "Add ingredients ({0})" with its distinguishing ingredient (or "Recipe N")

#### Scenario: An item with no grid recipe shows no crafting link
- **WHEN** an item has no grid recipe (or only non-grid production methods)
- **THEN** its Handbook page shows no crafting link

#### Scenario: Clicking a crafting link creates the composite task
- **WHEN** the player clicks an "Add ingredients" link on an item's Handbook page
- **THEN** a `Craft` parent bound to that recipe and its ingredient subtasks are added to the resolved Scribe surface
