# scriptorium-inventory Specification

## Purpose
TBD - created by archiving change add-scriptorium-inventory. Update Purpose after archive.
## Requirements
### Requirement: The Scriptorium provides a Scribe-items-only inventory
The Scriptorium block SHALL own a small inventory of exactly two slots that accepts ONLY Scribe
document items (collectibles implementing `IScribeDocumentItem` — the Notebook, the Clockmaker's
Notebook, and the Tablet in any state). Any non-Scribe item SHALL be rejected at the slot and never
stored. The inventory belongs to the Scriptorium block only; the Lectern and the item-hosted
surfaces (Notebook/Tablet dialogs) SHALL NOT gain it.

#### Scenario: A Scribe item is accepted
- **WHEN** the player places a Notebook, Clockmaker's Notebook, or Tablet into a Scriptorium
  inventory slot
- **THEN** the item is stored in that slot and remains the same ItemStack (its document and all
  attributes are preserved unchanged)

#### Scenario: A non-Scribe item is rejected
- **WHEN** the player attempts to place any item that does not implement `IScribeDocumentItem` (e.g.
  a plank, an ingot) into a Scriptorium inventory slot
- **THEN** the slot refuses the item and nothing is stored

#### Scenario: The inventory holds at most two items
- **WHEN** both slots already hold a Scribe item
- **THEN** no third Scribe item can be placed until a slot is emptied

### Requirement: The inventory is surfaced as its own Scriptorium dialog tab
The Scriptorium dialog SHALL present the inventory as a distinct nav-rail tab labeled **"Transcribe"**,
selectable alongside the existing Read / Task Editor / Pinned / Guest Book / Settings tabs. Selecting the
tab SHALL show the two slots and allow moving Scribe items between the player and the slots. Switching
away from and back to the tab SHALL show the current stored contents. This tab SHALL appear only on the
Scriptorium dialog, not on any other Scribe surface. The "Transcribe" name reflects that the view exists
for copying documents (and, later, import/export), not general storage.

#### Scenario: The inventory tab is reachable
- **WHEN** the player opens a Scriptorium and selects the Transcribe tab from the nav rail
- **THEN** the two slots are shown with their current contents
- **AND** the nav-button tooltip and the view heading both read "Transcribe"

#### Scenario: The tab is Scriptorium-only
- **WHEN** the player opens a Lectern, Notebook, or Tablet dialog
- **THEN** no Transcribe tab is present

### Requirement: Item moves are server-authoritative
Moving a Scribe item into or out of a Scriptorium slot SHALL be resolved by the server, which owns
the authoritative inventory contents; the client SHALL reflect the server's result rather than
mutating stored contents locally. This follows the mod's existing server-authoritative model for
all placed-block state.

#### Scenario: A move is confirmed by the server
- **WHEN** the player moves a Scribe item into or out of a Scriptorium slot
- **THEN** the change is applied on the server and the resulting slot contents are synced back to
  the client(s) viewing the block

### Requirement: The inventory persists and syncs via the vanilla Sign pattern
The Scriptorium's inventory SHALL persist and synchronize through the same vanilla pattern the block
already uses for its document (`ToTreeAttributes` / `FromTreeAttributes`, `MarkDirty`,
server-authoritative), so stored items survive world save/reload and are visible to all players
viewing the block. Adding the inventory SHALL be additive: an existing placed Scriptorium saved
before this change SHALL load with an empty inventory rather than failing.

#### Scenario: Stored items survive reload
- **WHEN** a Scriptorium holds Scribe items and the world is saved and reloaded
- **THEN** the same items are still in the same slots after reload

#### Scenario: A pre-existing Scriptorium loads with an empty inventory
- **WHEN** a Scriptorium placed before this change is loaded
- **THEN** it loads successfully with both slots empty

### Requirement: Breaking the Scriptorium returns its stored items
Breaking a Scriptorium block SHALL drop any Scribe items held in its inventory into the world (as a
vanilla container does), so a stored document is never destroyed by breaking the block. This is in
addition to the block's existing document-survival behavior.

#### Scenario: Stored items drop on break
- **WHEN** a Scriptorium holding one or more Scribe items is broken
- **THEN** those items are dropped in the world as recoverable ItemStacks with their documents
  intact

### Requirement: The inventory is storage only
This capability SHALL store and return whole Scribe items only. It SHALL NOT read, merge, copy, or
otherwise interpret the documents inside the stored items. Any document-transfer behavior
(copy/paste) or serialization (import/export) is out of scope and defined by separate capabilities
that build on this one.

#### Scenario: Storing an item does not alter documents
- **WHEN** a Scribe item is placed into and later removed from a Scriptorium slot without any other
  action
- **THEN** the item's document is byte-for-byte unchanged and the Scriptorium's own document is
  unchanged

