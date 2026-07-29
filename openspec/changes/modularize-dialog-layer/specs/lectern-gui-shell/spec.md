## MODIFIED Requirements

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
