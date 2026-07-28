# lectern-crafting Specification

## Purpose
TBD - created by archiving change add-lectern-recipe. Update Purpose after archive.
## Requirements
### Requirement: The Lectern is craftable in survival from a writing-desk ingredient set

The mod SHALL provide a grid-crafting recipe whose output is one `scribe:scribelectern` block, so
the Lectern is obtainable in survival play rather than only from the creative inventory. The recipe
SHALL consume a wooden frame and writing implements: four wooden planks, metal nails, a sheet of
parchment, a feather, and plain leather. The recipe SHALL be authored as a data-only asset that the
game auto-loads; it SHALL NOT require any C# registration code.

#### Scenario: Crafting yields one Lectern

- **WHEN** a player places the required ingredients in the crafting grid in the recipe's arrangement
  (including a bowl of ink, per the ink requirement)
- **THEN** the crafting output is exactly one `scribe:scribelectern` block

#### Scenario: The Lectern remains available in creative

- **WHEN** a player opens the creative inventory
- **THEN** the Lectern is still listed there (the recipe adds a survival path; it does not remove
  the existing creative-inventory availability)

### Requirement: The recipe consumes one litre of ink supplied in a bowl

The recipe SHALL require ink in the form of one litre of black dye (`game:dye-black`) held in a
fired bowl (`game:bowl-*-fired`), matching the vanilla ink-and-quill liquid-container mechanism. The
bowl SHALL be consumed by the craft. The recipe SHALL NOT match when the bowl is empty or contains a
different liquid, so an unfilled or wrongly-filled bowl cannot produce a Lectern.

#### Scenario: A bowl of black dye satisfies the ink requirement

- **WHEN** the ink slot holds a fired bowl containing at least one litre of black dye and all other
  ingredients are present
- **THEN** the craft succeeds, one litre of black dye is consumed, and the bowl is consumed with it

#### Scenario: An empty or wrongly-filled bowl does not craft the Lectern

- **WHEN** the ink slot holds an empty bowl, or a bowl containing a liquid other than black dye
- **THEN** the recipe does not produce a Lectern

### Requirement: Ingredient variant tolerance

The recipe SHALL accept any wood type for its planks and any metal for its nails, while requiring
plain (undyed, normal) leather specifically. This keeps the recipe craftable from commonly-available
materials without pinning it to a single wood or metal, while keeping the leather ingredient
unambiguous.

#### Scenario: Any wood and any metal are accepted

- **WHEN** the player supplies planks of any wood type and nails of any metal, with the other
  ingredients present
- **THEN** the recipe still matches and produces a Lectern

#### Scenario: Only plain leather is accepted

- **WHEN** the player supplies a dyed or non-plain leather variant in the leather slot
- **THEN** the recipe does not match (plain normal leather is required)

