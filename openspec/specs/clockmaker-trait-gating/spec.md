# clockmaker-trait-gating Specification

## Purpose
TBD - created by archiving change scribe-0-2-0-release-content. Update Purpose after archive.
## Requirements
### Requirement: Crafting the Clockmaker's Notebook requires the tinkerer trait

The Clockmaker's Notebook grid recipe SHALL require the `tinkerer` trait so that,
by default, only players whose character class grants that trait (vanilla: the `clockmaker` class)
can craft it. The requirement SHALL be authored data-only via the recipe's native `requiresTrait`
field; enforcement is the game's own `CharacterSystem` `MatchesGridRecipe` handler and SHALL NOT
require a custom recipe-matching event handler in the mod.

The plain Notebook and Lectern recipes SHALL NOT carry any trait requirement.

#### Scenario: A tinkerer-trait player can craft the Clockmaker's Notebook

- **WHEN** a player whose active character class grants the `tinkerer` trait places the Clockmaker's
  Notebook ingredients in the correct arrangement
- **THEN** the craft succeeds and yields one `scribe:scribeclockmakernotebook`

#### Scenario: A player without the trait cannot craft it (requirement enabled)

- **WHEN** a player without the `tinkerer` trait (and without it in their `extraTraits`) attempts the
  craft while the requirement is enabled
- **THEN** the craft does not complete (the recipe does not match for that player), consistent with
  vanilla trait-gated recipes such as the `clothier` tailoring recipes

#### Scenario: No character system present does not block crafting

- **WHEN** no character class is assigned to the player (no character system active)
- **THEN** the trait requirement does not block the craft (matching vanilla allow-when-classless
  behavior)

### Requirement: A server setting can disable the trait requirement

The mod SHALL expose a server-side worldconfig boolean that controls whether the Clockmaker's
Notebook trait requirement is enforced, defaulting to enforced (requirement ON). When the setting
disables the requirement, the mod SHALL clear the recipe's `requiresTrait` at server startup so the
recipe matches for every player, matching the game's allow-all behavior when `RequiresTrait` is
null. The setting SHALL NOT depend on any non-vanilla mod (no ConfigLib requirement).

#### Scenario: Disabling the requirement opens the recipe to all players

- **WHEN** the server operator sets the worldconfig toggle to disable the requirement and the server
  starts (or the world is created with it disabled)
- **THEN** any player, regardless of class or traits, can craft the Clockmaker's Notebook

#### Scenario: Default world enforces the requirement

- **WHEN** a world is created without changing the toggle
- **THEN** the trait requirement is enforced (only tinkerer-trait players can craft the Clockmaker's
  Notebook)

