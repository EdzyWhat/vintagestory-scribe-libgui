## ADDED Requirements

### Requirement: History records into both the Notebook and the Clockmaker's Notebook

Automatic history recording SHALL treat the Clockmaker's Notebook (`ItemClockmakerNotebook`) the
same as the plain Notebook (`ItemScribeNotebook`) when locating a held notebook to record world
events into (deaths, temporal storms, boss kills, and other history event kinds). Inventory
detection SHALL match both sibling item classes, so a player carrying only a Clockmaker's Notebook
still accumulates a history chronicle.

#### Scenario: A held Clockmaker's Notebook receives history events

- **WHEN** a world event that Scribe records (such as the player's death) occurs while the player is
  carrying a Clockmaker's Notebook and no plain Notebook
- **THEN** the event is recorded into the Clockmaker's Notebook's history chronicle

#### Scenario: Plain Notebook history behavior is unchanged

- **WHEN** a player carrying a plain Notebook triggers a recordable world event
- **THEN** the event is recorded into the Notebook's history exactly as before
