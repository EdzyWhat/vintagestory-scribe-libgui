## MODIFIED Requirements

### Requirement: Editor rows are editable multi-line LibGUI widgets
The editor view SHALL render each task/note row as a self-stateful, `ValueKey`-keyed LibGUI widget
containing (for a task) a checkbox and an editable multi-line text field. The field SHALL wrap long text
onto multiple lines at the row's text width rather than scrolling a single line horizontally, and the
focused row's height SHALL grow or shrink dynamically as wrapped-line count changes so the row behaves
like a static wrapped row; rows below SHALL shift and the scroll region SHALL update accordingly. Exactly
one field SHALL be actively editing at a time. The dialog SHALL keep the editing **caret** scrolled into
view — following the caret rather than the whole row: when an edit or a caret move would place the caret
outside the scroll viewport, the list SHALL scroll the minimum needed to bring the caret back into view
(top-aligning the caret if it is above the viewport, bottom-aligning it if below), and a caret already
inside the viewport SHALL NOT cause any scroll. This SHALL hold even when the focused row is taller than
the viewport, with no per-keystroke oscillation of the scroll position. Caret position and focus SHALL be
preserved across the height change. The row list SHALL scroll continuously within the dialog's scroll
viewport with no row painting outside it and no page-turn navigation.

#### Scenario: Typing past the line width wraps and grows the row
- **WHEN** the player types in a focused editor row until the text overflows the row's text width
- **THEN** the text wraps onto a new line within the field, the focused row's height increases to fit, the
  rows below shift down, and the scroll region updates — and deleting the text back reverses this

#### Scenario: A growing focused row keeps the caret in view
- **WHEN** typing grows the focused row so the caret would extend past the bottom edge of the scroll region
- **THEN** the list scrolls just enough that the caret (and the line it sits on) remains visible, and no
  more

#### Scenario: A row taller than the viewport does not bounce
- **WHEN** the focused row is taller than the scroll viewport and the player types additional characters
- **THEN** the scroll position follows the caret and stays stable — it does NOT alternate between the top
  and bottom of the row on successive keystrokes

#### Scenario: Caret already visible does not scroll
- **WHEN** the player types or moves the caret while the caret is already within the visible viewport
- **THEN** the scroll position does not change

#### Scenario: Keyboard navigation follows the caret
- **WHEN** the player moves the caret with the arrow keys, Tab / Shift+Tab, or Enter (advancing or
  retreating between rows) such that the caret would land outside the viewport
- **THEN** the list scrolls the minimum needed to bring the caret back into view

#### Scenario: Document-nav shortcut jumps to the first/last row
- **WHEN** the player presses the document-top shortcut (Cmd+Up on macOS, or Ctrl+Up / Ctrl+Home on
  Windows) or the document-bottom shortcut (Cmd+Down, or Ctrl+Down / Ctrl+End) in a focused editor row
- **THEN** focus moves to the first (or last) row of the document with the caret at that row's start
  (or end), the row being left is committed, and the list scrolls so the newly focused caret is in view
  — while plain Up/Down (no modifier) still moves the caret one visual line within the row, Home/End
  still move to the current line's start/end, and Alt/Option+Up/Down is a plain one-line move

#### Scenario: Only one row edits at a time
- **WHEN** the player moves focus from one editor row to another
- **THEN** editing becomes active on the newly focused row's field and the previously focused row is no
  longer being edited, and at no point are two rows actively editing

#### Scenario: The editable row list scrolls within the viewport
- **WHEN** the editor view has more rows than fit the visible content area and the player scrolls
- **THEN** every row remains reachable by scrolling, no row paints outside the scroll viewport, and there
  are no page-turn controls
