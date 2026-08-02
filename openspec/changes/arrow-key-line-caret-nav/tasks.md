## 1. Implement Up/Down caret navigation in the field

- [x] 1.1 In `src/Mod/ScribeMultilineField.cs`, add the vertical-move geometry next to the existing
      caret helpers. Because the geometry (`visualLines`/`CaretToLineCol`/`OffsetAtPosition`/
      `MeasureWidth`) lives in the render box (`ScribeMultilineFieldRender`) — not the State — it
      landed there as one public `CaretOffsetVertical(fromCaret, direction)` (direction ±1) rather
      than two `CaretLineUp/Down` methods. It derives the caret's `(line, col)` via `CaretToLineCol`,
      measures its live X the paint way (`PadX + MeasureWidth(prefix)`), picks `line + direction`, and
      reuses `OffsetAtPosition` at that X on the target line's vertical middle. Edges without the
      mapper: target line `< 0` → `0`; `>= visualLines.Count` → `text.Length`. No goal-column memory.
- [x] 1.2 In `OnKeyDown`, add `case GlKeys.Up:` / `case GlKeys.Down:` alongside Left/Right, each
      calling `MoveCaret(CaretVertical(∓1), e.Shift)` (the State-side `CaretVertical` mirrors the
      click path's `OffsetAt`: resolve the render box through the GestureDetector proxy, delegate to
      `CaretOffsetVertical`), then `Handled(e)`. No new widget state.

## 2. Build & stage

- [x] 2.1 `dotnet build src/Mod/Mod.csproj -c Debug` — zero new warnings/errors (only the 2
      pre-existing warnings in GuiDialogClockmakerNotebook.cs / ItemScribeNotebook.cs).
- [x] 2.2 `bash build/restage.sh Debug` — staged 49 files; fresh DLL in place.

## 3. Manual in-game verification

- [x] 3.1 Manually test in-game (Lectern editor): with a multi-line row, press Up/Down and
      confirm the caret moves to the nearest column on the visual line above/below, staying within
      the row (no focus change, no commit). Works across both soft-wrapped and `\n`-broken lines.
      — Confirmed 2026-08-01 (user report).
- [x] 3.2 Manually test in-game (edge lines): Up on the first visual line jumps the caret to the
      start of the text; Down on the last visual line jumps it to the end. An empty/single-line row
      is a harmless no-op-ish move. — Confirmed 2026-08-01 (user report).
- [x] 3.3 Manually test in-game (Shift-extends): hold Shift with Up/Down and confirm the selection
      extends to the new caret rather than collapsing. — Confirmed 2026-08-01 (user report).
- [x] 3.4 Manually test in-game (Pin Tab): repeat 3.1–3.3 on a multi-line Pin Tab row and confirm
      identical behavior (same shared field), with no row-to-row focus movement.
      — Confirmed 2026-08-01 (user report).

## 4. Close out

- [x] 4.1 `openspec validate arrow-key-line-caret-nav --strict` — passes.
- [x] 4.2 Add the four in-game items to `TESTING.md` (fingerprints `6076bd50` / `83d68ffd` /
      `7bc7a0bc` / `63d445b4`) for playtest; verdicts to follow after in-game confirmation.
