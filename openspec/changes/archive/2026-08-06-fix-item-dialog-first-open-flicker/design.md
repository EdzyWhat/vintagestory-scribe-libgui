## Context

Item-hosted Scribe dialogs (notebook, clockmaker's notebook, tablet) close themselves when the
player switches away from the held item. Two events drive this:

- `capi.Event.AfterActiveSlotChanged` → `OnActiveSlotChanged` — the active hotbar slot NUMBER
  changed (a real hand-switch).
- `IInventory.SlotModified` → `OnHotbarSlotModified` — a slot's CONTENTS changed; the handler
  forwards to `OnActiveSlotChanged` only when the modified slot is the active one.

Both funnel into `ActiveHandItemHostsThisDocument()` (in `ScribeDialogBase`), which reads the active
hand stack, and returns true only if it is an `IScribeDocumentItem` whose stored `DocId` equals the
open dialog's `host.Document.DocId`. If false, the handler calls `TryClose()`.

That DocId-strict comparison was introduced deliberately (see the method's doc-comment): an earlier
guard tested only "is the new hand item some Scribe item," which wrongly kept a dialog open when the
player keyed the hotbar to a DIFFERENT Scribe item. DocId identity fixed that.

The regression: the SAME strict comparison is wrong on the `SlotModified` path. On a first open of a
not-yet-crafted item, the client generates a fresh `ScribeDocument`/`DocId` locally
(`NotebookHost` ctor) and notifies the server. The server records the one-time "Picked up" history
entry, `MarkDirty()`s the slot, and re-syncs the stack back to the client — but intentionally WITHOUT
the document (it can't know the client-generated DocId; see `TryRecordPickedUpOnSlot` and its
comment). The re-sync fires `SlotModified` on the active slot, `ActiveHandItemHostsThisDocument()`
reads a stack whose DocId no longer matches, and the dialog closes one frame after opening. The
second open finds the `PickedUp` entry already present → no re-sync → no flicker.

This was diagnosed by reading the code end-to-end (open path → server notify handler → client
re-sync → close guard), consistent with the project's "measure/trace, don't theorize" rule for GUI
lifecycle bugs.

## Goals / Non-Goals

**Goals:**
- Eliminate the first-open flicker for all three item-hosted dialogs.
- Preserve close-on-drop and close-on-switch-to-a-different-Scribe-item exactly as they are today.
- Preserve the tablet's in-place wet→hard/fired transition, which also rides `SlotModified`.
- Keep the change confined to `src/Mod/`; no Core, codec, or network-protocol changes.

**Non-Goals:**
- Reworking how a not-yet-crafted item generates its client-side DocId, or making the server aware of
  the client DocId on open. That is a larger sync-semantics change and is unnecessary to fix the
  flicker.
- Changing the `AfterActiveSlotChanged` (real hand-switch) semantics in any way.
- Suppressing or reordering the "Picked up" history entry.

## Decisions

### Decision 1: Split the close rule by trigger — strict identity for hand-switch, presence-only for in-place re-sync

Keep `OnActiveSlotChanged` (the real hand-switch) calling the existing DocId-strict
`ActiveHandItemHostsThisDocument()` → `TryClose()`. Change `OnHotbarSlotModified` so that an in-place
modification of the active slot closes the dialog ONLY when the active hand no longer holds a Scribe
document item at all — i.e. a "presence" check, not a DocId-identity check.

Rationale: the two triggers answer different questions. A slot-number change asks "am I still holding
the item this dialog is for?" — identity is right. An in-place content rewrite of the slot I'm still
holding asks "did the thing in my hand stop being a Scribe item?" — presence is right, because the
physical item did not change; only its bytes were re-synced. This is the smallest change that fixes
the flicker without weakening the switch-to-different-item guard.

Concretely, add a sibling helper on `ScribeDialogBase` next to `ActiveHandItemHostsThisDocument()`,
e.g. `ActiveHandHoldsAnyScribeDocumentItem()` returning whether the active hand stack's collectible
is an `IScribeDocumentItem` (the presence half of the existing check, without the DocId comparison).
`OnHotbarSlotModified` closes only when that returns false.

**Alternatives considered:**
- *Give the guard a grace period / ignore the first N frames after open.* Rejected: frame-count
  hacks are fragile and this project has explicitly moved away from timing-based GUI workarounds.
- *Make the client re-adopt the re-synced stack's DocId (write the client document back on
  SlotModified).* Rejected: heavier, risks fighting the server's authoritative sync, and touches the
  delicate open-time DocId generation the code comments warn against.
- *Have the server echo the client DocId in the PickedUp re-sync so identity still matches.* Rejected
  as a larger protocol change for no user-visible benefit beyond what Decision 1 already achieves.

### Decision 2: Apply to both handler copies; the clockmaker inherits

`GuiDialogScribeNotebook` and `GuiDialogScribeTablet` each carry their own `OnActiveSlotChanged` /
`OnHotbarSlotModified` pair, so both are edited. `GuiDialogClockmakerNotebook : GuiDialogScribeNotebook`
inherits the notebook's handlers unchanged, so it is fixed transitively — no separate edit.

### Decision 3: Guard the tablet wet→hard/fired transition explicitly

The tablet uses `SlotModified` for its legitimate in-place state change. Before finalizing, confirm
in the tablet's handler that a state transition (wet→hard, hard→fired) still triggers whatever
refresh/close it intends, and that the presence-only rule does not suppress it. If the transition
relies on a rebuild/close, keep that path intact and gate ONLY the flicker-causing DocId-mismatch
close behind the new presence rule.

## Risks / Trade-offs

- **[The presence-only rule keeps a dialog open in a genuine edge case]** e.g. the active slot's item
  is swapped in place for a *different* Scribe item without the slot number changing. → In practice a
  content swap that changes identity is rare on the active slot and would still be caught by the next
  real `AfterActiveSlotChanged`; and a different Scribe document opened over the top would register
  its own host. Acceptable given the flicker affects 100% of first opens.
- **[Regressing the tablet state transition]** → Explicitly covered by Decision 3 and a dedicated
  test scenario; verify wet→hard while the dialog is open still behaves correctly.
- **[Verification is manual/in-game]** the whole chain (open → server round-trip → SlotModified) is a
  client GUI interaction not covered by the Core suite. → Add a `TESTING.md` item; test with a
  picked-up (not crafted) item, and also confirm a self-crafted item never flickered (crafter is
  suppressed from the PickedUp entry, so it is a useful control).

## Migration Plan

Pure behavioral fix in `src/Mod/`. No data migration, no save-format change, no config change.
Rollback is reverting the handler edits. Ships in 1.0.0.

## Open Questions

- Does the tablet's wet→hard/fired transition currently depend on the DocId-mismatch close path in
  any way, or is it fully independent? Resolve by reading the tablet's `SlotModified`/state-resolve
  code during implementation (Decision 3); if independent, the fix is purely additive.
