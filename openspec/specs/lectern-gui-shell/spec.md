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

### Requirement: Editor rows reserve a drag-handle affordance column
Each editor-view row SHALL reserve a drag-handle (grip) affordance column and render a grip control
in it, so the row exposes a visible grab point for reordering. This column SHALL be present in the
editor view only (the read view exposes no per-row controls beyond the checkbox). The grip's width,
like the row's other affordance columns, SHALL scale with the text-size preference rather than
staying a fixed size. Providing the actual drag-to-reorder *interaction feedback* (a lift-ghost,
insertion indicator, or drop-settle animation) is out of scope for this requirement — this requires
only that the column and its grip control exist.

#### Scenario: Editor rows show a grip control
- **WHEN** a row is composed in the editor view
- **THEN** a drag-handle grip control is present in a reserved column for that row

#### Scenario: Read view rows have no grip control
- **WHEN** a row is composed in the read view
- **THEN** no drag-handle grip control is present for that row

#### Scenario: The grip column scales with text size
- **WHEN** the text-size preference is changed
- **THEN** the reserved grip column's width scales with it, consistent with the row's other
  affordance columns and checkbox

### Requirement: No assignment UI in the lectern
The lectern GUI SHALL NOT expose any column, toggle, or other control for a block's
assignment field. The field exists in the underlying document model but has no consumer
in this capability.

#### Scenario: Assignment is not visible or editable from the lectern
- **WHEN** the lectern's editor or read view is composed
- **THEN** no assignment-related column, label, or control appears anywhere in the dialog

### Requirement: Read view is rendered by a LibGUI dialog
The lectern's read view SHALL be rendered by a dialog built on the LibGUI framework (modid `gui`),
subclassing LibGUI's `GuiDialogBlockEntityBase`, rather than by the native `GuiComposer`-based
`GuiDialogScribeLectern` read view. The dialog SHALL open from the normal lectern interaction path and
receive its document state through the existing server-authoritative flow (the `scribe` network channel
and its packets), NOT by directly reusing an in-memory `Document` reference and NOT via any debug/chat
command. The dialog's block-entity lifecycle — open, close via the window's close control, title-bar
drag, and minimize/expand — SHALL work as the native dialog's did.

#### Scenario: Right-clicking a lectern opens the LibGUI read view
- **WHEN** a player interacts with a placed lectern to view it
- **THEN** a LibGUI-rendered dialog opens showing the lectern's document, populated from the
  server-synced document state
- **AND** the dialog can be closed, dragged by its title bar, and minimized/expanded

#### Scenario: No debug command is involved in the real open path
- **WHEN** the read view opens in normal play
- **THEN** it opens through the lectern's interaction + packet flow, not through a `.scribespike` (or any
  other) chat command, and it does not depend on the throwaway spike dialog

### Requirement: Read view renders the document as a scrollable widget tree
The read view SHALL render the document as a LibGUI widget tree — a window frame containing a free-text
section and a scrollable list of task/note rows — laid out declaratively (flex/`Column`/`ListView`/`Row`)
rather than by absolute-bounds composition. A document with more rows than fit the visible height SHALL
remain fully reachable by scrolling the list, with no row rendered permanently off-screen and no row
content painting outside the scroll viewport. The list SHALL scroll continuously (no page-turn
navigation).

#### Scenario: A long document remains fully reachable
- **WHEN** a lectern's document has more tasks and/or note sections than fit the visible content area
- **THEN** the row list scrolls, and every row remains reachable by scrolling — no row is rendered
  permanently off-screen, and no row paints outside the scroll viewport

#### Scenario: No page-turn controls are present
- **WHEN** the read view is rendered
- **THEN** the row list is a single continuously scrollable list with no "Prev"/"Next" page-turn
  controls or page-count indicator

### Requirement: Read-view rows are self-stateful and keyed
Because LibGUI's `ListView` caches its child widgets by index and does not rebuild them when the parent
calls `SetState`, each interactive read-view row SHALL be a self-stateful widget that manages its own
visual state, and rows SHALL carry a stable `ValueKey` identity so the list can track them across
document changes and (in later changes) reorders. A row SHALL NOT depend on the parent rebuilding it to
reflect its own state changes.

#### Scenario: A row reflects its own state change without a parent rebuild
- **WHEN** a read-view row's interactive control changes that row's displayed state (e.g. its checkbox
  is clicked)
- **THEN** the row updates its own display via its own state, without relying on the parent list
  rebuilding it

### Requirement: Read view switches to editing via the existing native editor
While the editor view is not yet migrated to LibGUI, the LibGUI read view SHALL remain read-only and
SHALL provide a control that switches to editing by opening the existing native `GuiDialogScribeLectern`
editor view. Switching between viewing and editing SHALL keep full edit functionality available (this is
an interim seam; a later change replaces the native editor with a LibGUI editor view).

#### Scenario: Switching to editor opens the working native editor
- **WHEN** the player activates the read view's "switch to editor" control
- **THEN** the existing native editor view opens with full editing functionality (unchanged from before
  the migration)

### Requirement: Read-view checkbox toggles task done state without the editor lock
The read view's task checkbox SHALL be interactive: clicking it toggles that task's done state.
Because the read view holds no editor lock, toggling done SHALL be an always-allowed server
action that does NOT require acquiring the single-editor lock, applied server-authoritatively
and re-synced to all viewers. A player SHALL be able to toggle a task's done state from the read
view even while another player holds the editor lock. No other part of a read-view row SHALL be
interactive — the read view exposes no text editing, drag, or per-row icon controls. The checkbox
MAY be rendered with LibGUI's stock checkbox widget; its skeuomorphic custom-glyph appearance is not
required by this requirement.

