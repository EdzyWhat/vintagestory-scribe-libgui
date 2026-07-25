## RENAMED Requirements

- FROM: `### Requirement: Read view switches to editing via the existing native editor`
- TO: `### Requirement: Read view switches to editing via the tab bar`

## MODIFIED Requirements

### Requirement: Read view switches to editing via the tab bar
The LibGUI read view SHALL provide a navigation tab bar (the `gui-navigation-tabs` capability) as its
switch-to-editing affordance, replacing the single gear-button header. Selecting the edit tab SHALL
switch to editing through the dialog's existing lock-acquiring editor-access path — it SHALL NOT flip a
local view-mode flag directly — so switching between viewing and editing keeps full edit functionality
available and preserves the single-editor lock round-trip. The read/edit tab and the settings tab SHALL
share one dialog shell (one nav row over one body); the settings view SHALL NOT be composed on a separate
early-returning code path.

#### Scenario: Selecting the edit tab switches to editing
- **WHEN** the player selects the read view's edit tab
- **THEN** the dialog switches to the editor view through the existing lock-acquiring editor-access path
  (not a direct flag flip), with full editing functionality available

#### Scenario: The tab bar is the switch-to-editing affordance
- **WHEN** the read view is composed
- **THEN** its switch-to-editing affordance is the shared navigation tab bar, and no standalone gear
  button is the navigation control

### Requirement: Editor view is rendered by the LibGUI dialog
The lectern's editor view SHALL be rendered by the same LibGUI dialog that renders the read view
(`GuiDialogScribeLecternLibGui`), NOT by the native `GuiComposer`-based `GuiDialogScribeLectern`.
Switching between the read view and the editor view SHALL be an internal view swap within that single
dialog — no separate native dialog SHALL be opened for editing. The read/edit page and the settings page
SHALL share a single dialog shell: a navigation tab bar (`gui-navigation-tabs`) over a single
view-switched body, with the settings view no longer composed on a separate early-returning path.
Navigation between views SHALL be driven by the tab bar delegating to the dialog's real navigation
methods (switch-to-read, request-editor-access, open-settings, close-settings), NOT by the tab bar
flipping a view-mode flag itself. Returning from the editor view (by finishing editing) SHALL return to
the LibGUI read view, and entering the editor view SHALL acquire the lectern's single-editor lock through
the existing server flow, releasing it when the editor view is left. Replacing the gear button with the
tab bar SHALL change only the affordance — the lock acquisition and release behavior SHALL be identical.

#### Scenario: Switching to editor stays in the LibGUI dialog
- **WHEN** the player selects "edit" from the LibGUI read view's tab bar
- **THEN** the same dialog swaps to an editor view rendered on LibGUI, and no native editor dialog opens

#### Scenario: Finishing editing returns to the LibGUI read view
- **WHEN** the player finishes editing and leaves the editor view
- **THEN** the dialog returns to the LibGUI read view (not a native read view), and the editor lock is
  released

#### Scenario: Entering the editor acquires the editor lock
- **WHEN** the editor view is entered via the tab bar's edit selection
- **THEN** the single-editor lock is acquired through the existing server flow (the request-access
  round-trip), and is released when the editor view is left — the tab bar routes to that flow rather than
  flipping a local flag

#### Scenario: All views share one shell
- **WHEN** the read, editor, or settings view is composed
- **THEN** each is rendered inside the same shell (one navigation tab bar over one body), and the settings
  view is not composed on a separate early-returning code path

## ADDED Requirements

### Requirement: The settings tab shares the shell and returns to the prior view
The settings page SHALL be reachable from a settings tab within the shared dialog shell, and SHALL be
shown within the same dialog rather than as a separate chrome-less view. Opening settings SHALL go through
the dialog's existing open-settings path, which commits and releases the editor lock when settings is
entered from the editor and records which view was active beforehand. Leaving the settings page (selecting
the read/edit tab or a Back affordance) SHALL go through the existing close-settings path, returning to
the view that was active before settings was opened — the read view, or the editor view (re-acquiring the
editor lock through the existing request-access round-trip) when the editor was active beforehand.

#### Scenario: Selecting the settings tab shows settings in the same dialog
- **WHEN** the player selects the settings tab
- **THEN** the settings page is shown within the same dialog shell (the tab bar remains visible), via the
  existing open-settings path, and the editor lock is released if the editor was active

#### Scenario: Leaving settings returns to the read view
- **WHEN** the player opened settings from the read view and then leaves settings
- **THEN** the dialog returns to the read view via the existing close-settings path

#### Scenario: Leaving settings returns to the editor and re-acquires the lock
- **WHEN** the player opened settings from the editor view and then leaves settings
- **THEN** the dialog returns to the editor view via the existing close-settings path, re-acquiring the
  single-editor lock through the existing request-access round-trip (the `wasEditorBeforeSettings`
  return path), not a direct flag flip

### Requirement: The title bar color reflects the active view mode
The dialog's `WindowFrame` title bar SHALL be given an explicit color appropriate to the active view mode
in `Build()`. Because the title bar reads its default colors at construction and does not follow a
`Theme` wrap, the dialog SHALL pass an explicit `titleBarColor`/`textColor` computed for the current mode
rather than relying on theme inheritance for the title band.

#### Scenario: The title bar is colored per mode
- **WHEN** the dialog is built for a given view mode
- **THEN** the `WindowFrame` receives an explicit title-bar color and text color for that mode, rather
  than the title band being left to inherit a theme it does not follow
