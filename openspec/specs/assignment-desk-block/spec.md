# assignment-desk-block

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers the Assignment Desk block: a placeable Scribe writing station whose dialog hosts exactly
two tabs, Assignment (the mod's only create-and-send surface) and the shared Inbox tab.
## Requirements
### Requirement: Craftable, placeable Assignment Desk block
The system SHALL provide an Assignment Desk block, obtainable via a crafting recipe (and in
creative), that hosts a Scribe document dialog with six tabs, in nav order: Create Assignments
(the default view), Sent Assignment History, Inbox, Read, Editor, and Settings. It SHALL reuse
the existing writing-station block-entity and dialog base classes (server-authoritative lock,
autosave, persistence/sync) rather than introducing a parallel mechanism.

#### Scenario: Placing and opening the Assignment Desk
- **WHEN** a player crafts or spawns an Assignment Desk and right-clicks it
- **THEN** the block registers and renders its own model, and its dialog opens showing the
  Create Assignments tab by default, with Sent Assignment History, Inbox, Read, Editor, and
  Settings all reachable via nav buttons in that order

#### Scenario: Access grant lands on the last-active tab, never a nonexistent view
- **WHEN** a player who already has the Assignment Desk's dialog open on a non-default tab
  (Sent Assignment History, Inbox, Read, or Editor) receives a fresh access grant (e.g. a
  right-click reopen)
- **THEN** the dialog stays on that same tab rather than being forced back to Create Assignments
  or to a Notebook-style default Read landing

### Requirement: The Assignment tab is the sole creation surface for assignments
The Assignment Desk's Assignment tab SHALL be the only surface in the mod that lets a player
create a task and send it to another player. No other Scribe surface (Lectern, Scriptorium,
Chalkboard, Notebook, Tablet, the standalone Inbox block) SHALL expose a create-and-send
affordance; those surfaces may only view/act on assignments already sent (see `inbox-tab`).
Creation on the Assignment tab SHALL happen by staging an existing Scribe item into the tab's
staging slot and sending one or more of that document's rows, per the `assignment-multi-item-
creation` capability — not by typing freeform text.

#### Scenario: Only the Assignment Desk can send an assignment
- **WHEN** a player is at a Lectern, Scriptorium, Chalkboard, or the standalone Inbox block
- **THEN** no control for creating and sending a new assignment to another player is present

#### Scenario: Creation is staging-and-select, not freeform text entry
- **WHEN** the player opens the Create Assignments tab
- **THEN** the tab presents a staging slot and (once a document is staged) a selectable row list,
  with no freeform text field for authoring a new task from scratch

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

### Requirement: The Assignment tab is labeled "Create Assignments" with a plus icon
The Assignment Desk's create-and-send tab SHALL be labeled "Create Assignments" (both its visible
tab label and its hover tooltip), and its nav button SHALL render a dedicated plus-glyph icon
rather than the scroll icon used for other Scribe surfaces' document-related affordances.

#### Scenario: The tab reads "Create Assignments"
- **WHEN** the Assignment Desk's dialog renders its Assignment/Inbox tab-switcher
- **THEN** the create-and-send tab's label and hover tooltip both read "Create Assignments"

#### Scenario: The tab shows a plus icon
- **WHEN** the Assignment Desk's dialog renders the Create Assignments nav button
- **THEN** it shows a plus-glyph icon, distinct from the scroll icon used elsewhere in the mod

### Requirement: The create-and-send form's target-player row uses a flex layout
The create-and-send form's target-player row SHALL be laid out as a single horizontal row using
LibGUI's `Row`/`Expanded` flex primitives: a fixed-width "Send to" label, an `Expanded(flex: 1)`
player picker taking the remaining width, and a fixed-width Send button — replacing a
vertically-stacked layout where the label, picker, and button each occupied their own full-width
row.

#### Scenario: Send-to row lays out horizontally
- **WHEN** the Create Assignments tab renders its create-and-send form
- **THEN** the "Send to" label, the player picker, and the Send button render side by side on one
  row, with the player picker occupying the flexible remaining space between the two fixed-width
  controls

