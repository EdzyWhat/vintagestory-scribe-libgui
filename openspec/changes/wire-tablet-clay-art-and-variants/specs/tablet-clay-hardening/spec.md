## MODIFIED Requirements

### Requirement: A wet clay tablet dries into a hard clay tablet over time

A wet (freshly crafted) clay tablet SHALL dry out over approximately two in-game days into a hard clay
tablet of the SAME clay color, using Vintage Story's native item `Harden` transition. The hardened output
SHALL be the `-hard` variant of that clay color (hard state is the item's own variant, not a stack
attribute). A wax tablet SHALL NOT dry, and an already-fired clay tablet SHALL NOT dry.

#### Scenario: A wet clay tablet hardens after ~2 in-game days

- **WHEN** a wet clay tablet has existed (not time-frozen, not in a creative slot) for its full fresh-hours
  duration plus its transition duration
- **THEN** it becomes the `-hard` variant of the same clay color

#### Scenario: Wax and fired tablets do not dry

- **WHEN** a wax tablet or an already-fired clay tablet exists for that same duration
- **THEN** it does not transition into a hard tablet

### Requirement: Hardening carries the tablet's document and preserves its clay type

Hardening SHALL preserve on the hard output the entire document (tasks, notes, and title). Because the
native transition rebuilds a fixed output stack and does not copy input attributes, the tablet SHALL
override its transition behaviour (`OnTransitionNow`) to copy the document onto the hardened output. The
clay color SHALL be preserved because the hardened stack is the `-hard` variant of the same clay color as
the input.

#### Scenario: Task data survives hardening

- **WHEN** a wet clay tablet carrying a document with tasks and notes hardens
- **THEN** the resulting hard tablet carries the same document (same tasks, notes, and title) and is the
  `-hard` variant of the same clay color

### Requirement: A hard clay tablet rehydrates to wet when exposed to water

A hard clay tablet SHALL soften back to a wet clay tablet — resetting its dry-out timer and keeping its
document — when it is exposed to water, mirroring how a lit torch is extinguished by water. Exposure SHALL be
detected two ways: (a) the tablet item stack is dropped into a water block, and (b) the tablet is the active
held item while its holder enters water (swims/wades). Rehydration SHALL swap the `-hard` variant back to
the soft variant of the same clay color and restart the dry-out timer so the softened tablet can dry again.
A fired clay tablet SHALL NOT rehydrate.

#### Scenario: A hard tablet dropped into water softens

- **WHEN** a hard clay tablet item enters (is dropped into / floats in) a water block
- **THEN** it becomes the soft variant of the same clay color, keeps its document, and its dry-out timer is
  reset to full

#### Scenario: A held hard tablet softens when its holder enters water

- **WHEN** a player holding a hard clay tablet as the active item enters water
- **THEN** the held tablet becomes soft, keeps its document, and its dry-out timer is reset

#### Scenario: A fired tablet is unaffected by water

- **WHEN** a fired clay tablet is dropped into water or its holder enters water
- **THEN** it remains fired and read-only
