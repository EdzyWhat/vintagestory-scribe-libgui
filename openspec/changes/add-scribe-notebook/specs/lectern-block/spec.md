## MODIFIED Requirements

### Requirement: Lectern registers in the host registry (replaces position-based routing)
The Lectern block entity SHALL register itself in the `DocId → IScribeDocumentHost` registry
on `Initialize` and unregister on `OnBlockRemoved`. All packets previously sent with
`PosX/PosY/PosZ` routing fields SHALL now carry only the `DocId` (16-byte array). No
player-visible behavior changes; this is purely an internal routing change.

#### Scenario: Lectern is reachable by DocId after chunk load
- **WHEN** a chunk containing a Lectern is loaded and the Lectern initializes
- **THEN** the host registry contains an entry for that Lectern's `DocId`

#### Scenario: Lectern is not reachable after block removal
- **WHEN** a Lectern block is broken or removed
- **THEN** the host registry no longer contains an entry for that Lectern's `DocId`
