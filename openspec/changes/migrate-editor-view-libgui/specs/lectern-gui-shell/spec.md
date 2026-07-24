## ADDED Requirements

### Requirement: Editor view is rendered by the LibGUI dialog
The lectern's editor view SHALL be rendered by the same LibGUI dialog that renders the read view
(`GuiDialogScribeLecternLibGui`), NOT by the native `GuiComposer`-based `GuiDialogScribeLectern`.
Switching between the read view and the editor view SHALL be an internal view swap within that single
dialog — no separate native dialog SHALL be opened for editing. Returning from the editor view (by
finishing editing) SHALL return to the LibGUI read view, and entering the editor view SHALL acquire the
lectern's single-editor lock through the existing server flow, releasing it when the editor view is left.

#### Scenario: Switching to editor stays in the LibGUI dialog
- **WHEN** the player activates "switch to editor" from the LibGUI read view
- **THEN** the same dialog swaps to an editor view rendered on LibGUI, and no native editor dialog opens

#### Scenario: Finishing editing returns to the LibGUI read view
- **WHEN** the player finishes editing and leaves the editor view
- **THEN** the dialog returns to the LibGUI read view (not a native read view), and the editor lock is
  released

#### Scenario: Entering the editor acquires the editor lock
- **WHEN** the editor view is entered
- **THEN** the single-editor lock is acquired through the existing server flow, and is released when the
  editor view is left

### Requirement: Editor rows are editable multi-line LibGUI widgets
The editor view SHALL render each task/note row as a self-stateful, `ValueKey`-keyed LibGUI widget
containing (for a task) a checkbox and an editable multi-line text field. The field SHALL wrap long text
onto multiple lines at the row's text width rather than scrolling a single line horizontally, and the
focused row's height SHALL grow or shrink dynamically as wrapped-line count changes so the row behaves
like a static wrapped row; rows below SHALL shift and the scroll region SHALL update accordingly. Exactly
one field SHALL be actively editing at a time; the focused row SHALL remain scrolled into view as it grows,
preserving caret position and focus across the height change. The row list SHALL scroll continuously within
the dialog's scroll viewport with no row painting outside it and no page-turn navigation.

#### Scenario: Typing past the line width wraps and grows the row
- **WHEN** the player types in a focused editor row until the text overflows the row's text width
- **THEN** the text wraps onto a new line within the field, the focused row's height increases to fit, the
  rows below shift down, and the scroll region updates — and deleting the text back reverses this

#### Scenario: A growing focused row stays in view
- **WHEN** typing grows the focused row so it would extend past the bottom edge of the scroll region
- **THEN** the list scrolls so the focused row and the caret remain visible

#### Scenario: Only one row edits at a time
- **WHEN** the player moves focus from one editor row to another
- **THEN** editing becomes active on the newly focused row's field and the previously focused row is no
  longer being edited, and at no point are two rows actively editing

#### Scenario: The editable row list scrolls within the viewport
- **WHEN** the editor view has more rows than fit the visible content area and the player scrolls
- **THEN** every row remains reachable by scrolling, no row paints outside the scroll viewport, and there
  are no page-turn controls

### Requirement: Editor input captures keystrokes while focused
While an editor row's text field holds focus, the dialog SHALL capture keyboard input so that typed keys
edit the field and do NOT leak through to the game (e.g. player movement, hotbar selection, or other
keybinds). Releasing focus (leaving the editor view or committing out of all fields) SHALL restore normal
key handling.

#### Scenario: Typing does not trigger game keybinds
- **WHEN** the player types letters that also match game keybinds (e.g. movement keys) while an editor
  field is focused
- **THEN** the characters are inserted into the field and the game does not act on them (the player does
  not move, the hotbar does not change)

#### Scenario: Focus release restores game input
- **WHEN** the player leaves the editor view or no field is focused
- **THEN** keyboard input is no longer captured by the dialog and normal game key handling resumes

## MODIFIED Requirements

### Requirement: Editor rows navigate and commit by keyboard
The editor SHALL let the player move between rows and add rows from the keyboard while editing. Pressing
Tab (without Shift) SHALL commit the current row's edit and move focus to the next row WITHOUT inserting a
tab glyph; pressing Shift+Tab SHALL commit and move focus to the previous row. Pressing Enter (without
Shift) SHALL commit the current row's edit and insert a NEW task directly beneath it, moving focus to that
new row, WITHOUT inserting a line break into the current row; pressing Shift+Enter SHALL instead insert a
hard line break into the row's text (growing the row) rather than committing. Committing an edit (by Tab,
Shift+Tab, Enter, or losing focus) SHALL apply the change through the existing lock-gated server edit path
(`ScribeEditDocumentMessage`), server-authoritatively. Pressing Esc SHALL commit the focused row (via the
same blur-commit path) and close the dialog — a fast panic-close, not an in-place revert. On commit, the
row's text SHALL be normalized by trimming trailing blank lines and trailing whitespace while preserving
interior newlines, and the read view SHALL render those interior newlines as hard line breaks.

