## 1. Hover card fix: Task Notice special case

- [x] 1.1 In `src/Mod/ScribeDocumentSlot.cs`'s `BuildSummaryCard`, add a branch for
  `stack.Collectible is ItemScribeTaskNotice` carrying an assignment (reuse or expose
  `ItemScribeTaskNotice`'s existing `IsSealed`-equivalent check rather than re-deriving it):
  render the item name, then a line formatted via `Lang.Get("scribe:scribe-assignment-assigned-
  by", assignerName, assignedDate)`, then a line via `Lang.Get("scribe:scribe-tasknotice-
  addressed-to", recipientName)` — no Title or per-kind-count lines. A blank Task Notice falls
  through to the existing generic path unchanged. Verify by building and checking the card in
  code review against `ItemScribeTaskNotice.GetHeldItemInfo`'s existing formatting for
  consistency.
- [ ] 1.2 Manual playtest: hover a sealed, addressed Task Notice in a Scriptorium slot, an
  Assignment Desk slot, and an Inbox restricted slot — confirm all three show the new
  assigner/addressee lines instead of `Title: (Untitled)`. Hover a blank Task Notice in the same
  slot types and confirm the card is unchanged ("never opened").

## 2. Read how `Assignment.Seen` behaves for a not-yet-Accepted Task-Notice-embedded assignment

- [x] 2.1 Read `ScribeAssignment.Seen`'s current read/write sites (Core + Mod) to determine
  whether it is already meaningfully set/read for an assignment still embedded in a Sent/
  Unaccepted Task Notice's document (as opposed to only after Accept, when it becomes a normal
  tracked row). Record the finding as a one-line comment at the new trigger-check call site (task
  3.1) either way, so the next reader doesn't have to re-derive it. If NOT already meaningful in
  that state, use design.md Decision 2's fallback: the signal is gated purely on the assignment
  still being Sent/Unaccepted (ends only on Accept/Decline/removal from the slot), with no
  separate "opened but not resolved" suppression for a first pass.
  - Finding: `Seen` is set by `ScribeDialogBase.MarkInboxSeenIfNeeded`, sent unconditionally by
    every path that makes ANY Inbox view active — it would flip true from opening a different
    Inbox or the Assignment Desk's Inbox tab, well before the physical notice in THIS Inbox is
    resolved. Not used; see design.md Decision 2 (updated) and
    `BlockEntityInbox.HoldsUndiscoveredNoticeFor`'s doc comment.

## 3. Inbox-instance presence signal: block particles

- [x] 3.1 Implemented as a client-side `RegisterGameTickListener` in `BlockEntityInbox.Initialize`
  (`OnInboxNoticeParticleTick`), additive to the base class's existing unseen-assignment tick —
  see design.md Decision 3 (updated during implementation: the existing ambient-particle pattern
  turned out to be entirely client-side already, via the block entity's own tick, not a server
  scan/ping; no new network message needed). Checks the Inbox's own restricted slots via the new
  `BlockEntityInbox.HoldsUndiscoveredNoticeFor(slot, targetUid)` predicate (shared with tasks
  4.1/4.2 below) and calls `ScribeAssignmentParticleEmitter.SpawnAt(capi, Pos, seedBurst)` within
  `DetectionRadius`, mirroring `BlockEntityScribeWritingStation.OnAssignmentParticleTick` exactly.
  Verified by build; manual confirmation is task 5.1.

## 4. Inbox-instance presence signal: tab + slot shimmer

- [x] 4.1 In `src/Mod/GuiDialogScribeInbox.cs`, compute a per-open-dialog bool (does THIS Inbox's
  inventory currently hold, in a restricted slot, a sealed notice addressed to the local player
  per task 2's trigger) and pass it as the `shimmer:` argument to the existing Inbox Inventory nav
  `TitleButton` call (distinct from `ShowInboxShimmer`, which stays wired to the four
  cross-surface nav buttons unchanged). Implemented as `ShowInboxInventoryShimmer()`, reusing
  `BlockEntityInbox.HoldsUndiscoveredNoticeFor`. Verified by build; manual confirmation is task
  5.2.
- [x] 4.2 In the same file's `BuildInboxInventoryContent`, wrap the specific matching slot's
  widget (from task 4.1's per-slot check, not the whole row) in `ScribeShimmerWrap` the same way
  `TitleButton` does, so only that slot's icon shimmers among the others. Verified by build;
  manual confirmation is task 5.2.

## 5. Manual verification

- [x] 5.1 Manual playtest: send a Task Notice to a recipient, have them place the sealed notice
  into one of their Inbox's restricted slots while online — confirm that Inbox block starts
  emitting the ambient particle effect, visible only to them; Accept or Decline it — confirm the
  particles stop.
  - Confirmed 2026-09-06 (self-addressed singleplayer test): particle emission works — a debug
    trace added during investigation confirmed the trigger (`HoldsUndiscoveredNoticeFor`) and
    the tick were firing correctly the whole time; the initial "no particles" report traced to
    `/time stop` being active in the test world, which suppresses the particle system engine-wide
    (unrelated to this change's code). Diagnostic logging removed after confirming.
- [x] 5.2 Manual playtest: with the same setup, open the Inbox — confirm the Inbox Inventory nav
  tab button shimmers and the specific slot holding the notice also shimmers, while other filled
  slots do not; Accept or Decline the notice — confirm both shimmers stop.
  - Confirmed 2026-09-06: tab shimmer and per-slot shimmer both confirmed working.
- [ ] 5.3 Manual playtest: confirm none of the above (particles, tab shimmer, slot shimmer, or
  the hover card change from section 1) alter the notice's document, assignment state, or any
  Inbox inventory contents — purely observe, then check the assignment's state and the Inbox's
  contents are exactly as they were before observing.
