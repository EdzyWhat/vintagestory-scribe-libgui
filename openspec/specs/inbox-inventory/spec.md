# inbox-inventory Specification

## Purpose
The standalone Inbox block's own inventory: eight slots split between Task-Notice-only storage
and general open storage, so the block can hold physically-delivered assignments and incidental
items without leaving the player's assignment workflow.

## Requirements

### Requirement: The Inbox provides a mixed restricted/open inventory
The Inbox block SHALL own an inventory of exactly 8 slots, arranged as 2 rows of 4. Of these, 4
slots (the first row) SHALL accept ONLY Task Notice items (`ItemScribeTaskNotice`) and SHALL
reject any other item at the slot without storing it. The remaining 4 slots (the second row)
SHALL accept any item with no restriction. The inventory belongs to the Inbox block only; no
other Scribe surface SHALL gain it.

#### Scenario: A Task Notice is accepted into a restricted slot
- **WHEN** the player places a Task Notice item into one of the 4 restricted slots
- **THEN** the item is stored in that slot and remains the same ItemStack, its document and all
  attributes preserved unchanged

#### Scenario: A non-Task-Notice item is rejected from a restricted slot
- **WHEN** the player attempts to place any item other than a Task Notice (including another
  Scribe item, such as a Notebook) into one of the 4 restricted slots
- **THEN** the slot refuses the item and nothing is stored

#### Scenario: Any item is accepted into an open slot
- **WHEN** the player places any item, Scribe or otherwise, into one of the 4 open slots
- **THEN** the item is stored in that slot with no restriction applied

### Requirement: The inventory is surfaced as its own Inbox Inventory tab
The Inbox block's dialog SHALL present the inventory as a distinct tab labeled **"Inbox
Inventory"**, reachable via a nav switcher alongside the Inbox tab. Selecting the tab SHALL show
all 8 slots, laid out as 2 rows of 4 and centered both horizontally and vertically within the
tab's content region, and SHALL allow moving items between the player and the slots. Switching
away from and back to the tab SHALL show the current stored contents. This tab SHALL appear only
on the standalone Inbox block, not on any other Scribe surface.

#### Scenario: The inventory tab is reachable
- **WHEN** the player opens the Inbox block and selects the Inbox Inventory tab from the nav
  switcher
- **THEN** the 8 slots are shown, arranged as 2 rows of 4 and centered in the tab, with their
  current contents

#### Scenario: The tab is Inbox-block-only
- **WHEN** the player opens the Assignment Desk, Lectern, Scriptorium, Chalkboard, Notebook, or
  Tablet dialog
- **THEN** no Inbox Inventory tab is present

### Requirement: Inventory slots visually match the Assignment Desk's slot styling
All 8 Inbox Inventory slots SHALL use the same slot size, border color, and background color as
the Assignment Desk's inventory slots. The 4 restricted (Task-Notice-only) slots SHALL
additionally show the same background-image hint the Assignment Desk uses on its own Task
Notice slots to indicate the expected item type. The 4 open slots SHALL NOT show that
background image.

#### Scenario: Restricted slots show the Task Notice hint image
- **WHEN** the Inbox Inventory tab renders an empty restricted slot
- **THEN** the slot shows the same size, border color, background color, and background-image
  hint as an empty Task Notice slot on the Assignment Desk

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
all players viewing the block. Adding the inventory SHALL be additive: an existing placed Inbox
block saved before this change SHALL load with an empty inventory rather than failing.

#### Scenario: Stored items survive reload
- **WHEN** an Inbox block holds items in its inventory and the world is saved and reloaded
- **THEN** the same items are still in the same slots after reload

#### Scenario: A pre-existing Inbox block loads with an empty inventory
- **WHEN** an Inbox block placed before this change is loaded
- **THEN** it loads successfully with all 8 slots empty

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
