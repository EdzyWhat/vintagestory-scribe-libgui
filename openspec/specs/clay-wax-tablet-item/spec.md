# clay-wax-tablet-item Specification

## Purpose
TBD - created by archiving change add-tablet-items-and-crafting. Update Purpose after archive.
## Requirements
### Requirement: Two tablet material variants from one item class

The system SHALL provide a single `ItemScribeTablet` class exposed through an item type with a
`material` variant axis of `[clay, wax]`, yielding two distinct tablet items (a clay tablet and a
wax tablet). Both variants SHALL have `MaxStackSize = 1` and SHALL appear in the Creative-mode
inventory. There SHALL be no separate soft/fired state axis in this change (firing is deferred).

#### Scenario: Both variants exist in Creative

- **WHEN** a player in Creative mode browses the Scribe items
- **THEN** both a clay tablet and a wax tablet are present, each a variant of the same
  `ItemScribeTablet` item type

#### Scenario: Tablets are not stackable

- **WHEN** a player attempts to stack two tablet items of the same variant
- **THEN** the stack size remains 1

### Requirement: Tablets are crafted by simple grid recipes

Each tablet variant SHALL be obtainable through a simple crafting-grid recipe (NOT clayforming):
the clay tablet from a clay + sticks style recipe and the wax tablet from a beeswax + frame style
recipe. Crafting a tablet SHALL, server-side, record a `Crafted` history entry on the resulting
item stack in the same manner as the Notebook.

#### Scenario: Clay tablet crafted on the grid

- **WHEN** a player places the clay tablet recipe ingredients in the crafting grid and takes the
  output
- **THEN** a clay tablet item is produced and its stack carries a `Crafted` history entry naming
  the crafter

#### Scenario: Wax tablet crafted on the grid

- **WHEN** a player places the wax tablet recipe ingredients in the crafting grid and takes the
  output
- **THEN** a wax tablet item is produced and its stack carries a `Crafted` history entry naming
  the crafter

### Requirement: Tablet document persists on the ItemStack

A tablet's document SHALL be stored on the `ItemStack`'s attributes under the key `"scribeDocument"`
by pure reuse of `ScribeDocumentAttributes` — the same codec and attribute key the Notebook and
Lectern use. No new persistence code or network packet SHALL be introduced; the existing
`ScribeNotebookSaveMessage` (and its frozen registration order) is reused for server write-through.
A fresh tablet with no prior document SHALL open with an empty document carrying a fresh `DocId`.

#### Scenario: Fresh tablet starts empty with a fresh DocId

- **WHEN** a player obtains a new tablet that carries no document attribute
- **THEN** opening it shows an empty document with a freshly generated `DocId`

#### Scenario: Document and title survive close and reopen

- **WHEN** a player writes tasks and a title into a tablet, closes the dialog, and reopens the same
  tablet
- **THEN** the same tasks and title are shown

#### Scenario: Document survives drop and pickup

- **WHEN** a player drops a tablet carrying a document and then picks the same item back up
- **THEN** the document (with its `DocId` and task ids) is unchanged, because the bytes ride on the
  ItemStack attributes

### Requirement: Tablet held tooltip shows the document title

The tablet SHALL append the stored document's title (quoted, or an untitled placeholder) to its
held/inventory tooltip, reading the document via `ScribeDocumentAttributes.TryReadFrom`. A tablet
that has never been opened carries no document attribute and SHALL show the placeholder.

#### Scenario: Titled tablet shows its title in the tooltip

- **WHEN** a player hovers a tablet whose document has a non-default title
- **THEN** the tooltip includes that title

#### Scenario: Never-opened tablet shows the placeholder

- **WHEN** a player hovers a tablet that has never been opened
- **THEN** the tooltip shows the untitled placeholder line rather than erroring

### Requirement: Right-click opens the Scribe document dialog

Right-clicking (or using the interaction key) while holding a tablet SHALL open the Scribe document
editing dialog for the document stored in that specific tablet stack. A shift + right-click SHALL
pass through to the base collectible behaviors (including GroundStorable) rather than opening the
dialog. In this change the dialog opened is the **existing** Scribe document dialog (the bespoke
tablet dialog is a later proposal).

#### Scenario: Right-click opens the document

- **WHEN** a player right-clicks while holding a tablet
- **THEN** the Scribe document editing dialog opens showing that tablet's document

#### Scenario: Interim reuse of the existing dialog

- **WHEN** a tablet is opened in this change
- **THEN** the dialog shown is the existing `GuiDialogScribeNotebook`, reused so the item is
  testable before the bespoke tablet dialog (Proposal C) exists

#### Scenario: Shift+right-click stores on the ground

- **WHEN** a player shift + right-clicks while holding a tablet
- **THEN** the base ground-storage behavior runs and the dialog does not open

### Requirement: TabletHost adapts the tablet stack to the dialog

The system SHALL provide a `TabletHost` implementing `IScribeDocumentHost` as a thin variant of
`NotebookHost`. It SHALL report a layout with aspect ratio `1160/1024`, a `DefaultDocumentTitle` of
`"Tablet"`, and SHALL enforce the tablet document policy (see `scribe-document-policy`) at the
mutation boundary. It SHALL reuse the Notebook's server write-through and `Flush()` flow (writing
the document back to the ItemStack and syncing via the existing save packet).

#### Scenario: Host reports tablet layout and title

- **WHEN** the dialog requests the layout and default title from a `TabletHost`
- **THEN** the layout aspect ratio is `1160/1024` and the default title is `"Tablet"`

#### Scenario: Host enforces the policy at the mutation boundary

- **WHEN** an edit would exceed the tablet policy's caps
- **THEN** `TabletHost` refuses the mutation at the boundary, leaving the uncapped `ScribeDocument`
  model unchanged in its own contract

