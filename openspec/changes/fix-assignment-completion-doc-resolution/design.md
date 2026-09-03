## Context

`CompleteTaskForPlayer` (`src/Mod/ScribeModSystem.PinOperations.cs:113`) is the server-side choke
point for completing a pinned task (HUD/Pin Tab). It calls `TryResolveDocHost` (line 289) to find
the task's owning document: a registry lookup (covers Lecterns, self-registered server-side) with
a fallback that scans only the acting player's own inventory for a matching Notebook (Notebooks
are never registered server-side). Only inside the `if (resolved)` branch does it call
`NotifyAssignmentDoneChanged` (`src/Mod/ScribeModSystem.Assignment.cs:451`), which marks the
canonical `ScribeAssignmentStore` record Completed and pushes sync to both parties.

`NotifyAssignmentDoneChanged` takes an `assignmentOnBlock` parameter (the resolved document
block's own `Assignment` object). It uses this for two things: (1) a pre-filter gate, and (2) —
this is load-bearing, not redundant — mutating that live object's `State`/date so the resolved
block's own embedded `Assignment` reflects Completed too. The server's actual Done-toggle path
(`ScribeCompletion.ApplyLeaf`/`ApplyBoundParent`, called via `ApplyGivenDone`) sets `block.Done`
directly and does **not** touch `block.Assignment` — that only happens here. (A separate method,
`src/Core/ScribeDocument.ToggleTask`, does mirror `Done` onto `Assignment` itself, but it is not
part of this server completion path, so it doesn't help here.) The canonical store lookup at line
455 (`assignmentStore.TryGet(taskId)`) is addressed purely by `taskId` and never needed the block
— but the block-mutation side-effect does need it, when a block is available.

## Goals / Non-Goals

**Goals:**
- Make the canonical assignment-store derivation in `NotifyAssignmentDoneChanged` independent of
  document/Notebook resolution, since it never structurally needed it.
- Call this derivation on every Done→true toggle from `CompleteTaskForPlayer`, not only when the
  document happens to resolve.
- Add `Trace()` logging to every currently-silent early-return on this path.

**Non-Goals:**
- Widening `TryResolveDocHost`'s inventory scan (e.g. searching other players, containers, or a
  location index). Rejected — see Decision below; it's unnecessary once the store-level derivation
  no longer depends on resolution at all, and would add real cost/complexity for no benefit here.
- Reconciling a document's own embedded `Assignment.State` when it later becomes resolvable again
  (e.g. refreshing a stale "Accepted" marker icon on a task row once the Notebook is picked back
  up). This is a narrower, purely cosmetic gap (see Risks) left for a follow-up if it turns out to
  matter in practice.

## Decisions

**D1 — Re-gate `NotifyAssignmentDoneChanged` on the canonical store record; keep `assignmentOnBlock`
as an optional mirror target, and call it unconditionally.**
Change the method's primary gate from `assignmentOnBlock`'s state to
`assignmentStore.TryGet(taskId)`'s state — the canonical record is addressed purely by `taskId`
and is sufficient on its own to decide whether to derive Completed (including correctly staying a
no-op for an already-Discarded record, since that's the store's own state). `assignmentOnBlock`
becomes purely optional: when present and itself Accepted, still mirror the completion onto that
live object (preserving today's behavior exactly for the resolved case — this mutation is the only
thing keeping a resolved block's own embedded `Assignment` in sync, per Context above); when null
(document unresolvable) or already non-Accepted, skip the mirror but still complete the canonical
record. `CompleteTaskForPlayer` calls this on every Done→true toggle regardless of whether
`TryResolveDocHost` resolved, passing the resolved block's `Assignment` when available and `null`
otherwise.
- *Alternative considered (and initially, incorrectly, chosen)*: drop `assignmentOnBlock` entirely
  and gate/act on the store alone. Rejected on closer reading of `ScribeCompletion.ApplyLeaf` —
  that would silently regress the resolved case too, since nothing else mutates a resolved block's
  own `Assignment` object on this path.
- *Alternative considered*: widen `TryResolveDocHost`'s fallback to scan other players'
  inventories or a block-position index for the Notebook. Rejected — it only reduces how often the
  document fails to resolve, it doesn't fix the actual defect (the store-side derivation shouldn't
  have been coupled to resolution in the first place), and scanning other players' inventories
  server-side for every completion is real added cost and a privacy-adjacent smell for no behavior
  the spec actually asks for.
- *Alternative considered*: retry/defer the derivation (queue it, retry next tick or next time the
  document resolves). Rejected — needless complexity once D1 shows the store-side derivation never
  needed the document; a retry queue would be solving a problem that no longer exists.

**D2 — Add `Trace()` calls at every silent early-return on this path.**
`CompleteTaskForPlayer`'s (former) unresolved branch and all three of `NotifyAssignmentDoneChanged`'s
guard clauses (not-done, no store, block/store record not an Accepted assignment) get a `Trace()`
line, matching the existing convention elsewhere in this file (e.g. `CompleteUnpinnedTaskAtSource`'s
guards already do this). This is precautionary, not required by D1's fix, but the complete absence
of any trace output was exactly what made this bug invisible for as long as it was.

## Risks / Trade-offs

- [Risk] A document's own embedded `Assignment.State` can stay stale ("Accepted") on its task row
  after the canonical store has moved to Completed, if the document was unresolvable at the moment
  of completion → Mitigation: this only affects a cosmetic marker icon on that one row within the
  document itself (`ScribeDialogBase.Layout.cs`'s `IsAcceptedAssignment` check), not the
  Assignee's Inbox or the Assigner's Sent Assignment History (the actually-reported bug, both of
  which read the canonical store, not the block). Left as a known, documented gap; revisit only if
  it proves to matter in practice.
- [Risk] Re-gating on the canonical store instead of `assignmentOnBlock` changes which check runs
  first → Mitigation: the store-only gate is still correct for the one case the original ordering
  protected against (an already-Discarded record whose block is stale-Accepted) since the store
  itself reports Discarded, not Accepted, in that case; confirmed by re-reading the discard path in
  `OnServerReceivedAssignmentAction`/`NotifyAssignmentDiscardOnDelete`.

## Migration Plan

No data or format changes. Purely a behavior fix in the Mod layer; no rollback concerns beyond a
normal revert.
