# lectern-gui-shell

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. This is a new capability
covering the skeuomorphic layout and interaction shell of the lectern's GUI dialog.

## Requirements

### Requirement: Lectern dialog uses a portrait, custom-drawn backdrop
The lectern's GUI dialog (both read view and editor view) SHALL be laid out in a portrait
(taller-than-wide) aspect ratio and SHALL render a custom-drawn backdrop image in place of
the engine's default shaded dialog panel.

#### Scenario: Opening the lectern shows a portrait, skinned dialog
- **WHEN** a player right-clicks or shift+right-clicks a placed lectern
- **THEN** the opened dialog is taller than it is wide, and its background is the custom
  backdrop image rather than the default `AddShadedDialogBG` panel

#### Scenario: Backdrop is swappable without a code change
- **WHEN** the placeholder backdrop asset is replaced with a different image (or draw
  routine) of matching dimensions
- **THEN** the dialog renders the new backdrop with no changes required to
  `GuiDialogScribeLectern.cs`'s layout or composition logic

### Requirement: Task/note row list scrolls within a clipped region
The lectern GUI's task/note row list SHALL render inside a scrollable, clipped content
region, so a document with more rows than fit the visible dialog height remains fully
reachable by scrolling, with no content rendered off-screen and unreachable.

#### Scenario: A long document remains fully reachable
- **WHEN** a lectern's document has enough tasks and/or note sections that their combined
  height exceeds the dialog's visible content area
- **THEN** a scrollbar or equivalent scroll interaction appears, and every row remains
  reachable by scrolling — no row is rendered permanently off-screen

#### Scenario: The existing text-size cap stopgap is superseded
- **WHEN** the scrollable region is in place
- **THEN** the text-size slider's upper bound is no longer constrained by the absence of a
  scrollable region (the `MaxTextSizePercent` cap introduced as a stopgap for task 8.15 may
  be revisited, since the original overflow problem it guarded against is now handled by
  scrolling instead)

### Requirement: Row list scrolls continuously; no pagination
The lectern GUI SHALL present the task/note row list as a single continuously scrollable
list. It SHALL NOT split content into discrete fixed-size pages with page-turn navigation.

#### Scenario: No page-turn controls are present
- **WHEN** the lectern's editor or read view is composed
- **THEN** no "Prev"/"Next" page-turn controls or page-count indicator are present in the
  dialog — only the continuous scroll region from the requirement above

### Requirement: Row checkbox scales with the text-size preference
A task row's checkbox SHALL render at a size proportional to the current text-size
preference, in the same proportion as the row's text and height, rather than a fixed
pixel size.

#### Scenario: Checkbox grows and shrinks with text size
- **WHEN** the player changes the text-size preference to a larger or smaller value
- **THEN** every task row's checkbox visibly grows or shrinks along with the row's text,
  rather than staying a constant size while the text around it changes

### Requirement: Row icons are hover-conditional
A row's per-row icon controls (at minimum the delete icon and the pin-toggle icon) SHALL
be visually hidden unless the mouse is currently positioned over that row, rather than
always rendered.

#### Scenario: An icon appears only while hovering its row
- **WHEN** the mouse moves over a task or note row
- **THEN** that row's icon controls become visible, and become hidden again once the
  mouse moves off that row

#### Scenario: Hovering does not disturb active typing
- **WHEN** the mouse moves over a row while the player is actively typing in a different
  row's text field
- **THEN** the typing field's focus and caret position are unaffected by the hover-driven
  visibility change

### Requirement: Focus ring is scoped to the active field
When a text field (a task's text input or a note's text area) has input focus, the GUI
SHALL visually indicate focus on that field specifically, not on the row as a whole.

#### Scenario: Only the focused field is highlighted
- **WHEN** the player clicks into a row's text field to edit it
- **THEN** a focus indicator appears around that field, and no other part of the row
  (its checkbox, icons, or drag handle) is highlighted as focused

### Requirement: Task rows expose a pin-toggle affordance
Each task row in the editor view SHALL provide a control that toggles the task's pinned
flag. Text-section rows SHALL NOT expose this control.

#### Scenario: Toggling pin from the GUI
- **WHEN** the player activates a task row's pin-toggle control
- **THEN** the task's pinned flag flips, and the control's visual state reflects the new
  value

#### Scenario: Text sections have no pin control
- **WHEN** a text-section row is composed
- **THEN** no pin-toggle control is present for that row

### Requirement: No assignment UI in the lectern
The lectern GUI SHALL NOT expose any column, toggle, or other control for a block's
assignment field. The field exists in the underlying document model but has no consumer
in this capability.

#### Scenario: Assignment is not visible or editable from the lectern
- **WHEN** the lectern's editor or read view is composed
- **THEN** no assignment-related column, label, or control appears anywhere in the dialog

### Requirement: Read-view rows are custom-drawn in the interactive render pass
The lectern read view SHALL render each task/note row as a single custom-drawn element in the
interactive render pass (not as static-baked chrome), so that the row list is clipped natively
by the dialog's scroll clip region. No row content SHALL bleed outside the clipped scroll region
at its top or bottom edge.

