## ADDED Requirements

### Requirement: A log helper surfaces Scribe diagnostics from the game logs
The repo SHALL provide a documented helper that tails and filters the Vintage Story
`server-main.txt` and `client-main.txt` logs for Scribe-relevant output, so a developer can
watch the `[scribe]` trace and asset-load errors during an in-game session without hand-
constructing paths and filters each time.

#### Scenario: Developer follows the Scribe trace live
- **WHEN** a developer runs the log helper during a running game session
- **THEN** it follows the current `server-main.txt` (and optionally `client-main.txt`) and
  shows `[scribe]`-prefixed lines and asset/mod-load errors, resolving the log path from the
  standard VintagestoryData location

### Requirement: A dev-world launch profile presets developer debug settings
The repo SHALL document a repeatable "dev world" launch profile that starts the game into a
flat creative test world with developer mode and the useful debug settings (extended debug
info, error reporter) enabled, so the iterate loop starts from a consistent diagnostic
baseline.

#### Scenario: Developer launches the documented dev profile
- **WHEN** a developer follows the documented dev-world launch profile
- **THEN** the game starts into a creative test world with developer mode enabled and the
  documented debug settings preset, without hand-toggling them each session

### Requirement: Fast-iterate reload commands are documented for this project
The repo SHALL capture, in its modding-reference notes, the fast in-game iterate techniques
that avoid a full relaunch (`.reload textures`/`shapes`/`shaders`/`lang`, `CTRL+F1` world
reload) and the assessed viability of C# Hot Reload for this project's compiled-mod setup on
the developer's platform.

#### Scenario: The reload workflow is recorded in the reference notes
- **WHEN** `VSAPI-NOTES.md` (or the documented reference location) is inspected
- **THEN** it lists the applicable `.reload`/world-reload commands and states whether Hot
  Reload works for this mod's build setup, so the technique is not re-derived per session

### Requirement: Atlas reference material and next-adoption notes are captured
The repo SHALL keep the Atlas source and wiki available as local, gitignored reference
material and SHALL record which not-yet-used Atlas 0.11.0 capabilities are candidates for
adoption, so the harness the project already depends on is used more fully over time.

#### Scenario: Atlas references are present and ignored by git
- **WHEN** the repo is inspected
- **THEN** `reference/atlas` (source) and `reference/atlas-wiki` (wiki) exist and are
  gitignored, and a note records candidate Atlas capabilities to adopt next (e.g.
  `ExecuteCommand` assertions and `atlas diff` differential regression)
