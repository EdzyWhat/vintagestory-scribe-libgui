## MODIFIED Requirements

### Requirement: An unfired clay tablet fires in a firepit into a fired clay tablet

An unfired clay tablet — whether wet or hard — SHALL be smeltable in a firepit — without a crucible/container,
at the clay firing temperature — producing the `-fired` variant of the same clay color, following the same
firepit path Vintage Story uses for firing clay pottery. Because firing uses `smeltingType` `cook` (and a pit
kiln only forms over `Fire`-type items), the tablet SHALL fire in a firepit and SHALL NOT be pit-kiln fired.
The fired output is the `-fired` variant (fired state is the item's own variant, not a stack attribute) and,
being fired, SHALL count as read-only regardless of any prior hard state — the fired variant takes precedence.
A wax tablet SHALL NOT be fireable, and an already-fired clay tablet SHALL NOT be re-fireable into another
tablet.

#### Scenario: An unfired clay tablet fires to a fired clay tablet

- **WHEN** an unfired clay tablet (wet or hard) is placed in a firepit and reaches the firing temperature for
  the required duration
- **THEN** it becomes the `-fired` variant of the same clay color

#### Scenario: Wax and already-fired tablets do not fire

- **WHEN** a wax tablet or an already-fired clay tablet is placed in a firepit
- **THEN** it does not smelt into another tablet

#### Scenario: A clay tablet cannot be pit-kiln fired

- **WHEN** a player attempts to form a pit kiln over a clay tablet
- **THEN** no kiln forms (the tablet's `cook` smelt type is not a `Fire`-type item), so firing happens only
  in a firepit

#### Scenario: Firing a hard tablet preserves its document and makes it permanent

- **WHEN** a hard clay tablet carrying a document is fired
- **THEN** the resulting `-fired` tablet carries the same document and is permanently read-only (it can no
  longer rehydrate)

### Requirement: Firing carries the tablet's document through the transformation and preserves its clay type

Firing a clay tablet SHALL preserve on the fired output the entire document (tasks, notes, and title),
mirroring how a Notebook's data carries into a Clockmaker's Notebook. Because the firepit builds its output
from a fixed smelt stack that does not copy input attributes, the tablet SHALL override the smelt behavior
to copy the document onto the fired output. The clay color SHALL be preserved because each clay tablet
variant smelts to the `-fired` variant of its own color — the smelt SHALL NOT change one clay color into
another.

#### Scenario: Task data survives firing

- **WHEN** a soft clay tablet carrying a document with tasks and notes is fired
- **THEN** the resulting fired tablet carries the same document (same tasks, notes, and title)

#### Scenario: Clay color survives firing

- **WHEN** a soft `clay-blue` tablet is fired
- **THEN** the resulting fired tablet is the `clay-blue-fired` variant
