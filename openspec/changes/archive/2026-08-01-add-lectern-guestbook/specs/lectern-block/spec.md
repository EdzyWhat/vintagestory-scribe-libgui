## ADDED Requirements

### Requirement: Lectern block entity persists and exposes a guestbook
`BlockEntityScribeLectern` SHALL hold a `GuestbookStore` instance alongside its document store.
The guestbook SHALL be serialized into tree attributes under a distinct key (e.g. `"guestbook"`)
and SHALL NOT overlap with the document's attribute keys.

#### Scenario: Guestbook serializes independently of the document
- **WHEN** `ToTreeAttributes` is called on a lectern with both document content and guestbook entries
- **THEN** each is stored under its own key and neither overwrites the other

#### Scenario: Guestbook deserializes on load
- **WHEN** `FromTreeAttributes` is called on a freshly-loaded block entity
- **THEN** the guestbook entries match what was written before the chunk was saved

### Requirement: Lectern GUI open triggers a server-side visitor record
When the Lectern GUI is opened by a client, the client SHALL send a "record visitor" packet
to the server. The server SHALL delegate to `GuestbookStore.TryAddEntry` and, if a new entry
was added, SHALL mark the block entity dirty and send an updated guestbook sync packet back to
the opening client.

#### Scenario: GUI open causes server to write entry and respond
- **WHEN** a client opens the Lectern GUI
- **THEN** the server records the visitor (if not duplicate) and sends the current guestbook
  state back to the opening client

#### Scenario: No dirty-mark on duplicate open
- **WHEN** a client opens the Lectern GUI a second time on the same in-game day
- **THEN** the server does not call `MarkDirty` (no new entry was written)
