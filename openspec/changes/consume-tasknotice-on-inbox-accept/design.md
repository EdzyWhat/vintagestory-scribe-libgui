## Context

`ApplyTaskNoticeAction` (`src/Mod/ScribeModSystem.Delivery.cs`) already fully owns Accept/Decline
when triggered from the notice item's own dialog — it resolves the exact held slot, applies the
transition, and unconditionally clears that slot afterward. The gap is the *other* path:
`OnServerReceivedAssignmentAction` (`src/Mod/ScribeModSystem.Assignment.cs`), which the Inbox tab's
Accept/Decline/Cancel/Discard buttons hit on any surface, has no notion that a given `TaskId` might
have a physical notice sitting somewhere. It transitions the `ScribeAssignmentStore` record fine,
but never looks for or touches the item.

The proximity-discovery heartbeat (`OnTaskNoticeProximityTick` /
`FindAddressedNoticePosition`, same file) already solves the "find a notice near this player"
problem for the discovery ping — it checks the target player's own carried slots
(`ScribeModSystem.EnumerateCarriedSlots`), then dropped `EntityItem`s and block-entity container
inventories within `NoticeScanRadius` (12 blocks) of their live position. This design reuses that
exact machinery for a second purpose: locate-and-consume, not just locate-and-ping.

## Goals / Non-Goals

**Goals:**
- Whenever the generic assignment-action path resolves a transition, best-effort consume any
  sealed Task Notice for that `TaskId` within the same bounds the proximity signal already
  searches.
- Share the search implementation between the discovery ping and this new consume path — no
  parallel scanning logic.
- Leave the existing lazy self-consume behavior (`ApplyTaskNoticeAction` clearing the slot
  regardless of transition success) as the unconditional fallback — this change only shrinks how
  often that fallback is the only thing that ever fires.

**Non-Goals:**
- No global, unbounded search for a notice anywhere in the world/any player's inventory. Scope is
  intentionally identical to the existing proximity signal's — same radius, same "carried +
  nearby" shape, online players only.
- No new persistence tracking "which slot currently holds notice X" — items already move freely
  (dropped, stored, re-picked-up) and the codebase has no such index for any other item; this
  change does not introduce one.
- Offline players are out of scope for the *nearby container* half of the search (no live position
  to scan around) — matching `OnTaskNoticeProximityTick`'s existing `AllOnlinePlayers` bound.

## Decisions

**Which player's vicinity to search.** All four transitions (Accept, Decline, Cancel, Discard) use
the assignment's `TargetPlayerUid` (the Assignee) as the search anchor, not whoever sent the
packet — a notice is only ever carried by the Assignee, regardless of which party (Assigner via
Cancel, Assignee via Accept/Decline/Discard) triggered the transition. If that player is online,
search their carried slots plus nearby entities/containers exactly as
`FindAddressedNoticePosition` does today; if offline, skip the search entirely (no different from
today's proximity signal, which also only runs for online players).

**Match by `TaskId`, not by target UID.** The existing `FindAddressedNoticePosition` /
`NoticeAddressedTo` match a notice by "any block's `Assignment.TargetPlayerUid` equals this
player" — sufficient for a ping (any of their outstanding notices is worth a ping), but wrong here:
consuming a notice must be tied to the *specific* assignment that just resolved, not any notice
this player happens to be carrying. Generalize the matcher to take a `TaskId` predicate; the
discovery-ping call site keeps its "any row addressed to me" behavior by passing a broader
predicate, and the new consume call site passes an exact `TaskId` match.

**Consume in place, not "find then separately delete."** Reuse the same traversal
(`EnumerateCarriedSlots`, `GetEntitiesAround`, chunk `BlockEntities`) but change the terminal action
from "return this position" to "null out this slot / remove this entity and return whether
anything was removed." A dropped notice's backing `EntityItem` is despawned outright (`Die` with
`EnumDespawnReason.Removed`) the same way a normal item pickup removes it; a container slot is
cleared via `slot.Itemstack = null; slot.MarkDirty()`, mirroring `ApplyTaskNoticeAction`'s existing
consumption of a held slot.

**Bookkeeping parity.** A proactively-consumed notice decrements `outstandingNoticeCountByTargetUid`
for that recipient exactly once, the same as `ApplyTaskNoticeAction` already does on a normal
Accept/Decline — otherwise the proximity heartbeat would keep scanning for a notice that no longer
exists.

**Multi-row notices.** A sealed notice can carry more than one row (batch send). If the resolved
assignment is one row of a multi-row notice and the other rows are still unresolved, the notice is
NOT consumed (it's still a live carrier for the other rows) — the search only proceeds to
remove/clear the item once every row it carries is no longer in a state where its own dialog would
offer Accept/Decline (i.e., every row has itself resolved, via any path). This mirrors the
all-or-nothing framing `task-notice-item`'s existing requirements already use for a sealed notice
(one item, one set of rows, resolved together at Accept/Decline time).

## Risks / Trade-offs

- **[Risk]** A notice moved out of the scan radius before the Inbox-tab action fires (e.g., mailed
  into a chest far from the Assignee) is never proactively found. → **Mitigation**: this is the
  documented, accepted fallback — it self-consumes the next time anyone interacts with it, exactly
  as today; not a regression.
- **[Risk]** Despawning a dropped `EntityItem` that a player might currently be watching/reaching
  for could look abrupt. → **Mitigation**: this only happens after the assignment it carries has
  already resolved through the Inbox tab — the item is already stale/dead at that point, so
  removing it is strictly better than leaving a broken, misleading interactive item on the ground.
- **[Risk]** Scanning nearby containers on every Inbox-tab Accept/Decline/Cancel/Discard adds a
  bounded amount of per-action work (same cost class as the existing per-tick proximity scan, just
  now also triggered per-action). → **Mitigation**: identical radius/bounds to the already-shipped
  heartbeat scan; this is a one-off call at action time, not a new recurring tick.

## Migration Plan

No data migration — this is new server-side behavior on an existing action handler, no schema or
message-format change. Rollout is a normal mod version bump; nothing to roll back beyond reverting
the change if a problem surfaces.
