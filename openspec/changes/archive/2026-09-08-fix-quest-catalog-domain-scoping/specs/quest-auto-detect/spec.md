## ADDED Requirements

### Requirement: A backend's quest catalog includes content from any installed mod, not just its own domain
For either supported backend (VS Quest or Progression Framework), the system SHALL read that
backend's `config/quests` catalog across every installed mod's assets, not scoped to the backing
framework mod's own domain. Real quest content is always contributed by a separate dependent mod
under that mod's own domain (for example Seafarer's quests for Progression Framework, or VS
Village's quests for VS Quest) — scoping the read to the framework's own domain SHALL NOT be
treated as sufficient, since the framework mod itself typically ships no quest content at all.

#### Scenario: A dependent mod's quests appear in the Progression Framework catalog
- **WHEN** Progression Framework is installed and enabled, and a separate mod (e.g. Seafarer)
  ships quest definitions under its own mod domain
- **THEN** those quest definitions appear in the Progression Framework catalog used for auto-detect
  and the Quest Link picker

#### Scenario: A dependent mod's quests appear in the VS Quest catalog
- **WHEN** VS Quest is installed and enabled, and a separate mod (e.g. VS Village) ships quest
  definitions under its own mod domain
- **THEN** those quest definitions appear in the VS Quest catalog used for auto-detect and the
  Quest Link picker

#### Scenario: A framework with no bundled quest content still surfaces dependent-mod quests
- **WHEN** a backend framework mod ships no `config/quests` content under its own domain at all
- **THEN** the catalog is still populated from whatever other installed mods contribute quests
  under their own domains, rather than being empty
