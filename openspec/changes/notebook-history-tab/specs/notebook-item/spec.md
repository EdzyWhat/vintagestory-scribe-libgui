## MODIFIED Requirements

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

## ADDED Requirements

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
