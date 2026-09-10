## Why

A sealed Task Notice's Accept/Decline buttons are only ever wired to the notice item's own dialog
(`GuiDialogTaskNotice`), but the exact same assignment can also be Accepted or Declined through
the generic Inbox tab (any Assignment Desk, Lectern, or Inbox block's Inbox view), since that view
has no notion of "this record arrived as a physical notice." When that happens, the assignment
record transitions correctly, but the physical notice item is never touched — it's left sitting in
the player's inventory (or a nearby container), still rendering as sealed and still opening into a
dialog that offers Accept/Decline as if nothing happened. A 2026-09-09 playtest report confirmed
this: "the assigned task notice should be consumed/deleted when it is accepted via the Inbox tab...
[it's] left confusingly interactive." The notice does silently self-consume the next time it's
touched (`ApplyTaskNoticeAction` unconditionally clears the slot after processing an Accept/Decline
regardless of whether the transition actually applied), but nothing proactively cleans it up at the
moment the record resolves elsewhere, so it can linger indefinitely looking like a live, actionable
item.

## What Changes

- When an Accept, Decline, Cancel, or Discard transition succeeds through the generic assignment-
  action path (`OnServerReceivedAssignmentAction` — the Inbox tab's action buttons, on any surface),
  the system additionally searches for and consumes any sealed Task Notice item still carrying that
  assignment's `TaskId`, so it never lingers as a stale, falsely-interactive item.
- The search reuses the existing Task Notice proximity-scan's own bounds (the affected player's
  carried inventory, plus dropped items/containers within the same radius around their live
  position) rather than adding a new global item-tracking mechanism — the same scope the discovery
  ping already searches, just triggered by a state change instead of a chunk crossing.
- If no matching notice is found in that scope (moved out of range, held by an offline/absent
  player, etc.), nothing changes: the notice keeps behaving exactly as it does today — stale but
  self-consuming the next time someone interacts with it. This change narrows how often that
  fallback is needed; it does not remove it.

## Capabilities

### Modified Capabilities
- `task-notice-item`: adds a requirement that a Task Notice carrying an assignment SHALL be
  proactively located and consumed when that assignment resolves through any path other than the
  notice's own Accept/Decline dialog, within the existing proximity-scan's search scope.

## Impact

- `src/Mod/ScribeModSystem.Assignment.cs` (`OnServerReceivedAssignmentAction`): trigger the new
  consume-search after a successful `TryApplyAction`.
- `src/Mod/ScribeModSystem.Delivery.cs`: generalize `FindAddressedNoticePosition` (currently
  matches by target UID, for the discovery ping) into a TaskId-matching variant that also removes
  the found stack, shared by both the discovery ping and this new consume path; also touches
  `outstandingNoticeCountByTargetUid` bookkeeping so a proactively-consumed notice decrements it
  the same way `ApplyTaskNoticeAction` already does.
- No new persistence, no new network messages — this runs entirely server-side inside the existing
  assignment-action handler.
