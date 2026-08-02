## ADDED Requirements

### Requirement: A clay tablet records its clay type and fired appearance on the stack

A clay tablet SHALL record on its `ItemStack` a `clayType` value of `red`, `blue`, or `fire`, set at
craft time from the clay ingredient consumed, and a `fired` appearance value (soft or fired). These
values SHALL be stored as stack attributes and SHALL be preserved across dialog close/reopen and across
drop/pickup, carried by the existing document save and pickup flow (no new network packet). They SHALL
be an appearance record only: this requirement SHALL NOT add the soft→fired firing gameplay
transformation (still deferred). A wax tablet SHALL NOT be required to carry a `clayType`. When a
clay tablet carries no `clayType` or `fired` attribute, consumers SHALL treat it as `red` + soft.

#### Scenario: Clay type is captured at craft

- **WHEN** a clay tablet is crafted from blue clay
- **THEN** the resulting tablet stack records `clayType = blue`

#### Scenario: Clay type and fired persist across drop and pickup

- **WHEN** a clay tablet recording a given `clayType` and `fired` value is dropped and picked back up
- **THEN** the picked-up stack still records the same `clayType` and `fired` value

#### Scenario: Recording fired does not fire the tablet

- **WHEN** the `fired` appearance value is recorded on a tablet stack
- **THEN** no soft→fired transformation, archive-on-fire, or other firing gameplay occurs — the value
  only influences the tablet's recorded appearance

#### Scenario: A clay tablet with no recorded type defaults to red + soft

- **WHEN** a clay tablet stack carries no `clayType` or `fired` attribute
- **THEN** consumers treat it as `clayType = red` and unfired
