# task-notice-inbox-presence-signal

## Purpose

A stronger, harder-to-miss presence signal for a sealed Task Notice addressed to the local
player that is physically sitting in one of the standalone Inbox block's own restricted slots —
layered on top of the existing generic nearby-scan particle ping, drawing the eye specifically to
the Inbox holding it once it's actually there.

## Requirements

### Requirement: An Inbox block holding an addressed, undiscovered notice emits the ambient particle signal

While at least one of an Inbox block's restricted inventory slots holds a sealed Task Notice
addressed to the local player that they have not yet discovered (opened/Accepted/Declined), that
specific Inbox block instance SHALL emit the existing ambient assignment-particle effect
(client-side, local to the addressed player only) for as long as the condition holds, in
addition to — not instead of — the existing generic nearby-scan ping.

#### Scenario: Placing an addressed notice into the Inbox starts the particle effect
- **WHEN** a sealed Task Notice addressed to the local player is placed into one of an Inbox
  block's restricted slots (by them or anyone else)
- **THEN** that Inbox block begins emitting the ambient particle effect, visible only to the
  addressed player

#### Scenario: Discovering the notice stops the particle effect
- **WHEN** the addressed player opens and Accepts or Declines the notice while it is in the Inbox
  (or removes it from the block)
- **THEN** that Inbox block stops emitting the particle effect

### Requirement: The Inbox Inventory tab button shimmers while an addressed, undiscovered notice is present

While the condition above holds for a given Inbox block, that block's own "Inbox Inventory" nav
tab button, when its dialog is open, SHALL show the existing shimmer sweep effect — a per-block,
inventory-driven trigger distinct from the existing coarser "player has any unseen assignment
anywhere" shimmer trigger used elsewhere.

#### Scenario: Opening the Inbox with an addressed notice inside shows the tab shimmer
- **WHEN** the addressed player opens an Inbox block that is currently holding a sealed,
  addressed, undiscovered notice in one of its restricted slots
- **THEN** the Inbox Inventory nav tab button shows the shimmer sweep

### Requirement: The specific notice's own slot shimmers while undiscovered

While the condition above holds for a specific slot, that slot's rendered icon, when the Inbox
Inventory tab is open, SHALL also show the shimmer sweep effect, so the exact slot holding the
notice is identifiable at a glance among the Inbox's other restricted slots.

#### Scenario: The addressed notice's own slot shimmers among other filled slots
- **WHEN** the addressed player views the Inbox Inventory tab with the addressed, undiscovered
  notice in one restricted slot and other unrelated items in other slots
- **THEN** only the slot holding the addressed, undiscovered notice shows the shimmer sweep; the
  other slots render normally

### Requirement: The signal is presentation only

None of the signals above SHALL alter the notice's stack, its document, its assignment state, or
any inventory contents — they are purely client-side visual indicators, following the existing
ambient-particle signal's own presentation-only precedent.

#### Scenario: Viewing the signals does not change any state
- **WHEN** the addressed player observes the Inbox block's particles, the tab shimmer, and the
  slot shimmer without taking any other action
- **THEN** the notice's document, assignment state, and the Inbox's inventory contents are
  unchanged
