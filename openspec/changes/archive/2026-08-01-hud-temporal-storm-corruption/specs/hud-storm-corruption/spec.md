## ADDED Requirements

### Requirement: HUD text is corrupted while a temporal instability trigger is active

The pinned-task HUD SHALL render all of its text (the title, task-row text, the "+N more" footer,
and the timer label and countdown) through a text-corruption transform when at least one instability
trigger is active. The transform SHALL reproduce the vanilla temporal-storm chat effect by injecting
Unicode combining diacritic marks between characters at a per-character probability equal to the
active corruption strength, rendering a "crazed" stacked-glyph appearance through the normal font
path. No GPU shader SHALL be required.

#### Scenario: Text corrupts when a trigger is active

- **WHEN** the HUD is visible and a temporal storm is active (or personal stability is below the
  low-stability threshold)
- **THEN** the title, task rows, "+N more" footer, and timer text are rendered with injected
  combining marks proportional to the active corruption strength

#### Scenario: Text is clean when no trigger is active

- **WHEN** no temporal storm is active and personal stability is at or above 50%
- **THEN** all HUD text renders normally with no injected marks

### Requirement: Two independent triggers drive corruption strength

Corruption strength (0..1) SHALL be computed from two independent triggers, and the effective
strength SHALL be the greater of the two:

- **Temporal storm** — strength is keyed to the active storm tier using vanilla's own glitch
  strengths: **≈0.53 for a Light storm, ≈0.67 for a Medium storm, and ≈0.90 for a Heavy storm**
  (`EnumTempStormStrength`).
- **Low personal stability** — when the player's temporal stability is below 0.50, strength ramps
  linearly from **0.0 at 0.50** to **1.0 at 0.10** (values at or below 0.10 clamp to 1.0); at or
  above 0.50 this trigger contributes 0.0.

#### Scenario: Storm tier sets strength

- **WHEN** a Medium temporal storm is active and personal stability is at full
- **THEN** the effective corruption strength is ≈0.67 (the Medium-storm glitch strength)

#### Scenario: Low stability ramps strength

- **WHEN** no storm is active and the player's temporal stability is 0.30
- **THEN** the effective corruption strength is 0.50 (halfway along the 0.50→0.10 ramp)

#### Scenario: The stronger trigger wins

- **WHEN** a Light storm (≈0.53) is active while personal stability is 0.20 (ramp ≈ 0.75)
- **THEN** the effective corruption strength is ≈ 0.75 (the greater of the two)

### Requirement: The title swaps to a storm call-to-action only during a storm

While a temporal storm is active, the HUD title SHALL display the localized "Survive the Storm"
string (`scribe:scribe-hud-title-storm`) in place of the normal "Pinned" title, held steady for the
storm's duration and reverting to "Pinned" when the storm ends. The low-stability trigger SHALL NOT
change the title; it only drives corruption.

#### Scenario: Storm swaps the title

- **WHEN** a temporal storm is active
- **THEN** the HUD title reads "Survive the Storm" (still subject to text corruption)

#### Scenario: Low stability alone keeps the normal title

- **WHEN** the low-stability trigger is active but no storm is present
- **THEN** the HUD title remains "Pinned" (corrupted per the active strength)

### Requirement: The corruption re-randomizes organically while active

While a trigger is active, the corrupted rendering SHALL be recomputed on a randomized interval
between 0 and 5 seconds, so the injected marks change over time and the text appears to writhe
rather than remaining static. Recomputation SHALL reuse the HUD's existing refresh mechanism and
SHALL NOT re-scramble every frame.

#### Scenario: Corruption shifts over time

- **WHEN** a trigger remains active across several seconds
- **THEN** the specific injected marks change on a randomized 0–5 second cadence

### Requirement: The effect degrades gracefully when storm state is unavailable

If the temporal-stability system is not present (for example on a server without the survival
content), the HUD SHALL behave as though no trigger is active: no corruption, no title swap, and no
errors.

#### Scenario: No temporal system present

- **WHEN** the temporal-stability mod system cannot be resolved on the client
- **THEN** the HUD renders normally with no corruption and no title swap
