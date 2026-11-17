## Context

Scribe's document rows come in three widgets: read rows (`ScribeReadRow` in `ScribeReadContent.cs`), editor
rows (`ScribeEditRow`) and Pin Tab rows (in `ScribePinnedContent.cs`). Each wires its actions to callbacks
the dialog owns:

- Read rows toggle completion and pins (`onToggleTask`, `onTogglePinned`) and open links. They have no
  delete or edit control.
- Editor rows delete and toggle pins by row index (`OnDelete(index)`, `OnTogglePinned(index)`) inside the
  locked scratch document, for every block kind including notes.
- Pin Tab rows complete, edit, delete and unpin by `(DocId, TaskId)` through `ScribeDeleteTaskMessage` and
  `ScribeSetPinMessage`, without the editor lock. Notes appear there too.

The dialog decides what is allowed: `ReadViewIsReadOnly` (true only on a hard or fired Tablet),
`ReadViewCompletionAndPinLive`, and `host.IsLockedByOther(uid)` for the editor lock. Every Lectern behaves
as `Public` (lectern-access-mode), so the lock is the only gate on editing a shared block.

LibGUI 4.0.0 facts this design depends on (read from `reference/vslibgui`):

- `GestureDetector` fires `OnTap` and `OnPress` for any mouse button.
- `EventDispatcher.DispatchPointerDown` marks the event handled, moves focus to the innermost focusable
  element under the pointer (for any button), and dispatches with `stopOnHandled`. An ancestor never sees a
  press that a child handles.
- `CompositedTransformFollower` places content at an offset from a `LayerLink` target. It does not clamp
  or flip.
- There is no built-in context menu.

The New Task picker (`ScribeAddKindPicker`) shows a floating menu through an `OverlayEntry` pair: a
full-screen barrier (`GestureDetector(onTap: Close)`) plus a follower linked to the anchor. It re-applies
the dialog's `CurrentShade` to overlay content (the overlay sits outside `ScribeGlobalTint`) and renders
labels in cuneiform on the Tablet. It has no Escape handling.

## Goals / Non-Goals

**Goals:**
- One menu widget and one owner per dialog, so later changes add entries in one place.
- Existing actions reuse their existing paths. The one new action, read-view Delete, uses the existing
  identity delete with a lock check.
- A right-click never triggers a row's left-click behavior.

**Non-Goals:**
- Assign, date entries.
- Keyboard navigation inside the menu (Escape closes it; nothing else).
- A menu on the HUD, the Task Notice, Inbox previews, or Guestbook or History rows.

## Decisions

- **A shared `ScribeRowContextMenu` on the picker's overlay pattern, owned by `ScribeDialogBase`.** The
  dialog holds at most one open menu (`openRowMenu`). Opening a menu closes any open one. It takes the row's
  `TaskId`, an anchor `LayerLink`, the pointer position and a list of entries (label key, action). The
  barrier closes the menu on any press, including a right-click; the next right-click then opens the new
  row's menu. Re-dispatching the closing press to the row underneath was considered and rejected as extra
  complexity for one click.
  Alternative: LibGUI's `Dropdown`. Rejected because a dropdown owns a selected value and a trigger button,
  while this menu is action-only and opened on an arbitrary row.

- **Right-clicks are forwarded, not detected at the row.** Because LibGUI delivers a press only to the
  innermost handler and does not filter buttons, every row child that handles presses checks the button:
  - the checkbox, link and tracker names, the collapse caret, the hover Pin/Unpin/Delete buttons and the
    read-only tablet's text detector act only on the left button;
  - `ScribeMultilineField.OnFieldPress` returns early on a right press, without moving the caret, changing
    the selection or starting a drag.
  A right press anywhere on a row, on a child or on the row background, calls the row's
  `onSecondaryPress(pointer)` callback. That callback is null when the row has no menu. The row background
  gets a `GestureDetector` whose handler ignores every button except the right one. It sits outside the
  children, so it only receives presses that land on no child. LibGUI still moves keyboard focus on any
  press; that is accepted. A blur that reaches an editor row's empty-row purge is suppressed for the
  right-press case, so right-clicking an empty new row does not delete it.

