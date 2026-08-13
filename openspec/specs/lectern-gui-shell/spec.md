# lectern-gui-shell

## Purpose

TBD - created via spec sync from change `skeuomorphic-lectern-gui`. This is a new capability
covering the skeuomorphic layout and interaction shell of the lectern's GUI dialog.
## Requirements
### Requirement: Lectern dialog uses a portrait, custom-drawn backdrop
The lectern's GUI dialog (both read view and editor view) SHALL be an art-sized outer box (the
`OuterArtBox`) whose width is the layout's driving width `W` and whose height matches the backdrop art's
aspect ratio (`H = W × 1160/1024`), rendering the custom-drawn backdrop image filling that box without
distortion in place of the engine's default shaded dialog panel. The dialog window SHALL be sized to the
`OuterArtBox` and SHALL be non-resizable so the art cannot be stretched off-aspect. The functional views
(read and editor) SHALL be laid out INSIDE the `OuterArtBox` so the backdrop art frames the functional
content rather than being filled edge to edge by it. When the backdrop preference is OFF, the box SHALL be
used without the texture (the existing fallback), and when the art asset is missing it SHALL fall back to a
flat placeholder color.

#### Scenario: Opening the lectern shows a portrait, skinned dialog
- **WHEN** a player right-clicks or shift+right-clicks a placed lectern with the backdrop enabled
- **THEN** the opened dialog is taller than it is wide, its background is the custom backdrop image
  rendered without distortion (not stretched off its native aspect ratio), and the functional read/editor
  content is laid out inside the box with the backdrop art visible framing it

#### Scenario: Backdrop is swappable without a code change
- **WHEN** the backdrop asset is replaced with a different image of the SAME aspect ratio
- **THEN** the dialog renders the new backdrop with no changes required to the dialog's layout or
  composition logic

#### Scenario: The window is not resizable
- **WHEN** the player attempts to resize the lectern dialog window
- **THEN** the window does not resize, so the backdrop art's aspect ratio (and therefore its
  distortion-free rendering) is preserved

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
SHALL visually indicate focus on that field specifically, not on the row as a whole. This
SHALL hold on every editor path, including the tablet cuneiform path — the focus border,
fill, and corner treatment SHALL be drawn around the focused input element, never around
the whole row `Container`. When the focused row is also pinned, the focus indicator SHALL
remain visually distinct from the pinned-row wash (a smaller, differently-shaped input
highlight inside the row's pinned tint), so the two states are never the same shape.

#### Scenario: Only the focused field is highlighted
- **WHEN** the player clicks into a row's text field to edit it
- **THEN** a focus indicator appears around that field, and no other part of the row
  (its checkbox, icons, or drag handle) is highlighted as focused

#### Scenario: Focused input on a pinned cuneiform row stays distinct from the pinned wash
- **WHEN** the player focuses the text input of a pinned task row on the tablet (cuneiform)
  path, where the row already carries the pinned-row tint
- **THEN** the focus indicator is drawn only around the input element (not the whole row),
  so the input's focus highlight and the row's pinned wash read as two distinct shapes
  rather than one ambiguous whole-row fill

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
staying a fixed size. Dragging the grip SHALL reorder the row: while a drag is in progress the GUI
SHALL indicate the drag by transforming the grip glyphs rather than by washing the row backgrounds —
the grabbed (source) row's grip SHALL become a left-pointing indicator and that row SHALL be dimmed
to read as lifted, every non-participating row's grip SHALL be hidden, and the row the pointer is
currently over (the prospective drop) SHALL show a right-pointing indicator in its grip. Releasing
SHALL move the row to that position via the document's reorder path (a move that extracts the row and
reinserts it, not a swap). A drag released on the row's original position SHALL make no change. Drag
feedback SHALL NOT be drawn as a row-background wash, so it never collides with the pinned-row
highlight; a pinned row SHALL continue to show its pinned highlight even while a drag is in progress.
The grip column's reserved width SHALL NOT change when its glyph is hidden or swapped, so mid-drag
feedback does not reflow the row. This drag-feedback behavior SHALL apply equally to every
reorderable row surface (the editor view and the Pin Tab), which share the same drag mechanism.

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

#### Scenario: Dragging the grip reorders the row with drop feedback
- **WHEN** the player presses a row's grip and moves the pointer over a different row
- **THEN** the grabbed row's grip shows a left-pointing indicator and that row is dimmed, every other
  non-target row's grip is hidden, and the row under the pointer shows a right-pointing indicator
  marking where the row will land, and on release the row moves to that position

