## Why

Three upcoming Scribe blocks — Notebook, Desk, and Clockmaker's Notebook — share the Lectern's
Read/Edit/Pinned views, three-column framing, visual theme, and completion-policy behavior.
All of that logic is currently locked in a 3,581-line monolith (`GuiDialogScribeLecternLibGui.cs`)
coupled directly to `BlockEntityScribeLectern` with no abstraction layer, meaning each new block
would copy-paste the entire dialog and diverge from the moment it was created.

## What Changes

- **New `IScribeDocumentHost` interface** — abstracts the 5 block-entity surfaces the dialog
  needs (block position, live document, lock query, optimistic-edit update, backdrop/layout/title
  metadata). Each block entity implements it.
- **New `ScribeLayout` + `ScribeLayoutProportions` structs** — generalize the former
  Lectern-only `LecternLayout` into a parameterized layout driven by width, art aspect ratio,
  and an overridable proportions record (so a Desk can widen its side columns without touching
  the base class).
- **New `ScribeDialogBase`** — absorbs ~95 % of the current monolith; operates entirely against
  `IScribeDocumentHost`. Exposes one virtual extension point (`GetExtraNavButtons()`) for per-item
  additional sidebar buttons.
- **Row content widgets split into separate files** — `ScribeLecternReadContent` →
  `ScribeReadContent` (in `ScribeReadContent.cs`), same for Editor and Pinned — names no longer
  imply Lectern.
- **`GuiDialogScribeLecternLibGui` slimmed to ~80 lines** — just a sealed subclass of the base
  with no overrides, plus the existing ctor wiring.
- **`BlockEntityScribeLectern` implements `IScribeDocumentHost`** — its `dialog` field retyped to
  `ScribeDialogBase?`.
- **`ScribeDocument.DefaultTitle` changed from `"Lectern"` to `"Untitled"`** — each host
  supplies its own per-item fallback title via `IScribeDocumentHost.DefaultDocumentTitle`.

## Capabilities

### New Capabilities

- `scribe-dialog-host-interface`: The `IScribeDocumentHost` interface and `ScribeLayout`/
  `ScribeLayoutProportions` structs that let any block entity drive the shared dialog layer
  without the dialog knowing the concrete block type.
- `scribe-dialog-base`: The shared `ScribeDialogBase` class: all view state, build methods,
  lock orchestration, autosave, title editing, scroll management, and the nav-button extension
  point. Operates solely against `IScribeDocumentHost`.

### Modified Capabilities

- `lectern-gui-shell`: The Lectern's GUI shell is no longer a monolith — it becomes a thin
  sealed subclass of `ScribeDialogBase`. The shell's documented layout ratios, nav buttons, and
  view-switch semantics are unchanged; only the implementation moves to the base class.

## Impact

- `src/Mod/GuiDialogScribeLecternLibGui.cs` — major rewrite (monolith → ~80-line subclass +
  content widget classes moved to separate files)
- `src/Mod/BlockEntityScribeLectern.cs` — adds `IScribeDocumentHost` impl, retypes `dialog` field
- `src/Core/ScribeDocument.cs` — one-line constant change (`DefaultTitle`)
- New files: `IScribeDocumentHost.cs`, `ScribeDialogBase.cs`, `ScribeReadContent.cs`,
  `ScribeEditorContent.cs`, `ScribePinnedContent.cs`
- No behavior changes: all existing Lectern functionality stays identical
- No network protocol changes, no asset changes, no JSON/patch changes
- `Core.Tests` and `Integration.Tests` suites are unaffected (test the model and block entity,
  not the dialog layer)
