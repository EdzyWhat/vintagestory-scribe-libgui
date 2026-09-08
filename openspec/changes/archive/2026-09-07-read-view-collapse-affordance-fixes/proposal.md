## Why

The 2026-09-06 playtest of `read-view-filter-and-collapse` (now archived) surfaced one
correctness bug and two affordance refinements that weren't caught by automated coverage or
the original manual-test list: the active filter pill silently resets when a row's
complete-state changes under it, and the collapse toggle's chrome/position don't match how
the row's other controls are laid out.

## What Changes

- **Fix pill-reset bug**: un-checking (or checking) a row while a non-All pill is active no
  longer resets the active pill back to All. The row instead falls back to normal
  shadow-row/hide behavior under the still-active pill, exactly as any other
  no-longer-matching row would.
- **Caret-only collapse toggle**: the collapse/expand control renders as a bare
  triangle/caret glyph, with no button border/background chrome around it.
- **Left-column collapse toggle position**: the collapse/expand control moves to the row's
  left column — the slot a grip/drag-handle would occupy if the row had one. Rows without an
  owned run continue to show nothing in that column.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `read-view-filter-pills`: clarify that changing a row's own match state (e.g. completing
  a task) never changes which pill is active — only an explicit pill click does.
- `read-view-subtask-collapse`: change the collapse toggle's requirement from an unspecified
  control to specifically a borderless caret glyph positioned in the row's left column.

## Impact

- Read View row-rendering code shared across Lectern/Notebook/Clockmaker's Notebook,
  Chalkboard, Scriptorium, Assignment Desk, and Inbox (never Tablet) —
  `ScribeDialogBase.Layout.cs`'s `BuildReadContent` and the row-widget builder for the
  collapse toggle.
- No persistence format changes; the persisted active-pill and collapse-state fields from
  `read-view-filter-and-collapse` are unaffected — only when/whether the pill selection
  mutates, and how the toggle renders/positions, changes.
