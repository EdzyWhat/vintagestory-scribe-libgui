# libgui-first-run-suppression Specification

## Purpose
TBD - Update Purpose after archive.

## Requirements

### Requirement: Scribe suppresses LibGUI's first-run theme dialog on a fresh client install

On a client install where LibGUI's shared `ModConfig/libgui.json` does not yet exist, Scribe SHALL
write that file with `Onboarded: true` and no theme override, before LibGUI's own
`GuiModSystem.StartClientSide` reads it, so LibGUI resolves its own default theme and never opens its
first-run theme-picker dialog.

#### Scenario: Fresh client install never sees the dialog

- **WHEN** a player launches the client with Scribe and LibGUI installed, and no `libgui.json` exists
  yet in `ModConfig`
- **THEN** LibGUI's first-run theme dialog does not open, and the active theme is LibGUI's own default
  theme (the same theme a player would get by opening the dialog and choosing the default preset)

#### Scenario: Other LibGUI-based mods are also onboarded

- **WHEN** a player has Scribe installed alongside another LibGUI-based mod, and no `libgui.json`
  exists yet
- **THEN** that other mod's LibGUI usage also resolves to the default theme with no first-run dialog,
  since the onboarding state is shared client-wide by design

### Requirement: An existing onboarding decision is never overridden

If `ModConfig/libgui.json` already exists — regardless of its `Onboarded` value — Scribe SHALL NOT
modify it.

#### Scenario: Already-onboarded client is left alone

- **WHEN** `libgui.json` already exists with `Onboarded: true` (a returning player, or theme already
  chosen)
- **THEN** Scribe makes no change to the file, and LibGUI's own startup behavior is unaffected

#### Scenario: A deliberately reset onboarding flag is respected

- **WHEN** `libgui.json` already exists but with `Onboarded: false` (e.g. a player manually edited the
  file to re-trigger the dialog for themselves)
- **THEN** Scribe makes no change to the file, and LibGUI's first-run dialog opens normally as LibGUI
  itself would decide

### Requirement: Failure to pre-seed the config never blocks startup

If reading or writing `libgui.json` fails for any reason, Scribe SHALL log a warning and continue
startup without throwing, leaving LibGUI's own onboarding behavior to run unmodified.

#### Scenario: An unexpected I/O or deserialization failure

- **WHEN** loading or storing the mod config throws for any reason
- **THEN** Scribe logs a warning and takes no further action; LibGUI's own `GuiModSystem.StartClientSide`
  proceeds exactly as it would without this change (the dialog may appear, exactly as it did before
  this change existed)
