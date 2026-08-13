## 1. Baseline

- [x] 1.1 Capture a green baseline: `dotnet build src/Mod/Mod.csproj` clean and
  `dotnet test tests/Core.Tests` green before touching anything (this change is GUI-layer;
  Core stays untouched, so the suite is a no-regression gate).
- [x] 1.2 Re-confirm the current line numbers before editing (they drift): the
  `pendingEnsureVisible` consumer in `ScribeDialogBase.Lifecycle.cs` (~209-215), the caret
  paint math in `ScribeMultilineField.cs` (`caretY = PadY + line * lineHeight`, ~166-173),
  the `IScribeEditableTextRender` interface (`ScribeCuneiformField.cs` ~37), and the
  field's `ResolveTextRender` proxy→child step (`ScribeMultilineField.cs` ~1049-1053).

## 2. Expose the caret rect (design D1)

- [x] 2.1 Add `bool TryGetCaretRect(out float localTop, out float height)` to the internal
  `IScribeEditableTextRender` interface — returns the caret top + line height in the render
  object's LOCAL coordinates, or `false` if layout has not run yet (`lineHeight`/
  `visualLines` not populated).
- [x] 2.2 Implement it on `ScribeMultilineFieldRender` using the same math the painter uses
  (`PadY + line * lineHeight` for top, `lineHeight` for height; derive `line` from the
  current caret via the existing `CaretToLineCol`). Guard on a completed layout pass.
- [x] 2.3 Implement it on `ScribeCuneiformFieldRender` with the same semantics so the
  cuneiform editor behaves identically (no regression on cuneiform surfaces).

## 3. Caret-based ensure-visible in the dialog (design D2)

- [x] 3.1 Add a helper (e.g. `EnsureCaretVisible(Element focusedFieldElement)`) in
  `ScribeDialogBase` that resolves the focused field's text render object from
  `editorFocusNodes[idx].Owner` via the proxy → `Children[0] as IScribeEditableTextRender`
  step (mirroring `ResolveTextRender`), and bails (no scroll) if it can't or if
  `TryGetCaretRect` returns `false`.
- [x] 3.2 Compute the caret's content-space Y by summing render-object `Y` up the parent
  chain to the `RenderViewport` (the same walk `Scrollable.EnsureVisible` performs); factor
  it into a shared helper if practical, else replicate it. `caretTop = fieldContentY +
  localTop`, `caretBottom = caretTop + height`.
