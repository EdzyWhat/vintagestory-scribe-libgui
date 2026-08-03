## ADDED Requirements

### Requirement: Each clay type is a discrete tablet item; fired appearance is a stack attribute

The clay tablet SHALL exist as three discrete registered items — one per Vintage Story clay type (red,
blue, fire) — expressed as composite states of the tablet's `material` variant axis
(`clay-red`, `clay-blue`, `clay-fire`), alongside the `wax` state. Each of the four items SHALL have its
own handbook entry and its own crafting recipe. The clay type SHALL therefore be the item's own variant,
NOT a stack attribute.

A clay tablet SHALL additionally record a `fired` appearance value (soft or fired) as a stack attribute,
preserved across dialog close/reopen and across drop/pickup by the existing document save and pickup flow
(no new network packet). The `fired` value SHALL be an appearance record only: this requirement SHALL NOT
add the soft→fired firing gameplay transformation (still deferred). When a tablet's `material` variant is
unrecognized or a `fired` attribute is absent, consumers SHALL treat it as red + soft.

#### Scenario: The three clay types are discrete craftable items

- **WHEN** a player browses the handbook or creative inventory
- **THEN** a Red Clay Tablet, a Blue Clay Tablet, and a Fire Clay Tablet appear as separate entries, each
  with its own recipe, alongside the Wax Tablet

#### Scenario: Clay type is fixed by the item, not a stack attribute

- **WHEN** a blue clay tablet is crafted from blue clay
- **THEN** the resulting item is the `clay-blue` variant (its clay type is the item itself, requiring no
  stack attribute to record it)

#### Scenario: Fired appearance persists across drop and pickup

- **WHEN** a clay tablet recording a given `fired` value is dropped and picked back up
- **THEN** the picked-up stack still records the same `fired` value, and the item is still the same clay
  variant

#### Scenario: Recording fired does not fire the tablet

- **WHEN** the `fired` appearance value is recorded on a tablet stack
- **THEN** no soft→fired transformation, archive-on-fire, or other firing gameplay occurs — the value
  only influences the tablet's recorded appearance

#### Scenario: An unrecognized variant or absent fired attribute defaults to red + soft

- **WHEN** a tablet stack has an unrecognized `material` variant or carries no `fired` attribute
- **THEN** consumers treat it as red + soft
