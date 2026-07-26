## ADDED Requirements

### Requirement: A single command builds, tests, and restages the mod
The repo SHALL provide one `build/` entry point that builds the mod, runs the Core unit
tests, runs the Atlas integration suite (excluding the `FixtureBuilders` world-builder
scenario), and restages the mod, so the full local verification loop is one invocation
rather than several remembered steps.

#### Scenario: Developer runs the full loop in one command
- **WHEN** a developer runs the verify/restage script
- **THEN** it builds the mod, runs `tests/Core.Tests`, runs `tests/Integration.Tests` with
  the `FixtureBuilders` filter excluded, and restages the built mod, using the Vintage Story
  install and mod dependencies already present on the machine

#### Scenario: A failing test stops the loop before restaging
- **WHEN** the Core tests or the Atlas suite fail during the script
- **THEN** the script exits non-zero and reports which stage failed, and does not silently
  restage a broken build

### Requirement: An opt-in pre-push hook gates pushes to main on the local suites
The repo SHALL provide an opt-in git `pre-push` hook, installed via a `build/` script, that
runs the Core tests and the Atlas integration suite before a push to `main` completes and
blocks the push when they fail.

#### Scenario: Pre-push hook blocks a push that breaks the suites
- **WHEN** the hook is installed and a developer pushes a branch that would update `main`
  with a change that fails the Core tests or the Atlas suite
- **THEN** the hook exits non-zero and the push is refused, naming the failing stage

#### Scenario: Hook installation is explicit, not automatic
- **WHEN** the repo is cloned
- **THEN** the hook is not active until the developer runs the documented installer script,
  and the installer and hook live under version control so they are discoverable

#### Scenario: The hook can be bypassed deliberately
- **WHEN** a developer needs to push despite the hook (e.g. a work-in-progress branch that is
  not `main`)
- **THEN** the standard `git push --no-verify` bypass remains available, and this is
  documented as the intended escape hatch