#### Scenario: Drag feedback does not collide with the pinned wash
- **WHEN** the player drags a row over a pinned row
- **THEN** the pinned row keeps its pinned highlight and the drop position is shown by the
  right-pointing grip indicator, with no row-background drag wash drawn that could be mistaken for
  the pinned highlight

#### Scenario: Releasing in place is a no-op
- **WHEN** the player begins a grip drag and releases it on the row's original position
- **THEN** no reorder occurs and no edit is sent

### Requirement: Editor rows expose a working delete control
Each editor-view row SHALL provide a delete control that removes that block from the document
through the server-authoritative edit path. The control SHALL be a real action (not a reserved
column or a logging stub). Deleting the row the player is currently editing SHALL commit or
discard that row's in-progress edit safely (no crash, no orphaned focus on a removed row). When a
row is deleted, its height SHALL collapse smoothly to zero in place — so the rows below move up to
fill the space — and the row SHALL be removed from the list only after that collapse completes.
While it collapses, the departing row SHALL be shown as a non-interactive snapshot (it holds no
edit focus). Any re-clamp of the scroll position to the shrunken list SHALL be deferred until the
collapse completes, so it does not fight the collapsing row's changing height.

#### Scenario: Delete control removes the row
- **WHEN** the player activates a row's delete control
- **THEN** that block is removed from the document and the row disappears from the list

#### Scenario: Deleting the focused row does not break focus
- **WHEN** the player deletes the row that currently holds edit focus
- **THEN** the editor does not crash and focus is not left pointing at the removed row

#### Scenario: A deleted row collapses before it leaves
- **WHEN** the player activates a row's delete control
- **THEN** the row's height collapses smoothly to zero in place and the rows below move up to meet
  it, and the row is removed from the list only after that collapse finishes

#### Scenario: Deleting the bottom row does not leave dead scroll space
- **WHEN** the list is scrolled to the bottom and the player deletes the last row
- **THEN** the row collapses and the viewport settles onto the shortened list without a dead-space
  flash, because the scroll re-clamp waits until the collapse completes

### Requirement: Pinned tasks show a resting indicator
A pinned task SHALL be visually distinguishable at rest — without hovering the row — in both the
read view and the editor view, so a pin toggled via the (hover-conditional) pin control remains
legible after the mouse leaves the row. Unpinned tasks and text-section rows SHALL show no such
indicator.

#### Scenario: A pinned task reads as pinned without hovering
- **WHEN** a task is pinned and the mouse is not over its row
- **THEN** the row shows a resting indicator distinguishing it from unpinned rows, in both views

#### Scenario: Unpinning removes the resting indicator
- **WHEN** a pinned task is unpinned
- **THEN** the resting indicator is removed from that row

### Requirement: No assignment UI in the lectern
The lectern GUI SHALL NOT expose any column, toggle, or other control for a block's
assignment field. The field exists in the underlying document model but has no consumer
in this capability.

#### Scenario: Assignment is not visible or editable from the lectern
- **WHEN** the lectern's editor or read view is composed
- **THEN** no assignment-related column, label, or control appears anywhere in the dialog

### Requirement: Read view is rendered by a LibGUI dialog
The lectern's read view SHALL be rendered by a dialog built on the LibGUI framework (modid `gui`),
subclassing `ScribeDialogBase` (which itself subclasses LibGUI's `GuiDialogBlockEntityBase`), rather
than by the native `GuiComposer`-based `GuiDialogScribeLectern` read view. The dialog SHALL open from
the normal lectern interaction path and receive its document state through the existing
server-authoritative flow (the `scribe` network channel and its packets), NOT by directly reusing an
in-memory `Document` reference and NOT via any debug/chat command. The dialog's block-entity lifecycle
— open, close via the window's close control, title-bar drag, and minimize/expand — SHALL work as the
native dialog's did.

#### Scenario: Right-clicking a lectern opens the LibGUI read view
- **WHEN** a player interacts with a placed lectern to view it
- **THEN** a LibGUI-rendered dialog opens showing the lectern's document, populated from the
  server-synced document state
- **AND** the dialog can be closed, dragged by its title bar, and minimized/expanded

#### Scenario: No debug command is involved in the real open path
- **WHEN** the read view opens in normal play
- **THEN** it opens through the lectern's interaction + packet flow, not through a `.scribespike` (or
  any other) chat command, and it does not depend on the throwaway spike dialog

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
The read view's task checkbox SHALL be interactive: clicking it completes that task by stable
identity, applying the player's completion policy (Keep/Sink/Unpin/Delete) the same way every other
Scribe surface does. Because the read view holds no editor lock, completing SHALL be an always-allowed
server action that does NOT require acquiring the single-editor lock, applied server-authoritatively
and re-synced to all viewers. A player SHALL be able to complete a task from the read view even while
another player holds the editor lock. No other part of a read-view row SHALL be interactive except its
checkbox and its pin-toggle control (see the read-view pin-toggle requirement). The checkbox MAY be
rendered with LibGUI's stock checkbox widget; its skeuomorphic custom-glyph appearance is not required.

