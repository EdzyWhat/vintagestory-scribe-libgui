# notebook-item

## Purpose

TBD - created via spec sync from change `add-scribe-notebook`. The Notebook is a carriable
item that exposes the full Scribe GUI (minus Guestbook) for a document stored in the
player's `ItemStack`. It integrates with the host registry for server-authoritative
persistence, mirroring the Lectern's edit/sync flow but bound to an item rather than a
placed block.
## Requirements
### Requirement: Notebook is a carriable item with a full Scribe GUI
The system SHALL provide an item (`ItemScribeNotebook`) that the player can hold in their
inventory and interact with (right-click / use key) to open the full Scribe dialog (Read,
Editor, Pinned, **History**, Settings tabs). The Notebook GUI SHALL NOT include a Guestbook
tab. The dialog SHALL use the same visual backdrop and layout proportions as the Lectern.

#### Scenario: Player opens notebook from hotbar
- **WHEN** a player right-clicks (or uses the interaction key) while holding a Notebook item
- **THEN** the Scribe dialog opens in Read view, showing the document stored in that
  specific Notebook item

#### Scenario: No Guestbook tab
- **WHEN** the Scribe dialog opens for a Notebook
- **THEN** no Guestbook tab or nav button is present in the navigation column

#### Scenario: History tab is present
- **WHEN** the Scribe dialog opens for a Notebook
- **THEN** a History nav button is present in the navigation column and clicking it shows
  the notebook's history entries

### Requirement: Notebook document persists in the ItemStack
The Notebook's document SHALL be stored in the `ItemStack`'s attributes under the key
`"scribeDocument"` using the same `ScribeDocumentAttributes` serialization used by the
Lectern's break/place flow. A fresh Notebook (no prior document) SHALL open with an empty
document containing a fresh `DocId`.

#### Scenario: Document survives inventory move
- **WHEN** a player moves their Notebook to a different slot
- **THEN** the document contents are unchanged

#### Scenario: Fresh notebook starts empty
- **WHEN** a player obtains a new Notebook with no existing document data
- **THEN** opening it shows an empty document with a fresh `DocId`

#### Scenario: Stacked notebooks are disallowed
- **WHEN** a player attempts to stack two Notebook items
- **THEN** the stack size remains 1 (Notebook is not stackable)

### Requirement: Notebook saves are server-authoritative
All document edits made in the Notebook dialog SHALL be sent to the server and applied
there, exactly as Lectern edits are. The server SHALL write the updated document back to
the `ItemStack` and broadcast a sync reply to the owning player's client.

#### Scenario: Edit in notebook dialog is persisted
- **WHEN** a player edits a task or note in the Notebook dialog and the autosave flush fires
- **THEN** the server applies the edit to the `ItemStack.Attributes` and syncs the updated
  document back to the client

### Requirement: Notebook access is owner-only
Only the player currently holding the Notebook SHALL be able to edit it. Because the item
can only be held by one player at a time, no explicit locking mechanism is required; the
server SHALL reject edit packets from any player who does not hold the relevant item.

#### Scenario: Only the holder can edit
- **WHEN** a player who is not holding the Notebook sends an edit packet for it
- **THEN** the server ignores the packet

### Requirement: Notebook dialog closes automatically when item leaves the hand
The Notebook dialog SHALL close whenever the item is no longer in the player's active hand
or inventory (e.g. dropped, traded, placed into a chest while the dialog is open). This
ensures the dialog cannot remain open for an item the player no longer holds.

#### Scenario: Dialog closes when item is dropped
- **WHEN** a player drops the Notebook item while the dialog is open
- **THEN** the dialog closes

### Requirement: Notebook is available in the Creative inventory
The Notebook item SHALL appear in the Creative-mode inventory tab so players and server
operators can obtain it. No crafting recipe is provided in this change.

#### Scenario: Creative-mode access
- **WHEN** a player in Creative mode opens the inventory
- **THEN** the Notebook item is present in the Scribe creative tab (or the Tools tab if no
  dedicated Scribe tab exists)

### Requirement: Notebook ItemStack carries a history blob alongside the document
The Notebook's `ItemStack` SHALL store history data under `"scribeHistory"` using the
`HistoryStore` codec, alongside the existing `"scribeDocument"` key. Both blobs SHALL be
written atomically in the same `Flush()` call.

#### Scenario: Both blobs present after first open
- **WHEN** a player opens a fresh Notebook for the first time
- **THEN** both `"scribeDocument"` and `"scribeHistory"` are present in `ItemStack.Attributes`

#### Scenario: Missing scribeHistory treated as empty store
- **WHEN** a Notebook exists in a world save that predates this change (no `scribeHistory` key)
- **THEN** opening it shows an empty History tab with no error