#### Scenario: Tab commits and advances
- **WHEN** the player finishes typing in a row and presses Tab (without Shift)
- **THEN** the row's new text is committed through the server edit path and focus moves to the next row,
  and no tab glyph is inserted into the row's text

#### Scenario: Enter commits and inserts a new task below
- **WHEN** the player presses Enter (without Shift) while editing a row
- **THEN** the row's edit is committed, a new empty task is inserted directly beneath it, focus moves to
  that new task, and no line break is inserted into the original row's text

#### Scenario: Shift+Enter inserts a hard line break
- **WHEN** the player presses Shift+Enter while editing a row
- **THEN** a line break is inserted at the caret, the row's height grows to fit the new line, and focus
  stays in the row (no commit, no new row)

#### Scenario: Shift+Tab commits and retreats
- **WHEN** the player presses Shift+Tab while editing a row
- **THEN** the row's edit is committed and focus moves to the previous row

#### Scenario: Esc commits and closes
- **WHEN** the player presses Esc while editing a row
- **THEN** the focused row's edit is committed through the server path and the dialog closes (not an
  in-place revert)

#### Scenario: Committed text has trailing blank lines trimmed
- **WHEN** the player commits a row whose text ends in one or more blank lines or trailing whitespace
- **THEN** the committed text has its trailing blank lines and whitespace removed, while any interior
  newlines between text are preserved

### Requirement: Editor caret conventions match desktop editing idioms cross-platform
The editor's text field SHALL support desktop caret-navigation idioms on macOS as well as Windows.
Cmd+Left / Cmd+Right SHALL move the caret to the start / end of the current line; Alt/Option+Left /
Alt/Option+Right SHALL move the caret by whole words; and holding Shift with any caret-movement key SHALL
extend the selection rather than collapse it. These conventions SHALL be provided by the LibGUI editor
field itself (the mechanism is no longer required to be a `GuiElementTextInput` subclass), so that the
macOS modifier combinations are honored rather than ignored.

#### Scenario: Cmd+Arrow jumps to line ends on macOS
- **WHEN** the player presses Cmd+Right (or Cmd+Left) while editing a row on macOS
- **THEN** the caret moves to the end (or start) of the line rather than doing nothing

#### Scenario: Alt/Option+Arrow skips by word
- **WHEN** the player presses Alt/Option+Right (or Left) while editing a row
- **THEN** the caret moves by one whole word in that direction rather than being ignored

#### Scenario: Shift extends selection during caret movement
- **WHEN** the player holds Shift while pressing any caret-movement combination (arrow, word-skip, or
  line-end)
- **THEN** the text selection extends to the new caret position instead of collapsing

## REMOVED Requirements

### Requirement: Editor-view rows are custom-drawn in the interactive render pass
**Reason**: This requirement mandated the native mechanism (custom `ScribeRowElement` drawn in the
engine's interactive render pass in `ScribeRowMode.Edit`, clipped by the engine's native scroll-clip
region, scrolled by a continuous offset shift). Under LibGUI the editor view is a declarative widget tree
whose `ListView` viewport does the clipping and continuous scrolling. The observable behavior it protected
— a long editable document stays fully reachable by scrolling with no content painting outside the scroll
region — is preserved by the new requirement "Editor rows are editable multi-line LibGUI widgets."
**Migration**: None for players. The native `ScribeRowElement` editor path is superseded by the LibGUI
`ListView`/`Row` widget tree; the native editor is retired by this change.

### Requirement: Editor view edits in place with a single floating input
**Reason**: This requirement mandated the native mechanism (exactly one live `GuiElementTextInput`
repositioned onto the focused row, non-focused rows drawing static labels, focused row suppressing its
label, alignment via the shared `RowTextLayout` metric, and the label↔input handoff with no visible
shift). Under LibGUI each row owns its own editable field widget, so there is no single floating input to
reposition and no label/input handoff to align. The observable behaviors it protected — wrapping,
dynamic row growth/shrink, keep-focused-row-in-view, one active editor at a time, Enter=commit /
Shift+Enter=break with trailing-blank-line normalization — are preserved by the new requirements "Editor
rows are editable multi-line LibGUI widgets" and the modified "Editor rows navigate and commit by
keyboard."
**Migration**: None for players. The single-floating-input mechanism is superseded by per-row LibGUI
editable fields; edits still commit through the same lock-gated server path with the same semantics.
