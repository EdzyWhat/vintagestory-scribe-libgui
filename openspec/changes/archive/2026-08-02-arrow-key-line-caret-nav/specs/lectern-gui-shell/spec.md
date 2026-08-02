## MODIFIED Requirements

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
