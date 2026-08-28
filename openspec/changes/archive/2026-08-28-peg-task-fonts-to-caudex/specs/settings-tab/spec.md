## MODIFIED Requirements

### Requirement: Setting a control writes through immediately with live preview
Changing any control on the settings surface SHALL immediately write the new value to the player's
client-local preferences and persist it, with no separate apply or confirm step. The change SHALL take
effect live: a change to a HUD-affecting preference SHALL update the HUD without reopening it, and a
change to the window font-size scale SHALL update an open host dialog's **task text** (Read/Edit and
other task-font surfaces) without reopening it. The settings form's own labels, helptext, and
checkboxes SHALL stay at the unscaled settings size (`BaseSettingsFontSize` / `BaseSettingsCheckboxSize`)
in LibGUI's default body face — they SHALL NOT re-scale with Window Text Size and SHALL NOT inherit
Task Text Font. Values SHALL be normalized/clamped on write, so a control can never persist an
out-of-range value.

#### Scenario: A HUD preference updates the HUD live
- **WHEN** a player changes the HUD anchor, rows, width, offsets, or HUD font scale on the settings surface
- **THEN** the pinned-task HUD reflects the change immediately, without being closed and reopened

#### Scenario: The window font scale updates the open dialog live
- **WHEN** a player changes the window font-size scale while a Lectern dialog is open
- **THEN** the dialog's Read/Edit (and other task-text) re-renders at the new scale without the dialog
  being closed and reopened

#### Scenario: The settings form stays at 100% when window text size changes
- **WHEN** a player changes the window font-size scale on the settings surface
- **THEN** the settings form's own text and checkboxes stay at the unscaled settings size
- **AND** they still render in LibGUI's default body face, not the selected Task Text Font

#### Scenario: No separate apply step
- **WHEN** a player changes a control and then dismisses the settings surface
- **THEN** the change was already persisted at the moment it was made, with no apply or confirm action required