#### Scenario: Rows are clipped, not culled, at the scroll boundary
- **WHEN** the read view's document has more rows than fit the visible content area and the
  player scrolls so a row straddles the top or bottom edge of the scroll region
- **THEN** that row is drawn partially — clipped exactly at the region boundary — rather than
  popping fully in or out of existence, and no part of any row paints outside the region

#### Scenario: Scrolling is continuous and sub-row
- **WHEN** the player scrolls the read view by any increment (wheel, thumb drag, or track)
- **THEN** the rows slide continuously by the scrolled amount, including partial-row offsets,
  with no snap-to-row-boundary and no full recompose required per scroll step

### Requirement: Read-view rows render a structural lined-paper ruling
Each read-view row SHALL draw a lined-paper ruling as a structural part of the row (drawn per
row and scrolling with the row), rather than relying on separately-baked divider chrome. The
spacing (padding) between the row text and its ruling SHALL scale with the current text-size
preference. The ruling SHALL be authored so its visual can be replaced (e.g. with an image)
without changing the row's layout logic.

#### Scenario: Ruling scrolls with its row
- **WHEN** the player scrolls the read view
- **THEN** each row's ruling moves together with that row's text and checkbox as one unit,
  staying aligned to the row it belongs to

#### Scenario: Ruling padding scales with text size
- **WHEN** the player changes the text-size preference to a larger or smaller value
- **THEN** the padding between a row's text and its ruling grows or shrinks in proportion,
  rather than staying a fixed pixel gap

### Requirement: Read-view checkbox is a custom-drawn glyph
Task rows in the read view SHALL render their checkbox as a custom-drawn glyph rather than the
engine's default `GuiElementSwitch` control. The glyph SHALL continue to scale with the
text-size preference (consistent with the existing checkbox-scaling requirement).

#### Scenario: Checkbox shows done and not-done states
- **WHEN** a task row is drawn in the read view
- **THEN** its checkbox glyph reflects the task's current done state (a checked vs. unchecked
  appearance), drawn by the mod rather than the default engine switch

### Requirement: Read-view checkbox toggles task done state without the editor lock
The read view's task checkbox SHALL be interactive: clicking it toggles that task's done state.
Because the read view holds no editor lock, toggling done SHALL be an always-allowed server
action that does NOT require acquiring the single-editor lock, applied server-authoritatively
and re-synced to all viewers. A player SHALL be able to toggle a task's done state from the read
view even while another player holds the editor lock. No other part of a read-view row SHALL be
interactive — the read view exposes no text editing, drag, or per-row icon controls.

#### Scenario: Clicking a read-view checkbox toggles done
- **WHEN** the player clicks a task row's checkbox in the read view
- **THEN** that task's done state flips, the change is applied server-authoritatively (without
  requiring the editor lock) and synced back, and the checkbox glyph updates to reflect the new
  state

#### Scenario: Toggling done works while someone else is editing
- **WHEN** a player clicks a read-view task checkbox while a different player holds the lectern's
  editor lock
- **THEN** the toggle is still applied and synced, and is not rejected for lack of the lock

#### Scenario: The rest of a read-view row is inert
- **WHEN** the player clicks or hovers a read-view row anywhere other than its checkbox
- **THEN** no edit field opens, no row reorder begins, and no per-row icon control activates

### Requirement: Editor-view rows are custom-drawn in the interactive render pass
The lectern editor view SHALL render each task/note row as the same custom-drawn
`ScribeRowElement` used by the read view (in `ScribeRowMode.Edit`), drawn in the interactive
render pass so the row list is clipped natively by the dialog's scroll clip region. No editor
row content SHALL bleed outside the clipped scroll region at its top or bottom edge, and the
editor view SHALL scroll by a continuous offset shift rather than a per-step recompose.

#### Scenario: Editor rows are clipped, not culled, at the scroll boundary
- **WHEN** the editor view has more rows than fit the visible content area and the player
  scrolls so a row straddles the top or bottom edge of the scroll region
- **THEN** that row is drawn partially — clipped exactly at the region boundary — rather than
  popping fully in or out of existence, and no part of any row (text, checkbox, ruling, or the
  active edit field) paints outside the region

#### Scenario: Editor scrolling is continuous and sub-row
- **WHEN** the player scrolls the editor view by any increment (wheel, thumb drag, or track)
- **THEN** the rows slide continuously by the scrolled amount, including partial-row offsets,
  with no snap-to-row-boundary and no full recompose per scroll step

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

### Requirement: Editor caret conventions match desktop editing idioms cross-platform
The editor's text input SHALL support caret navigation idioms on macOS as well as Windows.
Cmd+Left / Cmd+Right SHALL move the caret to the start / end of the current line; Alt/Option+
Left / Alt/Option+Right SHALL move the caret by whole words; and holding Shift with any caret-
movement key SHALL extend the selection rather than collapse it. These SHALL be provided by a
`GuiElementTextInput` subclass that routes the macOS modifier combinations onto the engine's
existing caret-movement logic (which otherwise responds only to Ctrl and discards Alt).

