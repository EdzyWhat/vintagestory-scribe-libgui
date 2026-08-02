## ADDED Requirements

### Requirement: The editor footer Information button toggles the Scribe Editor Features handbook page

The editor footer's Information (ⓘ) button SHALL toggle the "Scribe Editor Features" handbook page
rather than only opening it. When the survival handbook dialog is NOT open, clicking the button SHALL
open the handbook to the Scribe Editor Features page (`handbook://craftinginfo-scribe-editor-reference`)
via the game's registered `"handbook"` link protocol, exactly as before. When the survival handbook
dialog IS open, clicking the button SHALL close it. This behavior applies uniformly to every dialog
whose editor footer is built by `ScribeEditorContent` — the Lectern, the plain Notebook, the
Clockmaker's Notebook, and the always-edit tablet — because they share the one footer button.

The button SHALL detect and close the handbook using only public Vintage Story API, WITHOUT taking a
type reference to, or reflecting into, the survival mod's private handbook dialog or mod system: the
open handbook is discovered by scanning `capi.Gui.OpenedGuis` for the `GuiDialog` whose public
`ToggleKeyCombinationCode` equals `"handbook"`, and it is closed by calling that dialog's public
`TryClose()`. This preserves the deliberate decoupling of the current implementation.

When the handbook is open on a DIFFERENT page (not the Scribe Editor Features page), the button SHALL
navigate the handbook to the Scribe Editor Features page rather than closing the handbook — a
"focus, don't hide" rule — so a player who opened the handbook to another entry is taken to the
reference instead of losing the handbook entirely; a subsequent click, with our page then showing,
closes it.

When the survival mod (and thus the `"handbook"` link protocol and dialog) is not loaded, the button
SHALL be a graceful no-op on both the open and the close paths — no crash and no exception — matching
today's fail-safe behavior.

The button's hover tooltip (lang key `scribe:scribe-gui-editor-reference-tooltip`) SHALL convey the
toggle (open/close) affordance rather than an open-only label.

#### Scenario: Clicking the Information button opens the reference when the handbook is closed

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  not currently open
- **THEN** the survival handbook opens to the Scribe Editor Features page
  (`handbook://craftinginfo-scribe-editor-reference`)

#### Scenario: Clicking the Information button closes the handbook when it is already showing the reference

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  open on the Scribe Editor Features page
- **THEN** the survival handbook dialog closes

#### Scenario: Clicking the Information button navigates to the reference when the handbook is open elsewhere

- **WHEN** a player in the editor clicks the Information (ⓘ) button and the survival handbook dialog is
  open on a different page than the Scribe Editor Features page
- **THEN** the handbook navigates to the Scribe Editor Features page (it is not closed), and a
  subsequent click of the button closes the handbook

#### Scenario: The handbook is detected and closed without coupling to the survival mod's private dialog

- **WHEN** the button determines whether the handbook is open and needs closing
- **THEN** it does so by scanning `capi.Gui.OpenedGuis` for the `GuiDialog` whose
  `ToggleKeyCombinationCode` is `"handbook"` and calling its public `TryClose()`, taking no type
  reference to `GuiDialogHandbook`/`ModSystemSurvivalHandbook` and using no reflection

#### Scenario: Graceful no-op when the survival handbook is not loaded

- **WHEN** a player clicks the Information (ⓘ) button in a game where the survival mod (and its
  `"handbook"` link protocol) is not loaded
- **THEN** nothing happens — no handbook opens, no dialog closes, and no crash or exception occurs

#### Scenario: The toggle behavior is shared across every editor footer

- **WHEN** the Information (ⓘ) button is present on the Lectern, plain Notebook, Clockmaker's Notebook,
  or tablet editor footer
- **THEN** it exhibits the same open/close toggle behavior in each, because all four share the
  `ScribeEditorContent` footer button
