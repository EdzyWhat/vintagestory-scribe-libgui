## ADDED Requirements

### Requirement: Item-task targets preserve attribute-encoded identity

An item-task target (a Link's reference, a Tracker's target item, or a Craft parent's output item)
SHALL preserve the identifying ItemStack attributes of an **attribute-encoded item** — an item whose
identity is carried in `ItemStack.Attributes` rather than fully in its collectible code (a lantern's
`material`/`glass`/`lining`, a meal's contents, etc.). The target SHALL remain a single plain string
so `Core` stays free of any Vintage Story API dependency; the Mod layer owns the encoding and
decoding.

The encoding SHALL be **lossless for the meaningful attributes and stable**: transient or
game-managed attributes (`GlobalConstants.IgnoredStackAttributes` and `durability`) SHALL be excluded,
and the remaining attributes SHALL be serialized deterministically so the same item always yields the
same target string. An item with no meaningful attributes SHALL encode to its bare collectible code,
identical to a fully-code-identified item.

Resolving a target string back to a game item SHALL rebuild the full attributed `ItemStack`, so the
resolved item exposes the same display name, Handbook page, and recipe/inventory identity as the item
the task was created from.

#### Scenario: An attribute-encoded item round-trips to the correct item

- **WHEN** a task target is created from a specific attribute-encoded item (e.g. the "Copper Lantern"
  Handbook page) and then resolved
- **THEN** the resolved item is that same variant — its display name and Handbook page match the item
  the task was created from, not a generic or attribute-less fallback

#### Scenario: A fully-code-identified item is unchanged

- **WHEN** a task target is created from an item whose identity is entirely in its collectible code
  (e.g. `game:ingot-copper`)
- **THEN** the stored target string is that bare code, and it resolves exactly as before this change

### Requirement: Legacy bare-code targets remain resolvable

A target string stored before this change — a bare collectible code with no attribute encoding —
SHALL continue to resolve to its item exactly as it did previously. No save migration SHALL be
required, and a document containing legacy targets SHALL open, render, and function without change.

#### Scenario: An existing document's targets still work

- **WHEN** a document saved before this change (whose Tracker/Link targets are bare collectible codes)
  is opened
- **THEN** every target resolves and renders as it did before, with no migration step and no error

### Requirement: Attribute-encoded items expose their Handbook and recipe affordances

Because an attribute-encoded item's recipe and name depend on its attributes, Scribe SHALL use the
item's full attributed stack when deriving its Handbook affordances. An attribute-encoded item that
has a grid recipe SHALL therefore offer an "Add Crafting Task" link on its Handbook page (the recipe
match SHALL succeed against the attributed stack), and opening a task's Handbook page SHALL navigate
to the variant-correct page — preferring a collectible's own Handbook page code when it provides one.

#### Scenario: A lantern's Handbook page offers "Add Crafting Task"

- **WHEN** the player opens the Handbook page of a craftable attribute-encoded item (e.g. a lantern)
- **THEN** an "Add Crafting Task" link is present, resolving to that item's grid recipe

#### Scenario: Opening a task's page navigates to the variant page

- **WHEN** the player activates the Handbook link on a Link/Tracker/Craft task for an attribute-encoded
  item
- **THEN** the game opens the Handbook page for that specific variant
