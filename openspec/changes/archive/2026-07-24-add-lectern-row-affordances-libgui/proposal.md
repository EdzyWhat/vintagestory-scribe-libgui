## Why

The LibGUI editor rebuild left the lectern editor with only a checkbox and an editable text
field per row. Three per-row affordances that the pre-LibGUI native editor had — **delete a
row**, **reorder rows by mouse-drag**, and **pin/unpin a task** — were deferred during the
migration and never rebuilt. As a result a player currently **cannot remove a task, cannot
reorder tasks, and cannot pin a task** from the lectern at all: the only way to "delete" is to
blank a row's text (which leaves an empty row behind). The underlying document model already
supports all three (`ScribeDocument.DeleteBlock`, `MoveBlock`, `TogglePinned` and
`ScribeBlock.Pinned` exist and are unit-tested) — they are simply not wired to any GUI control.
This change re-establishes those affordances on LibGUI.

## What Changes

- Add a **per-row delete control** to the LibGUI editor rows, wired to `ScribeDocument.DeleteBlock`.
- Add a **per-row pin/unpin control** to task rows (not text-section rows), wired to
  `ScribeDocument.TogglePinned`, with a resting indicator so a pinned task reads as pinned
  without hovering, in both the read and editor views.
- Add **mouse-drag row reordering** to the editor view via a drag-handle (grip), wired to
  `ScribeDocument.MoveBlock`, including drag interaction feedback (a lift/drop indicator) —
  LibGUI has no built-in reorderable list, so this is built on `GestureDetector` pointer
  handling with hover-target tracking and the rows' existing `ValueKey<int>` identity.
- Route all three mutations through the **existing lock-gated autosave** path
  (`FlushIfDirty` → `ScribeEditDocumentMessage`); no new network message type is expected.
  (One open decision, resolved in design: whether the read view also exposes a lock-free
  pin-toggle like its existing lock-free task-done toggle, or pinning is editor-only.)
- Keep per-row controls **hover-conditional** and scaling with the text-size preference,
  consistent with the existing `lectern-gui-shell` requirements for these affordances.

Non-goals: no new document-model capabilities (Core is unchanged); no HUD rendering of pinned
tasks (a later tier); no text-section creation control (removed earlier by design); no
multi-select or drag-across-documents.

## Capabilities

### New Capabilities
<!-- none — this change wires existing model behavior to GUI controls; it does not introduce a new capability area -->

### Modified Capabilities
- `lectern-gui-shell`: the deferred per-row affordances become working LibGUI controls — the
  editor row's delete and pin controls perform their actions (not just reserve columns), and the
  reserved drag-handle column gains real drag-to-reorder interaction with drop feedback (the
  interaction feedback these requirements previously carved out as out-of-scope).
- `lectern-block`: the GUI gains the ability to **delete a block** and **reorder blocks**,
  joining the existing "Pin a task from the GUI" requirement, so the full set of document
  mutations the model supports is reachable from the lectern.

## Impact

- **Code:** `src/Mod/GuiDialogScribeLecternLibGui.cs` (editor row widgets `ScribeEditRow`/
  `ScribeEditRowState` and the content tree; new dialog methods wrapping the Core mutations,
  mirroring the existing `OnClickAddTask`). Possibly a small custom drag/gesture helper or a
  reorderable-column widget. Read-view row (`ScribeReadRow`) if the resting pin indicator and/or
  a read-view pin toggle are added.
- **Model:** none — `ScribeDocument.DeleteBlock`/`MoveBlock`/`TogglePinned` and `ScribeBlock.Pinned`
  already exist and are unit-tested; this change only calls them.
- **Network/persistence:** none new — edits flow through the existing lock-gated
  `ScribeEditDocumentMessage` autosave and persist via the existing tree-attribute path.
- **Assets:** the custom SVG glyphs (`scribepin`, `scribegrip`, `scribeclose`/delete) are already
  registered (`lectern-gui-shell`); this change consumes them from LibGUI widgets (verifying how
  LibGUI renders a registered icon code, or drawing them via a small custom render widget).
- **Prior art (rebuilt on LibGUI, not reused):** archived native changes
  `restore-row-affordance-columns`, `refine-row-affordance-visuals-2`,
  `lectern-drag-reorder-feedback` inform the intended layout/interaction.
