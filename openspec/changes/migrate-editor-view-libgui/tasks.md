## 1. Promote the multi-line editable field to production

- [ ] 1.1 Create the production multi-line field widget from `SpikeScribeMultilineField.cs` (rename out of
  the `Spike` namespace into a production type, e.g. `ScribeMultilineField`), keeping the public-API
  architecture (`RenderBox` wrap/measure/paint + `RenderObjectWidget` bridge + `StatefulWidget` +
  `IFocusable`/`IKeyCharHandler`/`IKeyDownHandler`).
- [ ] 1.2 Promote the prototype edit subset to the full text-editing model: insert/backspace/delete, and
  decide (per design Open Question) whether to keep the plain `(text, caret, selection)` model or move to a
  controller abstraction — record the choice in a code comment.
- [ ] 1.3 Port the caret-navigation model from `ScribeRowTextInput.cs`: Left/Right/Home/End, word-skip,
  line-end, and Shift-extends-selection.
- [ ] 1.4 Port the macOS caret translations from `ScribeRowTextInput.cs`: Cmd+Left/Right = line start/end,
  Alt/Option+Left/Right = word-skip (so the engine's Ctrl-only/Alt-discarding behavior is corrected).
- [ ] 1.5 Port clipboard support (cut/copy/paste) and selection replacement.
- [ ] 1.6 Implement the key semantics: Enter (no Shift) = commit-and-advance with NO line break inserted;
  Shift+Enter = insert hard line break (grow the row); Shift+Tab = commit-and-retreat; Esc =
  commit-and-close.
- [ ] 1.7 Implement commit-time normalization: trim trailing blank lines and trailing whitespace while
  preserving interior newlines.

## 2. Build the LibGUI editor view

- [ ] 2.1 Add an editor content tree to `GuiDialogScribeLecternLibGui` alongside the read content tree, and
  a dialog-state flag selecting which `Build()` renders (read vs editor).
- [ ] 2.2 Implement editor rows as self-stateful, `ValueKey`-keyed widgets (checkbox + `ScribeMultilineField`),
  in a `variableHeight` `ListView` so each row measures to its wrapped height (inherited change-1 pattern).
- [ ] 2.3 Ensure exactly one field is actively editing at a time (focus moves the active editor; no two
  rows edit simultaneously).
- [ ] 2.4 Implement dynamic row growth/shrink on wrap-line-count change: the focused row grows/shrinks, rows
  below shift, and the scroll region updates.
- [ ] 2.5 Keep the focused row and caret scrolled into view as the row grows past the viewport bottom edge.
- [ ] 2.6 Apply the keypress-leak fix: capture all keyboard input while a field is focused
  (`CaptureAllInputs()`-equivalent), restoring normal game input on focus release.

## 3. Wire read↔editor swap and commits

- [ ] 3.1 Replace the read view's "switch to editor" hand-off (currently opens the native dialog) with an
  internal view swap that enters editor mode in the same dialog.
- [ ] 3.2 Acquire the single-editor lock when entering editor mode and release it when leaving, through the
  existing server flow (no new packets).
- [ ] 3.3 Route row commits through the existing lock-gated `ScribeEditDocumentMessage` path
  (server-authoritative), unchanged in semantics.
- [ ] 3.4 On leaving the editor view (finish/Esc), return to the LibGUI read view and refresh from the
  re-synced document (via the existing `RefreshReadView`/`ForceRebuild` path).
- [ ] 3.5 Update `BlockEntityScribeLectern.cs` open path so a single LibGUI dialog serves both views (no
  native editor hand-off) and re-verify Creative walk-away auto-close still fires while a field is focused.

## 4. Retire the native editor

- [ ] 4.1 Remove the editor path from `GuiDialogScribeLectern.cs` (`ComposeEditorView` and editor-only
  helpers/state); delete the dialog entirely if nothing else references it after the swap.
- [ ] 4.2 Delete `SpikeScribeMultilineField.cs` (superseded by the production field).
- [ ] 4.3 Delete native editor helpers that are now unreferenced — check each of `ScribeRowElement`,
  `ScribeRowTextInput`, `RowTextLayout`, `ScribeRowListScrollbar`, `ScribeBlockRowCell` and remove only
  those with no remaining references (build-verified, not assumed).

## 5. Build, test, playtest

- [ ] 5.1 `dotnet build src/Mod/Mod.csproj -c Release` builds clean (0 warnings/errors).
- [ ] 5.2 `dotnet test tests/Core.Tests` is green (no Core change expected).
- [ ] 5.3 `bash build/restage.sh Release` and `bash build/restage.sh Debug` stage successfully; assert
  `Gui.dll` is NOT present in the staged Mods folder.
- [ ] 5.4 In-game editor playtest (record verdicts via the testing checklist): switch read→editor stays in
  the LibGUI dialog; type/wrap/grow a row; Enter=commit-advance, Shift+Enter=break, Shift+Tab=retreat,
  Esc=commit-close; macOS Cmd/Alt carets + Shift-selection; clipboard; commit syncs to other viewers;
  keystrokes don't leak to the game; finishing editing returns to the LibGUI read view; walk-away
  auto-close still fires in survival while editing.

## 6. Docs

- [ ] 6.1 Append editor-port LibGUI lessons (public-API multi-line field, focus/keystroke capture,
  variable-height editable `ListView`, growing-row keep-in-view) to the `## LibGUI` section of
  `VSAPI-NOTES.md`.
- [ ] 6.2 Add the editor-view playtest items to `TESTING.md`.
