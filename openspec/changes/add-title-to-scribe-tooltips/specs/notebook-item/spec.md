## ADDED Requirements

### Requirement: Notebook items show their document title on the held/inventory tooltip
Hovering a plain Notebook or a Clockmaker's Notebook in a hotbar or inventory slot SHALL display a
title line on its held-item tooltip, formatted as `Title: "<title>"` with the title wrapped in
double quotes, sourced from the document stored in the ItemStack. When the item carries no document
yet (it has never been opened) or its stored title is the model default `"Untitled"`, the line SHALL
still appear using the placeholder `Title: "(untitled)"`. The title line SHALL be additive — it does
not remove the standard held-item info lines.

#### Scenario: Titled notebook shows its quoted title in the inventory
- **WHEN** a player hovers a Notebook whose document title is "Field Journal" in their inventory
- **THEN** the item tooltip includes a line reading `Title: "Field Journal"`

#### Scenario: Never-opened notebook shows the placeholder
- **WHEN** a player hovers a freshly crafted Notebook that has never been opened (no stored document)
- **THEN** the item tooltip includes a line reading `Title: "(untitled)"`

#### Scenario: Clockmaker's Notebook shows the carried-over title
- **WHEN** a Notebook with title "Field Journal" is upgraded into a Clockmaker's Notebook
- **THEN** hovering the Clockmaker's Notebook shows a line reading `Title: "Field Journal"`
