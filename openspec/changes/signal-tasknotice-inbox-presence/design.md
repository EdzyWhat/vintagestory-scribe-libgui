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
"Not yet discovered" for a notice sitting in an Inbox slot is exactly the same condition that
already gates the generic nearby-scan ping and the notice's own Sent→Received transition
(`refine-task-notice-ux`): the notice is addressed to the local player and its embedded
assignment is not yet Accepted or Declined (still Sent or Unaccepted). Read the embedded
`ScribeBlock.Assignment` state directly off each restricted slot's stack in
`GuiDialogScribeInbox` (client-side, no new server data needed — the notice's document already
carries this) rather than introducing a separate "seen" bit: Accept/Decline already ends the
signal for free (the notice leaves the slot), so there is no separate off-switch to design.
- **Open question**: whether to also suppress the signal once the player has simply *opened* the
  notice once (right-clicked to view it) without yet Accepting/Declining — the broader
  `HasUnseenAssignment`/`Assignment.Seen` flag exists for exactly this kind of "acknowledged but
  not yet resolved" state elsewhere. Deferred to task 1 below: read how `Seen` is set/read for a
  Task-Notice-embedded assignment before Accept, and use it if it's already meaningful in that
  state, else keep it simple (signal ends only on Accept/Decline/removal) for a first pass.

### Decision 3: Trigger computation lives in `GuiDialogScribeInbox`, not the block entity
The block entity (`BlockEntityInbox`) stays a plain data holder; the client-only "does this
matter to ME, the locally-viewing player" check (comparing `TargetPlayerUid` to the local
player) is a dialog-side concern, matching how `ShowInboxShimmer` and the existing proximity
ping are both resolved client-side already. The block-attached particle emission needs a
per-tick check independent of whether the dialog is open (a player should see the Inbox glowing
from across the room), so that half lives alongside the existing `OnTaskNoticeProximityTick`
family in `ScribeModSystem.Delivery.cs` — checked once per tick per online player with an
outstanding notice (reusing the existing `outstandingNoticeCountByTargetUid` gate to avoid
scanning every Inbox on every tick for every player).

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
