## Why

In both read and editor view, a long task/note wraps onto multiple lines and its row grows to
fit — this is correct, driven by `ScribeRowElement.RowHeightFixed` →
`capi.Gui.Text.GetMultilineTextHeight`. But when the player clicks into a long row **to edit
it**, the floating input collapses to a single line: the text runs off the left/right edges and
most of it is invisible, and typing past the row's width scrolls horizontally instead of
wrapping. The static label and the focused input therefore disagree about how the same text is
laid out — a jarring, unusable inconsistency the tester flagged.

**Root cause (decompile-confirmed, `VintagestoryAPI.dll`):** the editor's in-place input is
`ScribeRowTextInput : GuiElementTextInput`. `GuiElementTextInput` sets `multilineMode = false`
on its shared base `GuiElementEditableTextBase`, so it is inherently single-line and
horizontally-scrolling. VS's multi-line editable is a *sibling* class, `GuiElementTextArea`, on
the same base: it sets `multilineMode = true` **and** ships an `Autoheight` mechanism whose
`TextChanged()` recomputes `Bounds.fixedHeight` from `GetMultilineTextHeight` on every keystroke
— exactly the wrap-and-grow behavior the row already uses for its static label.

## What Changes

- The editor's floating edit input wraps its text onto multiple lines (matching the static
  label) instead of scrolling a single line horizontally, by rebasing `ScribeRowTextInput` on
  the engine's multi-line editable (`GuiElementTextArea`) rather than the single-line
  `GuiElementTextInput`. All existing behaviors are preserved: the Mac caret conventions
  (Cmd/Alt/Shift+arrows), cross-row Enter/Shift+Tab navigation and Esc panic-close, blur-commit,
  the borderless look, and the two clip corrections (off-screen render skip + scissor re-assert).
- As the player types text that overflows onto a new line (or deletes back), the **focused
  row's height grows/shrinks dynamically and naturally**, the rows below it shift, and the
  scroll region updates — so the focused row behaves exactly like a static wrapped row, live.
- The focused row stays in view as it grows (reusing the existing `scrollFocusedRowIntoView`
  one-shot), and the caret/focus is preserved across the height-driven recompose (reusing the
  existing `RecomposeEditorViewPreservingFocus` path).
- **Shift+Enter inserts a hard line break** inside the focused row (the standard "Enter submits,
  Shift+Enter newlines" convention), while plain Enter keeps its S2 commit-and-advance behavior.
  A hard newline grows the row exactly like a soft wrap does (same auto-height path). On commit,
  the text is **trailing-trimmed** — interior newlines the player put between text are preserved,
  but trailing blank lines / trailing whitespace are stripped so a row can't commit as
  empty-looking-but-tall.

So a row's text is now a genuine multi-line string: soft-wrapped for display AND able to carry
player-inserted `\n`. This is a change from S2's single-logical-line model, but a cheap one: the
read view already renders embedded `\n` as hard breaks (`TextDrawUtil.Lineize` has an explicit
`'\n'` case, decompile-confirmed), `GetMultilineTextHeight` measures them, and the codec writes
`ScribeBlock.Text` as a length-prefixed UTF-8 string — so `\n` round-trips through persistence and
network sync **with no codec version bump** and no read-view change. A codebase grep found no
`.Split`/truncation/single-line assumption on `Text`.

## Capabilities

### New Capabilities

(none — this corrects the behavior of the existing editor in-place input in `lectern-gui-shell`)

### Modified Capabilities

- `lectern-gui-shell`: the "Editor view edits in place with a single floating input" requirement
  gains that the floating input SHALL wrap long text onto multiple lines (aligned with the static
  label's wrapping) and SHALL grow/shrink the row height dynamically as typed text wraps/unwraps,
  shifting subsequent rows and the scroll region — rather than presenting a single horizontally-
  scrolling line.

## Impact

- `src/Mod/ScribeRowTextInput.cs`: change the base class `GuiElementTextInput` →
  `GuiElementTextArea`; reconcile the `ComposeTextElements` border-strip override against the
  TextArea's own compose (which already builds the highlight texture we hand-rolled); hook the
  per-keystroke height change so the dialog re-measures and recomposes the row list.
- `src/Mod/GuiDialogScribeLectern.cs`: on the input's height change, re-measure the focused
  row (and thus `rowHeights`/content height) and recompose preserving focus + scroll-into-view.
  Reuses the existing `RecomposeEditorViewPreservingFocus` and `scrollFocusedRowIntoView`.
- Wrap-parity risk: the static label wraps via `AutobreakAndDrawMultilineTextAt` and the input
  wraps via `GuiElementTextArea`'s own line-breaking. If they break at different points for the
  same width, focus/blur would jump by a line. Design.md addresses reconciling the wrap width so
  the handoff stays jump-free (S2 spent real effort on this via the shared `RowTextLayout`).
- No `Core` changes required for correctness: `ScribeBlock.Text` may now carry `\n`, but the
  codec (length-prefixed UTF-8 string) and read-view rendering already handle it — no codec
  version bump, no networking change, no persistence change. The commit-time trailing-trim is a
  `Mod`-layer normalization at the edit-commit site (a plain `TrimEnd`), not a Core model change.
  Client-render + edit behavior only, covered by manual playtest.
- Sequencing: lands BEFORE `restore-row-affordance-columns` — both touch the editor's
  `ScribeRowElement` / row-measure / recompose path, and settling row-height behavior first means
  the affordance-column work builds on a stable layout rather than rebasing over it.
