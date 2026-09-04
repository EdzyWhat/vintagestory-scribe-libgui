## Why

Playtest feedback (2026-09-03) found two problems with how an addressed Task Notice sitting at
rest (not yet accepted/declined) communicates itself to its recipient: the generic Scribe-item
hover summary card shows a meaningless `Title: (Untitled)` line for a Task Notice (which has no
title — it's blocks-only), and the existing ambient "assignment nearby" particle signal is a
one-shot point-in-space ping rather than something that keeps drawing the eye to the Inbox block
and the specific notice once it's actually sitting in one of the Inbox's restricted slots.

## What Changes

- The Scribe-item hover summary card (`scribe-item-hover-summary`) special-cases a Task Notice
  carrying an assignment: instead of the generic title + per-kind-count lines, it shows who
  assigned it and who it's addressed to. A blank Task Notice, and every other Scribe item type,
  keep today's generic card unchanged.
- A new, stronger presence signal for a sealed, addressed, not-yet-discovered Task Notice sitting
  in one of the Inbox block's own restricted slots: the Inbox block itself emits the existing
  ambient particle effect (not just a one-shot ping), its "Inbox Inventory" tab button shows the
  existing shimmer sweep, and that specific slot's icon also shows the shimmer sweep.

## Capabilities

### New Capabilities
- `task-notice-inbox-presence-signal`: the Inbox-block-specific strengthened presence signal
  (block-attached particles + tab shimmer + slot shimmer) for an addressed, undiscovered notice
  sitting in one of the Inbox's own restricted slots. Kept separate from the existing
  `task-notice-proximity-signal` capability (the generic nearby-scan point ping, defined in the
  not-yet-archived `add-assignment-physical-delivery-mode` change) rather than modifying it,
  since that capability isn't archived yet and this is additive to it, not a change to its own
  behavior.

### Modified Capabilities
- `scribe-item-hover-summary`: the hover summary card's "Item-slot hover shows a compact Scribe
  document summary" requirement gains a Task-Notice-specific exception.

## Impact

- **Code**: `src/Mod/ScribeDocumentSlot.cs` (`BuildSummaryCard`), `src/Mod/GuiDialogScribeInbox.cs`
  (Inbox Inventory nav button + per-slot rendering in `BuildInboxInventoryContent`), and the
  Inbox block entity/tick path that currently drives the existing nearby-scan ping
  (`ScribeModSystem.Delivery.cs`'s `OnTaskNoticeProximityTick` family) — extended to also trigger
  the Inbox-block-attached signal when the addressed notice is IN the block's own inventory
  rather than merely nearby.
- **Reused, not duplicated**: `ScribeAssignmentParticleEmitter.SpawnAt(capi, BlockPos, ...)`
  (already block-position-capable) and `ScribeShimmerWrap` (already used for the cross-surface
  Inbox nav-button shimmer, gated today by the coarser `ScribeDialogBase.ShowInboxShimmer` —
  "the player has ANY unseen assignment" — this change adds a narrower, per-Inbox-instance
  trigger for the tab button and a new per-slot trigger, without touching the existing coarse
  signal's own behavior).
- **No spec-level change** to the Task Notice's document round-trip, the Sent/Received/Accept/
  Decline state machine, or the Inbox's slot restrictions/capacity (see the sibling change
  `redesign-inbox-block-placement-and-capacity` for the latter).
