## Context

Two existing, reusable primitives already do most of the work here:
- `ScribeAssignmentParticleEmitter.SpawnAt` has both a `BlockPos` overload (used today by every
  writing-station's "unseen assignment" ambient field) and a `Vec3d` overload (used today by the
  Task Notice proximity ping's point-in-space case). No new particle code is needed.
- `ScribeShimmerWrap` already wraps an arbitrary child widget in the looping shimmer sweep, keyed
  by a plain `bool Active`. It's already wired to the four cross-surface Inbox nav buttons via
  `ScribeDialogBase.ShowInboxShimmer` (`modSystem.HasUnseenAssignment && !IsInboxView` — a coarse,
  player-wide "you have SOME unseen assignment" signal). This change needs a narrower, per-Inbox-
  instance, per-slot signal, so it adds new trigger booleans rather than reusing
  `ShowInboxShimmer` itself.
- The hover summary card's single generic code path (`ScribeDocumentSlot.BuildSummaryCard`) is
  shared by the Scriptorium, Assignment Desk, and Inbox restricted slots alike (all three route
  through `ScribeInventorySlotStyle.Build` → `ScribeDocumentSlot`), so the Task-Notice special
  case is written once and applies everywhere a Task Notice can sit in a restricted slot.

See proposal.md for the "why."

## Goals / Non-Goals

**Goals:**
- Reuse the two existing visual primitives (particle emitter, shimmer wrap) with new trigger
  conditions — no new rendering mechanism.
- Fix the hover card in exactly one place (`BuildSummaryCard`) so the fix applies uniformly
  wherever a Task Notice can be hovered in a restricted slot.

**Non-Goals:**
- No change to the existing generic nearby-scan ping (`task-notice-proximity-signal`, not yet
  archived) — this change is additive on top of it, for the specifically-in-an-Inbox case.
- No change to the coarser `ShowInboxShimmer` cross-surface nav-button trigger or its own
  behavior.
- No new "discovered" concept beyond what's needed to turn the signal off — see Decision 2.

## Decisions

### Decision 1: Hover-card fix is a single Task-Notice branch in `BuildSummaryCard`
Check `stack.Collectible is ItemScribeTaskNotice` before the existing `ScribeDocumentAttributes.
TryReadFrom` branch; if true AND the notice carries an assignment (mirrors `ItemScribeTaskNotice.
IsSealed`'s own check — reuse that logic rather than re-deriving it, exposing it if it isn't
already accessible from here), render two lines using the existing `scribe:scribe-assignment-
assigned-by` and `scribe:scribe-tasknotice-addressed-to` lang keys (already used by
`GuiDialogTaskNotice`/`ItemScribeTaskNotice.GetHeldItemInfo` respectively — same formatting, not
new copy) instead of the title + count lines. A blank Task Notice (or a sealed one somehow
missing its assignment data) falls through to the existing generic path unchanged.
- **Alternative considered**: give `IScribeDocumentItem` a virtual "custom summary lines" hook so
  any item type could override the card. Rejected as over-engineering for a single special case —
  no other Scribe item currently needs this, and `Code readability enables features`-style
  precedent in this codebase favors the smallest clear branch over a speculative extension point.

### Decision 2: The Inbox-instance signal's trigger reuses the assignment's own state, not a new "discovered" flag
"Not yet discovered" for a notice sitting in an Inbox slot needs no new state at all: a slot
either holds a sealed, addressed Task Notice, or it doesn't. Accept/Decline consumes the physical
notice item, so the moment it's gone this predicate is naturally false again — no explicit "off"
transition to design or write.
- **Resolved (was an open question): `ScribeAssignment.Seen` is NOT used.** Read on
  implementation: `Seen` flips true via `ScribeDialogBase.MarkInboxSeenIfNeeded`, sent
  unconditionally by every path that makes ANY Inbox view active — opening a completely
  different Inbox block, or even the Assignment Desk's Inbox tab, marks it true well before the
  physical notice sitting in THIS Inbox is actually Accepted/Declined. Using it would turn the
  signal off prematurely. The simple "slot holds a sealed, addressed notice" check (implemented
  as `BlockEntityInbox.HoldsUndiscoveredNoticeFor`) is both simpler and correct.

### Decision 3: Trigger computation lives in `BlockEntityInbox` itself, as a client-side tick — not a server round-trip
Investigating the existing ambient "unseen assignment" field
(`BlockEntityScribeWritingStation.OnAssignmentParticleTick`, registered via the base class's own
`Initialize`) found it is **entirely client-side already**: each block entity runs its own
`RegisterGameTickListener`-driven check against the client's locally-synced state and calls
`ScribeAssignmentParticleEmitter.SpawnAt` directly — no server tick, no ping message. Since a
placed Inbox's inventory contents already replicate to every client with that chunk loaded (the
same vanilla block-entity sync every Scribe surface uses), `BlockEntityInbox` can run the exact
same pattern against its OWN inventory with no new network plumbing: a second
`RegisterGameTickListener` call in `BlockEntityInbox.Initialize` (additive to the base class's
existing one, not replacing it), checking `HoldsUndiscoveredNoticeFor` against the local
player's UID for each of its own restricted slots. This supersedes this decision's original text
(a server-side scan in `ScribeModSystem.Delivery.cs` mirroring `OnTaskNoticeProximityTick`,
pushing a ping to the client) — task 3.1 explicitly invited "whichever is cheaper," and this is
simpler (no new message type, no per-tick server-side chunk/inventory scan across all online
players) and more consistent with the codebase's existing ambient-particle precedent.
`GuiDialogScribeInbox` (tasks 4.1/4.2) reuses the same `HoldsUndiscoveredNoticeFor` predicate for
its tab/slot shimmer checks, so all three signals share one trigger definition.

## Risks / Trade-offs

- **[Risk]** Running a per-tick scan of "does any Inbox within particle-visible range hold an
  addressed notice for this player" could be more expensive than the existing single nearby-item
  scan if done naively. → Mitigation: gate on the same `outstandingNoticeCountByTargetUid` count
  the existing proximity tick already uses (cheap early-exit for the common case of zero
  outstanding notices), and only re-derive per-Inbox state when that count is nonzero.
- **[Risk]** A notice could move between Inboxes, into a Scriptorium slot, or into a player's own
  inventory between ticks, briefly showing or hiding the block-particle signal a tick late. →
  Mitigation: acceptable — the existing proximity ping already has this same class of lag, and
  the block particle stopping/starting a tick behind reality is not player-visible at that
  granularity.

## Migration Plan

No persisted state changes — this is entirely new client-side presentation logic and one
hover-card branch. Nothing to migrate for existing saves.
