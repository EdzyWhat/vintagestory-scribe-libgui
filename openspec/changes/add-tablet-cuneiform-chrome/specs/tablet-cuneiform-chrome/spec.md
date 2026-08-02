## ADDED Requirements

### Requirement: Tablet task rows are a live cuneiform input surface

The tablet dialog SHALL render its editable task rows as a live cuneiform input surface when cuneiform
is enabled: as the player types, edits, or deletes text, the row's content SHALL render as cuneiform
strokes in place (not as normal font that converts on blur). Each focused row SHALL display a
**synthetic caret** — a drawn bar positioned at the current character boundary — because cuneiform has
no native caret. The synthetic caret SHALL **blink** at the same cadence as the normal editable field
(a static, non-blinking caret is not sufficient). The row SHALL reuse the existing multiline-field text
buffer and keyboard handling (typing, backspace/delete, arrow-key caret navigation, and **Shift+Enter to
insert a line break**), driving a cuneiform renderer instead of the normal text renderer. A **trailing
space** typed at the end of the text SHALL advance the caret immediately (the caret position SHALL NOT
wait for a following non-space character). Clicking within a row SHALL place the caret at the nearest
character boundary. This live-cuneiform input SHALL be tablet-only and SHALL NOT affect the Lectern or
Notebook editor.

#### Scenario: Typing renders live cuneiform in a row

- **WHEN** a player focuses a tablet task row (cuneiform enabled) and types
- **THEN** the typed text appears as cuneiform strokes in the row as it is entered, with a caret bar at
  the current character boundary

#### Scenario: Caret navigation and click-to-place work over cuneiform

- **WHEN** a player presses the left/right arrow keys or clicks within a cuneiform row
- **THEN** the caret moves to the corresponding character boundary, positioned against the cuneiform
  glyph advances

#### Scenario: The caret blinks while a row is focused

- **WHEN** a cuneiform row is focused and idle
- **THEN** its synthetic caret blinks at the same cadence as the normal editable field, rather than
  rendering as a static bar

#### Scenario: Shift+Enter inserts a line break

- **WHEN** a player presses Shift+Enter while editing a cuneiform row
- **THEN** a line break is inserted at the caret and the row grows to a new wrapped line, matching the
  normal field's Shift+Enter behavior

#### Scenario: A trailing space advances the caret immediately

- **WHEN** a player types a space at the end of a cuneiform row's text
- **THEN** the caret advances by the space's width immediately, without waiting for a following
  non-space character

#### Scenario: Editing persists exactly as before

- **WHEN** a player edits a tablet row's text and the edit commits
- **THEN** the underlying task text is updated and saved exactly as it is for the normal editor, because
  only the row's rendering (and caret) differs — the buffer and save path are unchanged

#### Scenario: Incumbent editors are unaffected

- **WHEN** a player edits a task row in the Lectern or a Notebook
- **THEN** the row renders and behaves in the normal font, unchanged by this capability

### Requirement: A focused tablet row changes appearance without swapping widgets

A tablet task row SHALL be borderless with a transparent background at rest and SHALL gain a visible
border and background when it is clicked or focused, driven by the row's already-present container
styling (not by swapping the row to a different widget type). The cuneiform glyph rendering SHALL
remain active in both the resting and focused states — only the border/background appearance changes.
Row appearance colors SHALL be taken from the resolved theme.

#### Scenario: Focus reveals the row frame

- **WHEN** a player clicks or focuses a tablet task row
- **THEN** the row shows a border and a visible background, and reverts to borderless/transparent when
  it loses focus, with the text remaining cuneiform throughout

### Requirement: The tablet title bar is a live cuneiform input

The tablet dialog SHALL render the title bar as a live cuneiform input when cuneiform is enabled: the
title text SHALL render as cuneiform strokes both at rest and while being typed, with a synthetic
caret while editing. The title's pencil-to-edit, focus, and blur/Enter/Escape commit-and-save
machinery SHALL remain intact and unchanged — only the rendering (and caret) becomes cuneiform. The
Lectern and Notebook dialogs SHALL be unaffected and continue rendering their title bars in the normal
font.

#### Scenario: Title types and rests in cuneiform

- **WHEN** a player opens a tablet with cuneiform enabled and edits the title
- **THEN** the title renders as cuneiform strokes while typing (with a caret) and at rest, and
  committing (blur, Enter, or Escape) saves it exactly as it does today

#### Scenario: Incumbent dialogs keep the normal title font

- **WHEN** a player opens the Lectern or a Notebook
- **THEN** its title bar renders in the normal font, unchanged by this capability

### Requirement: The tablet button labels render in the cuneiform font

The tablet dialog SHALL render its button labels (e.g. "Add task") in the cuneiform glyph font when
cuneiform is enabled, under the same single fallback branch as the title and rows. A cuneiform button
label SHALL be sized to read at the same rendered height as the adjacent readable-text buttons — the
label SHALL NOT render visibly shorter than the sibling info/gear buttons in the same footer row.

#### Scenario: Button labels render cuneiform

- **WHEN** a player opens a tablet with cuneiform enabled
- **THEN** the button labels are rendered as cuneiform strokes

#### Scenario: Cuneiform labels match the sibling buttons' height

- **WHEN** the footer renders a cuneiform "Add task" label beside the info and gear buttons
- **THEN** the cuneiform label reads at the same rendered height as those buttons, not noticeably
  smaller

### Requirement: The tablet footer has a settings gear button beside the info button

The tablet dialog's footer action row SHALL include a settings gear button positioned immediately to
the right of the Information button and styled identically to it (same icon treatment, size, padding,
and color, using the registered gear icon). Activating it SHALL open the Scribe Settings window.

#### Scenario: Gear button opens settings

- **WHEN** a player clicks the settings gear button in the tablet footer
- **THEN** the Scribe Settings window opens

#### Scenario: Gear button matches the info button styling

- **WHEN** the tablet footer renders
- **THEN** the gear button appears immediately right of the Information button, styled identically to it

### Requirement: Scribe Settings stays in the normal readable font

The cuneiform treatment SHALL stop at the tablet page. The Scribe Settings window SHALL always render
in the normal readable font regardless of the tablet's cuneiform state.

#### Scenario: Settings is legible even with cuneiform enabled

- **WHEN** a player opens Scribe Settings while cuneiform is enabled
- **THEN** every settings label and control renders in the normal readable font

### Requirement: The tablet remains one material-keyed parameterized type

The cuneiform-chrome rendering SHALL be material-agnostic. The tablet SHALL remain ONE parameterized
type keyed by a material texture variable (differing only in backdrop and ink color), and this change
SHALL NOT introduce a subclass or code path that branches on the tablet's material. Ink color for the
cuneiform surfaces SHALL be read from the resolved tablet theme, not from a per-material branch.

#### Scenario: Cuneiform chrome does not branch on material

- **WHEN** the tablet renders its cuneiform title, rows, and buttons for any material variant
- **THEN** the same code path is used for every material, taking only the ink color from the resolved
  theme
