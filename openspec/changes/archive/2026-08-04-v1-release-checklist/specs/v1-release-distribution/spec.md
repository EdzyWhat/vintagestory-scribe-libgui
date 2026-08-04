## ADDED Requirements

### Requirement: A CHANGELOG.md documents v0.1.0
The repository SHALL contain a `CHANGELOG.md` at the root using the Keep a Changelog convention. The
v0.1.0 entry SHALL list the features shipped, the hard dependencies, and the release date.

#### Scenario: CHANGELOG exists and is parseable
- **WHEN** a user inspects the repository root
- **THEN** `CHANGELOG.md` exists with a `## [0.1.0]` section listing at minimum: lectern block,
  task checklist, pinned-task HUD, multiplayer-safe, survival-craftable, and the `game 1.22.0` and
  `gui 2.0.0` dependencies

### Requirement: CREDITS lists all third-party assets
The `CREDITS` file at the repository root SHALL name every third-party asset bundled in the mod,
including all font files shipped in `src/Mod/assets/scribe/textures/fonts/`, with their license
names and source URLs. JeanPierre (Wanderer's Sketchbook) SHALL be credited as an inspiration.

#### Scenario: New fonts are credited
- **WHEN** a font TTF is added to the fonts asset directory
- **THEN** that font's entry (name, author, license, URL) appears in CREDITS before the release tag

#### Scenario: JeanPierre credit is present
- **WHEN** a user reads CREDITS
- **THEN** there is an entry acknowledging Wanderer's Sketchbook and its author JeanPierre as an
  inspiration for Scribe

### Requirement: Pin Tab is verified in-game before release
The mod SHALL NOT be tagged for release until all 11 scribe-pin-editor verification tasks (7.1–7.11)
have been run in-game and their verdicts recorded in TESTING.md. The Pin Tab items SHALL appear in
TESTING.md under a `## scribe-pin-editor` section before any in-game run begins.

#### Scenario: Pin Tab items exist in TESTING.md
- **WHEN** a developer runs the playtest checklist
- **THEN** the 11 Pin Tab items from scribe-pin-editor tasks.md 7.1–7.11 are present and traceable

#### Scenario: All Pin Tab items confirmed before tag
- **WHEN** the release tag is created
- **THEN** every Pin Tab TESTING.md item carries a Confirmed verdict

### Requirement: Multiplayer behavior is tested before the public claim is made
The mod SHALL be tested on a second machine (headless server + 2nd client) confirming: live
cross-session read-view sync, independent per-lectern documents, editor lock refusing a second
editor but allowing a second reader, and drag-reorder + settings persistence. Results SHALL be
recorded in TESTING.md (add-lectern-block 7.5–7.7).

#### Scenario: Multiplayer pass recorded
- **WHEN** the release tag is created
- **THEN** TESTING.md items c127b9ad (7.5), 2a105a38 (7.6), and the reorder/settings item (7.7)
  each carry a Confirmed verdict

### Requirement: Survival craftability is verified before release
The mod SHALL be verified in a real survival world: the Lectern is craftable from the grid recipe,
appears in the Lectern's handbook entry, and opens/functions without Creative-mode reach.

#### Scenario: Survival pass recorded
- **WHEN** the release tag is created
- **THEN** the survival-pass items in TESTING.md (RELEASE.md A5) carry a Confirmed verdict

### Requirement: v3-blob codec migration is covered by a unit test
`tests/Core.Tests/` SHALL contain a test that constructs a valid v3-format byte array and calls
`ScribeDocumentCodec.TryDeserialize`, asserting the result is `true` and the output document has a
fresh `DocId` (generated on migration) and the same block content as the source. This test SHALL
pass on CI with no game install.

#### Scenario: v3 blob deserializes to v4 document
- **WHEN** `TryDeserialize` is called with a hand-crafted v3 byte array
- **THEN** it returns `true`, the output `document.DocId` is a non-empty Guid, and the task text
  matches the v3 source content

#### Scenario: Codec test passes on CI
- **WHEN** `dotnet test tests/Core.Tests/` is run without a game install
- **THEN** the v3-blob test passes alongside all other Core tests
