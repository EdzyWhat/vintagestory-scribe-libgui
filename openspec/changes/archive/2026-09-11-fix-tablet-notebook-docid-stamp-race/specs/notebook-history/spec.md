## ADDED Requirements

### Requirement: Recording a PickedUp history entry never persists a document
Recording a Notebook or Tablet's one-time PickedUp history entry (on first dialog open, or
whenever the server otherwise resolves a document host for a carried item — including the
periodic carried-notebook sweep that also detects `TemporalStorm` starts and flushes queued
history entries every ~10 seconds for every online player) SHALL persist only the history
store. It SHALL NOT cause a fresh `DocId` to be minted and persisted onto a stack the player
has not yet saved a document to themselves, regardless of how many times or from how many
call sites a document host is constructed over that stack in the meantime.

#### Scenario: PickedUp recording on a brand-new notebook writes no document
- **WHEN** the server resolves a document host over a Notebook or Tablet that has never had a
  document saved to it, and records that player's first-pickup history entry
- **THEN** the stack still has no `scribeDocument` attribute afterward — only `scribeHistory`
  was written

#### Scenario: The periodic sweep still records Death/PvpKill/BossKill/TemporalStorm on a
brand-new notebook
- **WHEN** the periodic sweep or a live history-recording event (Death, PvpKill, BossKill,
  TemporalStorm) fires while a player carries a Notebook or Tablet that has never had a
  document saved to it
- **THEN** the history entry is still recorded normally (carrying a document is not a
  precondition for history), and no `DocId` is stamped onto the stack as a side effect

#### Scenario: PickedUp recording on an already-documented notebook is unaffected
- **WHEN** a document host is constructed over a Notebook or Tablet that already has a saved
  document, and records a PickedUp entry
- **THEN** history recording proceeds exactly as before this change, and the existing document
  is untouched
