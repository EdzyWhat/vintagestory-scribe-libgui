# v1-3-release-cut Specification

## Purpose
TBD - created by archiving change cut-v1-3-0. Update Purpose after archive.
## Requirements
### Requirement: modinfo version is 1.3.0
`src/Mod/modinfo.json` SHALL set `"version"` to `"1.3.0"`. Dependencies SHALL remain `"game": "1.22.0"` and `"gui": "3.1.0"`. `requiredOnClient` and `requiredOnServer` SHALL stay true.

#### Scenario: modinfo reads 1.3.0
- **WHEN** a player or the mod DB inspects `modinfo.json` in the 1.3.0 package
- **THEN** the version field is `1.3.0` and the game/gui dependencies are unchanged from 1.2.1

### Requirement: CHANGELOG has a dated 1.3.0 entry and complete compare links
`CHANGELOG.md` SHALL contain a `## [1.3.0]` section (Keep a Changelog) listing player-facing Added / Changed / Fixed bullets for everything shipped since `v1.2.1`, plus an explicit save-compat note (document codec v8 / pin codec v5; 1.0–1.2 worlds open; 1.3 writes are not readable by 1.2 clients). Dev-only work (`/scribe tablet`, playtest reconcile) SHALL NOT appear. The footer SHALL include compare links for `[1.3.0]` and the previously missing `[1.2.1]`.

#### Scenario: 1.3.0 section is present and dated
- **WHEN** a reader opens `CHANGELOG.md` after this cut
- **THEN** there is a `## [1.3.0] - YYYY-MM-DD` section whose bullets cover Crafting Tasks, the Chalkboard, tablet readability, and the one-way v8 write

#### Scenario: Compare-link footers include 1.2.1 and 1.3.0
- **WHEN** a reader scrolls to the CHANGELOG footer
- **THEN** `[1.3.0]` compares `v1.2.1...v1.3.0` and `[1.2.1]` compares `v1.2.0...v1.2.1`

### Requirement: Public version surfaces agree on 1.3.0
The README status line, `docs/media/mod-page.txt`, `docs/media/mod-page.html`, `docs/media/mod-page-inline.html`, and `docs/media/wiki/Home.md` SHALL all identify the current release as **v1.3.0**. Feature copy on those surfaces SHALL mention the Chalkboard and Crafting Tasks (and the already-shipped Scriptorium) rather than advertising a planned “v1.2 Writing Desk.”

#### Scenario: README status line is 1.3.0
- **WHEN** a reader opens `README.md`
- **THEN** the status line names v1.3.0, not v1.1.0

#### Scenario: Mod-page roadmap marks 1.2 and 1.3 shipped
- **WHEN** a reader opens any of the three mod-page files
- **THEN** v1.2 (Scriptorium / Transcribe / Tracker / Link) and v1.3 (Crafting Tasks / Chalkboard) read as released, and the old “Writing Desk planned” row is gone

### Requirement: ROADMAP reframes v1.3 as what shipped
`ROADMAP.md` SHALL mark the v1.2 Scriptorium cluster (block, Tracker/Link, Transcribe) as shipped, mark v1.3 as Crafting Tasks + Chalkboard (plus tablet readability) shipped, and move the assignment system (Assign & History / Inbox) to **later** without inventing a new version number. The wall-mounted Chalkboard SHALL stay distinct from the v6 drawable-chalkboard idea.

#### Scenario: Staged plan no longer calls v1.2 in-progress or v1.3 assignment
- **WHEN** a reader opens `ROADMAP.md`
- **THEN** v1.2 is shipped, v1.3 is this cut, and assignment is listed as later — not as the 1.3 headline

### Requirement: Wiki drafts describe the 1.3 surfaces
`docs/media/wiki/` SHALL be updated so Home, Items, and Crafting cover the Scriptorium and Chalkboard, with dedicated `Scriptorium.md` and `Chalkboard.md` pages. The Chalkboard page SHALL state the 10-task cap, wall-mount placement, and that it is not the drawable v6 board. Publishing those drafts to the GitHub wiki remains a manual post-cut step.

#### Scenario: Wiki Home nav and roadmap include 1.3
- **WHEN** a reader opens `docs/media/wiki/Home.md`
- **THEN** the nav links to Scriptorium and Chalkboard pages and the roadmap ticks v1.2 and v1.3 as released

#### Scenario: Crafting page lists Scriptorium and Chalkboard recipes
- **WHEN** a reader opens `docs/media/wiki/Crafting-the-Lectern.md`
- **THEN** it documents the Scriptorium (same writing kit as the Lectern, eight planks) and Chalkboard (planks + charcoal + nails, no ink kit) recipes

