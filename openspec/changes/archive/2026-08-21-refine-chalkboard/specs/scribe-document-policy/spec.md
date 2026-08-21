## ADDED Requirements

### Requirement: The chalkboard host caps tasks at 10 with pins uncapped

The chalkboard block entity SHALL report a `ScribeDocumentPolicy` with `MaxBlocks = 10`
(counting task blocks only) and `MaxPins = null` (pins uncapped), and SHALL NOT be read-only.
The cap SHALL be enforced at the same host/editor mutation boundary the tablet uses (the
boundary that consults `CanAdd`/`CanPin`), NOT inside `ScribeDocument`. Because the chalkboard
is a shared placed block whose pins are per-player, it SHALL NOT reuse the `Tablet` preset
(which caps pins at 1); it SHALL supply its own policy value capping tasks without capping pins.

#### Scenario: Eleventh task is refused on a chalkboard

- **WHEN** a chalkboard document already holds 10 task blocks and the add-task boundary consults
  `CanAdd`
- **THEN** `CanAdd` returns false, the block is not added, and the "add task" affordance is
  disabled with the standard in-game refusal surfaced (the same observable path the tablet uses)

#### Scenario: Notes are not counted toward the chalkboard cap

- **WHEN** a chalkboard document holds freeform note/text blocks in addition to task blocks
- **THEN** only task blocks count toward the `MaxBlocks = 10` cap, so notes may be added
  regardless of how many notes already exist

#### Scenario: Chalkboard pins are uncapped

- **WHEN** any number of players pin tasks from a chalkboard and `CanPin` is consulted
- **THEN** `CanPin` always returns true, because the chalkboard policy leaves `MaxPins` null
  (unlike the `Tablet` preset's 1-pin cap)

#### Scenario: Tenth task is still allowed on a chalkboard

- **WHEN** a chalkboard document holds 9 task blocks and the add-task boundary consults `CanAdd`
- **THEN** `CanAdd` returns true