- [x] 3.3 Apply the single-outcome minimal scroll against `sharedScrollController` (public
  `Offset`, `MaxScrollExtent`, `ViewportSize`): if `caretBottom > offset + viewport` →
  bottom-align; else if `caretTop < offset` → top-align; else no change. Clamp to
  `[0, MaxScrollExtent]` and `JumpTo` only when the delta exceeds a small epsilon
  (match `Scrollable`'s `< 0.5f` no-op guard). Do NOT use the two-guard form.
- [x] 3.4 Replace the `Scrollable.EnsureVisible(element)` call in the `pendingEnsureVisible`
  block (`ScribeDialogBase.Lifecycle.cs`) with `EnsureCaretVisible(...)`. Leave the six
  `pendingEnsureVisible = true` trigger sites in `ScribeDialogBase.Editor.cs` unchanged.

## 4. Build & Core tests

- [x] 4.1 `dotnet build src/Mod/Mod.csproj` clean (0 new warnings); `dotnet test
  tests/Core.Tests` green (no Core change expected — no-regression gate). If any pure
  geometry helper landed in a Core-testable seam, add coverage; otherwise note the fix is
  GUI-layer and relies on the in-game gate.

## 5. In-game playtest gate

- [x] 5.1 `bash build/restage.sh Debug`, relaunch, open a Lectern editor. Type a long
  task/note until the row is TALLER than the viewport: confirm the view no longer bounces
  per keystroke — it follows the caret and stays stable.
- [x] 5.2 Confirm caret-follow on growth: keep typing so the caret would pass the bottom
  edge → the view scrolls just enough to keep the caret line visible, no more; delete back
  → reverses. Typing with the caret already visible causes no scroll.
- [x] 5.3 Confirm keyboard navigation follows the caret: arrow keys, Tab / Shift+Tab, and
  Enter advance/retreat between rows all bring an off-screen caret back into view.
- [x] 5.4 Confirm no regression elsewhere: adding a row still scrolls it into view; mid-list
  add/delete/reorder still holds the viewport (reconcile behavior); a cuneiform editor
  surface follows the caret the same way.
- [x] 5.5 Decide the edge margin (design Open Question): 8px margin chosen and confirmed
  in-game (see design Open Questions). Verdicts recorded into `TESTING.md`.

## 6. Merge gate

- [x] 6.1 `dotnet build` clean; `dotnet test tests/Core.Tests` green.
- [x] 6.2 `openspec validate scroll-follow-caret-in-editor` passes.
- [x] 6.3 The in-game gate (§5) is green on at least the Lectern editor.

## 7. Cmd/Ctrl+Up/Down first-/last-row navigation (delta)

Folded in during the first playtest: plain Up/Down already moves one visual line, but there
was no document-top/bottom jump. Add one on the platform-correct shortcuts — Cmd+Up/Down on
macOS, Ctrl+Up/Down (and Ctrl+Home/End) on Windows. macOS Cmd is dropped by LibGUI before the
field sees it, so `ScribeDialogBase.OnKeyDown` remaps Cmd+Up/Down → Ctrl+Up/Down at the raw-
`KeyEvent` layer (the same seam that already remaps Cmd+Left/Right → Home/End). Rides the same
scroll-follow seam this change built (the edge caret is placed, then followed into view).

- [x] 7.1 Add `OnJumpToFirstRow` / `OnJumpToLastRow` callbacks to `ScribeMultilineField`,
  fired from the `Up` / `Down` / `Home` / `End` handlers when **Ctrl alone** is held
  (`e.Ctrl`, NOT the `Ctrl || Alt` word-jump gate — so Alt/Option+Up/Down stays a plain
  one-line move); otherwise the existing one-line `CaretVertical` move (Up/Down) or line
  start/end (Home/End). Remap Cmd+Up/Down → Ctrl+Up/Down in `ScribeDialogBase.OnKeyDown` so
  macOS Cmd reaches the field. Add a public `PlaceCaretAtEdge(bool atStart)` on the field
  State (collapse selection, snap caret to start/end, reset blink, fire the scroll-follow notify).
- [x] 7.2 Thread the two callbacks field → `ScribeEditRow` → `ScribeEditorContent` → dialog,
  mirroring `OnCommitAndAdvance`/`OnCommitAndRetreat`.
- [x] 7.3 Dialog handlers `EditorJumpToFirstRow` / `EditorJumpToLastRow` (shared
  `JumpToEditorEdge`): commit the row being left, `FocusEditorRow(0 | last)`, then resolve
  the target field's State via its focus node's owning element
  (`ResolveEditorFieldState`) and `PlaceCaretAtEdge` — start for the first row, end for the
  last. The placement + `FocusEditorRow`'s `pendingEnsureVisible` bring the edge into view.
- [x] 7.4 Update the in-game editor reference handbook entry
  (`craftinginfo-scribe-editor-reference-text`) with the new Row-navigation shortcut, and
  record the feature under `[Unreleased]` → Added in `CHANGELOG.md` (v1.1).
- [x] 7.5 In-game gate: in a Lectern/Notebook editor with several rows, Cmd+Up (macOS) /
  Ctrl+Up or Ctrl+Home (Windows) jumps focus to the FIRST row with the caret at its start and
  scrolls the top into view; Cmd+Down / Ctrl+Down or Ctrl+End jumps to the LAST row, caret at
  its end, bottom in view. Confirm plain Up/Down still moves one visual line, Home/End still
  move to line start/end, and Alt/Option+Up/Down does NOT jump (plain one-line move). Confirm
  the row left behind is committed (and an abandoned empty row still self-destructs). Verify on
  a cuneiform tablet surface too. macOS is the primary dev platform — verify Cmd there.
