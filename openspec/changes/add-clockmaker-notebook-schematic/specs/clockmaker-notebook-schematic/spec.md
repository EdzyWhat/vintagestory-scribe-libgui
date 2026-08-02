## ADDED Requirements

### Requirement: A schematic item grants a trait-free craft path

The mod SHALL provide a **Clockmaker's Notebook Schematic** item (`scribe:clockmakerschematic`)
that acts as a reusable blueprint for crafting the Clockmaker's Notebook. There SHALL be a grid
recipe whose output is the Clockmaker's Notebook, which requires the schematic and does NOT carry
any `requiresTrait` restriction, so any player holding the schematic can complete it regardless of
character class. This recipe SHALL sit alongside the existing `tinkerer`-gated recipe rather than
replacing it: a Clockmaker with the trait continues to craft the Clockmaker's Notebook without a
schematic, and a player holding the schematic crafts it without the trait.

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

### Requirement: The schematic is sold by Commodities and Treasure Hunter traders

The schematic SHALL be added to the ware pools of the **Commodities** trader and the **Treasure
Hunter** trader by patching their shared tradelists, without overwriting any existing wares. It
SHALL be priced as a rare, single-stock item in the traders' currency (temporal/rusty gears), so
it is an occasional find rather than a guaranteed stock item. The patch SHALL NOT alter any other
trader type's wares.

#### Scenario: The schematic can appear in a Commodities trader's stock
- **WHEN** a Commodities trader's ware pool is rolled
- **THEN** the schematic is one of the entries eligible to appear for sale, at its configured
  gear price and single stock size, alongside the trader's existing wares

#### Scenario: The schematic can appear in a Treasure Hunter trader's stock
- **WHEN** a Treasure Hunter trader's ware pool is rolled
- **THEN** the schematic is one of the entries eligible to appear for sale, at its configured
  gear price and single stock size, alongside the trader's existing wares

#### Scenario: Other traders are unaffected
- **WHEN** any trader that is not a Commodities or Treasure Hunter trader is stocked
- **THEN** its ware pool does not include the schematic and is otherwise unchanged
