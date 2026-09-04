## Purpose

The Task Notice is a hand-carried, physical delivery item for an out-of-range or offline
assignment: a blank notice is a plain crafting supply, a sealed notice carries one pending
assignment's task data until its recipient accepts or declines it.

## ADDED Requirements

### Requirement: Blank and sealed Task Notices render visually distinguishable models
The system SHALL render a blank Task Notice and a sealed Task Notice with visually distinct
models, so a player can tell the two states apart by looking at the item (in hand, on the
ground, or in a slot) without needing to read its tooltip.

#### Scenario: A blank notice looks different from a sealed one
- **WHEN** a player holds or views a blank Task Notice and, separately, a sealed Task Notice
- **THEN** the two render with visibly different models

#### Scenario: A notice's model updates immediately when it changes state
- **WHEN** a blank Task Notice becomes sealed (or a sealed one's document is otherwise cleared)
- **THEN** its rendered model updates to match its new state without requiring the item to be
  re-picked-up, re-equipped, or the game restarted
