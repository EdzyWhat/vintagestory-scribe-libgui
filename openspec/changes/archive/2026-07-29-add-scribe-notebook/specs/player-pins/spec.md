## MODIFIED Requirements

### Requirement: Pin messages carry DocId instead of BlockPos
All pin/unpin/complete-pin network packets SHALL carry the target task's `DocId` and
`TaskId` as the sole address fields (no `PosX/PosY/PosZ`). The server SHALL route these
packets through the host registry. This change does not alter any pin behavior visible to
the player.

#### Scenario: Pin packet routes by DocId
- **WHEN** a player pins or unpins a task in any Scribe GUI
- **THEN** the outbound packet contains the `DocId` (16-byte array) and `TaskId` only, and
  the server resolves the host via the registry
