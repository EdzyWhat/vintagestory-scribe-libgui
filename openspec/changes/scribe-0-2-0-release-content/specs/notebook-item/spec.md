## ADDED Requirements

### Requirement: The Clockmaker's Notebook is treated as a notebook everywhere the plain Notebook is

The Clockmaker's Notebook (`ItemClockmakerNotebook`) is a sibling item class of the plain Notebook
(`ItemScribeNotebook`); both host a `ScribeDocument` plus a history chronicle. Every code path that
locates, saves, or keeps a notebook dialog open SHALL treat the two classes equivalently, so a player
carrying only a Clockmaker's Notebook gets the full notebook experience. This covers at minimum:
automatic history recording (inventory detection), server-side persistence of task/note edits,
server-side DocId→host resolution, and the open dialog's active-slot handling.

#### Scenario: Task and note edits to a Clockmaker's Notebook persist

- **WHEN** a player edits tasks or notes in a Clockmaker's Notebook and closes the dialog (or the
  notebook otherwise flushes)
- **THEN** the edits are saved server-side and remain present when the notebook is reopened and after
  a world save/reload

#### Scenario: The dialog stays open while the Clockmaker's Notebook is held

- **WHEN** a player has a Clockmaker's Notebook dialog open and changes their active hotbar slot to a
  different slot that still holds the same Clockmaker's Notebook (or the slot is merely re-selected)
- **THEN** the dialog does not spuriously close

#### Scenario: A held Clockmaker's Notebook receives history events

- **WHEN** a world event that Scribe records (such as the player's death) occurs while the player is
  carrying a Clockmaker's Notebook and no plain Notebook
- **THEN** the event is recorded into the Clockmaker's Notebook's history chronicle

#### Scenario: Plain Notebook behavior is unchanged

- **WHEN** a player carrying a plain Notebook edits it or triggers a recordable world event
- **THEN** edits persist and history records exactly as before
