# inbox-inventory Specification

## Purpose
The standalone Inbox block's own inventory: eight slots split between Scribe-items-only storage
and general open storage, so the block can hold physically-delivered assignments and incidental
items without leaving the player's assignment workflow.

## Requirements

### Requirement: The Inbox provides a mixed restricted/open inventory
The Inbox block SHALL own an inventory of exactly 12 slots, arranged as 3 rows of 4. Of these, 8
slots (the first two rows) SHALL accept ONLY Scribe items (any item implementing
`IScribeDocumentItem`, including a Task Notice in either its blank or sealed state, matching the
Scriptorium's own Scribe-items-only slot restriction) and SHALL reject any other item at the slot
without storing it. The remaining 4 slots (the third row) SHALL accept any item with no
restriction. The inventory belongs to the Inbox block only; no other Scribe surface SHALL gain
it.

#### Scenario: A Task Notice is accepted into a restricted slot
- **WHEN** the player places a Task Notice item, blank or sealed, into one of the 8 restricted
  slots
- **THEN** the item is stored in that slot and remains the same ItemStack, its document and all
  attributes preserved unchanged

#### Scenario: A non-Scribe item is rejected from a restricted slot
- **WHEN** the player attempts to place any item that is not a Scribe item (does not implement
  `IScribeDocumentItem`) into one of the 8 restricted slots
- **THEN** the slot refuses the item and nothing is stored

#### Scenario: Any item is accepted into an open slot
- **WHEN** the player places any item, Scribe or otherwise, into one of the 4 open slots
- **THEN** the item is stored in that slot with no restriction applied

### Requirement: The inventory is surfaced as its own Inbox Inventory tab
The Inbox block's dialog SHALL present the inventory as a distinct tab labeled **"Inbox
Inventory"**, reachable via a nav switcher alongside the Inbox tab. Selecting the tab SHALL show
all 12 slots, laid out as 3 rows of 4 and centered both horizontally and vertically within the
tab's content region, and SHALL allow moving items between the player and the slots. Switching
away from and back to the tab SHALL show the current stored contents. This tab SHALL appear only
on the standalone Inbox block, not on any other Scribe surface.

#### Scenario: The inventory tab is reachable
- **WHEN** the player opens the Inbox block and selects the Inbox Inventory tab from the nav
  switcher
- **THEN** the 12 slots are shown, arranged as 3 rows of 4 and centered in the tab, with their
  current contents

#### Scenario: The tab is Inbox-block-only
- **WHEN** the player opens the Assignment Desk, Lectern, Scriptorium, Chalkboard, Notebook, or
  Tablet dialog
- **THEN** no Inbox Inventory tab is present

### Requirement: Inventory slots visually match the Assignment Desk's slot styling
All 12 Inbox Inventory slots SHALL use the same slot size, border color, and background color as
the Assignment Desk's inventory slots. The 8 restricted (Scribe-items-only) slots SHALL
additionally show a background-image hint indicating the expected item type, matching the
Scriptorium's generic Scribe-items-only watermark (not the Assignment Desk's Task-Notice-specific
one, since the Inbox's restriction is not Task-Notice-specific). The 4 open slots SHALL NOT show
that background image.

#### Scenario: Restricted slots show the Scribe-item hint image
- **WHEN** the Inbox Inventory tab renders an empty restricted slot
- **THEN** the slot shows the same size, border color, and background color as an empty slot on
  the Assignment Desk, with the Scriptorium's generic Scribe-item background-image hint

#### Scenario: Open slots show no hint image
- **WHEN** the Inbox Inventory tab renders an empty open slot
- **THEN** the slot shows the same size, border color, and background color as the Assignment
  Desk's slots, but no background image

### Requirement: Item moves are server-authoritative
Moving an item into or out of an Inbox Inventory slot SHALL be resolved by the server, which
owns the authoritative inventory contents; the client SHALL reflect the server's result rather
than mutating stored contents locally. This follows the mod's existing server-authoritative
model for all placed-block state.

#### Scenario: A move is confirmed by the server
- **WHEN** the player moves an item into or out of an Inbox Inventory slot
- **THEN** the change is applied on the server and the resulting slot contents are synced back
  to the client(s) viewing the block

### Requirement: The inventory persists and syncs via the vanilla Sign pattern
The Inbox block's inventory SHALL persist and synchronize through the same vanilla pattern the
block already uses for its Inbox tab data (`ToTreeAttributes`/`FromTreeAttributes`,
`MarkDirty`, server-authoritative), so stored items survive world save/reload and are visible to
all players viewing the block. Growing the inventory from 8 to 12 slots SHALL be additive: an
existing placed Inbox block saved before this change SHALL load with its existing items in their
original slots and the 4 newly-added slots empty, rather than failing or reordering existing
contents.

#### Scenario: Stored items survive reload
- **WHEN** an Inbox block holds items in its inventory and the world is saved and reloaded
- **THEN** the same items are still in the same slots after reload

#### Scenario: A pre-existing Inbox block loads with an empty inventory
- **WHEN** an Inbox block placed before the Inbox ever had this inventory at all (no inventory
  data persisted) is loaded
- **THEN** it loads successfully with all 12 slots empty

#### Scenario: A pre-existing 8-slot Inbox block loads with its items intact and 4 new empty slots
- **WHEN** an Inbox block placed before this change (with its old 8-slot inventory) is loaded
- **THEN** it loads successfully with its existing items unchanged in their original slots, and
  the 4 newly-added slots empty

### Requirement: Breaking the Inbox block returns its stored items
Breaking an Inbox block SHALL drop any items held in its inventory into the world (as a vanilla
container does), so a stored Task Notice or other item is never destroyed by breaking the block.
This is in addition to the block's existing Inbox-data-survival behavior.

#### Scenario: Stored items drop on break
- **WHEN** an Inbox block holding one or more items in its inventory is broken
- **THEN** those items are dropped in the world as recoverable ItemStacks, unchanged

### Requirement: The inventory is storage only
This capability SHALL store and return whole items only. It SHALL NOT read, merge, copy, or
otherwise interpret a stored Task Notice's document, and SHALL NOT act on assignment state.
Any assignment action (Accept/Decline) remains exclusively available from the Task Notice's own
held-item dialog, per the `task-notice-item` capability.

#### Scenario: Storing a Task Notice does not alter its document or act on the assignment
- **WHEN** a Task Notice is placed into and later removed from a restricted Inbox Inventory slot
  without any other action
- **THEN** its document is byte-for-byte unchanged and its assignment's state is unchanged
