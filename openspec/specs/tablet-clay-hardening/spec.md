# tablet-clay-hardening Specification

## Purpose
The clay tablet's wet → hard life-cycle: a freshly crafted (wet) clay tablet dries into a
read-only hard tablet of the same clay color over ~2 in-game days via the native `Harden`
transition, carrying its document forward, and a hard tablet rehydrates back to wet (via
water drop, entering water while held, or a deliberate crouch-right-click quench on a
water container). Fired tablets are permanent and unaffected by water — see `tablet-firing`.
## Requirements
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

### Requirement: A hard clay tablet is read-only but not permanent

A hard clay tablet SHALL be read-only: opening it SHALL present its document view-only with no way to add,
check, pin, reorder, or edit tasks and no way to edit the title. Unlike a fired tablet, this read-only state
SHALL be reversible via rehydration.

#### Scenario: A hard tablet cannot be edited

- **WHEN** a player opens a hard clay tablet that carries tasks and notes
- **THEN** the content is shown read-only and no task can be added, checked, pinned, reordered, or edited,
  and the title cannot be changed

### Requirement: A hard clay tablet rehydrates to wet when exposed to water

A hard clay tablet SHALL soften back to a wet clay tablet — resetting its dry-out timer and keeping its
document — when it is exposed to water, mirroring how a lit torch is extinguished by water. Exposure SHALL be
detected three ways: (a) the tablet item stack is dropped into a water block, (b) the tablet is the active
held item while its holder enters water (swims/wades), and (c) the player deliberately crouches and
right-clicks a water-filled liquid container while holding the tablet, mirroring the vanilla metal-quench
gesture. Rehydration SHALL swap the `-hard` variant back to the soft variant of the same clay color and
restart the dry-out timer so the softened tablet can dry again. All three paths SHALL share the same
variant-swap and document-preservation behavior. A fired clay tablet SHALL NOT rehydrate by any path.

#### Scenario: A hard tablet dropped into water softens

- **WHEN** a hard clay tablet item enters (is dropped into / floats in) a water block
- **THEN** it becomes the soft variant of the same clay color, keeps its document, and its dry-out timer is
  reset to full

#### Scenario: A held hard tablet softens when its holder enters water

- **WHEN** a player holding a hard clay tablet as the active item enters water
- **THEN** the held tablet becomes soft, keeps its document, and its dry-out timer is reset

#### Scenario: A held hard tablet softens when quenched in a water container

- **WHEN** a player holding a hard clay tablet crouches and right-clicks a liquid container that holds water
  (e.g. a bucket or barrel of water)
- **THEN** the held tablet becomes the soft variant of the same clay color, keeps its document, and its
  dry-out timer is reset, and the container's normal fill/pour interaction does not also fire

#### Scenario: Quenching only responds to a water container

- **WHEN** a player holding a hard clay tablet crouches and right-clicks an empty container, a container of a
  non-water liquid, or a non-container block
- **THEN** the tablet does not soften from that gesture and the existing crouch-right-click ground-storage
  placement behavior still applies

#### Scenario: A fired tablet is unaffected by water

- **WHEN** a fired clay tablet is dropped into water, its holder enters water, or it is used to right-click a
  water container while crouched
- **THEN** it remains fired and read-only

### Requirement: A hard clay tablet with no writing shows an empty-state message

When a hard clay tablet carries no document content (no tasks and no notes — e.g. one obtained already hard),
its dialog SHALL show a small centered message indicating it is dried and can be edited again after being
dunked in water, rather than an empty editable surface.

#### Scenario: A blank hard tablet explains itself

- **WHEN** a player opens a hard clay tablet that has no tasks and no notes
- **THEN** the dialog shows a small centered message that the tablet has dried out (and how to soften it),
  and offers no editing affordance