#### Scenario: Clicking a read-view checkbox toggles done
- **WHEN** the player clicks a task row's checkbox in the read view
- **THEN** that task is completed by identity, the player's completion policy is applied
  server-authoritatively (without requiring the editor lock) and synced back, and the checkbox
  updates to reflect the new state

#### Scenario: Toggling done works while someone else is editing
- **WHEN** a player clicks a read-view task checkbox while a different player holds the lectern's
  editor lock
- **THEN** the completion is still applied and synced, and is not rejected for lack of the lock

#### Scenario: The rest of a read-view row is inert
- **WHEN** the player clicks or hovers a read-view row anywhere other than its checkbox or its
  pin-toggle control
- **THEN** no edit field opens, no row reorder begins, and no other per-row control activates

### Requirement: Editor view is rendered by the LibGUI dialog
The lectern's editor view SHALL be rendered by the same LibGUI dialog that renders the read view
(`GuiDialogScribeLecternLibGui`, a sealed subclass of `ScribeDialogBase`), NOT by the native
`GuiComposer`-based `GuiDialogScribeLectern`. Switching between the read view and the editor view
SHALL be an internal view swap within that single dialog — no separate native dialog SHALL be opened
for editing. Returning from the editor view (by finishing editing) SHALL return to the LibGUI read
view, and entering the editor view SHALL acquire the lectern's single-editor lock through the existing
server flow, releasing it when the editor view is left.

The dialog SHALL enter the editor view ONLY after the server has actually granted the single-editor
lock. It SHALL NOT enter the editor view optimistically (before the grant reply) nor on a refused
reply.

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

#### Scenario: Editor view is entered only after the lock is granted
- **WHEN** the player activates "switch to editor" and the server grants the lock
- **THEN** the dialog swaps to the editor view only upon receiving the granted reply

### Requirement: A contended editor lock blocks entry and reports it to the player
The lectern's single-editor lock SHALL be **transient server-session state only**. The server's
authoritative lock holder SHALL be released whenever the holder leaves the editing session by ANY
path — closing the dialog, switching to the read view or another tab, or disconnecting — and SHALL
be cleared when the block entity is loaded. Consequently the lock SHALL NEVER survive the holder
leaving the editor, a second player's relog, or a server restart: it can prevent a *concurrent*
second editor, but it can never become a permanent lockout that bars a lectern from ever being
edited again. The lock MAY be mirrored to clients via the block-entity sync (to drive the
contended-editor affordance), but that synced value SHALL NOT be treated as authoritative across a
block load.

