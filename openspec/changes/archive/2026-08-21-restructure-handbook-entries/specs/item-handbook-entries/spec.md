## ADDED Requirements

### Requirement: Per-object entries are uniqueness-first and link to shared articles

Each object's Handbook entry (Lectern, Notebook, Scriptorium, Chalkboard, Tablet, Clockmaker's Notebook)
SHALL describe only what is unique to that object and link out to the shared explainer articles for
material it has in common with other surfaces, rather than duplicating the shared tab/view tour inline.
The Chalkboard entry is the reference for tone and length: a brief "what makes this unique" framing plus
a link to the shared reference. Genuinely object-specific content (e.g. the Scriptorium's Transcribe,
the Guest Book on placed surfaces, the Notebook's History, the Clockmaker timer, the Tablet's
wax/hard/fired material states) is retained, framed as the delta from the shared baseline.

#### Scenario: A per-object entry does not restate the shared tab tour

- **WHEN** a player reads the Lectern, Notebook, or Scriptorium Handbook entry
- **THEN** it does not contain a full copy of the shared Read / Task Editor / Pinned tab tour; instead it
  states which tabs the surface has and links to the shared Tabs & Views article for the tour

#### Scenario: Object-unique content is preserved

- **WHEN** an object has behavior not shared with the other surfaces (Scriptorium Transcribe, Guest Book,
  Notebook History, Clockmaker timer, Tablet material states)
- **THEN** that content remains in the object's entry, described as what distinguishes it

#### Scenario: Entries stay concise

- **WHEN** a per-object entry is authored under this restructure
- **THEN** it reads at roughly the Chalkboard entry's length — uniqueness-first, with shared material
  reached by link — rather than an exhaustive standalone document
