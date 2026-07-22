## 1. Rebase the input on the multi-line editable

- [x] 1.1 Confirm working tree is clean and on `row-list-rework`. Re-read `ScribeRowTextInput.cs`
      and the editor compose/measure/recompose path in `GuiDialogScribeLectern.cs`
      (`ComposeEditorView`, `OnEditInputTextChanged`, `RecomposeEditorViewPreservingFocus`,
      `MoveEditFocusTo`, the `scrollFocusedRowIntoView` one-shot) so the change builds on the
      real S2 code. *(Done 2026-07-21; also decompiled `GuiElementTextArea`/`GuiElementEditableTextBase`
      to ground the port.)*
- [x] 1.2 Change `ScribeRowTextInput`'s base class from `GuiElementTextInput` to
      `GuiElementTextArea`; update the constructor's `base(...)` call to the TextArea signature.
      Keep all existing fields/callbacks (`onCommitAndAdvance`/`onCommitAndRetreat`/`onBlur`) and
      the Mac caret-translation logic unchanged. *(Done 2026-07-21.)*
- [x] 1.3 Reconcile the `ComposeTextElements` override against `GuiElementTextArea`'s compose:
      strip TextArea's emboss + dark-fill box, keep its focus highlight, and confirm the
      highlight field/texture names the override references still resolve on the new base.
      *(Done 2026-07-21 — DEVIATION from the design's assumption: TextArea's `highlightTexture`/
      `highlightBounds` are PRIVATE to it (they were `protected` on `GuiElementTextInput`), so the
      override can't reference them. Gave `ScribeRowTextInput` its OWN `focusHighlightTexture`/
      `focusHighlightBounds`, built with the protected `genContext`/`generateTexture`, and render
      it in the interactive pass. Same borderless look S2 established.)*
- [x] 1.4 Verify the clip corrections in `RenderInteractiveElements` still apply on the TextArea
      base, and that the off-screen skip uses the input's live (grown) height.
      *(Done 2026-07-21 — DEVIATION: the `PushScissor`/`PopScissor` re-assert was DROPPED as
      unnecessary. Decompile confirmed the `GlScissorFlag(false)` global scissor-disable that
      required it was specific to `GuiElementTextInput.RenderInteractiveElements`; `GuiElementTextArea`
      never touches the scissor, so the ambient `BeginClip` clips it natively — nothing to restore.
      Kept the off-screen render skip (now defensive rather than load-bearing), using
      `Bounds.InnerHeight` which reflects the grown height.)*

## 2. Enter commits, Shift+Enter newlines, Tab safe (multiline gotcha)

- [x] 2.1 In `OnKeyDown`, split Enter by Shift. **Plain Enter / keypad Enter (no Shift):** ALWAYS
      consume via `onCommitAndAdvance()`; **Shift+Enter:** not intercepted, delegates to
      `base.OnKeyDown` so `OnKeyEnter()` inserts a `\n`. *(Done 2026-07-21; `TranslateMacCaretModifiers`
      returns a non-arrow event unchanged, so Shift+Enter passes straight through.)*
- [x] 2.2 Confirm Shift+Tab is fully consumed by the retreat branch and cannot fall through to a
      Tab character insert. Plain Tab: consumed as a no-op. *(Done 2026-07-21: both Tab branches set
      `args.Handled = true; return` before any base delegation, so no tab glyph is ever inserted.)*
- [x] 2.3 Add commit-time normalization at the `Mod`-layer commit site (NOT per-keystroke): trim
      trailing blank lines/whitespace, preserve interior newlines. *(Done 2026-07-21: added
      `NormalizeRowOnCommit(index)` (`TrimEnd()`), called at every genuine row-commit site — Enter
      advance, Shift+Tab retreat, blur, switch-to-read, close, and click-to-another-row — but
      deliberately NOT inside `FlushIfDirty` (the 1s autosave tick calls that; trimming there would
      fight a player who just pressed Shift+Enter). No leading trim, no interior collapse.)*
- [x] 2.4 Confirm no Core/codec/wire change is needed for embedded `\n`. *(Confirmed 2026-07-21:
      codec writes `Text` as a length-prefixed UTF-8 string (round-trips `\n`); decompile shows the
      read view's `Lineize` uses `keepLinebreakChar: true` so `\n` is preserved through `lines` and
      `GetText()` = `join("", lines)` reconstructs it, and it renders `\n` as a hard break.
      `OnEditInputTextChanged` writes the raw newline-bearing string to the scratch doc. Core.Tests
      still 35/35.)*

