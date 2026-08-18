## ADDED Requirements

### Requirement: Craft task kind, fields, and recipe binding
The document model SHALL support a `Craft` block kind (`Craft = 4`, appended to the existing kind
enumeration and never renumbering existing values). A `Craft` block SHALL carry the same output
reference and quantity fields as `Tracker` — a target output item (`TargetItemCode`, a plain string
asset code), a target quantity (`TargetQuantity`, integer ≥ 1), and a current-progress count
(`CurrentQuantity`, integer ≥ 0) — plus a **recipe binding**: a stable recipe-signature string that
identifies which grid recipe variant the task generates its ingredients from. A `Craft` block SHALL
also retain the fields common to every block (text, completed flag, `Depth`, `TaskId`, assignment).
`TargetQuantity` SHALL be clamped to at least 1; `CurrentQuantity` SHALL be clamped to
`[0, TargetQuantity]` whenever set. The recipe signature SHALL persist through every codec.

#### Scenario: A Craft task carries its output target and recipe binding
- **WHEN** a Craft task is created for output `game:plank-aged` from a chosen grid recipe with `TargetQuantity` 16
- **THEN** the block's kind is `Craft`, its `TargetItemCode` is `game:plank-aged`, its `TargetQuantity`
  is 16, its `CurrentQuantity` is 0, and its recipe-signature field identifies the chosen variant

#### Scenario: Target quantity is clamped to at least one
- **WHEN** a Craft task is created or edited with a target quantity of 0 or negative
- **THEN** the stored `TargetQuantity` is 1

#### Scenario: Recipe binding survives a save/load round-trip
- **WHEN** a document containing a Craft task is serialized and deserialized (binary, JSON, or TSV)
- **THEN** the recipe-signature field is preserved so the task regenerates from the same variant

### Requirement: A Craft task auto-generates one ingredient subtask per recipe ingredient
On creation, a `Craft` task SHALL generate one child **`Tracker`** block per counting ingredient of
its bound recipe, placed contiguously directly below the parent at `Depth` 1 (subtasks, per the
`task-subtasks` capability). Each child's `TargetItemCode` SHALL be the ingredient's item code and
its `TargetQuantity` SHALL be **ingredient quantity × crafts-needed**, where crafts-needed is
`ceil(TargetQuantity ÷ recipe output quantity)`. Children are ordinary Tracker rows: they render,
count carried inventory, and complete exactly like any Item Tracker. The parent `Craft` row itself
tracks the output item's carried count.

#### Scenario: Ingredients are generated at batch quantity
- **WHEN** a Craft task targets 16 of an output whose recipe yields 4 per craft and consumes 2 of ingredient A per craft
- **THEN** crafts-needed is 4 (ceil(16 ÷ 4)) and a child Tracker for ingredient A is generated with `TargetQuantity` 8 (2 × 4)

#### Scenario: Output quantity greater than one rounds crafts up
- **WHEN** a Craft task targets 5 of an output whose recipe yields 4 per craft
- **THEN** crafts-needed is 2 (ceil(5 ÷ 4)) and every ingredient child is scaled by 2

#### Scenario: Each ingredient becomes its own subtask row
- **WHEN** a recipe has three distinct counting ingredients
- **THEN** three `Tracker` children are created at `Depth` 1 directly below the `Craft` parent, one per ingredient

### Requirement: Ingredient matching resolves wildcard families and concrete bound variables
An ingredient child's carried-count SHALL match the recipe ingredient's intent. A **wildcard/family**
ingredient (e.g. `linen-*`, `bowl-*-fired`) SHALL count any carried stack in that family. An
ingredient bound to an output variable (a `{var}` substitution, e.g. a `plank-{wood}` recipe whose
ingredient is the same-wood log) SHALL resolve to the **concrete** bound code for the chosen variant,
not broadened to the whole family. This requires extending the tracker ingredient resolver so it can
resolve wildcard/family codes in addition to concrete codes.

#### Scenario: A wildcard ingredient counts the whole family
- **WHEN** an ingredient child's code is a family wildcard such as `game:linen-*`
- **THEN** any carried stack matching that family counts toward the child's `CurrentQuantity`

#### Scenario: A variable-bound ingredient counts only the concrete variant
- **WHEN** the chosen recipe variant binds an ingredient via a `{var}` output substitution (e.g. birch plank ← birch log)
- **THEN** the generated child's code is the concrete bound code (birch log), and only that concrete item counts, not the whole log family

### Requirement: Liquid ingredients are surfaced as a non-counting note in v1
An ingredient declared as a liquid requirement (via `liquidContainerProps` / `requiresLitres`) SHALL
NOT be emitted as a counting Tracker child in this version. Instead, the Craft task SHALL surface it
as a non-counting note (a `Text` block at `Depth` 1) describing the liquid and amount, or omit it —
it SHALL NOT be litre-counted. Litre-accurate liquid tracking is deferred.

#### Scenario: A liquid ingredient does not become a counting Tracker
- **WHEN** a recipe requires a liquid ingredient (e.g. 0.25 L honey via `requiresLitres`)
- **THEN** no counting Tracker child is generated for the liquid; it appears as a non-counting note or is omitted

