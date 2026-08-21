## ADDED Requirements

### Requirement: A standalone explainer article describes the shared tabs and views

The mod SHALL provide a single standalone Handbook guide article — registered like the existing
`craftinginfo-scribe-*` articles (a page-definition JSON under `assets/scribe/config/handbook/` plus its
`title`/`text` lang keys) — that describes the tabs and views shared across the writing surfaces (Read,
Task Editor, Pinned, Guest Book, History) once, in one place. It notes which surfaces carry which tab
(e.g. Guest Book on placed surfaces, History on portable items, Transcribe / Import-Export on the
Scriptorium) and links to the existing editor-reference article for deeper editing mechanics. The
per-object entries link to this article rather than restating the tour.

#### Scenario: The shared Tabs & Views article exists and is reachable

- **WHEN** a player opens the Handbook and navigates to the Scribe guide articles
- **THEN** a standalone "Tabs & Views" article is present that describes the shared Read / Task Editor /
  Pinned / Guest Book / History tabs and notes which surfaces have which

#### Scenario: The shared article is not duplicated per object

- **WHEN** the shared tab/view tour is authored
- **THEN** it lives in exactly one article, and per-object entries reference it by link instead of
  containing their own copy of the same tour
