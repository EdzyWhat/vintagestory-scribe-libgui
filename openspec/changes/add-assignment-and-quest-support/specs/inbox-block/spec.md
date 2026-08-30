## ADDED Requirements

### Requirement: Craftable, placeable Inbox block
The system SHALL provide an Inbox block, obtainable via a crafting recipe (and in creative),
whose sole capability is showing the shared Inbox tab (`inbox-tab` capability) so a player can
receive and act on assignments. It SHALL reuse the existing writing-station block-entity and
dialog base classes for persistence/sync rather than introducing a parallel mechanism.

#### Scenario: Placing and opening the Inbox block
- **WHEN** a player crafts or spawns an Inbox block and right-clicks it
- **THEN** the block registers and renders its own model, and its dialog opens directly to the
  Inbox tab with no other tab present

### Requirement: The Inbox block has no create-and-send capability
The Inbox block SHALL NOT expose any control for creating a new task or sending an assignment to
another player — only the Assignment Desk's Assignment tab can do that (see
`assignment-desk-block`).

#### Scenario: Inbox block offers no Assignment tab
- **WHEN** a player opens the Inbox block's dialog
- **THEN** no Assignment tab or create-assignment control is present, only the Inbox tab

### Requirement: Inbox block dimensions are supplied via IScribeDocumentHost
The Inbox block's block entity SHALL implement `IScribeDocumentHost.GetLayout` to supply its own
width/aspect-ratio/proportions, following the same per-host layout mechanism every other Scribe
surface uses. The bounding box SHALL be the player's Pixel Art Size preference as width, with
height 1.2× that width; the Inbox tab's row-list content region SHALL render as a 1:1 square
within that box, matching the Assignment Desk's Inbox tab dimensions exactly.

#### Scenario: The bounding box and content ratio match the Assignment Desk's Inbox tab
- **WHEN** the standalone Inbox block's dialog opens with the player's Pixel Art Size set to some
  width W
- **THEN** the dialog's overall bounding box is W wide by 1.2×W tall, and the Inbox row-list
  content region within it is a 1:1 square, identical in proportion to the Assignment Desk's own
  Inbox tab

### Requirement: Inbox block persistence and sync follow the vanilla Sign pattern
The Inbox block entity SHALL persist and synchronize using the same `ToTreeAttributes`/
`FromTreeAttributes`/`SendBlockEntityPacket`/`MarkDirty` pattern every other Scribe block uses,
server-authoritative, with no Inbox-specific persistence mechanism.

#### Scenario: An Inbox block's state survives a server restart
- **WHEN** a server hosting a placed Inbox block restarts
- **THEN** the block's data is intact and its Inbox tab shows the same assignments as before
  the restart