When another player already holds the lectern's single-editor lock, the dialog SHALL NOT allow a second
player to enter the editor view. The second player's "switch to editor" affordance SHALL reflect the
unavailable lock (visibly disabled/inert while the lock is held by another player), and activating it
SHALL NOT open the editor view — the second player remains in the read view. When editor access is
refused (or otherwise unavailable), the dialog SHALL surface Vintage Story's native in-game error
notification with player-facing copy indicating another player is editing (e.g. "Another player is
making edits."). A refused request SHALL NOT leave the second player in an editor view whose edits are
silently discarded.

#### Scenario: Second player is blocked while the first edits
- **WHEN** player 1 holds the lectern's editor lock and player 2 activates "switch to editor" on the same lectern
- **THEN** player 2 does not enter the editor view, remains in the read view, and sees a native in-game
  error indicating another player is editing

#### Scenario: Contended editor affordance reflects the held lock
- **WHEN** player 2 views a lectern whose editor lock is held by player 1
- **THEN** player 2's "switch to editor" affordance is shown as unavailable/inert rather than appearing
  freely usable

#### Scenario: Lock releases so the next player can edit
- **WHEN** player 1 leaves the editor view (or disconnects) and releases the lock, and player 2 then
  activates "switch to editor"
- **THEN** player 2 is granted the lock and enters the editor view normally

#### Scenario: A sole editor is never spuriously refused
- **WHEN** no other player holds the lock and a player activates "switch to editor"
- **THEN** the player enters the editor view and their edits persist (no revert), because the lock is
  granted and retained for the duration of the editor session

#### Scenario: Closing the dialog releases the lock even when not in editor mode
- **WHEN** player 1 acquired the editor lock, then switched to the read view (or another tab) without
  fully closing, and then closes the dialog
- **THEN** the server's lock holder is released on that close, so player 2 can subsequently enter the
  editor

#### Scenario: The lock does not lock out after the holder leaves or a second player relogs
- **WHEN** player 1 leaves the editing session (closes the dialog, switches to read, or disconnects),
  or a lectern is loaded whose in-memory holder was somehow still set
- **THEN** the loaded/updated lectern has no editor lock held, and any player — including one who
  relogs — may enter the editor view

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

### Requirement: Editor input captures keystrokes while focused
While an editor row's text field holds focus, the dialog SHALL capture keyboard input so that typed keys
edit the field and do NOT leak through to the game (e.g. player movement, hotbar selection, or other
keybinds). When NO editor field holds focus — including while the editor view is open but the player has
clicked away from every row (e.g. after adding a task via "New Task" and unfocusing it) — the dialog SHALL
NOT capture input, so global hotkeys (e.g. the Handbook key) fire normally. Input capture SHALL therefore
be gated on a field actually holding focus, NOT merely on the editor view being active. Releasing focus
(leaving the editor view or committing out of all fields) SHALL restore normal key handling.

#### Scenario: Typing does not trigger game keybinds
- **WHEN** the player types letters that also match game keybinds (e.g. movement keys) while an editor
  field is focused
- **THEN** the characters are inserted into the field and the game does not act on them (the player does
  not move, the hotbar does not change)

#### Scenario: Focus release restores game input
- **WHEN** the player leaves the editor view or no field is focused
- **THEN** keyboard input is no longer captured by the dialog and normal game key handling resumes

#### Scenario: Hotkeys fire after clicking away from a new task row
- **WHEN** the player adds a task via "New Task" in the editor view and then clicks away so no editor
  field holds focus
- **THEN** global hotkeys (e.g. the Handbook key) fire normally, exactly as they would if the editor were
  opened without any task having been created

### Requirement: Editor caret conventions match desktop editing idioms cross-platform
The editor's text field SHALL support desktop caret-navigation idioms on macOS as well as Windows.
Cmd+Left / Cmd+Right SHALL move the caret to the start / end of the current line; Alt/Option+Left /
Alt/Option+Right SHALL move the caret by whole words; the Up / Down arrows SHALL move the caret to the
nearest column on the visual line above / below the caret (a "visual line" being a wrapped or
hard-broken display line), where pressing Up on the first visual line SHALL move the caret to the start
of the text and pressing Down on the last visual line SHALL move it to the end; and holding Shift with
any caret-movement key SHALL extend the selection rather than collapse it. Up / Down SHALL move the
caret only WITHIN the focused row — they SHALL NOT move focus to another row and SHALL NOT commit the
row. These conventions SHALL be provided by the LibGUI editor field itself (the mechanism is no longer
required to be a `GuiElementTextInput` subclass), so that the macOS modifier combinations are honored
rather than ignored.

#### Scenario: Cmd+Arrow jumps to line ends on macOS
- **WHEN** the player presses Cmd+Right (or Cmd+Left) while editing a row on macOS
- **THEN** the caret moves to the end (or start) of the line rather than doing nothing

#### Scenario: Alt/Option+Arrow skips by word
- **WHEN** the player presses Alt/Option+Right (or Left) while editing a row
- **THEN** the caret moves by one whole word in that direction rather than being ignored

#### Scenario: Up/Down move the caret between visual lines
- **WHEN** the player presses Up (or Down) while editing a multi-line row with the caret not on the
  first (or last) visual line
- **THEN** the caret moves to the nearest column on the visual line above (or below), staying within
  the same row without committing or changing focus

#### Scenario: Up on the first line and Down on the last line jump to the text ends
- **WHEN** the player presses Up while the caret is on the first visual line, or Down while the caret
  is on the last visual line
- **THEN** the caret moves to the start of the text (Up) or the end of the text (Down) rather than
  doing nothing

#### Scenario: Shift extends selection during caret movement
- **WHEN** the player holds Shift while pressing any caret-movement combination (arrow,
  word-skip, line-end, or Up/Down line navigation)
- **THEN** the text selection extends to the new caret position instead of collapsing

### Requirement: Editor rows navigate and commit by keyboard
The editor SHALL let the player move between rows and add rows from the keyboard while editing. Pressing
Tab (without Shift) SHALL commit the current row's edit and move focus to the next row WITHOUT inserting a
tab glyph; pressing Shift+Tab SHALL commit and move focus to the previous row. Tab / Shift+Tab traversal
SHALL visit only the rows' editable text fields, in row order; it SHALL NOT stop focus on a row's
completion checkbox (the checkbox remains operable by mouse click). Pressing Enter (without
Shift) SHALL commit the current row's edit and insert a NEW empty task directly beneath it, moving focus to
that new row, WITHOUT inserting a line break into the current row; pressing Shift+Enter SHALL instead insert
a hard line break into the row's text (growing the row) rather than committing. Pressing Enter on a row that
is itself empty or whitespace-only SHALL NOT stack a second empty task; it SHALL be a no-op on the row set.
Committing an edit (by Tab, Shift+Tab, Enter, or losing focus) SHALL apply the change through the existing
lock-gated server edit path (`ScribeEditDocumentMessage`), server-authoritatively; except that committing a
task row whose text is empty or whitespace-only SHALL remove that task (see "An empty task row is removed
when it loses focus") rather than saving it. Pressing Esc SHALL commit the focused row (via the same
blur-commit path) and close the dialog — a fast panic-close, not an in-place revert. On commit, a
non-empty row's text SHALL be normalized by trimming trailing blank lines and trailing whitespace while
preserving interior newlines, and the read view SHALL render those interior newlines as hard line breaks.

#### Scenario: Tab commits and advances
- **WHEN** the player finishes typing in a row and presses Tab (without Shift)
- **THEN** the row's new text is committed through the server edit path and focus moves to the next row,
  and no tab glyph is inserted into the row's text

#### Scenario: Tab traversal skips the row checkbox
- **WHEN** the player presses Tab or Shift+Tab to move between rows in the editor
- **THEN** focus moves directly from one row's editable text field to an adjacent row's editable text
  field, never landing on a row's completion checkbox, so a single Tab advances one row

#### Scenario: Enter commits and inserts a new empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty row
- **THEN** the row's edit is committed, a new empty task is inserted directly beneath it, focus moves to
  that new empty task, and no line break is inserted into the original row's text

#### Scenario: Enter on an empty row does not stack another empty task
- **WHEN** the player presses Enter (without Shift) while the focused task row is itself empty or
  whitespace-only
- **THEN** no additional empty task is inserted (the row set is unchanged)

#### Scenario: Shift+Enter inserts a hard line break
- **WHEN** the player presses Shift+Enter while editing a row
- **THEN** a line break is inserted at the caret, the row's height grows to fit the new line, and focus
  stays in the row (no commit, no new row)

#### Scenario: Shift+Tab commits and retreats
- **WHEN** the player presses Shift+Tab while editing a row
- **THEN** the row's edit is committed and focus moves to the previous row

#### Scenario: Committing an empty task removes it
- **WHEN** the player commits a task row (by Tab, Shift+Tab, Enter, losing focus, or closing) whose text
  is empty or whitespace-only
- **THEN** the task is removed from the document rather than saved, and focus moves to the row above when
  one exists

#### Scenario: Esc commits and closes
- **WHEN** the player presses Esc while editing a row
- **THEN** the focused row is committed via the blur-commit path (a non-empty row is saved and normalized;
  an empty task row is removed) and the dialog closes

#### Scenario: Committed text has trailing blank lines trimmed
- **WHEN** the player commits a non-empty row whose text ends in one or more blank lines or trailing
  whitespace
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
The LibGUI lectern dialog SHALL source its task-row font size from the player's single client-local
preference store (the same store that holds the other per-player preferences), applying the window
font-size scale to a built-in base font size. The remaining task-row layout values (per-row vertical and
horizontal padding, checkbox size, checkbox-to-text gap, and the editor field's internal padding) SHALL
be built-in constants rather than user configuration. The dialog SHALL derive its row sizing from the
live preference store each time it builds its content, so that a font-size change made in the settings
view takes effect on the open dialog. When the preference store has never been written, the dialog SHALL
fall back to a font-size scale of `1.0` (the base sizes) without error.

#### Scenario: Changing the font scale in settings applies to the open dialog
- **WHEN** the player changes the window font-size scale in the settings view
- **THEN** the lectern's rows re-render at the new size without the dialog being closed and reopened

#### Scenario: Unset preferences fall back to base sizing
- **WHEN** the player has never changed a font-size preference and opens the lectern
- **THEN** the lectern opens normally using the base font size (scale `1.0`), with no error

### Requirement: Row sizing scales through a single factor
The lectern's scalable row-sizing values SHALL be derived by multiplying a built-in base font size (and
any font-derived spacing) by the player's single window font-size scale factor, applied at one place
before the values reach the row widgets. With the scale factor at its default of `1.0`, the rendered
sizes SHALL equal the base values (a no-op). Any fixed control-centering offsets that depend on the font
size SHALL be computed from the measured text/control heights at the current scale rather than from
constants tuned to a single font size, so the checkbox and grip stay centered on a row at any scale.

#### Scenario: Default scale reproduces the base sizes
- **WHEN** the window font-size scale factor is at its default value of `1.0`
- **THEN** each row's font size and scalable spacing equal the base values

#### Scenario: A non-default scale multiplies the sizes uniformly
- **WHEN** the window font-size scale factor is set to a value other than `1.0`
- **THEN** the scalable row-sizing values are multiplied by that factor for both the read and editor
  views, and the checkbox and grip remain vertically centered on a single-line row

### Requirement: Lectern dialog offers a settings view in its central region
The Lectern dialog SHALL offer a settings view as a third selectable state of its central content
region, alongside the read and editor views, reachable from a gear control in the dialog chrome that is
present in both the read and editor views. Switching to the settings view SHALL replace the read/editor
content while leaving the dialog's chrome in place, and the view SHALL provide a way to return to the
previously shown read or editor content.

#### Scenario: Gear switches the central region to settings
- **WHEN** a player activates the gear control in an open Lectern dialog
- **THEN** the dialog's read/editor content is replaced by the settings view and the dialog chrome remains

#### Scenario: Leaving settings returns to the prior view
- **WHEN** the settings view is shown and the player leaves it
- **THEN** the dialog returns to the read or editor view that was shown before

### Requirement: New tasks are created empty
When the player adds a block in the editor view — via the footer add control (the kind
picker) or by committing a row with Enter (insert-below) — the new block SHALL be created
with empty text rather than seeded with a placeholder literal (e.g. "New task"). This applies
to both kinds the picker creates: a Standard Task and a Note are each created empty. The new
row SHALL be focused so the player can type into the empty field immediately, with no
boilerplate text to select and delete first. Enter (insert-below) SHALL continue to insert a
task, matching the surrounding task-editing flow.

#### Scenario: Add task creates an empty focused row
- **WHEN** the player uses the add control to add a Standard Task
- **THEN** a new task row is added with empty text and receives focus, and its text field contains
  no pre-filled placeholder characters

#### Scenario: Add note creates an empty focused row
- **WHEN** the player uses the add control to add a Note
- **THEN** a new text-section row (no checkbox) is added with empty text and receives focus, and its
  text field contains no pre-filled placeholder characters

#### Scenario: Enter inserts an empty task below
- **WHEN** the player presses Enter (without Shift) while editing a non-empty task row
- **THEN** the current row is committed and a new empty task is inserted directly beneath it and
  focused, containing no pre-filled placeholder characters

### Requirement: An empty task row is removed when it loses focus
While in the editor view, when a row whose text is empty or whitespace-only loses focus (by
clicking away, moving to another row, switching to the read view, or closing the dialog), the
editor SHALL remove that block from the document rather than persisting it, and SHALL move focus to
the row immediately above the removed row when one exists. This applies to any empty editor row —
a task **or** a note — whether just created and abandoned without typing, or an existing block whose
text the player cleared (e.g. with select-all then Delete) — so that abandoned empty rows never grow
the list and a cleared row can be removed from the keyboard alone. Removal SHALL be applied through
the existing lock-gated server edit path and SHALL NOT leave an empty task or note visible in the read
view or persisted across reload. (The Core document model still stores text verbatim for both kinds;
this removal is an editing-layer behavior, not a model invariant.)

#### Scenario: Abandoned empty new task is removed on blur
- **WHEN** the player adds a task, types nothing, and moves focus away from that empty row
- **THEN** the empty task is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Abandoned empty new note is removed on blur
- **WHEN** the player adds a note, types nothing, and moves focus away from that empty row
- **THEN** the empty note is removed from the document and does not appear in the read view or
  after reload, and the list is not grown by the abandoned add

#### Scenario: Clearing a task's text then blurring removes the row
- **WHEN** the player selects all of an existing task row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the task is removed from the document and focus moves to the row directly above it

#### Scenario: Clearing a note's text then blurring removes the row
- **WHEN** the player selects all of an existing note row's text, deletes it, and then moves focus
  away from the now-empty row
- **THEN** the note is removed from the document and focus moves to the row directly above it

#### Scenario: Focus moves to the row above
- **WHEN** an empty row that is not the first row is removed on losing focus
- **THEN** focus moves to the task/note row that was directly above the removed row

#### Scenario: Switching to read or closing does not persist an empty row
- **WHEN** a task or note row is empty and the player switches to the read view or closes the dialog
- **THEN** the empty row is removed rather than saved, and the read view / reloaded document shows
  no empty row

### Requirement: Editor rows enforce a per-kind character limit with player feedback

Each editor row's text SHALL be bounded by a character limit that depends on its kind: a
Standard Task to the task limit (1,000 characters) and a Note to the larger note limit (10,000
characters). The limit SHALL be enforced live in the editor field — once a row is at its
limit, further typed input is ignored and an over-long paste is truncated to fit — so the text
committed to the document never exceeds the cap. The document codec SHALL apply the same limit
as a server-authoritative backstop by **clipping** an over-long row's text to its kind's limit
on read, for BOTH kinds; it SHALL NOT reject or drop the whole document because one row is
over-long.

When the player's input is blocked or truncated because a row is at its limit, the editor
SHALL surface a transient in-game error that names the kind and its limit (e.g. "Tasks are
limited to 1,000 characters." / "Notes are limited to 10,000 characters."), via the same
in-game error channel used for other editor refusals (tablet-full, editor lock). The character
count shown in the message SHALL be derived from the enforced limit constant rather than being
written literally into the message text, so the message and the enforced cap cannot drift
apart.

#### Scenario: Typing at a task's limit is prevented with feedback

- **WHEN** a task row already contains its maximum characters and the player types another
  character
- **THEN** the extra input is ignored (the stored text is unchanged) and a transient in-game
  error stating the task character limit is shown

#### Scenario: Typing at a note's limit is prevented with feedback

- **WHEN** a note row already contains its maximum characters and the player types another
  character
- **THEN** the extra input is ignored and a transient in-game error stating the note character
  limit is shown

#### Scenario: An over-long paste is truncated to the limit

- **WHEN** the player pastes text that would push a row past its kind's limit
- **THEN** only the portion that fits up to the limit is inserted, and the limit-feedback
  message is shown

#### Scenario: The codec clips an over-limit note instead of dropping the document

- **WHEN** a document is read whose note row exceeds the note limit
- **THEN** that note's text is clipped to the note limit and the rest of the document loads
  normally, rather than the whole document being rejected

### Requirement: The lectern lays its content out proportionally from one driving width
The lectern dialog's layout SHALL be derived from a single driving width `W` (the "Pixel Art Size"): every
structural region's size SHALL be expressed as a proportion of `W` (or of `H = W × 1160/1024`). The
`OuterArtBox` SHALL contain, stacked top to bottom, a `TitleBar` band of height `0.13 × H` and a
`SectionInnerBox` of `0.9 × W` by `0.8 × H` (centered horizontally), leaving the remaining vertical space
as bottom margin. Changing `W` SHALL rescale the entire layout consistently.

#### Scenario: All regions scale with the driving width
- **WHEN** the Pixel Art Size `W` changes
- **THEN** the outer box, title bar, inner section, and its columns all resize in proportion, preserving
  their relative ratios and the framed appearance

### Requirement: The lectern has a draggable title bar with title text and SVG buttons
The `TitleBar` band SHALL be the dialog's draggable region (click-drag within it moves the window). It SHALL
contain a bottom-anchored, horizontally-centered `TitleTextButtons` row (`0.75 × W` wide, `0.065 × H` tall)
holding the dialog's title text on the left (rendered at the window text size scaled by ×1.1) and a
right-aligned group of icon buttons drawn from the mod's custom SVGs. The group SHALL include a close button
that reuses the delete SVG at 1.4× the delete control's size. Each button SHALL provide a tooltip. Closing
and dragging SHALL work without relying on the stock window frame.

#### Scenario: The title bar drags the window and closes it
- **WHEN** the player click-drags inside the title bar band
- **THEN** the window moves; and clicking the close button (the 1.4× delete SVG) closes the dialog

#### Scenario: Title text and buttons are laid out and labeled
- **WHEN** the lectern opens
- **THEN** the title text sits on the left of the bottom-anchored centered row at window-text ×1.1, the SVG
  button group sits on the right, and hovering any button shows its tooltip

### Requirement: The inner section is a three-column layout framing the scrolling content
The `SectionInnerBox` SHALL be a row of three full-height columns: a left spacer column (`0.0675 × W`), a
tasks column (`0.765 × W`) that hosts the existing scrollable read/editor content, and a right column
(`0.0675 × W`) holding a vertical stack of icon buttons for navigation (Scribe Settings, Read view, Edit
view, Pinned tasks). The navigation buttons SHALL be icon-only and SHALL each provide a tooltip. The three
column widths SHALL sum to the inner box width so no column overflows.

#### Scenario: The scrolling content sits in the center column framed by side columns
- **WHEN** the lectern opens
- **THEN** the existing task/note scroll region renders in the center column, with the left spacer and the
  right icon-button column on either side, all within the framed inner section

#### Scenario: The right column exposes tooltipped navigation icons
- **WHEN** the player hovers a button in the right column
- **THEN** its tooltip appears, and activating it performs its navigation (open settings, switch to read,
  switch to edit, or show pinned tasks)

### Requirement: The title bar shows a drag-grip affordance
The Lectern dialog's title-bar button row SHALL include a drag-grip icon (the mod's registered
`scribegrip` SVG) positioned to the LEFT of the close button, so the fully-draggable title-bar band is
visually discoverable. The grip SHALL be a passive affordance marking the drag zone — it SHALL be tinted
as a non-primary control and SHALL provide a localized tooltip indicating the band can be dragged to move
the window. The window's drag behavior SHALL remain owned by the title-bar band itself (the grip does not
need its own drag gesture), so dragging works anywhere in the band, not only on the grip.

