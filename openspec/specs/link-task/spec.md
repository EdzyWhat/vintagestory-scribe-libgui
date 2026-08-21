# link-task Specification

## Purpose
TBD - created by archiving change add-tracker-link-tasks. Update Purpose after archive.
## Requirements
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

### Requirement: A Link task behaves as a hyperlink from every surface
Clicking a Link task's label SHALL open its referenced Handbook page, whether the click occurs in a
Scribe UI (Scriptorium, Lectern, Notebook, Tablet) or on the pinned-task HUD. This activation is
distinct from the row's completion control: opening the page SHALL NOT complete or delete the Link;
completion remains a separate, explicit player action.

#### Scenario: Clicking a link in the Scribe UI opens the handbook page
- **WHEN** the player clicks a Link task's label in any Scribe UI
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Clicking a link on the HUD opens the handbook page
- **WHEN** the player clicks a pinned Link task on the HUD
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Activating a link leaves its completion state unchanged
- **WHEN** the player activates (opens) a Link task that is not completed
- **THEN** after the page opens, the Link is still not completed

### Requirement: Cooked-meal Handbook pages offer an Add Link action
A cooked-meal (and pie) Handbook page SHALL present an "Add to Scribe" section with a single Add Link
action, consistent with the guide/explainer page's Add Link. Clicking it SHALL create a guide-page
Link targeting that meal's recipe Handbook page (`handbook-mealrecipe-<code>`), labeled with the meal's
displayed title. Meal pages SHALL NOT offer a Tracker or Crafting Task action (a meal has no stable
countable item and is not a grid recipe).

#### Scenario: A cooked meal page shows Add Link

- **WHEN** the player opens a cooked meal's Handbook page (e.g. Vegetable Stew)
- **THEN** an "Add to Scribe" section with a single Add Link action is shown, and no Tracker or Craft
  action appears

#### Scenario: Adding a meal Link creates a guide-page Link to the meal recipe page

- **WHEN** the player clicks Add Link on a meal page and a Scribe surface is open or openable
- **THEN** a Link row is added whose stored target is the meal's recipe page code and whose label is
  the meal's title, and opening that Link navigates back to the meal recipe Handbook page

#### Scenario: The meal Link row displays a readable title, not a raw key

- **WHEN** a meal Link row is shown in the read/editor view, the Pinned tab, or the HUD
- **THEN** it displays the meal's resolved title (e.g. "Vegetable Stew"), not a raw lang key or page
  code

