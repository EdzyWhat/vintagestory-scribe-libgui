## ADDED Requirements

### Requirement: Mod-wide handbook content reflects the Chalkboard and Crafting Tasks
The shared guide pages (`craftinginfo-scribe-getting-started`, `craftinginfo-scribe-editor-reference`, `craftinginfo-scribe-pinned-hud`, `craftinginfo-scribe-task-types`) SHALL describe the Chalkboard as a placed writing surface and Crafting Tasks as a third item-bound type created from an item's Handbook page (recipe variants, ingredient subtasks). They SHALL NOT claim there are only two item-bound types or that those types cannot be added from the editor Add button without also naming Crafting Tasks. Leftover copy errors in those articles (`featues`, “Item Item Tracker”, the incomplete “enrich your experiences with other”) SHALL be corrected as part of the same refresh. Per-object Chalkboard copy (`handbook-chalkboard-about-*`) stays uniqueness-first.

#### Scenario: Getting Started lists the Chalkboard
- **WHEN** a player reads the "Getting Started" handbook guide
- **THEN** the craft-list of Scribe blocks includes the Chalkboard with a working handbook cross-link, alongside Lectern and Scriptorium

#### Scenario: Getting Started names Crafting Tasks
- **WHEN** a player reads the "Getting Started" task-types paragraph
- **THEN** it names Crafting Tasks in addition to Item Trackers and Links, and does not say Scribe has only two item-bound types

#### Scenario: Editor reference no longer says only two handbook-only types
- **WHEN** a player reads the Editor Reference "Adding tasks" section
- **THEN** Crafting Tasks are named with Item Trackers and Links as handbook-created types

#### Scenario: Pinned HUD copy mentions Craft and is not doubled
- **WHEN** a player reads the Pinned Task HUD guide
- **THEN** it does not contain the phrase "Item Item Tracker" and it acknowledges that Crafting Tasks can be pinned