#### Scenario: The drag grip appears left of the close button
- **WHEN** the Lectern dialog is open
- **THEN** a drag-grip icon (the `scribegrip` SVG) is shown immediately to the left of the title bar's
  close button, and hovering it shows a tooltip indicating the title bar can be dragged to move the window

#### Scenario: Dragging still works across the whole band
- **WHEN** the player click-drags anywhere within the title-bar band (not only on the grip icon)
- **THEN** the window moves, since the drag zone is the whole band and the grip is only a discoverability cue

### Requirement: Read-view pin toggle preserves scroll position
When the player pins or unpins a task from the read view, the dialog SHALL preserve the read list's
current scroll offset across the rebuild that the pin change triggers. Toggling a pin SHALL NOT jump the
scroll list to the top; the list SHALL remain at the position the player had scrolled to (clamped only if
the list genuinely became shorter).

#### Scenario: Pinning a scrolled-down task keeps the scroll position
- **WHEN** the player has scrolled the read view down and pins (or unpins) a task
- **THEN** the read list stays at the same scroll position after the pin toggle rather than jumping back
  to the top

### Requirement: Read-view rows expose a pin-toggle affordance
Each task row in the read view SHALL provide a control that toggles the task's pinned state for the
acting player, addressed by stable identity, mirroring the editor view's pin control. Text-section
rows SHALL NOT expose this control. The control's visual state SHALL reflect whether the task is
currently pinned for the player.

