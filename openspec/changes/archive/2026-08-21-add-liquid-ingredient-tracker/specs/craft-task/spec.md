## ADDED Requirements

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
