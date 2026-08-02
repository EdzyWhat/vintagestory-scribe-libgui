## Why

Since the LibGUI 3.1.0 update (and the recent GUI-file rework), Tab / Shift+Tab in the Lectern's
Editor view and the Pinned view now stop focus on each row's completion **checkbox** in addition to
its text field. Previously these keys cycled only through the editable text inputs, so a player could
Tab straight from one task's text to the next. Now every advance lands on a checkbox first, doubling
the keystrokes needed to move between rows and interrupting fast keyboard editing — the exact flow the
"navigate and commit by keyboard" behavior was designed to make quick.

## What Changes

- Row completion checkboxes SHALL be excluded from the Tab / Shift+Tab focus-traversal order in the
  Lectern Editor view and the Pinned view. Tab continues to move between editable text fields only;
  the checkbox remains fully usable by mouse click.
- No change to Tab/Shift+Tab commit semantics, to Enter/Shift+Enter, or to how the checkbox toggles
  task completion — only which widgets the keyboard traversal visits.

## Capabilities

### New Capabilities
<!-- none — this is a behavior correction to existing capabilities -->

### Modified Capabilities
- `lectern-gui-shell`: the "Editor rows navigate and commit by keyboard" requirement is clarified so
  that Tab/Shift+Tab traversal targets the editable text fields only and skips the row checkbox.
- `pinned-task-tab`: the Pin Tab's editable-rows requirement is clarified the same way, so keyboard
  traversal on the Pinned view skips checkboxes too.

## Impact

- Affected UI: the Lectern Editor view and the Pinned (Pin Tab) view row widgets — specifically the
  focus/traversal wiring around the row checkbox vs. the `ScribeMultilineField` text input.
- No `src/Core/` impact (no document/model/codec change); no network or persistence change; no new
  dependency. Purely a client-side focus-traversal correction.
- Likely root cause: a LibGUI 3.1.0 change made the checkbox widget participate in focus traversal by
  default; the fix marks the checkbox non-traversable (mouse-only) on these views. Exact seam confirmed
  in design.
