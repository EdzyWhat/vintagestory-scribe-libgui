## ADDED Requirements

### Requirement: The Lectern's tooltip shows its document title
The Lectern SHALL display a title line on its tooltip, formatted as `Title: "<title>"` with the
title wrapped in double quotes, in BOTH forms: the placed block's look-at tooltip (sourced from the
block entity's live document title) and the block item's held/inventory hover (sourced from the
document carried on the ItemStack). When the document has no meaningful title (its stored title is
the model default `"Untitled"`, or the item carries no document yet), the line SHALL still appear
using the placeholder `Title: "(untitled)"`. The title line SHALL sit alongside the standard
mod/name lines, not replace them.

#### Scenario: Titled lectern shows its quoted title on hover
- **WHEN** a player looks at a placed Lectern whose document title is "Welcome to Ravenwood"
- **THEN** the block tooltip includes a line reading `Title: "Welcome to Ravenwood"`

#### Scenario: Untitled lectern shows the placeholder
- **WHEN** a player looks at a placed Lectern whose document has never been given a title (title is the default)
- **THEN** the block tooltip includes a line reading `Title: "(untitled)"`

#### Scenario: Lectern item in inventory shows its carried title
- **WHEN** a player hovers a Lectern block item in their inventory that was broken/picked up with the title "Welcome to Ravenwood"
- **THEN** the item tooltip includes a line reading `Title: "Welcome to Ravenwood"`

### Requirement: The Lectern does not advertise combustion stats
The Lectern SHALL NOT display burn/combustion information (burn temperature, burn duration) on its
tooltip, because those stats are irrelevant to how the block is used.

#### Scenario: No burn lines on the Lectern tooltip
- **WHEN** a player views the Lectern's tooltip (as a placed block or in inventory)
- **THEN** no "Burn temperature" or "Burn duration" line is shown
