## ADDED Requirements

### Requirement: Tablet dialog closes on switch-away but survives in-place re-sync

The tablet dialog SHALL close when the player switches their active hand away from the tablet whose
document it is showing, using the same document-identity rule as the Notebook dialog: on a real
active-hand change, the dialog closes unless the newly active hand item hosts the SAME document
(compared by the stable `DocId`).

On an in-place slot modification of the CURRENTLY held tablet (the active hotbar slot's contents are
rewritten by a server re-sync, e.g. the one-time "Picked up" history write), the dialog SHALL NOT
close solely because the re-synced stack's `DocId` no longer matches the open document. On this path
the dialog SHALL close ONLY if the active hand no longer holds a Scribe document item at all. This
prevents the first-open flicker on a freshly obtained tablet.

The tablet's legitimate in-place material-state transition (wet → hard → fired), which also arrives
via slot modification, SHALL continue to be handled correctly and SHALL NOT be broken by the
flicker fix.

#### Scenario: Dialog stays open on first open of a picked-up tablet
- **WHEN** a player opens, for the first time, a tablet they picked up (did not craft), triggering
  the server's one-time "Picked up" history write and an in-place re-sync of the held stack
- **THEN** the tablet dialog stays open (no flicker) and shows the document, without requiring a
  second right-click

#### Scenario: Dialog closes when switching to a different Scribe item
- **WHEN** a player switches the active hotbar slot to a DIFFERENT Scribe document item while the
  tablet dialog is open
- **THEN** the tablet dialog closes

#### Scenario: In-place wet-to-hard transition is preserved
- **WHEN** a held wet tablet transitions to hard (or hard to fired) in place while its dialog is open
- **THEN** the tablet's state transition is handled as before and is not regressed by the
  flicker-close fix
