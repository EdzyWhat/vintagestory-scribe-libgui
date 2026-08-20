## ADDED Requirements

### Requirement: Tablet title band wraps a long title to at most two lines

On the Tablet dialog, the title band SHALL wrap a title that is too wide for a single line onto a
second line, up to a maximum of two lines, instead of clipping the title to one line. This applies in
both the resting (display) state and the editing (title-field) state. A title that fits on one line
SHALL be rendered exactly as before (single line, no band-height change). A title longer than two
lines' worth SHALL clip at the end of the second line (cuneiform has no ellipsis glyph). This behavior
SHALL be scoped to the Tablet; the Lectern, Notebook, Scriptorium, and HUD title chrome SHALL remain
single-line as today. When cuneiform is disabled or the glyph bundle is unavailable, the tablet MAY
fall back to the base single-line title rendering.

#### Scenario: Long title wraps to two lines at rest

- **WHEN** a player views a tablet whose title is longer than one line of the title band
- **THEN** the resting title renders across two lines within the band, and the drag-grip, pencil, and
  close chrome stay clear of the wrapped title

#### Scenario: Long title wraps to two lines while editing

- **WHEN** a player edits a tablet title and types past the width of one line
- **THEN** the editing title field shows the text wrapped onto a second line rather than clipping the
  overflow off the right edge, and pressing Enter still commits the title (no newline is inserted)

#### Scenario: Short title and other surfaces unchanged

- **WHEN** a tablet title fits on one line, or a Lectern/Notebook/Scriptorium title of any length is shown
- **THEN** the title renders on a single line exactly as before, with no change to band height or layout
