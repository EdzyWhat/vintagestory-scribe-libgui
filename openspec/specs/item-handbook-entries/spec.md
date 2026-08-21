# item-handbook-entries Specification

## Purpose
TBD - created by archiving change scribe-0-2-0-release-content. Update Purpose after archive.
## Requirements
### Requirement: The Notebook has an in-game handbook entry describing its function

The `scribe:scribenotebook` item SHALL carry a `handbook` attribute block with one or more
`extraSections` describing what the Notebook does (carried personal notes and tasks, the full
Scribe editor minus the Guestbook, and how to craft it). The section text SHALL be supplied via
`scribe:`-domain lang keys in `lang/en.json`, following the existing Lectern handbook convention.

#### Scenario: Notebook handbook page shows functional guidance

- **WHEN** a player opens the survival handbook entry for the Notebook
- **THEN** the entry shows the item description plus the added `extraSections` explaining its use
  and crafting, not just the one-line `-desc`

### Requirement: The Clockmaker's Notebook has an in-game handbook entry describing its timer

The `scribe:scribeclockmakernotebook` item SHALL carry a `handbook` attribute block with
`extraSections` describing its function, including the built-in timer (real-time and in-game-time
countdowns) and how it is crafted from a Notebook. Section text SHALL be supplied via
`scribe:`-domain lang keys.

#### Scenario: Clockmaker's Notebook handbook page describes the timer

- **WHEN** a player opens the survival handbook entry for the Clockmaker's Notebook
- **THEN** the entry describes the timer feature and the crafting relationship to the plain Notebook

### Requirement: Mod-wide handbook content reflects the Notebook

The existing Lectern handbook sections (`handbook-scribelectern-*`) and the two guide pages
(`craftinginfo-scribe-getting-started`, `craftinginfo-scribe-editor-reference`) SHALL be refreshed
so that Scribe's in-game documentation reads coherently with the Notebook and Clockmaker's Notebook
present, rather than describing a Lectern-only mod.

#### Scenario: Getting-started guide mentions the carried notebook

- **WHEN** a player reads the "Getting Started" guide page in the handbook
- **THEN** the content acknowledges the Notebook (and Clockmaker's Notebook) as ways to carry notes,
  in addition to the Lectern, with working cross-links

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

