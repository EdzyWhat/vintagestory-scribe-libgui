## Why

Every player hits a jarring bug the **first time** they open any Scribe item they didn't craft
(notebook, clockmaker's notebook, tablet): the GUI opens and immediately flickers closed, and only
a second right-click makes it stay. The dialog's "close when the item leaves my hand" guard is
mis-firing on an in-place server re-sync of the *same* held item — not a real hand-switch. This is a
1.0 blocker: it makes the mod's core interaction feel broken on first contact.

## What Changes

- Harden the item-hosted dialogs' auto-close guard so an **in-place `SlotModified` re-sync of the
  currently held item** (same hotbar slot, contents rewritten by the server) does NOT close the
  dialog. Only a genuine switch-away — the active hand no longer holds *a Scribe document item at
  all* — closes it on the `SlotModified` path.
- Keep the existing **strict DocId comparison on the real hand-switch path** (`AfterActiveSlotChanged`)
  unchanged, so scrolling/keying the hotbar to a *different* Scribe item still closes the old dialog
  (the behavior `ActiveHandItemHostsThisDocument` was introduced to fix).
- Applies uniformly to all three item-hosted dialogs. The clockmaker's-notebook dialog inherits the
  notebook dialog's handlers, so it is covered by the notebook fix; the tablet dialog carries its own
  copy of the same handler and gets the matching fix — while preserving the tablet's legitimate
  wet→hard/fired in-place state transition (which also rides `SlotModified`).

The root cause (traced through the code): a first open of a not-yet-crafted item makes the client
generate a fresh `ScribeDocument`/`DocId` locally, then notifies the server; the server records the
one-time "Picked up" history entry, `MarkDirty()`s the slot, and re-syncs the stack back — but
deliberately without the client's document (it doesn't know the client-generated DocId). That
re-sync fires `SlotModified`, the guard reads a stack whose DocId no longer matches the open dialog,
concludes "switched away," and closes. The second open finds the `PickedUp` entry already present,
so no re-sync fires and the dialog stays.

## Capabilities

### New Capabilities
<!-- None — this is a behavioral correction to existing item-dialog lifecycle requirements. -->

### Modified Capabilities
- `notebook-item`: the "Notebook dialog closes automatically when item leaves the hand" requirement
  is refined to distinguish a genuine hand-switch-away (closes) from an in-place re-sync of the same
  held item (must NOT close). Covers the clockmaker's notebook via dialog inheritance.
- `clay-wax-tablet-item`: the equivalent tablet "close on switch-away" behavior gets the same
  refinement, explicitly preserving the wet→hard/fired in-place transition path.

## Impact

- **Code:** `src/Mod/GuiDialogScribeNotebook.cs` and `src/Mod/GuiDialogScribeTablet.cs`
  (`OnHotbarSlotModified` / `OnActiveSlotChanged` handlers); possibly a shared helper on
  `src/Mod/ScribeDialogBase.cs` next to `ActiveHandItemHostsThisDocument()`. No `src/Core/` change,
  no serialization/codec change, no network-protocol change.
- **Tests:** the behavior is a client-side GUI-lifecycle interaction (mouse + hotbar + server
  round-trip), so it is verified in-game via a `TESTING.md` item rather than the Core suite; no Atlas
  scenario change expected.
- **Risk:** low and localized. The one thing the fix must not regress is the tablet's in-place
  wet→hard/fired transition, which legitimately uses `SlotModified` — the design must handle it
  explicitly.
