## Context

`RefreshReadView()` (`ScribeDialogBase.ViewSwitching.cs:876-884`) unconditionally re-derives the
dialog's local `readViewFilterCategory`/`collapsedReadViewGroupIds` fields from the host's
persisted mirror (`host.ReadViewFilterCategory`/`host.CollapsedGroupIds`) every time it runs, with
no check for a pending local selection. Selecting a pill (`OnReadViewFilterCategoryChanged`,
`ViewSwitching.cs:845-851`) sets the dialog field locally, then fires a network packet
(`PersistReadViewState`) — the host's own mirror isn't updated until the server echoes the change
back through `SetReadViewStateFromReader` → `MarkDirty` → `FromTreeAttributes` →
`dialog.RefreshReadView()`.

Completing/un-completing a row (`OnReadViewCompleteTask`, `Layout.cs:985-1021`) applies an
optimistic local edit and then calls `RefreshReadView()` **synchronously, immediately** —
before the pill-selection round trip above has a chance to land. If a player selects a pill and
then completes a row in the same window, this synchronous refresh stomps the just-selected pill
back to whatever stale value the host mirror still holds (usually All). The same hazard exists at
every other `RefreshReadView()` call site that follows a row-state mutation:
`QuestObjectiveProgress.cs:53` and `TrackerCount.cs:204`.

The collapse toggle (`ScribeReadRowState.Build`, `ScribeReadContent.cs:608-621`) is built via
`ScribeRowButton` (`ScribeRowWidgets.cs:576-625`), which wraps its icon in a `GestureDetector` +
`Container` with full button chrome (background box, rounded corners, hover/press states) — the
same wrapper used for pin/delete/nav-tab buttons. It's appended as a separate slot *after* an
existing, always-present, fully-invisible grip spacer (`ScribeReadContent.cs:595-606`) that
reserves column alignment with the editor row's drag handle (there is no drag/reorder affordance
in Read View, per design D4 of an earlier change) — so today there are two adjacent slots: a dead
invisible spacer, then the bordered toggle.

## Goals / Non-Goals

**Goals:**
- Make the dialog's locally-selected pill authoritative until the server explicitly confirms a
  *different* value, so completing a row can never silently revert it.
- Replace the collapse toggle's chrome with a bare caret, placed in the existing (currently dead)
  grip-spacer column instead of a separate slot.

**Non-Goals:**
- No change to the persistence format (`readViewFilter`/`readViewCollapsed` on block entities,
  `scribeReadViewFilter`/`scribeReadViewCollapsed` on notebooks) or to `IScribeDocumentHost`'s
  contract shape.
- No change to how collapse state itself is computed or persisted — only the bug is in the pill
  mirror being stomped; collapse state isn't independently reported as reset by the playtester
  (only its toggle's chrome/position changes here).

## Decisions

**Guard `RefreshReadView` with a "pending local pill" flag, not a timestamp or sequence number.**
When `OnReadViewFilterCategoryChanged` sets the dialog's local `readViewFilterCategory`, it also
sets a `pendingReadViewFilterCategory` sentinel (the value just chosen). `RefreshReadView()` only
overwrites `readViewFilterCategory` from the host mirror when the host mirror's value equals the
pending sentinel (confirming the round trip landed) or when no pin is pending. The sentinel clears
once the mirror catches up. This is simpler than a monotonic sequence counter and sufficient here
because only one pill selection can be in flight at a time (radio-select, immediate re-render).
*Alternative considered*: skip `RefreshReadView`'s pill/collapse re-derivation entirely on the
completion path and only refresh the row list. Rejected — other players' concurrent pill/collapse
changes (multiplayer, shared block-entity hosts) do need to flow in on every refresh; the fix must
distinguish "this client's own pending change" from "someone else's confirmed change," not skip
refreshing altogether.

**Same guard shape for `collapsedReadViewGroupIds`.** Although no playtest report named a
collapse-state reset, the toggle handler follows the identical
local-then-network-echo shape as the pill handler, so the same "pending until confirmed" guard is
applied there too for consistency and to close off the same latent race.

**Move the caret into the existing grip-spacer slot rather than removing the spacer.** The grip
spacer's job (reserving column alignment with the editor view) is still needed for rows that show
no toggle. So the fix conditionally renders either the invisible grip glyph (no owned run) or a
bare caret glyph (has an owned run) in that one slot, instead of two adjacent slots.

**Caret renders as a plain glyph via `ScribeVsIconGlyph`, not `ScribeRowButton`.** `ScribeRowButton`
exists specifically to provide button chrome (background, hover/press, shadow) — using it and then
stripping the chrome via style overrides would fight the widget's purpose. Wrap the same
`GestureDetector` used elsewhere directly around a bare `ScribeVsIconGlyph("scribetriangleright"/
"scribetriangledown", ...)` instead, preserving the click/tap-to-toggle behavior without the
`Container`/`BoxStyle` wrapper.

## Risks / Trade-offs

[The pending-sentinel guard could get stuck if the server never echoes back (e.g. a dropped
packet)] → Low risk in practice: `MarkDirty(redrawOnClient: true)` always triggers a resync on any
subsequent block-entity save, and the sentinel only affects visual pill selection, not correctness
of the underlying document — worst case a player re-clicks the pill.

[Moving the caret into the grip column changes hit-testing/hover behavior for that column] →
Verify in-game per the existing task 5.2/5.3 manual tests (now folded into this change's tasks)
that the toggle remains clickable and rows without an owned run still show nothing there.
