## ADDED Requirements

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

## MODIFIED Requirements

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

## REMOVED Requirements

### Requirement: Read-view rows are custom-drawn in the interactive render pass
**Reason**: This requirement mandated the native mechanism (custom `ScribeRowElement` drawn in the
engine's interactive render pass, clipped by the engine's native scroll-clip region). Under LibGUI the
read view is a declarative widget tree whose `ListView` viewport does the clipping. The observable
behavior it protected — a long document stays fully reachable by scrolling with no content painting
outside the scroll region — is preserved by the new requirement "Read view renders the document as a
scrollable widget tree."
**Migration**: None for players. The native `ScribeRowElement` read path is superseded by the LibGUI
`ListView`/`Row` widget tree; the native editor view (which still uses `ScribeRowElement`) is unaffected
until its own migration change.

### Requirement: Read-view rows render a structural lined-paper ruling
**Reason**: The skeuomorphic lined-paper ruling was a native-read-view mechanism. It is **dropped for
good** in the LibGUI direction (decision 2026-07-23): the LibGUI lectern is taking a cleaner, more
modern visual direction, so the lined-paper ruling is removed from the roadmap rather than deferred.
The read view lands (and stays) functional without it.
**Migration**: None for players. The ruling is NOT re-introduced by any later LibGUI change — do not
re-add it. (This is a reversal of the earlier "deferred, will be re-added" framing.)

### Requirement: Read-view checkbox is a custom-drawn glyph
**Reason**: Requiring a custom-drawn checkbox glyph "rather than the engine's default `GuiElementSwitch`"
is native-mechanism-specific and a deferred visual concern. The LibGUI read view uses LibGUI's stock
checkbox for now; the custom-glyph appearance (and its text-size scaling) is deferred.
**Migration**: None for players. The custom checkbox glyph will be re-introduced as a LibGUI widget (or
custom `RenderBox`) in the later affordance/theme change, alongside the checkbox-stamp animation seam.
