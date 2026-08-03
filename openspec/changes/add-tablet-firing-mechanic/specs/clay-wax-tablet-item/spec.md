## ADDED Requirements

### Requirement: The clay tablet item declares firepit combustible/smelt properties

The clay tablet item SHALL declare combustible properties enabling firepit firing (a smelt stack producing
the fired clay tablet, at the clay firing temperature, needing no container, one-to-one), and SHALL
override its smelt behavior so the fired output carries the source tablet's document and `clayType` and is
marked `fired = true`. The wax tablet SHALL NOT declare firing combustible properties.

#### Scenario: Clay tablet is combustible, wax tablet is not

- **WHEN** the tablet item's combustible properties are queried
- **THEN** a soft clay tablet reports a firepit smelt into a fired clay tablet, and a wax tablet reports no
  such firing

#### Scenario: The smelt output preserves stack data

- **WHEN** the clay tablet's smelt behavior produces the fired output
- **THEN** that output carries the source tablet's document bytes and `clayType`, with `fired = true`
