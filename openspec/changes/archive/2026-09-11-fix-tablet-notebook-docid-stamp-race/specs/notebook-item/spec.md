## MODIFIED Requirements

### Requirement: Notebook document persists in the ItemStack
The Notebook's document SHALL be stored in the `ItemStack`'s attributes under the key
`"scribeDocument"` using the same `ScribeDocumentAttributes` serialization used by the
Lectern's break/place flow. A fresh Notebook (no prior document) SHALL open with an empty,
in-memory document carrying a fresh `DocId`; that `DocId` is a client-side proposal only
and SHALL NOT be written to the `ItemStack` merely because a dialog was opened or a document
host was constructed over the slot. It becomes the item's real, persisted `DocId` only when
the server accepts a save for it (see "Notebook saves are server-authoritative").

#### Scenario: Document survives inventory move
- **WHEN** a player moves their Notebook to a different slot
- **THEN** the document contents are unchanged

#### Scenario: Fresh notebook starts empty
- **WHEN** a player obtains a new Notebook with no existing document data
- **THEN** opening it shows an empty document with a fresh `DocId`

#### Scenario: Opening a fresh notebook writes nothing by itself
- **WHEN** a player opens a Notebook that has never had a document saved to it, and closes the
  dialog again without making any edit
- **THEN** the `ItemStack` still has no `scribeDocument` attribute — no `DocId` was persisted

#### Scenario: Stacked notebooks are disallowed
- **WHEN** a player attempts to stack two Notebook items
- **THEN** the stack size remains 1 (Notebook is not stackable)

### Requirement: Notebook saves are server-authoritative
All document edits made in the Notebook dialog SHALL be sent to the server and applied
there, exactly as Lectern edits are. The server SHALL write the updated document back to
the `ItemStack` and broadcast a sync reply to the owning player's client.

The server is the sole writer of a first-ever `DocId` onto a documentless stack: it accepts
the `DocId` carried by the first save it receives for that stack. No other server-side
operation — recording history, resolving a document host for any other reason — SHALL write
a `DocId` onto a documentless stack ahead of that first save (see `notebook-history`'s
PickedUp-recording requirement), so a player's own save is never rejected by a `DocId` some
unrelated background process minted first.

#### Scenario: Edit in notebook dialog is persisted
- **WHEN** a player edits a task or note in the Notebook dialog and the autosave flush fires
- **THEN** the server applies the edit to the `ItemStack.Attributes` and syncs the updated
  document back to the client

#### Scenario: First save on a fresh notebook establishes its DocId
- **WHEN** a player's first-ever save on a Notebook with no `scribeDocument` attribute arrives
- **THEN** the server writes that save's `DocId` and document onto the `ItemStack` as the
  notebook's real, persisted document
