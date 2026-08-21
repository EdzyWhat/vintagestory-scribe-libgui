## MODIFIED Requirements

### Requirement: Tracker task kind and fields
The document model SHALL support a `Tracker` block kind that carries a target item
(`TargetItemCode`, an item/block asset code — which MAY be **attribute-encoded** to identify a
specific variant of an attribute-encoded item (see the `attribute-encoded-item-identity` capability) —
stored as a plain string so `Core` stays free of the Vintage Story API), a target quantity
(`TargetQuantity`, an integer ≥ 1), and a current-progress count (`CurrentQuantity`, an integer ≥ 0).
A Tracker block SHALL also retain the fields common to every block (text, completed flag, depth,
`TaskId`, assignment). `TargetQuantity` SHALL be clamped to at least 1 on creation and edit;
`CurrentQuantity` SHALL be clamped to the range `[0, TargetQuantity]` whenever it is set. The kind
value SHALL be appended to the existing kind enumeration (never renumbering `Task` or `Text`).

A `TargetItemCode` for an **attribute-encoded item** SHALL resolve to that specific variant, so the
Tracker shows the variant-correct name and icon. A bare (non-attribute-encoded) `TargetItemCode` SHALL
resolve exactly as before.

#### Scenario: A tracker carries its target and progress
- **WHEN** a Tracker block is created for target item `game:ingot-copper` with `TargetQuantity` 5
- **THEN** the block's kind is `Tracker`, its `TargetItemCode` is `game:ingot-copper`, its
  `TargetQuantity` is 5, and its `CurrentQuantity` is 0

#### Scenario: A tracker for an attribute-encoded item resolves to that variant
- **WHEN** a Tracker block is created from a specific attribute-encoded item's Handbook page (e.g. the
  "Copper Lantern" page)
- **THEN** the Tracker shows that variant's name and icon rather than an attribute-less fallback

#### Scenario: Target quantity is clamped to at least one
- **WHEN** a Tracker is created or edited with a target quantity of 0 or a negative number
- **THEN** the stored `TargetQuantity` is 1

#### Scenario: Current quantity is clamped to the target range
- **WHEN** a Tracker's `CurrentQuantity` is set below 0 or above its `TargetQuantity`
- **THEN** the stored `CurrentQuantity` is clamped into `[0, TargetQuantity]`

### Requirement: Tracker progress is driven by carried inventory only
A Tracker's `CurrentQuantity` SHALL reflect only the matching items the player is **carrying**
(hotbar plus backpack/inventory the player holds), never items in world containers such as chests.
The count SHALL be recomputed when the player's carried inventory changes and periodically as an
edge-case safeguard, and the server SHALL be the authority for the persisted `CurrentQuantity`
(clients report, the server decides).

When the Tracker's target is an **attribute-encoded item**, a carried stack SHALL count only when it
matches that specific variant (its collectible matches and it satisfies the target's stored
attributes) — a copper-lantern Tracker counts copper lanterns and not other metals. A bare
(non-attribute-encoded) target SHALL match by collectible/wildcard exactly as before.

#### Scenario: Counting matches carried items
- **WHEN** the player is carrying 3 stacks totalling 12 units that match the Tracker's target
- **THEN** the Tracker's `CurrentQuantity` reflects the carried total (clamped to `TargetQuantity`)

#### Scenario: Exact-variant matching for an attribute-encoded target
- **WHEN** a Tracker targets copper lanterns and the player is carrying both copper and iron lanterns
- **THEN** only the copper lanterns are counted toward `CurrentQuantity`

#### Scenario: Items in a nearby chest are not counted
- **WHEN** matching items are present only in a chest near the player, not carried
- **THEN** the Tracker's `CurrentQuantity` does not include them

#### Scenario: Progress updates when carried inventory changes
- **WHEN** the player picks up or removes matching items from their carried inventory
- **THEN** the Tracker's `CurrentQuantity` is recomputed to the new carried total
