## Context

Scribe's task rows come in three widgets: read rows (`ScribeReadRow`), editor rows (`ScribeEditRow`) and
Pin Tab rows (in `ScribePinnedContent`). Each already wires its own actions to callbacks the dialog owns:
read rows toggle pins, editor rows delete and toggle pins by row index, and Pin Tab rows delete, unpin and
edit by `(DocId, TaskId)` through `ScribeDeleteTaskMessage` and `ScribeSetPinMessage`. Whether an action
is allowed is already decided by the dialog (`ReadOnly`, `CompletionAndPinLive`, the Lectern access mode,
editor lock state). Scribe has no right-click handling on rows today. The New Task picker
(`ScribeAddKindPicker`) shows a floating menu through an `OverlayEntry` pair (a full-screen barrier plus a
`CompositedTransformFollower` linked to the anchor with a `LayerLink`).

## Goals / Non-Goals

**Goals:**
- One menu widget shared by all three row types, so later changes add entries in one place.
- Each entry reuses the existing callback for its action. No new permission logic.

**Non-Goals:**
- Assign, date entries, or any action without an existing control.
- Keyboard navigation inside the menu beyond Escape to close.
- A menu on the HUD, on non-task rows (Text notes), or on Guestbook or History rows.

## Decisions

- **A shared `ScribeRowContextMenu` built on the picker's overlay pattern.** It takes a list of entries
  (label, optional icon, action) and an anchor. The barrier closes it on an outside click.
  Alternative: LibGUI's `Dropdown`. Rejected because a dropdown owns a selected value and a trigger button,
  while this menu is action-only and opened by right-click on an arbitrary row.
- **Rows build their entry list from the callbacks and flags they already receive.** An entry is added
  only when its callback is non-null and the row's existing gate allows it (the same condition that shows
  the matching hover button). This keeps "what is allowed" decided in exactly one place.
- **Right-click is detected with the row's existing pointer handling** (`PointerButton.Right` on press).
  The menu anchors at the row, aligned to the pointer's horizontal position, and is kept inside the dialog.
- **Edit from the read view reuses the dialog's existing editor entry,** including the editor lock grant on
  block surfaces, then focuses that row once the editor is built. A refused grant shows the existing
  locked-editor feedback.
- **The menu closes on any rebuild that removes its row,** so it never acts on a stale row.

## Risks / Trade-offs

- [Right-click inside an editor text field might be expected to do nothing] → The menu opens from the row,
  including over its text. A press that starts a text selection is a left-click, so there is no conflict.
- [Overlay menus have lifecycle hazards when the dialog rebuilds] → Reuse the picker's tested
  open/close pattern and close the menu in the row's dispose.
- [A ForceRebuild while the menu is open could remount buttons] → The menu uses plain gesture detectors,
  not `Button`, avoiding the known press-sound issue on remount.
