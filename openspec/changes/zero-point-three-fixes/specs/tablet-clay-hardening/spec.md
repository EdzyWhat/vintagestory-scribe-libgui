## MODIFIED Requirements

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
