## ADDED Requirements

### Requirement: Resting dialog titles wrap to at most two lines on all standard surfaces

Every Scribe dialog built on `ScribeDialogBase` — the Lectern, Notebook, Clockmaker's Notebook,
Scriptorium, Chalkboard, and the Tablet (both with cuneiform enabled and disabled) — SHALL wrap a
resting (non-editing) title that is too wide for a single line onto a second line, up to a maximum of
two lines, instead of clipping the title to one line. The wrapped title SHALL use the surface's
standard title font and layout, and SHALL grow into the title band's existing vertical slack via the
shared band-growth path so the drag-grip, pencil, and close chrome stay clear of the wrapped title. A
title that fits on one line SHALL render exactly as before — a single line with no change to band
height or layout on any surface.

#### Scenario: Long title wraps to two lines at rest

- **WHEN** a player opens any Scribe dialog (Lectern, Notebook, Clockmaker's Notebook, Scriptorium,
  Chalkboard, or Tablet) whose title is longer than one line of the title band
- **THEN** the resting title renders across two lines within the band, and the title-bar chrome
  (drag-grip, pencil where present, and close button) stays clear of the wrapped title

#### Scenario: Short title unchanged on every surface

- **WHEN** a title fits on a single line, on any of the standard surfaces
- **THEN** the title renders on one line exactly as before, with no change to band height or layout

#### Scenario: Tablet readable (cuneiform-off) path wraps like the other surfaces

- **WHEN** a player views a Tablet with cuneiform disabled and a title longer than one line
- **THEN** the resting title wraps to two lines using the readable RichText rendering, matching the
  Lectern/Notebook/Scriptorium behavior

### Requirement: A title longer than two lines is truncated on the second line

On the readable (non-cuneiform) rendering path, a title too long to fit within two lines SHALL be
truncated on the second line with an ellipsis (`...`). On the cuneiform Tablet path, where the glyph
set has no ellipsis, such a title SHALL clip at the end of the second line. In both cases the title
SHALL never occupy more than two lines.

#### Scenario: Overlong title ellipsizes on the readable path

- **WHEN** a Lectern/Notebook/Scriptorium/Chalkboard or cuneiform-off Tablet title is longer than two
  lines can display
- **THEN** the second line ends with an ellipsis and the title occupies exactly two lines

#### Scenario: Overlong title clips on the cuneiform path

- **WHEN** a cuneiform-enabled Tablet title is longer than two lines can display
- **THEN** the title clips at the end of the second line (no ellipsis glyph) and occupies exactly two lines

### Requirement: The editing title on the readable path remains single-line

On the readable (non-cuneiform) surfaces, the inline title EDITING field SHALL remain a single-line
input, because the stock LibGUI text field has no multi-line mode. Only the resting/display title
wraps to two lines. The cuneiform Tablet editing title MAY continue to wrap as it already does. Enter
SHALL still commit the title on every surface (no newline is inserted).

#### Scenario: Readable editing title does not wrap

- **WHEN** a player edits a Lectern/Notebook/Scriptorium/Chalkboard or cuneiform-off Tablet title and
  types past the width of one line
- **THEN** the editing field remains a single line (scrolling horizontally within the field), and on
  commit the resting title re-wraps to two lines if it is long enough

#### Scenario: Enter commits without inserting a newline

- **WHEN** a player presses Enter while editing any dialog title
- **THEN** the title is committed and the field reverts to the resting display, with no newline added

### Requirement: The Tablet title wrap width is governed by a single tunable proportion

The width at which the Tablet title wraps SHALL be governed by the title+buttons row width, which is
`ScribeLayoutProportions.TitleBtnsWFrac * W`. The Tablet SHALL use the shared default value for this
proportion unless a Tablet-specific override is set, so that the wrap point can be tuned in one place
without affecting title rendering correctness.

#### Scenario: Tablet inherits the shared default wrap width

- **WHEN** no Tablet-specific `TitleBtnsWFrac` override is set
- **THEN** the Tablet title wraps at the width derived from the shared default `TitleBtnsWFrac`

#### Scenario: Overriding the proportion moves the tablet wrap point

- **WHEN** a Tablet-specific `TitleBtnsWFrac` value is set in the tablet layout
- **THEN** the width at which the tablet title wraps changes accordingly, with no change to the
  two-line cap or to any other surface's title
