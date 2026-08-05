## MODIFIED Requirements

### Requirement: A schematic item grants a trait-free craft path

The mod SHALL provide a **Clockmaker's Notebook Schematic** item (`scribe:clockmakerschematic`)
that acts as a reusable blueprint for crafting the Clockmaker's Notebook. There SHALL be a grid
recipe whose output is the Clockmaker's Notebook, which requires the schematic and does NOT carry
any `requiresTrait` restriction, so any player holding the schematic can complete it regardless of
character class. This recipe SHALL sit alongside the existing `tinkerer`-gated recipe rather than
replacing it: a Clockmaker with the trait continues to craft the Clockmaker's Notebook without a
schematic, and a player holding the schematic crafts it without the trait. The schematic recipe's
ingredient pattern SHALL fit within the 3×3 crafting grid (the vanilla maximum), so it is a valid,
placeable, and craftable grid recipe.

#### Scenario: A non-Clockmaker crafts with the schematic
- **WHEN** a player without the `tinkerer` trait arranges the schematic recipe's ingredients
  (including the schematic) on the crafting grid
- **THEN** the recipe completes and yields one Clockmaker's Notebook

#### Scenario: A Clockmaker still crafts without the schematic
- **WHEN** a player with the `tinkerer` trait arranges the original trait-gated recipe's
  ingredients (with no schematic) on the crafting grid
- **THEN** that recipe still completes as before, unchanged by this capability

#### Scenario: A non-Clockmaker without the schematic still cannot craft it
- **WHEN** a player without the `tinkerer` trait and without the schematic attempts the
  trait-gated recipe
- **THEN** that recipe does not complete (the existing trait gate is unaffected)

#### Scenario: The schematic recipe fits the crafting grid
- **WHEN** the schematic recipe is registered and a player lays its ingredients out on the
  standard 3×3 crafting grid
- **THEN** the recipe's ingredient pattern occupies at most 3 columns and 3 rows, so it registers
  as a valid grid recipe and can be arranged and completed (rather than being silently unusable
  because it exceeds the grid dimensions)

### Requirement: The schematic is a reusable blueprint, not consumed

The schematic recipe SHALL retain the schematic in the crafting grid on completion (via
`consume: false`), so a single schematic enables unlimited Clockmaker's Notebook crafts. Only the
other ingredients SHALL be consumed. The schematic SHALL be a single-stack (`maxstacksize: 1`)
paper blueprint item modeled on the vanilla Glider Schematic; it SHALL carry its own display name
and in-game handbook entry, and it SHALL be obtainable in the Creative inventory.

#### Scenario: Crafting does not consume the schematic
- **WHEN** a player completes the schematic recipe
- **THEN** the schematic remains in the crafting grid and the other ingredients are consumed, so
  the player can craft again without buying another schematic

#### Scenario: The schematic is documented in the handbook
- **WHEN** a player opens the schematic's in-game handbook page
- **THEN** it explains that the schematic unlocks crafting the Clockmaker's Notebook without the
  Clockmaker class, and the Clockmaker's Notebook handbook mentions this schematic path alongside
  the trait path

#### Scenario: The Clockmaker's Notebook handbook shows both recipes as separate grids
- **WHEN** a player opens the Clockmaker's Notebook's in-game handbook page
- **THEN** the "Created by" section renders two distinct crafting grids — one for the trait-gated
  recipe and one for the schematic recipe — with the trait-gated grid marked by the vanilla
  `* Requires <trait> trait` asterisk note and the schematic grid carrying no trait note, and the
  two recipes are shown side by side rather than collapsed into a single cycling entry
