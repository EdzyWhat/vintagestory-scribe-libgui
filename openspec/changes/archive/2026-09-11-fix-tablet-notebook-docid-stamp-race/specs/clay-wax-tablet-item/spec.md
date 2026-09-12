## MODIFIED Requirements

### Requirement: Tablet document persists on the ItemStack

A tablet's document SHALL be stored on the `ItemStack`'s attributes under the key `"scribeDocument"`
by pure reuse of `ScribeDocumentAttributes` — the same codec and attribute key the Notebook and
Lectern use. No new persistence code or network packet SHALL be introduced; the existing
`ScribeNotebookSaveMessage` (and its frozen registration order) is reused for server write-through.
A fresh tablet with no prior document SHALL open with an empty, in-memory document carrying a
fresh `DocId`; that `DocId` is a client-side proposal only and SHALL NOT be written to the
`ItemStack` merely because a dialog was opened or a document host was constructed over the slot
(this includes the periodic background carried-item sweep and its PickedUp-history recording —
see `notebook-history`'s PickedUp-recording requirement, which the tablet shares via
`TabletHost`). It becomes the item's real, persisted `DocId` only when the server accepts a save
for it, under the same rules `notebook-item`'s "Notebook saves are server-authoritative"
requirement defines for the Notebook.

#### Scenario: Fresh tablet starts empty with a fresh DocId

- **WHEN** a player obtains a new tablet that carries no document attribute
- **THEN** opening it shows an empty document with a freshly generated `DocId`

#### Scenario: Opening a fresh tablet writes nothing by itself

- **WHEN** a player opens a tablet that has never had a document saved to it, and closes the
  dialog again without making any edit
- **THEN** the `ItemStack` still has no `scribeDocument` attribute — no `DocId` was persisted

#### Scenario: Document and title survive close and reopen

- **WHEN** a player writes tasks and a title into a tablet, closes the dialog, and reopens the same
  tablet
- **THEN** the same tasks and title are shown

#### Scenario: Document survives drop and pickup

- **WHEN** a player drops a tablet carrying a document and then picks the same item back up
- **THEN** the document (with its `DocId` and task ids) is unchanged, because the bytes ride on the
  ItemStack attributes
