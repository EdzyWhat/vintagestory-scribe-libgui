## MODIFIED Requirements

### Requirement: Edit packets carry DocId instead of BlockPos
All document-edit network packets (save document, set task done, request/release editor
lock) SHALL carry the `DocId` (16-byte array) as the sole document address field. No
`PosX/PosY/PosZ` fields. The server routes via the host registry. Document model and
serialization are unchanged.

#### Scenario: Edit packet routes by DocId
- **WHEN** a client flushes an autosave or explicit save
- **THEN** the packet contains the `DocId` only (no block position), and the server
  resolves the host via the host registry
