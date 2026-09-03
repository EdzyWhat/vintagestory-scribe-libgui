## MODIFIED Requirements

### Requirement: A Quest Link references an installed quest mod's catalog entry
When a supported quest mod (VS Quest or Progression Framework) is installed and enabled, a Link
block MAY carry a Quest-namespaced `LinkTarget` (e.g. a `quest:` prefix) identifying which
backend it targets and one entry in that backend's public `config/quests/*.json` asset catalog.
Resolving a Quest Link SHALL read only that static catalog for the quest's name/description —
never a live dependency reference, never write access. A Quest Link's name/description text
SHALL be captured at creation time and stored on the block, not re-derived from the catalog on
every render. The recorded backend SHALL be used for all later resolution (auto-detection,
progress mirroring, destination resolution) — see `quest-auto-detect`'s backend-attribution
requirement.

#### Scenario: A Quest Link is created from an installed quest catalog
- **WHEN** a player creates a Quest Link referencing an entry in an installed backend's quest
  catalog
- **THEN** the resulting Link block's `LinkTarget` identifies both that backend and that quest,
  and its displayed name/description are captured from the catalog at creation time

#### Scenario: A Quest Link never queries a live dependency
- **WHEN** a Quest Link is displayed or resolved
- **THEN** only the static catalog asset (or the block's own captured text) is read — no
  compiled reference to any quest mod's DLL is involved

#### Scenario: The picker offers entries from every installed backend
- **WHEN** a player creates a Quest Link with both VS Quest and Progression Framework installed
- **THEN** the picker lists catalog entries from both backends, and the created Link records
  whichever backend the chosen entry actually came from

## ADDED Requirements

### Requirement: Following a Progression Framework Quest Link opens that backend's ledger
When a player activates (clicks/taps) a Quest Link block whose recorded backend is Progression
Framework, the system SHALL open that mod's own ledger dialog via its public standalone toggle
hotkey, using the same activation path already used for every other link kind (no new button, no
new UI surface). A VS Quest Link's activation SHALL remain a no-op — VS Quest exposes no
standalone dialog to open this way (see design.md Decision 5).

#### Scenario: Activating a Progression Framework Quest Link opens the ledger
- **WHEN** a player clicks a Quest Link block whose recorded backend is Progression Framework, in
  any surface (read view, editor, Pin Tab, or HUD pin)
- **THEN** Progression Framework's own ledger dialog opens

#### Scenario: Activating a VS Quest Link remains a no-op
- **WHEN** a player clicks a Quest Link block whose recorded backend is VS Quest
- **THEN** nothing happens, matching the existing (pre-change) behavior

#### Scenario: A missing or renamed ledger hotkey degrades silently
- **WHEN** Progression Framework's ledger hotkey code is absent (e.g. renamed in a future PF
  release)
- **THEN** activating the Quest Link does nothing rather than throwing
