## 1. Promote the multi-line editable field to production

- [x] 1.1 Create the production multi-line field widget from `SpikeScribeMultilineField.cs` (rename out of
  the `Spike` namespace into a production type, e.g. `ScribeMultilineField`), keeping the public-API
  architecture (`RenderBox` wrap/measure/paint + `RenderObjectWidget` bridge + `StatefulWidget` +
  `IFocusable`/`IKeyCharHandler`/`IKeyDownHandler`).
- [x] 1.2 Promote the prototype edit subset to the full text-editing model: insert/backspace/delete, and
  decide (per design Open Question) whether to keep the plain `(text, caret, selection)` model or move to a
  controller abstraction — record the choice in a code comment. (Kept the plain
  `(text, caret, selection-anchor)` model — sufficient for a single-field-per-row editor.)
- [x] 1.3 Port the caret-navigation model from `ScribeRowTextInput.cs`: Left/Right/Home/End, word-skip,
  line-end, and Shift-extends-selection.
- [x] 1.4 Port the macOS caret translations from `ScribeRowTextInput.cs`: Cmd+Left/Right = line start/end,
  Alt/Option+Left/Right = word-skip. (Because LibGUI's `KeyboardEvent` drops the Command modifier, the Cmd
  combos are translated in the dialog's `OnKeyDown(KeyEvent)` override — where the mutable VS event still
  carries `CommandPressed` — before LibGUI maps it; Alt is delivered so word-skip works in the field directly.)
- [x] 1.5 Port clipboard support (cut/copy/paste) and selection replacement (Ctrl+A/C/X/V via
  `context.GetClipboard()`).
- [x] 1.6 Implement the key semantics (see design D4, revised post-playtest 2026-07-23): Tab = commit +
  advance (no tab glyph); Shift+Tab = commit + retreat; Enter (no Shift) = commit + insert a new task
  beneath the current row and focus it (Core `InsertTask`, unit-tested); Shift+Enter = insert hard line
  break (grow the row); Esc = commit + close (bubbles unhandled → dialog closes; blur-commit saves the edit).
- [x] 1.7 Implement commit-time normalization: trim trailing blank lines and trailing whitespace while
  preserving interior newlines.

## 2. Build the LibGUI editor view

- [x] 2.1 Add an editor content tree to `GuiDialogScribeLecternLibGui` alongside the read content tree, and
  a dialog-state flag (`isEditorMode`) selecting which `Build()` renders (read vs editor).
- [x] 2.2 Implement editor rows as self-stateful, `ValueKey`-keyed widgets (checkbox + `ScribeMultilineField`).
  NOTE (design revision): the editor uses a NON-virtualized `SingleChildScrollView` + `Column` of all rows,
  NOT a `variableHeight` `ListView` — LibGUI's `ListView` unmounts off-screen rows (destroying their focus
  node/state), which would break cross-row focus and drop a focused row that grows off-screen. Each field
  still auto-grows to its wrapped height; a lectern doc is small enough that non-virtualized has no cost.
- [x] 2.3 Ensure exactly one field is actively editing at a time (one `FocusNode` per row, owned by the
  dialog; `FocusManager` tracks a single primary focus; the parent moves focus row→row).
- [x] 2.4 Implement dynamic row growth/shrink on wrap-line-count change: the field's `RenderBox` sizes its
  height to the wrapped line count, and the `Column` re-lays-out so rows below shift and the scroll region
  updates.
- [x] 2.5 Keep the focused row and caret scrolled into view as the row grows past the viewport bottom edge
  (`Scrollable.EnsureVisible(focusedRow.Element)`, deferred to `OnRenderGUI` so it reads post-layout geometry).
- [x] 2.6 Apply the keypress-leak fix: `CaptureAllInputs()` returns true while in editor mode, and the field
  sets `e.Handled = true` on every consumed key so keystrokes don't reach the game; read mode does not capture.

## 3. Wire read↔editor swap and commits

- [x] 3.1 Replace the read view's "switch to editor" hand-off (previously opened the native dialog) with an
  internal view swap that enters editor mode in the same dialog.
- [x] 3.2 Acquire the single-editor lock when entering editor mode (server grant) and release it when leaving
  (`ScribeReleaseLockMessage`), through the existing server flow (no new packets).
- [x] 3.3 Route row commits through the existing lock-gated `ScribeEditDocumentMessage` path (throttled
  autosave tick + flush-on-commit), server-authoritative, unchanged in semantics.
- [x] 3.4 On leaving the editor view (Done editing / close), return to the LibGUI read view and refresh from
  the re-synced (optimistically-updated) document via `ForceRebuild`.
- [x] 3.5 Update `BlockEntityScribeLectern.cs` open path so a single LibGUI dialog serves both views (no
  native editor hand-off); walk-away auto-close preserved via the `InteractionRange` override (re-verify
  in-game in 5.4).

## 4. Retire the native editor

- [x] 4.1 Remove the native editor: deleted `GuiDialogScribeLectern.cs` entirely (nothing else referenced it
  after the swap).
- [x] 4.2 Delete `SpikeScribeMultilineField.cs` (superseded by the production field).
- [x] 4.3 Delete native editor helpers that are now unreferenced — deleted `ScribeRowElement`,
  `ScribeRowTextInput`, `RowTextLayout`, `ScribeRowListScrollbar`, `ScribeBlockRowCell`, and the
  native-dialog-only `ScribeInspectOverlay` (build-verified clean). `ScribeClientConfig` is KEPT (a
  code-unreferenced leaf POCO) pending a separate decision on it + the active `add-imgui-configlib-tuning`.

## 5. Build, test, playtest

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Release` builds clean (0 warnings/errors).
- [x] 5.2 `dotnet test tests/Core.Tests` is green (37/37; no Core change).
- [x] 5.3 `bash build/restage.sh Release` and `bash build/restage.sh Debug` stage successfully; asserted
  `Gui.dll` is NOT in the staged Mods folder (only `Scribe.dll` + `Scribe.Core.dll`).
- [ ] 5.4 In-game editor playtest (record verdicts via the testing checklist): switch read→editor stays in
  the LibGUI dialog; type/wrap/grow a row; Tab=commit-advance, Shift+Tab=retreat, Enter=new-task-below,
  Shift+Enter=break, Esc=commit-close; macOS Cmd/Alt carets + Shift-selection; clipboard; commit syncs to other viewers;
  keystrokes don't leak to the game; finishing editing returns to the LibGUI read view; walk-away
  auto-close still fires in survival while editing.

## 6. Docs

- [x] 6.1 Append editor-port LibGUI lessons (public-API multi-line field, focus/keystroke capture,
  non-virtualized editor scroll container, growing-row keep-in-view, Command-modifier gap) to the
  `## LibGUI` section of `VSAPI-NOTES.md`.
- [x] 6.2 Add the editor-view playtest items to `TESTING.md`.
