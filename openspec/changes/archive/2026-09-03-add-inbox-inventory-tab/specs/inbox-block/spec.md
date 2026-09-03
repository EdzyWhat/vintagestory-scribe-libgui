## MODIFIED Requirements

### Requirement: Craftable, placeable Inbox block
The system SHALL provide an Inbox block, obtainable via a crafting recipe (and in creative),
whose capabilities are the shared Inbox tab (`inbox-tab` capability) and the Inbox's own
Inbox Inventory tab (`inbox-inventory` capability), so a player can receive and act on
assignments and hold Task Notice items and other belongings at the same block. It SHALL reuse
the existing writing-station block-entity and dialog base classes for persistence/sync rather
than introducing a parallel mechanism.

#### Scenario: Placing and opening the Inbox block
- **WHEN** a player crafts or spawns an Inbox block and right-clicks it
- **THEN** the block registers and renders its own model, and its dialog opens directly to the
  Inbox tab, with the Inbox Inventory tab also reachable via a nav switcher

#### Scenario: Switching to the Inbox Inventory tab
- **WHEN** the player selects the Inbox Inventory tab from the Inbox block's nav switcher
- **THEN** the dialog switches to show the 8-slot inventory, and switching back to the Inbox tab
  shows the assignment row list unchanged

### Requirement: Inbox block dimensions are supplied via IScribeDocumentHost
The Inbox block's block entity SHALL implement `IScribeDocumentHost.GetLayout` to supply its own
width/aspect-ratio/proportions for its 2-tab layout, following the same per-host layout
mechanism every other Scribe surface uses. The bounding box SHALL be the player's Pixel Art Size
preference as width, with height 1.2× that width; within that box, the active tab's content
region (the Inbox tab's row-list, or the Inbox Inventory tab's slot grid) SHALL render as a 1:1
square, matching the Assignment Desk's own 2-tab layout dimensions exactly, with the remaining
vertical space occupied by the title bar and the Inbox/Inbox Inventory tab-switcher nav row.

#### Scenario: The bounding box and content ratio match the Assignment Desk's Inbox tab
- **WHEN** the standalone Inbox block's dialog opens with the player's Pixel Art Size set to some
  width W
- **THEN** the dialog's overall bounding box is W wide by 1.2×W tall, and the active tab's
  content region within it is a 1:1 square, identical in proportion to the Assignment Desk's own
  2-tab layout

#### Scenario: The Inbox Inventory tab's slot grid fits the same square region
- **WHEN** the player switches to the Inbox Inventory tab
- **THEN** the 8-slot grid renders centered within that same 1:1 square content region, without
  changing the dialog's overall bounding box