#### Scenario: Cmd+Arrow jumps to line ends on macOS
- **WHEN** the player presses Cmd+Right (or Cmd+Left) while editing a row on macOS
- **THEN** the caret moves to the end (or start) of the line, matching the behavior Ctrl+Arrow
  already provides, rather than doing nothing

#### Scenario: Alt/Option+Arrow skips by word
- **WHEN** the player presses Alt/Option+Right (or Left) while editing a row
- **THEN** the caret moves by one whole word in that direction rather than being ignored

#### Scenario: Shift extends selection during caret movement
- **WHEN** the player holds Shift while pressing any caret-movement combination (arrow,
  word-skip, or line-end)
- **THEN** the text selection extends to the new caret position instead of collapsing

### Requirement: Editor rows navigate and commit by keyboard
The editor SHALL let the player move between rows from the keyboard while editing. Pressing
Enter SHALL commit the current row's edit and move focus to the next row; pressing Shift+Tab
SHALL commit and move focus to the previous row. Committing an edit (by Enter, Shift+Tab, or
losing focus) SHALL apply the change through the existing lock-gated server edit path. Pressing
Esc SHALL commit the focused row (via the same blur-commit path) and close the dialog — a fast
panic-close, not an in-place revert.

#### Scenario: Enter commits and advances
- **WHEN** the player finishes typing in a row and presses Enter
- **THEN** the row's new text is committed through the server edit path and focus moves to the
  next row

#### Scenario: Shift+Tab commits and retreats
- **WHEN** the player presses Shift+Tab while editing a row
- **THEN** the row's edit is committed and focus moves to the previous row

#### Scenario: Esc commits and closes the dialog
- **WHEN** the player presses Esc while editing a row
- **THEN** the focused row's pending edit is committed (blur-commit fires on close) and the
  dialog closes, rather than reverting the row in place

#### Scenario: Blur commits the edit
- **WHEN** the player clicks away from an actively edited row without pressing Enter
- **THEN** the row's text is committed through the server edit path

### Requirement: Read and editor views share a single row-list width
The lectern's row list SHALL be a single consistent width across both the read view and the
editor view. Switching between views on the same lectern SHALL NOT change the row-list width.

#### Scenario: Row-list width is identical in both views
- **WHEN** the player switches between read and editor view on the same lectern
- **THEN** the row list occupies the same width in both views, with no visible reflow or
  resize of the list column

### Requirement: Custom row-control icons are registered as SVG assets

The mod SHALL register a documented set of custom SVG glyphs for the lectern's row-control
affordances (pin, drag-handle grip, delete/close, edit) at client initialization, each
available to GUI elements by a stable code string. The glyphs SHALL be shipped as SVG assets
under the `textures` asset category and authored as single-flat-color silhouettes so the
drawing caller supplies the ink color.

#### Scenario: The four icons are available by code after client init

- **WHEN** the client has finished `StartClientSide`
- **THEN** the codes `scribepin`, `scribegrip`, `scribeclose`, and `scribeedit` are each
  registered such that a GUI element drawing that code renders the corresponding custom SVG
- **AND** each renders recolored to the color the caller passes (not a baked-in color)

#### Scenario: Icon assets live under a real asset category

- **WHEN** the SVG files are placed in the mod's assets
- **THEN** they reside under `assets/scribe/textures/icons/` (the `textures` category), not a
  bare `icons/` folder that Vintage Story never scans
- **AND** each icon's `AssetLocation` resolves and loads its data via `TryGet`

### Requirement: Custom SVG icon registration survives asset unload

The custom-icon registration SHALL NOT capture a loaded asset object, because Vintage Story
unloads asset data after startup. The registered renderer SHALL re-resolve its asset on each
draw so that an unloaded asset is reloaded on demand, and SHALL degrade to drawing nothing
(never throwing) when the asset cannot be loaded.

#### Scenario: Icon still draws after the game unloads asset data

- **WHEN** an icon is drawn some time after client init (after the engine has unloaded asset
  data)
- **THEN** the icon renders correctly rather than crashing the client

#### Scenario: A missing or unloadable icon asset does not crash

- **WHEN** a registered icon's asset is missing or cannot be loaded at draw time
- **THEN** the renderer draws nothing and the client continues running
- **AND** the failure is logged for diagnosis

### Requirement: Icon registration is decoupled from row-control buttons

Registering the custom icons SHALL NOT, by itself, add or repoint any per-row control button.
The buttons that consume these codes are owned by separate changes; this capability only makes
the codes available.

#### Scenario: Registration adds no interactive controls

- **WHEN** the icons are registered but no consuming change has run
- **THEN** the live row's interactive behavior is unchanged (checkbox + text + ruling only)
- **AND** no button is wired to a code string that would otherwise be dead code