#### Scenario: Toggling pin from a read-view row
- **WHEN** the player activates a read-view task row's pin-toggle control
- **THEN** the task's pinned state for that player flips and the control's visual state reflects the
  new value

#### Scenario: Read-view text sections have no pin control
- **WHEN** a text-section row is composed in the read view
- **THEN** no pin-toggle control is present for that row

### Requirement: Every Lectern view completes a task with the player's completion policy
Completing a task via its checkbox SHALL apply the player's completion policy identically in all three
Lectern views (read, editor, and pinned), matching the pinned-task HUD. The editor view's checkbox
SHALL NOT be an exception: completing a task from the editor SHALL apply the same policy
(Keep/Sink/Unpin/Delete) by stable identity, rather than only toggling a local done flag. The policy
SHALL apply verbatim in every view with no per-view guard or confirmation — including a policy that
deletes the task or reorders it within the shared document.

#### Scenario: Editor checkbox applies the completion policy
- **WHEN** a player completes a task via its checkbox in the editor view
- **THEN** the player's completion policy is applied to that task by identity (the same result the
  read, pinned, and HUD surfaces produce), not merely a local done-flag toggle

#### Scenario: Same result regardless of view
- **WHEN** the same player with the same completion policy completes a given task from the read view,
  the editor view, the pinned view, or the HUD
