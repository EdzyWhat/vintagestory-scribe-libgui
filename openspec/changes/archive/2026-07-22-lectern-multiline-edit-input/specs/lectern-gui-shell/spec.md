## MODIFIED Requirements

### Requirement: Editor view edits in place with a single floating input
The editor view SHALL edit row text in place using exactly one live text input element that is
repositioned onto the row the player is editing. Every non-focused row SHALL draw its text as a
static label; the focused row SHALL suppress drawing its own text label for that frame (still
drawing its checkbox and ruling) so the input and label never both paint the same text. The
static label and the floating input SHALL align via the shared `RowTextLayout` metric so that
gaining or losing focus produces no visible shift in text position, baseline, or font size.

The floating input SHALL wrap long text onto multiple lines, breaking at the same width the
static label wraps at, rather than presenting a single horizontally-scrolling line. As the
player types text that overflows onto a new line (or deletes text back onto fewer lines), the
focused row's height SHALL grow or shrink dynamically to fit the wrapped text — measured the same
way a static row is measured — and the rows below it SHALL shift and the scroll region SHALL
update accordingly, so a focused row behaves exactly like a static wrapped row. The focused row
SHALL remain scrolled into view as it grows, and the caret position and focus SHALL be preserved
across the height-driven recompose.

Pressing Enter (without Shift) SHALL remain commit-and-advance and SHALL NOT insert a line break.
Pressing Shift+Enter SHALL insert a hard line break into the row's text, growing the row like a
soft wrap does. A row's text MAY therefore contain player-inserted newlines, which the read view
SHALL render as hard line breaks. On commit, the row's text SHALL be normalized by trimming
trailing blank lines and trailing whitespace while preserving interior newlines.

#### Scenario: Focusing a row hands off from label to input with no jump
- **WHEN** the player clicks into a row to edit it
- **THEN** the live input appears at that row aligned to where the static label was, the row
  stops drawing its own static label, and the text does not visibly shift position or size

#### Scenario: Only one input is live at a time
- **WHEN** the player moves focus from one row to another
- **THEN** the single input is repositioned onto the newly focused row, the previously focused
  row resumes drawing its static label, and at no point are two live inputs present

#### Scenario: Focusing a long wrapped row keeps it wrapped
- **WHEN** the player clicks into a row whose text is long enough to wrap onto multiple lines
- **THEN** the floating input shows the text wrapped across the same number of lines the static
  label showed, at the same row height, rather than collapsing to a single line with text
  running off the left/right edges

#### Scenario: Typing past the line width wraps and grows the row
- **WHEN** the player types in a focused row until the text overflows the row's text width
- **THEN** the text wraps onto a new line within the input, the focused row's height increases to
  fit, the rows below shift down, and the scroll region updates — and deleting the text back
  reverses this (the row shrinks and rows below shift up)

#### Scenario: A growing focused row stays in view
- **WHEN** typing grows the focused row so it would extend past the bottom edge of the scroll
  region
- **THEN** the list scrolls so the focused row (and the caret) remain visible

#### Scenario: Enter commits rather than inserting a newline
- **WHEN** the player presses Enter (without Shift) while editing a wrapped, multi-line row
- **THEN** the row's edit is committed and focus advances to the next row, and no line break is
  inserted into the row's text

#### Scenario: Shift+Enter inserts a hard line break
- **WHEN** the player presses Shift+Enter while editing a row
- **THEN** a line break is inserted at the caret, the row's height grows to fit the new line, and
  focus stays in the row (no commit-and-advance)

#### Scenario: Committed text has trailing blank lines trimmed
- **WHEN** the player commits a row whose text ends in one or more blank lines or trailing
  whitespace (e.g. from a trailing Shift+Enter)
- **THEN** the committed text has its trailing blank lines and whitespace removed, while any
  interior newlines between text are preserved

#### Scenario: Read view renders hard newlines
- **WHEN** a row whose committed text contains interior newlines is shown in the read view
- **THEN** each newline renders as a hard line break, and the row's height reflects the resulting
  line count
