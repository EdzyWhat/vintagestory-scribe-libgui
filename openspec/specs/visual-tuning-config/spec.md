# visual-tuning-config

## Purpose

Lets the ambient light sampler's and unseen-assignment particle effect's author-tuning constants
be edited via a local config file — optionally through a config library's GUI — without adding
any mod dependency or rebuilding the mod.

## Requirements

### Requirement: Visual tuning values are read from a local config file with built-in defaults

The mod SHALL read 9 named tuning values — the ambient light sampler's brightness-step count,
hue-step count, tint strength, and smoothing time constant; the unseen-assignment particle
effect's detection radius, rainbow-accent ratio, count multiplier, and seed-burst multiplier; and
a boolean flag controlling whether the dialog header's subtitle row (Row 2, per
`scribe-dialog-base`) renders — from a client-local config file at startup. Any value absent from
the file, or the file itself being absent, SHALL fall back to the mod's built-in default for that
value, and that default SHALL match the mod's pre-existing hardcoded behavior exactly (the
subtitle-row flag's default is on, matching its pre-existing unconditional-render behavior).

#### Scenario: No config file present

- **WHEN** the client-local visual-tuning config file does not exist
- **THEN** the mod applies its built-in defaults for all 9 values, and the ambient light,
  particle-effect rendering, and dialog subtitle row are unchanged from their pre-existing
  behavior

#### Scenario: Config file overrides a subset of values

- **WHEN** the config file exists and sets some but not all of the 9 values
- **THEN** the mod applies the file's values for the keys present and its built-in default for
  every key absent from the file

#### Scenario: A written override changes rendered behavior

- **WHEN** the config file sets the particle effect's detection radius to a value different from
  the default
- **THEN** the particle effect's proximity trigger uses the configured radius instead of the
  default on the next load

#### Scenario: A written boolean override hides the subtitle row

- **WHEN** the config file sets the subtitle-row flag to `false`
- **THEN** every dialog tab covered by the `scribe-dialog-base` header requirement renders without
  Row 2 on the next load

### Requirement: No config-library mod dependency is required

The mod SHALL NOT declare a dependency, hard or soft, on any config-library mod for this feature,
and SHALL NOT call any config-library API (no assembly reference, no `IsModEnabled` check tied to
this feature). The mod's own behavior — including reading the config file and applying its
values — SHALL be identical whether a config-library mod is installed or not. A config-library mod
that reads the shared `configlib-patches.json` manifest format MAY optionally provide a settings
screen that edits the same underlying file, but the mod's correctness SHALL NOT depend on one
being present.

#### Scenario: Feature works with no config-library mod installed

- **WHEN** no config-library mod is installed
- **THEN** the mod reads its visual-tuning config file directly (or applies defaults if absent)
  with no error, warning, or missing functionality attributable to the library's absence

#### Scenario: A config-library GUI edit is picked up

- **WHEN** a config-library mod that understands the shared manifest format is installed and used
  to change one of the 8 values, and the client is then restarted
- **THEN** the mod's next config-file read reflects the config-library-written value
