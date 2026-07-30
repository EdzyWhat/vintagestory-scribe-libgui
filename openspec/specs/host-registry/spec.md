# host-registry

## Purpose

TBD - created via spec sync from change `add-scribe-notebook`. The host registry maps
active `DocId` values to their `IScribeDocumentHost`, enabling DocId-based packet routing
for both Lectern blocks and Notebook items without relying on block position.

## Requirements

### Requirement: All document hosts are addressed by DocId, not BlockPos
The system SHALL maintain a runtime registry mapping each active `DocId` (a `Guid`) to its
`IScribeDocumentHost`. All network packets that previously carried `PosX/PosY/PosZ` for
routing SHALL instead carry the `DocId` as a 16-byte array. The server SHALL look up the
host in the registry using `DocId`; hosts not found in the registry SHALL be silently
ignored (the block may be unloaded or the item may have been closed).

#### Scenario: Packet routes to lectern by DocId
- **WHEN** a client sends an edit packet containing a `DocId`
- **THEN** the server looks up the registered host for that `DocId` and delivers the edit
  to that host, regardless of block position

#### Scenario: Packet for unregistered DocId is a safe no-op
- **WHEN** the server receives a packet whose `DocId` is not in the registry (e.g. chunk
  unloaded, item closed)
- **THEN** the server discards the packet without error

### Requirement: Lecterns register in the host registry on initialization
A `BlockEntityScribeLectern` SHALL register itself in the host registry when it is
initialized (`Initialize`) using its document's `DocId` as the key. It SHALL unregister
when the block is removed (`OnBlockRemoved`). If the block is broken and its item picked up
and re-placed, the new BE SHALL register the same `DocId` that was stored in the `ItemStack`.

#### Scenario: Lectern registers on chunk load
- **WHEN** a chunk containing a Lectern is loaded
- **THEN** the Lectern's `IScribeDocumentHost` is present in the registry under its `DocId`

#### Scenario: Lectern unregisters on block removal
- **WHEN** the Lectern block is broken or removed
- **THEN** the `DocId` is removed from the registry

### Requirement: NotebookHost registers while the dialog is open
A `NotebookHost` SHALL register in the host registry when the Notebook dialog opens and
unregister when the dialog closes (including on ESC, death, disconnect, or inventory close).
Multiple open notebooks owned by different players SHALL each have a distinct `DocId` and
register independently.

#### Scenario: Notebook registers on dialog open
- **WHEN** a player opens their Notebook dialog
- **THEN** that Notebook's `IScribeDocumentHost` is present in the registry under its `DocId`

#### Scenario: Notebook unregisters on dialog close
- **WHEN** the Notebook dialog closes (any cause)
- **THEN** that `DocId` is removed from the registry

#### Scenario: Two notebooks from different players both register
- **WHEN** two players each open their own Notebook simultaneously
- **THEN** both `DocId`s are present in the registry simultaneously without conflict
