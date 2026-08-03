## ADDED Requirements

### Requirement: The clay tablet item declares firepit combustible/smelt properties

Each clay tablet variant SHALL declare combustible properties enabling firepit firing (a smelt stack
producing the SAME clay tablet variant, at the clay firing temperature, needing no container, one-to-one),
and the item SHALL override its smelt behavior so the fired output carries the source tablet's document and
is marked `fired = true`. Clay type is preserved because each variant smelts to itself. The wax tablet
SHALL NOT declare firing combustible properties.

#### Scenario: Clay tablets are combustible, wax tablet is not

- **WHEN** the tablet item's combustible properties are queried
- **THEN** each soft clay tablet variant reports a firepit smelt into the same fired clay tablet variant,
  and a wax tablet reports no such firing

#### Scenario: The smelt output preserves stack data

- **WHEN** the clay tablet's smelt behavior produces the fired output
- **THEN** that output is the same clay variant and carries the source tablet's document bytes, with
  `fired = true`

### Requirement: The clay tablet item declares a Harden transition and carries data through it

Each clay tablet variant SHALL declare a native `Harden` transition (via `transitionablePropsByType`) whose
transitioned stack is the SAME clay variant and whose fresh-hours duration is approximately two in-game
days, and the item SHALL override its transition behaviour so the hardened output carries the source
tablet's document and is marked `hard = true`. The wax tablet SHALL NOT declare a hardening transition, and
an already-fired clay tablet SHALL NOT harden.

#### Scenario: Clay tablets declare hardening, wax does not

- **WHEN** the tablet item's transitionable properties are queried
- **THEN** each clay tablet variant reports a `Harden` transition into the same clay variant, and the wax
  tablet reports no hardening transition

#### Scenario: The hardened output preserves stack data

- **WHEN** the clay tablet's transition behaviour produces the hard output
- **THEN** that output is the same clay variant and carries the source tablet's document bytes, with
  `hard = true`

### Requirement: The clay tablet exposes editable state and a rehydration hook

The clay tablet item SHALL expose whether a stack is editable (editable ⇔ NOT `hard` AND NOT `fired`), via a
`hard` read helper mirroring the existing `fired` read helper. It SHALL provide a rehydration behaviour that,
on water exposure (item dropped into water, or held while its holder enters water), converts a `hard` clay
tablet stack back to a wet stack of the same clay variant — clearing `hard`, resetting the hardening timer,
and preserving the document — while leaving a `fired` tablet unchanged.

#### Scenario: Editable reflects both flags

- **WHEN** a tablet stack's editable state is queried
- **THEN** it is editable only when both `hard` and `fired` are false

#### Scenario: Rehydration softens a hard tablet, not a fired one

- **WHEN** the rehydration behaviour is applied to a hard clay tablet stack
- **THEN** the stack becomes a wet clay tablet of the same variant with `hard` cleared, the hardening timer
  reset, and the document preserved; a fired tablet stack is left unchanged
