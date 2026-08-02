## Context

`ScribeMultilineField` is the mod's custom multi-line editable text RenderObject (LibGUI's stock
`TextField` is single-line — see VSAPI-NOTES). It is the SAME widget used by both the Lectern editor
rows and the Pin Tab rows, so any caret change lands in both views at once.

Its `OnKeyDown` (`src/Mod/ScribeMultilineField.cs:523`) already handles Left/Right (char + word-skip),
Home/End (line start/end), Backspace/Delete, Enter/Shift+Enter, Tab/Shift+Tab, and Ctrl+A/C/X/V, all
routing caret moves through one helper, `MoveCaret(newCaret, extendSelection)`. **Up and Down are
simply absent** — those key codes fall through the switch, so the caret doesn't move.

The field already computes and caches everything vertical navigation needs, because painting and
click hit-testing need it too:
- `visualLines` (`List<ScribeVisualLine>`, each a `(Text, Start)` where `Start` is the source offset
  of that display line) — the wrapped + hard-broken lines from the last layout.
- `CaretToLineCol(offset)` → `(line, col)` — which visual line the caret is on and its column within it.
- `OffsetAtPosition(local)` — maps a local x/y point to the nearest source offset, using the same
  monotonic `MeasureWidth` prefix scan the paint path uses.

The mod's macOS-modifier translation happens one layer up in `ScribeDialogBase.OnKeyDown`
(`:438`): it maps Cmd+Left/Right → Home/End and Cmd+A/C/X/V → Ctrl+… before `base.OnKeyDown`
strips the Command modifier. **Plain Up/Down carry no Command modifier and need no translation** —
they arrive at the field's `OnKeyDown` directly. (There is no macOS "Cmd+Up/Down = document
start/end" idiom to add here; this change is plain Up/Down only.)

## Goals / Non-Goals

**Goals:**
- Up moves the caret to the nearest column on the visual line above; Down to the visual line below.
- Up on the first visual line → caret to text start (offset 0); Down on the last visual line →
  caret to text end (`text.Length`).
- Shift+Up / Shift+Down extend the selection to the new caret (reuse `MoveCaret`'s `extendSelection`).
- Identical behavior in the editor and the Pin Tab (one shared field).

**Non-Goals:**
- No row-to-row focus movement on Up/Down (that stays Tab's job) and no commit on Up/Down.
- No preferred-column ("goal column") memory across a run of Up/Down presses — see the decision
  below; the target column is derived from the live caret X each press.
- No `src/Core/`, network, persistence, or layout change; no new macOS Cmd+Up/Down idiom.

## Decisions

**Decision: Derive the vertical target from the caret's current X pixel, then reuse
`OffsetAtPosition` to land on the adjacent line.** On Up/Down, compute the caret's current
`(line, col)` via `CaretToLineCol`, measure its X (the same `PadX + MeasureWidth(prefix)` the paint
path draws at), pick the target line (`line ± 1`), and ask the existing point→offset mapper for the
nearest column on that line at that X. This reuses the exact geometry that click hit-testing and
painting already agree on, so the caret lands where the user visually expects and there is no second
measurement path to keep in sync. Edge cases fall out cleanly: target line `< 0` → offset 0; target
line `≥ visualLines.Count` → `text.Length`.

**Decision: No persistent "goal column."** Desktop editors often remember the column you started a
vertical run at, so moving Up/Down through a short line and back preserves your X. That requires
tracking state across keystrokes and invalidating it on any non-Up/Down edit. For a task/note field
(rows are short — a handful of wrapped lines, not paragraphs) the simpler "use the live caret X each
press" behavior is more than adequate and avoids the state-invalidation bugs. Left as a possible
future refinement if it's ever missed. (Matches how the field already treats Left/Right — no memory.)

**Decision: Keep it entirely inside `ScribeMultilineField.OnKeyDown`.** Two new cases
(`GlKeys.Up`, `GlKeys.Down`), each calling a small `CaretLineUp()` / `CaretLineDown()` that returns
the new offset, then `MoveCaret(offset, e.Shift)` + `Handled(e)` — mirroring the existing Left/Right
cases exactly. No dialog-layer change, no new fields on the widget.

## Risks / Trade-offs

- **[Empty / single-line rows]** → Up/Down on a single-visual-line row: Up goes to offset 0, Down to
  `text.Length` (first-line/last-line edge rules), which is harmless and matches editor norms. Verify
  an empty row (one empty visual line) is a safe no-op-ish move (0 == length == caret).
- **[Geometry must be current]** → `OffsetAtPosition`/`visualLines` are valid only after a layout
  pass; a focused, mounted, painted field always satisfies this (same precondition click hit-testing
  already relies on). No new timing risk.
- **[Interaction with wrapped vs. hard-break lines]** → `visualLines` already unifies soft-wrapped
  and `\n`-broken lines, so navigation treats them identically with no special-casing.

## Migration Plan

Pure client-side keyboard addition; no data, save-format, or network change. Ships in the mod build;
effect is immediate on next launch. Rollback = remove the two `OnKeyDown` cases.

## Open Questions

None blocking. The only judgment call (goal-column memory) is decided above as a deliberate non-goal.