- **THEN** the outcome is the same in every case (the policy's Keep/Sink/Unpin/Delete effect)

#### Scenario: Completing an editor task preserves other in-progress edits
- **WHEN** a player has unsaved text edits in some editor rows and completes a different task via its
  checkbox
- **THEN** the completion is applied and the other rows' in-progress text and the caret are preserved
  (not discarded by the reconciliation)

### Requirement: A divider separates each view's header from its scrolling list
Each Lectern view (read, editor, pinned) SHALL render a horizontal divider directly above its
scrolling task/note list, providing a straight visual edge between the view's header area and the
scroll region.

#### Scenario: Divider above the scroll area in every view
- **WHEN** any of the read, editor, or pinned views is shown
- **THEN** a horizontal divider is drawn directly above that view's scrolling list

### Requirement: The pinned view places its completion-policy picker above the list
In the pinned view, the completion-policy picker SHALL be positioned above the pinned-task list (as a
header), not below it as a footer. Changing the policy from this picker SHALL continue to update the
same per-player completion-policy preference that the settings surface edits.

#### Scenario: Policy picker sits above the pinned list
- **WHEN** the pinned view is shown
- **THEN** the completion-policy picker appears above the list of pinned tasks

#### Scenario: The pinned picker and the settings control stay in sync
- **WHEN** the player changes the completion policy from the pinned view's picker
- **THEN** the same per-player completion-policy preference is updated, and the settings surface
  reflects the same value

