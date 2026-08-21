## ADDED Requirements

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
