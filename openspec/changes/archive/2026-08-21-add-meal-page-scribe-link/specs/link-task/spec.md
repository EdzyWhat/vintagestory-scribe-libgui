## ADDED Requirements

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