### Requirement: A Craft task loosely self-heals its ingredient subtasks
When a Craft task's `TargetQuantity` changes, or when the document is opened, the task SHALL
re-derive its ingredient list and reconcile the **contiguous run of `Depth` 1 rows directly below it**
that it owns, matching them to ingredients by item code. For each ingredient it SHALL update the
matched child's `TargetQuantity` to the new batch amount, and create a child for any ingredient with
no matching row. It SHALL **never auto-delete** rows (player-added or player-edited rows survive),
and it SHALL manage exactly **one level deep** (it never descends into or creates depth-2 rows). Rows
the player manually promotes out of the depth-1 run (or reorders away) leave the managed set.

#### Scenario: Raising the target rescales existing ingredient subtasks
- **WHEN** the player increases a Craft task's `TargetQuantity` so crafts-needed doubles
- **THEN** each owned ingredient child's `TargetQuantity` is doubled in place, preserving its carried progress

#### Scenario: A missing ingredient subtask is recreated, others are left alone
- **WHEN** the player deletes one ingredient child and then edits the parent's target
- **THEN** the deleted ingredient's child is recreated at the correct quantity and the remaining children are only rescaled, not duplicated or removed

#### Scenario: Self-heal never deletes and never nests deeper
- **WHEN** self-heal runs against a Craft task whose depth-1 run contains extra player-added rows
- **THEN** the player-added rows are preserved, no row is auto-deleted, and no depth-2 row is created

### Requirement: Craft task progress and completion follow the Tracker mechanism
Both the `Craft` parent (output count) and its `Tracker` children SHALL derive `CurrentQuantity` from
the viewer's **carried** inventory (hotbar plus backpack), never world containers, recomputed on
carried-inventory change and periodically as a safeguard. Reaching target on any of these rows SHALL
trigger the existing per-player Tracker Completion setting (**completes** / **deletes** / **does
nothing**), default *completes*. The carried-count scan SHALL gate on a predicate that covers both
`Tracker` and `Craft` kinds so the `Craft` parent updates alongside its children.

#### Scenario: Counting matches carried items for a Craft parent and its children
- **WHEN** the player carries 8 of a Craft parent's output (target 16) and 3 of an ingredient child's item (target 8)
- **THEN** the parent shows `CurrentQuantity` 8 and that child shows `CurrentQuantity` 3

#### Scenario: Items in a chest are not counted
- **WHEN** matching items exist only in a nearby chest, not carried
- **THEN** neither the Craft parent nor its children include them in `CurrentQuantity`

#### Scenario: Default behavior completes a satisfied row
- **WHEN** a Craft parent or child with the default completion setting reaches its target
- **THEN** that row is marked completed

### Requirement: Craft task rows render distinctly with a have/need counter
A `Craft` parent row SHALL display the output item's icon, its name, and a `have/need` counter (e.g.
"Aged plank 8/16") using the same shortfall/satisfied states as an Item Tracker, and SHALL be visually
distinguishable from an Item Tracker (a craft-intent icon or a "Craft" label framing) so the player
sees intent at a glance. Ingredient children render as ordinary indented Tracker rows.

#### Scenario: Partial progress shows shortfall state
- **WHEN** a Craft parent shows `CurrentQuantity` 4 of `TargetQuantity` 16
- **THEN** the row shows "4/16" and a shortfall/partial progress state

#### Scenario: Craft parent is visually distinct from an Item Tracker for the same item
- **WHEN** a document contains both a Craft task and an Item Tracker for the same item
- **THEN** each row has a distinct label or icon identifying its kind

### Requirement: Craft tasks are created only from Handbook grid-recipe links
A `Craft` task SHALL be creatable **only** from an item's Handbook page, via an injected "Add Crafting
Task" link, and never from a bare editor footer click (the `Craft` kind requires item context and
no-ops on a null code). An item's Handbook page SHALL show **one crafting link per grid recipe
variant**, collapsing to a single link when the item has exactly one grid recipe, and showing **no
crafting link** when the item has no grid recipe. Each link SHALL be labeled by the variant's
distinguishing ingredient, with a "Recipe N" fallback when no distinguishing label is available.
Variants SHALL be grouped by recipe group, wildcard-material fan-out SHALL be collapsed to one link,
and disabled recipes and pure tool ingredients SHALL be filtered out. Clicking a link SHALL create
the `Craft` task (and generate its ingredient subtasks) on the resolved Scribe surface, reusing the
existing three-tier Handbook "Add to Scribe" surface resolution.

#### Scenario: A single-recipe item shows one crafting link
- **WHEN** an item has exactly one enabled grid recipe
- **THEN** its Handbook page shows one "Add Crafting Task" link that creates a Craft task bound to that recipe

#### Scenario: A multi-recipe item shows one link per variant
- **WHEN** an item has more than one distinct grid recipe variant (after grouping and wildcard collapse)
- **THEN** its Handbook page shows one labeled crafting link per variant, each labeled by its distinguishing ingredient (or "Recipe N")

#### Scenario: An item with no grid recipe shows no crafting link
- **WHEN** an item has no grid recipe (or only non-grid production methods)
- **THEN** its Handbook page shows no "Add Crafting Task" link

#### Scenario: Clicking a crafting link creates the composite task
- **WHEN** the player clicks an "Add Crafting Task" link on an item's Handbook page
- **THEN** a `Craft` parent bound to that recipe and its ingredient subtasks are added to the resolved Scribe surface
