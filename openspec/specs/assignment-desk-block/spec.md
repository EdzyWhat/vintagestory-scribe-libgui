# assignment-desk-block

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers the Assignment Desk block: a placeable Scribe writing station whose dialog hosts exactly
two tabs, Assignment (the mod's only create-and-send surface) and the shared Inbox tab.

## Requirements

### Requirement: Craftable, placeable Assignment Desk block
The system SHALL provide an Assignment Desk block, obtainable via a crafting recipe (and in
creative), that hosts a Scribe document dialog with two tabs: Assignment and Inbox. It SHALL
reuse the existing writing-station block-entity and dialog base classes (server-authoritative
lock, autosave, persistence/sync) rather than introducing a parallel mechanism.

#### Scenario: Placing and opening the Assignment Desk
- **WHEN** a player crafts or spawns an Assignment Desk and right-clicks it
- **THEN** the block registers and renders its own model, and its dialog opens showing the
  Assignment tab by default with an Inbox tab alongside it

### Requirement: The Assignment tab is the sole creation surface for assignments
The Assignment Desk's Assignment tab SHALL be the only surface in the mod that lets a player
create a task and send it to another player. No other Scribe surface (Lectern, Scriptorium,
Chalkboard, Notebook, Tablet, the standalone Inbox block) SHALL expose a create-and-send
affordance; those surfaces may only view/act on assignments already sent (see `inbox-tab`).

#### Scenario: Only the Assignment Desk can send an assignment
- **WHEN** a player is at a Lectern, Scriptorium, Chalkboard, or the standalone Inbox block
- **THEN** no control for creating and sending a new assignment to another player is present

### Requirement: The Assignment Desk hosts the shared Inbox tab
The Assignment Desk's Inbox tab SHALL be the same Inbox tab (`inbox-tab` capability) shown by
the standalone Inbox block and reachable via nav button from the Lectern, Scriptorium, and
Chalkboard — one implementation, not a per-surface duplicate.

#### Scenario: Assignment Desk's Inbox tab matches every other Inbox surface
- **WHEN** the player switches to the Assignment Desk's Inbox tab
- **THEN** it shows the same rows, state filter, and per-row behavior as the standalone Inbox
  block or a nav-button-opened Inbox view

### Requirement: Assignment Desk dimensions are supplied via IScribeDocumentHost
The Assignment Desk's block entity SHALL implement `IScribeDocumentHost.GetLayout` to supply its
own width/aspect-ratio/proportions for its 2-tab layout, following the same per-host layout
mechanism every other Scribe surface uses, rather than a hardcoded or shared dimension. The
bounding box SHALL be the player's Pixel Art Size preference as width, with height 1.2× that
width; within that box, the active tab's content region (the Assignment tab's create/send form,
or the Inbox tab's row list) SHALL render as a 1:1 square, with the remaining vertical space
occupied by the title bar and the Assignment/Inbox tab-switcher nav row.

#### Scenario: Assignment Desk sizes independently of Lectern/Scriptorium
- **WHEN** the Assignment Desk dialog opens
- **THEN** its dimensions come from its own `GetLayout` implementation and are not tied to the
  Lectern's or Scriptorium's aspect ratio, even though some row/nav widgets are visually reused

#### Scenario: The bounding box and tab content ratio
- **WHEN** the Assignment Desk dialog opens with the player's Pixel Art Size set to some width W
- **THEN** the dialog's overall bounding box is W wide by 1.2×W tall, and the active tab's own
  content region within it is a 1:1 square
