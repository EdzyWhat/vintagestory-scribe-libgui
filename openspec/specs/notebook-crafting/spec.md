# notebook-crafting Specification

## Purpose
TBD - created by archiving change scribe-0-2-0-release-content. Update Purpose after archive.
## Requirements
### Requirement: The Notebook is craftable in survival from a paper-and-leather writing set

The mod SHALL provide a grid-crafting recipe whose output is exactly one `scribe:scribenotebook`
item, so the Notebook is obtainable in survival play rather than only from the creative inventory.
The recipe SHALL consume a writing set built from paper and a leather cover, using the same
ingredient vocabulary as the existing Lectern recipe (baseline: `game:paper-parchment` and
`game:leather-normal-plain`). The recipe SHALL be authored as a data-only asset that the game
auto-loads; it SHALL NOT require any C# registration code.

#### Scenario: Crafting yields one Notebook

- **WHEN** a player places the required ingredients in the crafting grid in the recipe's arrangement
- **THEN** the crafting output is exactly one `scribe:scribenotebook` item

#### Scenario: The Notebook remains available in creative

- **WHEN** a player opens the creative inventory
- **THEN** the Notebook is still listed there (the recipe adds a survival path; it does not remove
  the existing creative-inventory availability)

### Requirement: The Notebook recipe closes the Clockmaker's Notebook ingredient gap

Because the Clockmaker's Notebook recipe consumes a `scribe:scribenotebook` as an input, the
Notebook recipe SHALL make the full Notebook → Clockmaker's Notebook crafting chain completable in
survival without creative access at any step.

#### Scenario: Full survival chain is completable

- **WHEN** a player crafts a Notebook via the survival recipe and then applies the Clockmaker's
  Notebook recipe to it
- **THEN** both crafts succeed using only survival-obtainable ingredients

