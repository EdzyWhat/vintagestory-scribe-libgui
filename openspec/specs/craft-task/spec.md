# craft-task Specification

## Purpose
TBD - created by archiving change add-liquid-ingredient-tracker. Update Purpose after archive.
## Requirements
### Requirement: Liquid ingredients generate counting Tracker subtasks

A Crafting Task SHALL generate a counting Tracker subtask for each liquid ingredient its recipe requires
(declared as `attributes.liquidContainerProps.requiresContent`, or a per-ingredient
`RecipeAttributes.requiresContent`), instead of a non-counting note. The subtask is an ordinary depth-1
Tracker child whose `TargetItemCode` is the liquid's own item/block code and whose counted unit is litres.
This supersedes the prior "liquid ingredients are surfaced as a note" behavior; the note remains only as the
unresolvable-liquid fallback.

#### Scenario: A one-litre liquid ingredient becomes a Tracker with target 1

- **WHEN** a Crafting Task is generated for a recipe requiring 1 L of `game:blackdye`
- **THEN** the task has a depth-1 Tracker subtask targeting `game:blackdye` with a target of `1`
- **AND** the subtask shows a live have/need counter, not a plain Text note

#### Scenario: Multiple distinct liquids each get their own Tracker

- **WHEN** a recipe requires two different liquids
- **THEN** each distinct liquid code generates its own depth-1 Tracker subtask
- **AND** two requirements sharing the same liquid code are merged into one subtask whose target sums their
  litres

### Requirement: Liquid Tracker target is the batch litres rounded up

A liquid Tracker subtask's target SHALL be the recipe's per-craft litre requirement multiplied by the number
of crafts the parent needs, with the batch **total** rounded up to the nearest whole litre (`ceil`), and at
least `1`. Rounding is applied to the batch total, never per craft.

#### Scenario: Fractional per-craft litres are ceiled at the batch total

- **WHEN** a recipe requires 0.1 L per craft and the parent needs 10 crafts
- **THEN** the liquid subtask target is `1` (ceil of the 1.0 L batch total), not `10`

#### Scenario: Whole-litre requirement scales linearly

- **WHEN** a recipe requires 2 L per craft and the parent needs 3 crafts
- **THEN** the liquid subtask target is `6`

### Requirement: Liquid quantity is counted from carried containers

The carried-inventory count for a liquid Tracker SHALL be the sum of litres of that liquid held in the
viewing player's carried liquid containers (hotbar + backpack), computed as `content.StackSize ÷ ItemsPerLitre`
for each container whose content matches the tracked liquid. Loose (non-container) stacks and block-stored or
placed-container liquids are never counted. The summed litres are floored to a whole number for the have/need
readout; "satisfied" remains `current ≥ target`.

#### Scenario: Litres across multiple containers are summed

- **WHEN** the player carries a bucket holding 4 L and a bowl holding 1 L of the tracked liquid
- **THEN** the Tracker's current count is `5`

#### Scenario: A container of a different liquid does not count

- **WHEN** the player carries a container holding a liquid other than the tracked one
- **THEN** that container contributes `0` to the Tracker's count

#### Scenario: Carrying at least the required whole litres satisfies the Tracker

- **WHEN** a liquid Tracker's target is `1` and the player carries a container holding exactly 1 L
- **THEN** the Tracker reads as satisfied and the player's tracker-completion setting is applied

### Requirement: Unresolvable liquids fall back to a note

When a liquid ingredient cannot be resolved to a concrete counting Tracker — its code is a wildcard, its
`requiresLitres` is missing or non-positive, or its item/block code does not resolve — the Crafting Task
SHALL surface it as the existing non-counting Text note rather than a broken Tracker.

#### Scenario: A wildcard liquid code degrades to a note

- **WHEN** a recipe's liquid requirement uses a wildcard code that resolves to no single item
- **THEN** the Crafting Task adds a Text note naming the liquid instead of a Tracker subtask
- **AND** no exception is thrown and the rest of the task's subtasks are unaffected

