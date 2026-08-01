## ADDED Requirements

### Requirement: The placed Lectern's tooltip shows its document title
Hovering a placed Lectern SHALL display a title line on its block-info tooltip, formatted as
`Title: "<title>"` with the title wrapped in double quotes, sourced from the block entity's live
document title. When the document has no meaningful title (its stored title is the model default
`"Untitled"`), the line SHALL still appear using the placeholder `Title: "(untitled)"`. The title
line SHALL sit alongside the standard mod/name lines, not replace them.

#### Scenario: Titled lectern shows its quoted title on hover
- **WHEN** a player looks at a placed Lectern whose document title is "Welcome to Ravenwood"
- **THEN** the block tooltip includes a line reading `Title: "Welcome to Ravenwood"`

#### Scenario: Untitled lectern shows the placeholder
- **WHEN** a player looks at a placed Lectern whose document has never been given a title (title is the default)
- **THEN** the block tooltip includes a line reading `Title: "(untitled)"`

### Requirement: The Lectern does not advertise combustion stats
The Lectern SHALL NOT display burn/combustion information (burn temperature, burn duration) on its
tooltip, because those stats are irrelevant to how the block is used.

#### Scenario: No burn lines on the Lectern tooltip
- **WHEN** a player views the Lectern's tooltip (as a placed block or in inventory)
- **THEN** no "Burn temperature" or "Burn duration" line is shown
