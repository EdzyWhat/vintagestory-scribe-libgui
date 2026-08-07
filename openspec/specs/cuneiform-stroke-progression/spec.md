# cuneiform-stroke-progression Specification

## Purpose
TBD - created by archiving change add-cuneiform-handwriting-feel. Update Purpose after archive.
## Requirements
### Requirement: The editable cuneiform field reveals strokes progressively while typing

The editable cuneiform field SHALL support a stroke-progression reveal: when text is committed, its strokes
SHALL appear over time rather than all at once. Strokes within a single letter SHALL reveal in quick
succession, and there SHALL be a longer pause between letters, keyed off the source-character-index change.
The reveal SHALL generalize the existing display-only fraction reveal to a per-letter/stroke-count model and
SHALL add reveal state to the editable field renderer (which has none today). When stroke progression is
disabled, text SHALL appear fully revealed immediately (today's behaviour).

#### Scenario: A newly typed letter presses in stroke by stroke

- **WHEN** a player types a character into an editable cuneiform field with stroke progression enabled
- **THEN** that letter's strokes appear in quick succession, and the next letter begins after a slightly
  longer pause

#### Scenario: Disabled progression reveals instantly

- **WHEN** stroke progression is disabled
- **THEN** committed text renders fully, immediately, with every stroke drawn

### Requirement: Only newly-added text animates; existing text is not replayed

On each commit, the reveal SHALL animate only the newly-added run of strokes, advancing the revealed count
from the previously-revealed total to the new total. Already-revealed text SHALL NOT re-animate on subsequent
keystrokes. Deletions and mid-line edits SHALL snap to the new total without a reverse animation.

#### Scenario: Prior letters stay put while a new one animates

- **WHEN** a player types a second character after the first has fully revealed
- **THEN** the first character remains fully drawn and only the second character's strokes animate in

#### Scenario: Deleting text does not animate backwards

- **WHEN** a player deletes characters
- **THEN** the remaining text is shown at its new total immediately, with no reverse reveal

### Requirement: An optional ghost lead-in for the animating letter

The field MAY render the not-yet-pressed strokes of the currently-animating letter as a faint outline that
the filled strokes catch up to, gated behind the same stroke-progression setting. This is optional polish:
the plain progressive fill SHALL be correct and complete on its own, and the ghost lead-in SHALL be
enable-able without changing the reveal timing or the final rendered result.

#### Scenario: Ghost lead-in, when enabled, does not change the outcome

- **WHEN** the ghost lead-in is enabled
- **THEN** the currently-animating letter shows a faint outline ahead of its filled strokes, and once the
  letter finishes revealing its final appearance is identical to the plain progressive fill

### Requirement: Handwriting effects are configurable via client config

Scribe client config SHALL expose a jitter strength (zero disables jitter) and a stroke-progression toggle
(with a reveal speed), both read at (re)build time like other client settings, defaulting to a tasteful
handwriting look (jitter on at a low strength, progression on). Setting jitter strength to zero and disabling
progression SHALL reproduce the current crisp, instant rendering exactly.

#### Scenario: Config toggles restore the crisp look

- **WHEN** jitter strength is set to zero and stroke progression is disabled
- **THEN** cuneiform text renders exactly as it does today (perfect geometry, instant reveal)

#### Scenario: Defaults give the handwriting feel

- **WHEN** the settings are left at their defaults
- **THEN** cuneiform text renders with subtle per-stroke jitter and types in with per-letter stroke
  progression