### Requirement: Crafting Task binds the recipe variant shown on the Handbook page
When the player creates a Crafting Task from an item's Handbook page, the task SHALL bind the grid
recipe whose resolved output matches THAT page's on-screen item — including its distinguishing
itemstack attributes for an attribute-encoded output (e.g. a metal lantern's `material` / `lining` /
`glass`) — and SHALL derive that variant's own ingredient list. Recipe-variant identity SHALL be keyed
on the output's attribute-qualified Handbook page code (`GuiHandbookItemStackPage.PageCodeForStack`),
NOT on the bare collectible code, so that distinct attribute variants of one code family never collapse
to a single recipe. For an attribute-less common item the identity SHALL be equivalent to today's
(no behavioral change to those items' tasks or links).

#### Scenario: A gold lantern's task uses the gold recipe, not the first metal variant
- **WHEN** the player opens the gold metal lantern's Handbook page and clicks "Add Crafting Task"
- **THEN** the created Craft task's ingredient subtasks list gold plate and gold nails/strips (the gold
  recipe's actual ingredients), not copper's

#### Scenario: Different metal lanterns produce different tasks
- **WHEN** the player creates Crafting Tasks from two different metal lanterns' Handbook pages
- **THEN** the two tasks bind distinct recipe variants with each metal's own ingredient list, rather
  than both resolving to the same (first-in-registry) variant

#### Scenario: Common attribute-less items are unchanged
- **WHEN** the player creates a Crafting Task for a common item whose output carries no distinguishing
  attributes (e.g. planks)
- **THEN** the resolved recipe, ingredient list, and link labels are identical to the prior behavior

### Requirement: Genuine wildcard ingredients render a readable family name
When a Crafting Task's derived ingredient is a genuine wildcard family (a broad ingredient the recipe
did NOT resolve to a single concrete item, e.g. `metalplate-*`), the ingredient's child Tracker SHALL
display a human-readable family name (e.g. "Any metal plate") rather than a raw `domain:code-*` string.
The child Tracker's stored matching code SHALL remain the wildcard (so it still counts any family
member). A `{var}`-bound (concrete) ingredient SHALL keep its exact resolved item name.

#### Scenario: A wildcard ingredient shows a family name
- **WHEN** a Crafting Task has an ingredient that is a genuine wildcard family
- **THEN** its subtask row shows a readable family label, not the raw wildcard code, and still counts
  any matching family member toward the target

### Requirement: Liquid and container ingredients are noted, not mis-counted
When a grid recipe's ingredient is a liquid portion (or a structural liquid container that is not a
countable discrete good), the Crafting Task SHALL surface it as a non-counting note rather than a
counted ingredient subtask, consistent with the existing liquid-note behavior.

#### Scenario: A liquid ingredient becomes a note
- **WHEN** a Crafting Task is derived from a recipe that consumes a liquid portion
- **THEN** that liquid appears as a note on the task, not as a counted ingredient subtask

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

### Requirement: Ingredient subtask counts redraw live when the parent target changes
When the player changes a `Craft` parent's target quantity from the editor's inline stepper, the
ingredient subtask rows SHALL visually redraw with their rescaled target counts immediately, within
the same editor view, WITHOUT requiring a view swap (edit↔read) or any other externally forced
redraw. The field the player is actively editing (the focused parent stepper) SHALL NOT be disrupted
— it keeps its caret/focus and continues stepping — while the unfocused ingredient steppers update
in place.

#### Scenario: Raising the parent target rescales the visible ingredient counts in place
- **WHEN** a Craft task is open in the editor and the player raises the parent's target quantity with
  the +/- stepper
- **THEN** each ingredient subtask row's displayed target count updates to its rescaled value in the
  same frame's redraw, with no view swap required

#### Scenario: The parent stepper is not disrupted by the child redraw
- **WHEN** the player steps the parent target repeatedly
- **THEN** the parent stepper retains focus and continues stepping smoothly while the child ingredient
  counts update beneath it

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

