## Why

`adopt-libgui-foundation` migrated the lectern **read** view to LibGUI but left the **editor** view on
the native absolute-bounds `GuiDialogScribeLectern` (design D2, an interim seam). That seam has a known
cost: the "switch to editor" control opens the native editor, and the native editor's "Done Editing"
returns to the *native* read view rather than the LibGUI one (backlogged during change 1). This change
is "change 2" of the migration — it ports the editor view to LibGUI, closing the seam so the whole
lectern GUI lives on one declarative widget tree, editing never round-trips through the native dialog,
and the native `GuiDialogScribeLectern` can be retired.

## What Changes

- **Port the multi-line editable row to production.** Promote the proven reference implementation
  `SpikeScribeMultilineField.cs` into a production LibGUI widget: a wrapping, auto-growing, focus-holding
  editable field built on LibGUI's public render/widget bases (LibGUI's stock `TextField` is single-line
  and `internal`).
- **Build a LibGUI editor view** in the existing `GuiDialogScribeLecternLibGui` dialog: a scrollable
  `ListView` of variable-height editable rows (checkbox + editable text), self-stateful and `ValueKey`-keyed
  (inherited pattern), on the inherited code-defined parchment theme.
- **Port the full keyboard model** from `ScribeRowTextInput.cs`: Enter = commit-and-advance (no line
  break), Shift+Enter = hard line break, Shift+Tab = commit-and-retreat, Esc = commit-and-close; caret
  navigation (Left/Right/Home/End, word-skip, line-end), selection (Shift+movement extends), clipboard,
  and the macOS Cmd/Alt caret translations.
- **Autosave / commit through the existing lock-gated server edit path** (`ScribeEditDocumentMessage`),
  unchanged from the native editor's semantics (server-authoritative, single-editor lock).
- **Apply the keypress-leak fix** deferred from change 1: the LibGUI editor captures all inputs while
  focused (`CaptureAllInputs()`) rather than letting unhandled keys fall through to the game.
- **Unify view switching within one dialog.** Read↔editor becomes an internal view swap in the LibGUI
  dialog; the backlogged native "Done Editing" → native-read-view return path is fixed because there is
  no longer a native dialog in the loop.
- **Retire the native editor.** Remove the editor path from `GuiDialogScribeLectern` (and delete the
  dialog if nothing else references it) and delete `SpikeScribeMultilineField.cs` once the port lands.
- Non-goals: no `src/Core/`, network-packet, codec, or persistence change — this is a Mod-layer view
  swap. Skeuomorphic visuals (custom checkbox glyph, text-size scaling, drag-reorder affordances) remain
  deferred to the later theme/affordance change. The lined-paper ruling stays dropped (change 1 decision).

## Capabilities

### New Capabilities
<!-- None. This migrates existing lectern-gui-shell behavior onto a new rendering framework. -->

### Modified Capabilities
- `lectern-gui-shell`: The editor-view requirements are re-expressed against LibGUI, mirroring how change
  1 re-expressed the read view. Specifically:
  - **REMOVED** (native-mechanism-specific, superseded by LibGUI): "Editor-view rows are custom-drawn in
    the interactive render pass" (the `ScribeRowElement`/scroll-clip mechanism); "Editor view edits in
    place with a single floating input" (the single repositioned `GuiElementTextInput` + `RowTextLayout`
    alignment mechanism) — the *observable* wrap/grow/commit behaviors it protected are preserved by new
    ADDED requirements below; "Editor caret conventions match desktop editing idioms cross-platform" as
    worded (mandates a `GuiElementTextInput` subclass) — re-expressed as a LibGUI-agnostic caret
    requirement.
  - **ADDED**: "Editor view is rendered by the LibGUI dialog" (one dialog owns read + editor, no native
    editor in the loop); "Editor rows are editable multi-line LibGUI widgets" (wrapping, auto-growing,
    keep-focused-row-in-view, one live editor at a time expressed against the widget tree); "Editor input
    captures keystrokes while focused" (the keypress-leak fix).
  - **MODIFIED**: "Editor rows navigate and commit by keyboard" (retain Enter/Shift+Enter/Shift+Tab/Esc
    semantics, re-expressed against the LibGUI field); "Editor caret conventions" (retain the macOS
    Cmd/Alt + Shift-selection idioms without mandating the native `GuiElementTextInput` subclass).
  - Kept as-is where framework-agnostic: "Read and editor views share a single row-list width."

## Impact

- **Code**: `src/Mod/GuiDialogScribeLecternLibGui.cs` (add the editor view + internal read↔editor swap);
  new production multi-line field widget (from `SpikeScribeMultilineField.cs`); `BlockEntityScribeLectern.cs`
  (open path now single-dialog, no native editor hand-off); `GuiDialogScribeLectern.cs` (editor path removed
  / dialog retired); delete `SpikeScribeMultilineField.cs`. Native editor helpers (`ScribeRowElement`,
  `ScribeRowTextInput`, `RowTextLayout`, `ScribeRowListScrollbar`, `ScribeBlockRowCell`) become dead once
  the native editor is gone — remove those no longer referenced.
- **Behavior**: fixes the backlogged switch-to-editor return path; editing stays server-authoritative and
  lock-gated (no semantic change to persistence/sync).
- **Dependencies**: none new — `gui` is already a production hard dep from change 1. No `src/Core/` or CI
  impact (Core untouched; cloud CI still builds/tests Core only).
- **Docs**: append any editor-port LibGUI lessons (public-API multi-line field, focus/keystroke capture,
  variable-height rows in an editable `ListView`) to `VSAPI-NOTES.md`; update `TESTING.md` with editor
  playtest items.
