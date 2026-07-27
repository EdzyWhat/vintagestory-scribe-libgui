## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Read-view pin toggle preserves scroll position
When the player pins or unpins a task from the read view, the dialog SHALL preserve the read list's
current scroll offset across the rebuild that the pin change triggers. Toggling a pin SHALL NOT jump the
scroll list to the top; the list SHALL remain at the position the player had scrolled to (clamped only if
the list genuinely became shorter).

#### Scenario: Pinning a scrolled-down task keeps the scroll position
- **WHEN** the player has scrolled the read view down and pins (or unpins) a task
- **THEN** the read list stays at the same scroll position after the pin toggle rather than jumping back
  to the top
