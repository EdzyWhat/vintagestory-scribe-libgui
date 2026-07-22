## 1. Make Core trim-agnostic

- [x] 1.1 In `src/Core/ScribeDocument.cs`, remove `.Trim()` from `AddTask` so a task is stored
      with its text verbatim; keep the `string.IsNullOrWhiteSpace(text)` rejection. Update the
      method's doc-comment (drop "Text is trimmed"). *(Done.)*
- [x] 1.2 In `SetBlockText`, remove the `bool trimTask = true` parameter and the
      `trimTask ? text.Trim() : text` conditional — store `text` verbatim for tasks (still
      rejecting blank/whitespace-only), unchanged for text sections. Update the doc-comment: drop
      the `<param name="trimTask">` block; state that task text is stored verbatim and
      normalization is the caller's responsibility. *(Done.)*

## 2. Update the Mod call sites

- [x] 2.1 In `src/Mod/GuiDialogScribeLectern.cs`, `OnEditInputTextChanged`: change
      `SetBlockText(index, text, trimTask: false)` to `SetBlockText(index, text)`. Trim the now-stale
      "trimTask: false" explanation in the comment down to why the live path stores raw text
      (Shift+Enter trailing newline must survive to grow the row; trimming happens on commit). *(Done.)*
- [x] 2.2 In `NormalizeRowOnCommit`, change `SetBlockText(index, trimmed, trimTask: false)` to
      `SetBlockText(index, trimmed)`. The `TrimEnd()`-only behavior is unchanged; simplify the
      comment (no longer contrasting against a `trimTask: true` default that no longer exists). *(Done.)*

## 3. Collapse to one wrapped-text-height primitive

- [x] 3.1 In `src/Mod/ScribeRowElement.cs`, expose the shared primitive.
      *(Done — DEVIATION from the design's "scale at the call site" wording: rather than promote the
      SCALED-unit `MeasureWrappedTextHeightScaled` and make the hint caller do its own
      `scaled()`/`/GUIScale` dance, added `internal static MeasureWrappedTextHeightFixed(capi, text,
      font, fixedWidth)` that OWNS the scale→measure→divide and returns fixed units.
      `MeasureWrappedTextHeightScaled` stays `private`. This keeps the unit-conversion knowledge in
      one place instead of leaking GUIScale to callers — a cleaner "single primitive". `RowHeightFixed`
      now calls the fixed wrapper too.)*
- [x] 3.2 In `src/Mod/GuiDialogScribeLectern.cs` (~line 403), replaced the
      `ScribeBlockRowCell.MeasureWrappedHeight(...)` call with
      `Math.Max(ControlRowHeight, ScribeRowElement.MeasureWrappedTextHeightFixed(capi, hint, RowFont(), listWidth))`.
      The full-width hint (no reserved columns) keeps its `ControlRowHeight` floor; single-paragraph
      Lang string, no trailing newline → renders at the same height as before. *(Done.)*
- [x] 3.3 Removed `ScribeBlockRowCell.MeasureWrappedHeight`. Grepped src/ + tests/: no remaining
      `MeasureWrappedHeight` reference. *(Done.)*

## 4. Update tests

- [x] 4.1 In `tests/Core.Tests/ScribeDocumentTests.cs`, renamed `AddTask_TrimsSurroundingWhitespace`
      → `AddTask_StoresTextVerbatim_WithoutTrimming` (asserts whitespace preserved). Replaced the
      three `trimTask`-era `SetBlockText` cases with a single
      `SetBlockText_StoresTaskTextVerbatim_WithoutTrimming` (asserts `"  Find tin\n"` stored as-is).
      *(Done.)*
- [x] 4.2 Kept the blank/whitespace-only rejection tests. The removed
      `SetBlockText_StillRejectsWhitespaceOnlyTask_WhenNotTrimming` case's coverage is preserved by
      adding `[InlineData("\n")]` to the existing `SetBlockText_RejectsBlankForTask` theory (maps to
      the spec's "Blank or whitespace-only task text is rejected" scenario). The new verbatim test
      exercises leading-whitespace preservation. *(Done.)*

## 5. Build, verify, close out

- [x] 5.1 `dotnet test tests/Core.Tests` — all green (37/37; net -3 `trimTask` cases, +1 verbatim
      test, +1 theory case). *(Done.)*
- [x] 5.2 Built `src/Mod` Debug + Release — both 0 warnings / 0 errors; the removed parameter and
      removed helper leave no dangling reference. *(Done.)*
- [ ] 5.3 (Optional, Mac-testable) `./build/restage.sh Debug` and spot-check the lectern: empty-list
      hint still renders correctly; commit-time trailing-trim + interior-newline behavior unchanged
      (i.e. this refactor is visibly a no-op). *(Left for the next in-game session — this is a
      behavior-preserving refactor covered by the Core tests + clean build.)*
