## ADDED Requirements

### Requirement: A Quest Link references an installed quest mod's catalog entry
When a quest mod (currently only VS Quest) is installed and enabled, a Link block MAY carry a
Quest-namespaced `LinkTarget` (e.g. a `quest:` prefix) identifying one entry in that mod's public
`config/quests/*.json` asset catalog. Resolving a Quest Link SHALL read only that static catalog
for the quest's name/description — never a live dependency reference, never write access. A
Quest Link's name/description text SHALL be captured at creation time and stored on the block,
not re-derived from the catalog on every render.

#### Scenario: A Quest Link is created from the installed quest catalog
- **WHEN** a player creates a Quest Link referencing an entry in vsquest's quest catalog
- **THEN** the resulting Link block's `LinkTarget` identifies that quest, and its displayed
  name/description are captured from the catalog at creation time

#### Scenario: A Quest Link never queries a live dependency
- **WHEN** a Quest Link is displayed or resolved
- **THEN** only the static catalog asset (or the block's own captured text) is read — no
  compiled reference to any quest mod's DLL is involved

### Requirement: Quest Links work on every surface Link tasks already work on
A Quest Link SHALL be usable anywhere an ordinary Link task can be created or shown (Notebook,
Tablet, Lectern, Scriptorium, Chalkboard) — it is not restricted to the Assignment Desk or any
place-bound surface, since it is a personal reference, not a social action.

#### Scenario: Creating a Quest Link from the Notebook
- **WHEN** a player with vsquest installed creates a Quest Link from their Notebook
- **THEN** the Quest Link is created and displayed exactly as any other Link task on that surface

### Requirement: A Quest Link degrades to a plain Link if its quest mod is later removed
If the quest mod backing a Quest Link is later uninstalled, the Link SHALL continue to render
using its captured-at-creation-time name/description text, with no error state and no removal —
it simply stops being eligible for further auto-detection enrichment (see `quest-auto-detect`).

#### Scenario: An orphaned Quest Link still renders
- **WHEN** a world previously used with vsquest is later loaded without vsquest installed
- **THEN** any existing Quest Links still display their captured text normally, with no error
  shown and no auto-detect behavior attempted