#### Scenario: Clicking a read-view checkbox toggles done
- **WHEN** the player clicks a task row's checkbox in the read view
- **THEN** that task's done state flips, the change is applied server-authoritatively (without
  requiring the editor lock) and synced back, and the checkbox updates to reflect the new state

#### Scenario: Toggling done works while someone else is editing
- **WHEN** a player clicks a read-view task checkbox while a different player holds the lectern's
  editor lock
- **THEN** the toggle is still applied and synced, and is not rejected for lack of the lock

#### Scenario: The rest of a read-view row is inert
- **WHEN** the player clicks or hovers a read-view row anywhere other than its checkbox
- **THEN** no edit field opens, no row reorder begins, and no per-row icon control activates

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
- **WHEN** the player holds Shift while pressing any caret-movement combination (arrow,
  word-skip, or line-end)
- **THEN** the text selection extends to the new caret position instead of collapsing

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

### Requirement: Read and editor views share a single row-list width
The lectern's row list SHALL be a single consistent width across both the read view and the
editor view. Switching between views on the same lectern SHALL NOT change the row-list width.

In addition, a task row SHALL occupy the same vertical space in the read view and the editor
view: for a single-line task, the read-view row and the editor-view row SHALL have identical
rendered height, and each task SHALL remain at the same vertical position when the player
switches views on the same lectern. This parity SHALL be achieved by unifying the row font
size, vertical alignment, per-row padding, and inter-row spacing between the two views. The
read-view row SHALL NOT draw a text-field border, while the editor-view row's field border
(drawn inside its existing internal padding) SHALL NOT change the row's height. Multi-line
rows are best-effort: they need not be pixel-identical when the read and editor wrap widths
or field chrome differ.

#### Scenario: Row-list width is identical in both views
- **WHEN** the player switches between read and editor view on the same lectern
- **THEN** the row list occupies the same width in both views, with no visible reflow or
  resize of the list column

#### Scenario: A single-line task keeps its position across a view switch
- **WHEN** the player switches between read and editor view on a lectern whose tasks each fit
  on a single line
- **THEN** each task's row occupies the same vertical height and the same vertical position in
  both views, so no task visibly jumps or shifts when the view changes

#### Scenario: Read-view rows have no border while matching the editor field's box
- **WHEN** a task row is shown in the read view
- **THEN** it draws no text-field border
- **AND** its text is inset vertically and horizontally to match the editor field's internal
  padding, so the text's top edge and left edge align with the editor field's text across a
  view switch

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

### Requirement: Lectern row sizing is sourced from client configuration
The LibGUI lectern dialog SHALL source its task-row sizing values (row font size, per-row
vertical and horizontal padding, checkbox size, checkbox-to-text gap, and the editor field's
internal horizontal and vertical padding) from a `ScribeClientConfig` instance loaded from
the client config file, rather than from hardcoded literals. The dialog SHALL load this
config when it is opened, so that editing the config file on disk and reopening the lectern
applies the new values. When no config file exists, the dialog SHALL fall back to built-in
defaults without error.

#### Scenario: Editing the config file and reopening applies new sizing
- **WHEN** the player edits a row-sizing value in the client config file and then opens the
  lectern
- **THEN** the lectern's rows render at the edited size

#### Scenario: Missing config file falls back to defaults
- **WHEN** the client config file does not exist and the player opens the lectern
- **THEN** the lectern opens normally using built-in default sizing values, with no error

### Requirement: Row sizing scales through a single factor
The lectern's scalable row-sizing values SHALL be derived by multiplying their configured
base values by a single client-side text-size scale factor, applied at one place before the
values reach the row widgets. With the scale factor at its default of `1.0`, the rendered
sizes SHALL equal the configured base values (a no-op).

#### Scenario: Default scale reproduces the configured base sizes
- **WHEN** the text-size scale factor is at its default value of `1.0`
- **THEN** each row's font size and scalable padding equal the configured base values

#### Scenario: A non-default scale multiplies the sizes uniformly
- **WHEN** the text-size scale factor is set to a value other than `1.0`
- **THEN** the scalable row-sizing values are multiplied by that factor for both the read and
  editor views

### Requirement: Row-sizing config is exposable via ConfigLib without a hard dependency
The mod SHALL expose its row-sizing configuration fields through ConfigLib's in-game settings
panel via a no-code manifest that reads and writes the same client config file. Every exposed
setting SHALL be declared as a floating-point type. The mod SHALL NOT declare a hard
dependency on ConfigLib: when ConfigLib is not installed, the lectern SHALL load and function
normally and the manifest SHALL simply go unread.

#### Scenario: Fields are tunable in the ConfigLib panel when ConfigLib is present
- **WHEN** ConfigLib is installed and the player edits a row-sizing field in its settings
  panel and saves
- **THEN** the value is written to the client config file and the next lectern open renders at
  the new size

#### Scenario: Mod works without ConfigLib installed
- **WHEN** ConfigLib is not installed
- **THEN** the mod loads and the lectern opens normally, with no missing-dependency warning
  and no reliance on ConfigLib being present
