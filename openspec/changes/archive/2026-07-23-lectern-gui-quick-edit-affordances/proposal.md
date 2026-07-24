## Status: SUPERSEDED by adopt-libgui-foundation (2026-07-23)

**Barely started (1/18 tasks); abandoned, not carried into the LibGUI rebuild.** Its read-view
task-toggle is re-proven by the LibGUI read view; its remaining work (read/editor row-list
width-unification, icon-column scaling) targets the native `GuiComposer` view split that LibGUI
replaces wholesale. Archived without syncing its `lectern-gui-shell` delta into `openspec/specs/`.
Kept for the record only.

## Why

Playtesting the `skeuomorphic-lectern-gui` redesign (via the playtest-checklist app,
2026-07-19) surfaced three concrete friction points in daily use of the lectern GUI that
don't require a redesign, just targeted fixes: checking off a task currently requires
switching to editor mode first (a two-step action for the single most common
interaction), Read view and Editor view render at different dialog widths (jarring when
switching between them), and the drag/pin/delete icon columns take up a fixed amount of
horizontal space regardless of text size, eating into the row's actual text area more
than necessary at larger scales.

## What Changes

- Read view gains the ability to toggle a task's done state directly, without first
  switching to editor mode. **This has a real design tradeoff, not just a UI change**:
  Read view is currently documented and implemented as lock-free and non-mutating
  (`BlockEntityScribeLectern.RequestAccess`: "Read access is always granted and never
  touches the lock"). Toggling a task from Read view requires either transiently
  acquiring the editor lock per click, or a new lighter-weight edit path that bypasses
  the lock model entirely for this one field. See design.md for the options and
  recommendation — this needs explicit sign-off before implementation, not a default
  guess.
- `ReadListWidth` and `EditorListWidth` (currently 300 / 340, `ScribeClientConfig.cs`)
  unify into a single width so switching between Read and Editor view doesn't visibly
  resize the dialog.
- The drag-handle, pin, and delete icon columns (`DragHandleWidth`, `PinWidth`,
  `DeleteWidth` in `ScribeClientConfig.cs`) shrink as `TextSizeScale` increases, freeing
  more horizontal space for the row's actual text at larger scales, mirroring how row
  height/toggle size already scale with `TextSizeScale`.

## Capabilities

### New Capabilities

(none — this extends existing GUI behavior, no new capability domain)

### Modified Capabilities

- `lectern-gui-shell`: Read view's task-toggle interaction and lock-touching behavior
  changes (currently: never touches the lock; proposed: may transiently touch it, or use
  a new lightweight edit path — pending design.md decision). Row-list width becomes
  shared between Read/Editor view instead of two separate constants. Icon-column widths
  become a function of `TextSizeScale` instead of fixed constants.

## Impact

- `src/Mod/GuiDialogScribeLectern.cs`: Read view's `ComposeReadView` currently renders
  plain `AddStaticText` per row with no interactive toggle element at all — adding one
  means Read view's row rendering needs an interactive checkbox (mirroring
  `ScribeBlockRowCell`'s task-row toggle), not just static text.
- `src/Mod/BlockEntityScribeLectern.cs`: `RequestAccess`/`ApplyEdit`'s lock semantics may
  need a new code path for a single-field, non-editor-mode edit, depending on
  design.md's decision.
- `src/Mod/ScribeClientConfig.cs`: `ReadListWidth`/`EditorListWidth` collapse into one
  field; `DragHandleWidth`/`PinWidth`/`DeleteWidth` become base values scaled by
  `TextSizeScale` at their use sites (mirroring `ToggleWidth`'s existing pattern) rather
  than fixed constants.
- No `Core` changes expected — `ScribeDocument.ToggleTask` already exists and is reused
  as-is; this is a `Mod`-layer GUI/networking change only.

## Dependency

Hard prerequisite: `skeuomorphic-lectern-gui`'s scroll fixes (tasks 3.4a viewport-relative
row Y and 3.4b scrollbar thumb-drag defer) must be implemented and its 3.5 manual test
must pass before this change begins. Both changes edit the same row-rendering code
(`ComposeReadView`/`ComposeEditorView`/`ScribeBlockRowCell`), and this change's interactive
Read-view toggle assumes a correctly-scrolling row list; building on the still-broken
scroll base would layer new behavior onto an unstable foundation. Enforced by task 1.2.