- **Entry gating is one pure function.** `ScribeRowMenuEntries.For(view, kind, readOnly, pinLive, isPinned)`
  returns the entry list, and rows call it instead of checking callbacks for null:

  | View | Edit | Pin / Unpin | Delete |
  |---|---|---|---|
  | Read, editable | yes | if `pinLive` | yes |
  | Read, read-only (hard or fired Tablet) | no | if `pinLive` | no |
  | Editor | no | yes | yes |
  | Pin Tab | no | Unpin | yes |

  Every block kind gets the menu. The Task Notice passes `enableRowMenu: false` to `ScribeReadContent`
  (its callbacks are no-ops), so it shows no menu.

- **Read-view Delete uses the identity delete with a lock check.** It sends `ScribeDeleteTaskMessage` with
  a new `RespectEditorLock` field (ProtoMember 4, default false, so the Pin Tab's messages are unchanged).
  The server's `DeleteTaskForPlayer` refuses when `RespectEditorLock` is set and the source host
  `IsLockedByOther(player)`, and replies with the existing `scribe:scribe-gui-locked` error. The client
  also checks `IsLockedByOther` first, so the common case refuses without a round trip. The delete applies
  the acting player's Subtask Behavior, like the Pin Tab. The row is not removed optimistically: it goes
  away when the server's document update arrives (immediately in singleplayer).

- **Edit is shown whenever the document can be edited, regardless of who holds the lock.** Choosing it runs
  the dialog's existing `TryEnterEditor`, so a held lock shows the existing locked feedback. The target row
  is carried through the async grant in `pendingEditFocusTaskId`, modelled on `pendingHandbookAppend`:
  - set before the grant request;
  - consumed in `EnterEditorMode`, which maps the `TaskId` to its scratch index and sets
    `autoFocusRowOnRebuild`;
  - cleared on refusal, dialog close, or when the task is no longer in the document.
  Rows without a text field (item kinds) are scrolled into view without a caret.

- **Entries act on the target at choose time, by `TaskId`.** Editor entries resolve the `TaskId` to the
  current index when chosen. Pin/Unpin reads the current pin state when chosen and sets that explicit state,
  so a label made stale by a pin change elsewhere never flips the wrong way. The menu closes when its
  `TaskId` is no longer shown in the current view (deleted, filtered out, or collapsed away). Rebuilds that
  keep the row (illumination shading, resyncs, `ForceRebuild`) leave it open, re-anchored to the remounted
  row through the same `LayerLink` key.

- **Placement.** The menu opens below the row at the pointer's x. It flips above the row when it would pass
  the dialog's bottom edge and shifts left when it would pass the right edge. The dialog's bounds come from
  the dialog's own layout size, which `ScribeDialogBase` already tracks.

- **Look.** Entries are text tiles with hover highlight, using the picker's tile styling, the dialog's
  `CurrentShade` and cuneiform labels on the Tablet. Choosing an entry is left-click only.

- **Escape and lifecycle.** `ScribeDialogBase.OnKeyDown` checks for an open menu before the focused field
  and `base`: Escape closes the menu and is consumed, so the dialog stays open. The menu also closes on view
  switch, dialog close and when its row scrolls out of the viewport.

- **Later toggle entries.** Change 3 adds a "Repeat yearly" checkbox entry, shown only on a dated, completable
  row, so the gating function will take "row has a date" as one more input. The entry model leaves room for
  a toggle kind that does not close the menu. Only action entries close on choose.

## Risks / Trade-offs

- [Every row child must ignore the right button] → The children are few and named above. The entry-gating
  tests can't catch a missed child, so the playtest list includes right-clicking each control.
- [Focus still moves on right-click] → Accepted. LibGUI does it before dispatch. The right-press blur is
  exempt from the empty-row purge, so no data changes.
- [macOS right-click] → Ctrl-click and two-finger tap must reach VS as the right button. Verified in
  playtest.
- [Read-view Delete is a new permission] → Limited to players who could already edit the document, and
  refused while someone else holds the lock, so it cannot race another player's editor save.
