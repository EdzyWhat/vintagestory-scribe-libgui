## Why

The editor and Pin Tab rows are multi-line text fields (a task/note can wrap or hold hard line
breaks), but the Up and Down arrow keys currently do nothing inside a focused row —
`ScribeMultilineField.OnKeyDown` handles Left/Right/Home/End but falls through on Up/Down. So the
only way to move the caret between the visual lines of a multi-line row is to click, or to Left/Right
all the way around. Every desktop text editor moves the caret to the line above/below on Up/Down;
its absence is a papercut the moment a row has more than one line.

## What Changes

- Inside a focused editor or Pin Tab row, the **Up** arrow SHALL move the caret to the nearest
  column on the visual line above, and the **Down** arrow to the nearest column on the visual line
  below, matching standard desktop editing. "Visual line" means a wrapped or hard-broken display
  line, so navigation works across both soft-wrapped and `\n`-separated lines.
- Pressing **Up on the first visual line** and **Down on the last visual line** SHALL move the caret
  to the start / end of the text respectively (the common editor behavior), not do nothing.
- Holding **Shift** with Up/Down SHALL extend the selection to the new caret position, consistent
  with the existing Shift+arrow behavior.
- Up/Down remain **within the current row** — they do NOT move focus between rows (that is Tab's
  job) and do NOT commit the row. Row-to-row movement and commit semantics are unchanged.
- The behavior is provided by the shared `ScribeMultilineField`, so it applies identically in the
  Lectern editor view and the Pin Tab.

## Capabilities

### New Capabilities
<!-- none — this extends the existing editor/pin caret behavior -->

### Modified Capabilities
- `lectern-gui-shell`: the "Editor caret conventions match desktop editing idioms cross-platform"
  requirement gains Up/Down visual-line caret navigation (with the first-line/last-line edge
  behavior and Shift-extends-selection).
- `pinned-task-tab`: the editable-rows requirement is clarified so the same Up/Down line navigation
  applies to Pin Tab rows (they reuse the editor field).

## Impact

- Affected code: `src/Mod/ScribeMultilineField.cs` — two new `GlKeys.Up` / `GlKeys.Down` cases in
  `OnKeyDown`, built on the field's existing visual-line geometry (`visualLines`, `CaretToLineCol`,
  and the column-nearest-X mapping already used by click hit-testing). No new bookkeeping.
- No `src/Core/` impact (no document/model/codec change); no network or persistence change; no new
  dependency. Purely a client-side caret-navigation addition.
- Keyboard-only surface: no visual/layout change, no effect on commit, Tab traversal, Enter/Shift+Enter,
  or mouse behavior.
