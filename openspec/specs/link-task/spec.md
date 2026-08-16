# link-task Specification

## Purpose
TBD - created by archiving change add-tracker-link-tasks. Update Purpose after archive.
## Requirements
### Requirement: Link task kind and reference field
The document model SHALL support a `Link` block kind that carries a reference target
(`LinkTarget`, a plain string identifying a Handbook page — e.g. an item/block asset code — stored
without any Vintage Story API dependency in `Core`). A Link block SHALL retain the fields common to
every block (text, completed flag, depth, `TaskId`, assignment) but SHALL NOT carry the Tracker
quantity fields. The kind value SHALL be appended to the existing kind enumeration (never
renumbering existing kinds). A Link is a reference, not a counter: it has no progress and is
completed only by the player, never automatically.

#### Scenario: A link carries its reference target
- **WHEN** a Link block is created referencing Handbook page for `game:ingot-copper`
- **THEN** the block's kind is `Link`, its `LinkTarget` is `game:ingot-copper`, and it has no
  Tracker quantity fields set

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

