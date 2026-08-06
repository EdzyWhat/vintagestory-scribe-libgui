## MODIFIED Requirements

### Requirement: Notebook dialog closes automatically when item leaves the hand
The Notebook dialog SHALL close whenever the item is no longer in the player's active hand
or inventory (e.g. dropped, traded, placed into a chest while the dialog is open). This
ensures the dialog cannot remain open for an item the player no longer holds.

The auto-close SHALL distinguish a genuine switch-away from an in-place re-sync of the *same*
held item. Specifically:

- On a real active-hand change (the player selects a different hotbar slot), the dialog SHALL
  close unless the newly active hand item hosts the SAME document (compared by the stable
  `DocId`) — so switching to a DIFFERENT Scribe item closes the old dialog, while a hotbar
  reorder that keeps the same item active leaves it open.
- On an in-place slot modification of the CURRENTLY held item (the active hotbar slot's contents
  are rewritten, e.g. by a server re-sync such as the one-time "Picked up" history write), the
  dialog SHALL NOT close solely because the re-synced stack's `DocId` no longer matches the open
  document. The dialog SHALL close on this path ONLY if the active hand no longer holds a Scribe
  document item at all. This prevents the first-open flicker where a fresh, not-yet-crafted item's
  server re-sync would otherwise close the dialog immediately after it opens.

This behavior applies to the Clockmaker's Notebook as well, whose dialog inherits the Notebook
dialog's active-slot handling.

#### Scenario: Dialog closes when item is dropped
- **WHEN** a player drops the Notebook item while the dialog is open
- **THEN** the dialog closes

#### Scenario: Dialog stays open on first open of a picked-up notebook
- **WHEN** a player opens, for the first time, a Notebook they picked up (did not craft), triggering
  the server's one-time "Picked up" history write and an in-place re-sync of the held stack
- **THEN** the dialog stays open (no flicker) and shows the document, without requiring a second
  right-click

#### Scenario: Dialog closes when switching to a different Scribe item
- **WHEN** a player switches the active hotbar slot to a DIFFERENT Scribe document item while the
  dialog is open
- **THEN** the dialog closes
