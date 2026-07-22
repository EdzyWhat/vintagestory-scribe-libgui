## 1. Make Core trim-agnostic

- [ ] 1.1 In `src/Core/ScribeDocument.cs`, remove `.Trim()` from `AddTask` so a task is stored
      with its text verbatim; keep the `string.IsNullOrWhiteSpace(text)` rejection. Update the
      method's doc-comment (drop "Text is trimmed").
- [ ] 1.2 In `SetBlockText`, remove the `bool trimTask = true` parameter and the
      `trimTask ? text.Trim() : text` conditional — store `text` verbatim for tasks (still
      rejecting blank/whitespace-only), unchanged for text sections. Update the doc-comment: drop
      the `<param name="trimTask">` block; state that task text is stored verbatim and
      normalization is the caller's responsibility.

## 2. Update the Mod call sites

- [ ] 2.1 In `src/Mod/GuiDialogScribeLectern.cs`, `OnEditInputTextChanged`: change
      `SetBlockText(index, text, trimTask: false)` to `SetBlockText(index, text)`. Trim the now-stale
      "trimTask: false" explanation in the comment down to why the live path stores raw text
      (Shift+Enter trailing newline must survive to grow the row; trimming happens on commit).
- [ ] 2.2 In `NormalizeRowOnCommit`, change `SetBlockText(index, trimmed, trimTask: false)` to
      `SetBlockText(index, trimmed)`. The `TrimEnd()`-only behavior is unchanged; simplify the
      comment (no longer contrasting against a `trimTask: true` default that no longer exists).

## 3. Collapse to one wrapped-text-height primitive

- [ ] 3.1 In `src/Mod/ScribeRowElement.cs`, change `MeasureWrappedTextHeightScaled` from
      `private static` to `internal static` so it can be shared. Keep its signature
      `(ICoreClientAPI capi, string text, CairoFont font, double scaledWidth)` and its doc-comment.
- [ ] 3.2 In `src/Mod/GuiDialogScribeLectern.cs` (~line 403), replace the
      `ScribeBlockRowCell.MeasureWrappedHeight(capi, hintText, RowFont(), listWidth, clientConfig.ControlRowHeight)`
      call with `ScribeRowElement.MeasureWrappedTextHeightScaled(...)` scaled the same way
      `RowHeightFixed` scales its width, then apply the `Math.Max(minHeight, …)` floor at the call
      site (preserving the old `ControlRowHeight` floor). Confirm the hint still renders at the
      same height (single-paragraph Lang string, no trailing newline → both measures agree).
- [ ] 3.3 Remove `ScribeBlockRowCell.MeasureWrappedHeight` from `src/Mod/ScribeBlockRowCell.cs`.
      Grep to confirm no other caller remains.

## 4. Update tests

- [ ] 4.1 In `tests/Core.Tests/ScribeDocumentTests.cs`, update `AddTask_TrimsSurroundingWhitespace`
      to assert verbatim storage (rename to reflect "preserves whitespace"), and adjust
      `SetBlockText_TrimsTaskByDefault` / the `trimTask`-specific cases
      (`SetBlockText_PreservesTrailingNewlineWhenNotTrimming`,
      `SetBlockText_StillRejectsWhitespaceOnlyTask_WhenNotTrimming`) to the new verbatim-storage
      contract — removing assertions that depend on the removed `trimTask` parameter.
- [ ] 4.2 Keep all blank/whitespace-only rejection tests (`AddTask_RejectsBlankText`,
      `SetBlockText_RejectsBlankForTask`). Add a test that `SetBlockText` on a task stores leading
      whitespace verbatim (maps to the new spec's "changing a task's text preserves surrounding
      whitespace" scenario).

## 5. Build, verify, close out

- [ ] 5.1 `dotnet test tests/Core.Tests` — all green.
- [ ] 5.2 Build `src/Mod` Debug + Release clean (0 warnings / 0 errors) to confirm the removed
      parameter and removed helper leave no dangling reference.
- [ ] 5.3 (Optional, Mac-testable) `./build/restage.sh Debug` and spot-check the lectern: empty-list
      hint still renders correctly; commit-time trailing-trim + interior-newline behavior unchanged
      (i.e. this refactor is visibly a no-op).
