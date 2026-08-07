## ADDED Requirements

### Requirement: Scribe documents are placeable on general shelves

Every Scribe document item — the Notebook (`scribenotebook`), the Clockmaker's Notebook
(`scribeclockmakernotebook`), and every Tablet variant (`scribetablet`: clay red/blue/fire ×
wet/hard/fired, plus wax) — SHALL be accepted by vanilla general shelves
(`BlockEntityShelf`). Acceptance is opted into via the item's `shelvable` attribute, using the
same layout vocabulary vanilla books use.

#### Scenario: Placing a Notebook on a shelf
- **WHEN** a player holds a Notebook and interacts with an empty slot on a general shelf
- **THEN** the Notebook is placed into that shelf slot and removed from the player's hand
- **AND** the shelved Notebook renders on the shelf using its `onshelfTransform`

#### Scenario: Placing a clay Tablet on a shelf
- **WHEN** a player holds any Tablet variant (wet, hard, fired, or wax) and interacts with an
  empty shelf slot
- **THEN** the Tablet is placed into that shelf slot
- **AND** the same behavior holds for all clay colours and all life-cycle states

### Requirement: Scribe documents are placeable on bookshelves

Every Scribe document item SHALL be accepted by vanilla bookshelves
(`BlockEntityBookshelf`), opted into via the item's `bookshelveable: true` attribute.

#### Scenario: Placing a Notebook on a bookshelf
- **WHEN** a player holds a Notebook and interacts with an empty bookshelf slot
- **THEN** the Notebook is placed into that bookshelf slot and rendered using its
  `onshelfTransform`

#### Scenario: A document without the opt-in is rejected
- **WHEN** an item that lacks `bookshelveable` is aimed at a bookshelf slot
- **THEN** the bookshelf does not accept it (baseline vanilla behavior, unchanged)

### Requirement: Scribe documents are placeable on cabinets

Every Scribe document item SHALL be accepted by vanilla cabinets, whose storage is driven by
the `Display` block behavior (`BEBehaviorDisplay`). Acceptance is opted into via a
`displayable.shelf` attribute block declaring the item's on-surface `size`; the declared size
MUST fit the cabinet's placement surface, or the game rejects the placement with its standard
"too large" in-game notice.

#### Scenario: Placing a Notebook in a cabinet
- **WHEN** a player holds a Notebook and interacts with an empty cabinet display slot that is
  large enough for the declared `displayable.shelf` size
- **THEN** the Notebook is placed into the cabinet and rendered using its `onshelfTransform`

#### Scenario: Oversized declaration is rejected gracefully
- **WHEN** a document's declared `displayable.shelf` size exceeds the target cabinet slot
- **THEN** the cabinet declines the placement and surfaces the vanilla "too large" notice
  rather than placing a clipping model

### Requirement: Document identity survives shelving

Placing a Scribe document on any supported surface (shelf, bookshelf, or cabinet) and later
retrieving it SHALL preserve the full item stack, including the Scribe document identity
(docId) carried in the stack's attributes, so the retrieved document reopens with its original
tasks and notes intact. No new persistence path is introduced — the document rides the vanilla
shelf inventory.

#### Scenario: Shelve and retrieve preserves content
- **WHEN** a player shelves a Notebook that has tasks and notes, then later takes it back into
  hand and opens it
- **THEN** the Notebook opens showing the same tasks and notes it had before shelving

#### Scenario: A hardened tablet keeps its locked state through shelving
- **WHEN** a player shelves a hardened (dried) clay Tablet and later retrieves it
- **THEN** the Tablet is still in its hardened state and its editing lock behaves exactly as
  it did before shelving

### Requirement: Each document declares an on-surface transform

Every Scribe document item SHALL declare an `onshelfTransform` (translation / rotation /
origin) so it sits correctly on the surface. All three storage systems (shelf, bookshelf,
cabinet) read this single transform key for positioning.

#### Scenario: Shelved documents are visually placed within surface bounds
- **WHEN** a Scribe document is placed on any supported surface
- **THEN** its rendered model sits within the slot's bounds without clipping into neighbours
  or floating off the surface (final offsets settled by in-game tuning)
