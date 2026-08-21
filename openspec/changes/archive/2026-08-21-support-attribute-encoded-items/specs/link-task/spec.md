## MODIFIED Requirements

### Requirement: Link task kind and reference field
The document model SHALL support a `Link` block kind that carries a reference target
(`LinkTarget`, a plain string identifying a Handbook page — an item/block asset code, which MAY be
**attribute-encoded** to identify a specific variant of an attribute-encoded item (see the
`attribute-encoded-item-identity` capability), and which is stored without any Vintage Story API
dependency in `Core`). A Link block SHALL retain the fields common to every block (text, completed
flag, depth, `TaskId`, assignment) but SHALL NOT carry the Tracker quantity fields. The kind value
SHALL be appended to the existing kind enumeration (never renumbering existing kinds). A Link is a
reference, not a counter: it has no progress and is completed only by the player, never automatically.

A `LinkTarget` for an **attribute-encoded item** SHALL resolve to that specific variant, so the Link
shows the variant-correct name and opens the variant-correct Handbook page rather than an
attribute-less fallback. A bare (non-attribute-encoded) `LinkTarget` SHALL resolve exactly as before.

#### Scenario: A link carries its reference target
- **WHEN** a Link block is created referencing Handbook page for `game:ingot-copper`
- **THEN** the block's kind is `Link`, its `LinkTarget` is `game:ingot-copper`, and it has no
  Tracker quantity fields set

#### Scenario: A link to an attribute-encoded item resolves to that variant
- **WHEN** a Link block is created from a specific attribute-encoded item's Handbook page (e.g. the
  "Copper Lantern" page)
- **THEN** the Link shows that variant's name and its label opens that variant's Handbook page, not an
  attribute-less fallback

#### Scenario: A link is not auto-completed
- **WHEN** any inventory or world change occurs
- **THEN** a Link task's completed flag is unchanged (only an explicit player action toggles it)