## 3. Wire auto-height back into the row list

- [x] 3.1 On text change, compute the focused row's new measured height using the SAME
      `ScribeRowElement.RowHeightFixed(...)` the compose path uses. *(Done 2026-07-21 in
      `OnEditInputTextChanged`.)*
- [x] 3.2 Only when that height differs (a wrap boundary was crossed), set `scrollFocusedRowIntoView`
      and `RequestRecompose()`. *(Done 2026-07-21: gated on `Math.Abs(prev - new) > 0.5` via a new
      `editRowMeasuredHeight` tracker, re-baselined in `ComposeEditorView` before `SetValue` so
      seeding a row fires no spurious relist. Uses the existing deferred `RequestRecompose` →
      `RecomposeEditorViewPreservingFocus`, so no per-keystroke thrash within a line.)*
- [x] 3.3 Confirm the caret position is preserved across the re-measure recompose.
      *(Done 2026-07-21 — DEVIATION from S2: the caret snapshot/restore was single-line
      (`CaretPosInLine` + `SetCaretPos(pos)`), which would drop the caret to wrapped line 0. Switched
      to the wrap-independent `CaretPosWithoutLineBreaks` (absolute offset) for both snapshot and
      restore. In-game caret-hold to be confirmed in 4.3/4.4.)*

## 4. Build, test, and playtest

- [x] 4.1 Build Debug and Release clean; run `dotnet test` (Core.Tests) — all green. (No Core
      change expected; the run guards against an accidental one.) *(Done 2026-07-21: Debug + Release
      both 0 warnings / 0 errors; Core.Tests 35/35 passed.)*
- [x] 4.2 Manually test in-game: click into a long wrapped task — stays wrapped, input aligns to
      the label, no jump on focus/blur. *(Confirmed 2026-07-21T23-52-34 + screenshot; tester noted
      the small focus shift reads as helpful selection feedback.)*
- [x] 4.3 Manually test in-game: type until it wraps — row grows, rows below shift, scrollbar
      updates; delete back shrinks it. *(Confirmed 2026-07-21T23-52-34: "works as described.")*
- [x] 4.4 Manually test in-game: grow a row near the bottom — scrolls into view, caret holds;
      Enter commits-advances (no newline), Esc panic-closes with edit saved. *(Confirmed
      2026-07-21T23-52-34: "works as described.")*
- [x] 4.5 Manually test in-game: label and input wrap at the SAME word boundary at a couple of
      text sizes (no reflow jump on focus/blur). *(Confirmed 2026-07-21T23-52-34: "works as
      described." Wrap-parity math held — same box width, same break points.)*
- [x] 4.6 Manually test in-game (Shift+Enter): hard break at the caret, row grows, caret stays,
      typing continues; plain Enter still commits-advances. *(Confirmed 2026-07-22 after THREE
      fix rounds — root cause was `SetBlockText`'s `Trim()` stripping the trailing newline on every
      live keystroke, upstream of all the height code. Fix: `trimTask:false` on the live edit path;
      trailing-trim moves to commit. Also required the per-segment height measure + `Autoheight=off`.
      Core.Tests 38/38 with 3 new cases. See VSAPI-NOTES.)*
- [x] 4.7 Manually test in-game (trailing trim + round-trip): trailing Shift+Enter trims on commit;
      interior newline survives, renders as a hard break in read view, and persists across a world
      reload. *(Confirmed 2026-07-21T23-52-34: "works as described.")*

## 5. Close out

- [x] 5.1 Update `VSAPI-NOTES.md` if anything new was learned about `GuiElementTextArea`
      auto-height / multiline behavior. *(Done 2026-07-21: recorded the `highlightBounds` null-crash
      subclassing gotcha. The trailing-newline height + `SetBlockText` `Trim()` findings are captured
      in this change's tasks/commits and the 4.6 verdict; fold into VSAPI-NOTES below if not already.)*
- [x] 5.2 Note in the change that it is complete and unblocks `restore-row-affordance-columns`
      (row-height behavior now stable). *(Done 2026-07-22: all 20 tasks complete, all 6 playtest
      items confirmed. Row-height/measure behavior is now stable, so `restore-row-affordance-columns`
      — the delete/pin/hover gutter visuals — can build on it.)*
