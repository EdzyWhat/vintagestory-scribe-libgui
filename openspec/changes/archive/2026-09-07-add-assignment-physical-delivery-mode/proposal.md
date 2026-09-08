## Why

Assignments today sync instantly over the network regardless of distance, which is fine for
players sharing a base but reads as unlimited-range instant communication once players spread
across a kingdom/faction-style server — the opposite of the "physical note left for someone"
fiction the Assignment system exists to support. This change adds a physical-item delivery path
for out-of-range or offline targets, gated by a server admin setting, without touching the
in-range flow at all.

## What Changes

- Add a server admin `DeliveryMode` setting: `AlwaysInstant` (today's behavior only),
  `AlwaysPhysical` (every send requires a Task Notice), or `Hybrid` (default — the range check and
  toggle below).
- In `Hybrid` mode, an Assign-time, one-time range check (Desk position vs. the target's live
  position, or their last-known position captured on logout if offline; 200-block default radius,
  admin-configurable) decides which delivery path is pre-selected for a new send. The check is
  never re-evaluated after send.
- The Create Assignments tab gains a symmetric two-position toggle, **"Local Inboxes" / "Send a
  Notice"**, pre-selected by the range check but always freely switchable by the player in either
  direction (no blocked/grayed-out position), plus an info (ⓘ) button explaining the mechanic.
  "Local Inboxes" mode is pixel-identical to today's shipped flow.
- "Send a Notice" mode reveals two additional slots on the tab: a stacking supply slot of blank
  Task Notices (consumed on send) and a non-stacking output slot where the sealed, populated
  notice appears after Send — nothing is auto-inserted into the player's inventory.
- Add a new **Task Notice** item: crafted from a knife, parchment, and a reed (yields 8 blank
  notices), reuses the existing `IScribeDocumentItem`/`ScribeDocumentAttributes` round-trip (no new
  serialization), reuses the existing scroll placeholder model. Opens via the same held-item
  right-click convention as Notebook/Tablet, showing the same document dialog in a locked/read-only
  state with explicit Accept/Decline buttons.
- Accepting a Task Notice converts it into a normal `ScribeAssignmentStore`-tracked assignment
  (mirroring the existing `AcceptedIntoLabel` placement mechanism) — from that point on it behaves
  exactly like an in-range assignment (Complete/Discard sync normally regardless of distance). An
  unaccepted Task Notice has no store record at all; there is no digital Cancel for it — physically
  retrieving or destroying the item is the equivalent action (consistent with Scribe's existing
  "lose the item, lose the content" document philosophy). Declining a Task Notice consumes it with
  no store record and no notification back to the Assigner, for the same reason.
- Add a periodic, cheap, per-player proximity scan (reusing the existing `OnStormTick` heartbeat
  idiom) that spawns the existing ambient particle/badge near a Task Notice at rest (dropped, or
  sitting in any container — not a dedicated Scribe mailbox block) close to its recipient, so
  discovery doesn't depend on the recipient stumbling across it by chance.

**Explicitly out of scope** (parked for future work, not designed here): a dedicated Mailbox
block with a deposit/pickup inventory tab; group/faction assignment targets (including an
"Anyone" target); a crafting-grid merge for combining two same-recipient notices; Envelopes-mod
wrap compatibility.

## Capabilities

### New Capabilities
- `assignment-delivery-mode`: the server-wide `DeliveryMode` setting and the Hybrid range check
  (Desk position vs. target's live/last-known position) that decides which delivery path a new
  assignment uses.
- `task-notice-item`: the Task Notice item itself — recipe, model, document round-trip, its
  locked-on-send read-only view with Accept/Decline, and its accept-time conversion into a normal
  tracked assignment (including the no-record/no-Cancel/no-notification consequences of true
  physical embodiment before Accept).
- `task-notice-proximity-signal`: the periodic proximity scan that surfaces the existing ambient
  particle/badge near an at-rest Task Notice close to its recipient.

### Modified Capabilities
- `assignment-desk-block`: the Create Assignments tab gains the delivery-mode toggle, its info
  button, and the two conditional slots (blank supply, sealed output) shown only in "Send a
  Notice" mode.
- `assignment-state-machine`: clarifies that an assignment created via an Accepted Task Notice
  begins its store-tracked lifecycle already at Accepted — the physical item, not a store record,
  carries the pending decision beforehand, and Decline on that item produces no store record.

## Impact

- `src/Core/`: new `DeliveryMode` config type and range-check logic; a last-known-position field
  persisted per player; the accept-time record-creation entry point for notice-originated
  assignments. No Vintage Story API references introduced here, per project convention.
- `src/Mod/`: a new Task Notice `CollectibleObject` implementing the existing
  `IScribeDocumentItem`/`BlockScribeWritingStation`-adjacent pattern; a right-click handler reusing
  the Notebook/Tablet document-dialog open path; the Create Assignments tab's toggle/slot UI; an
  `OnPlayerDisconnect` hook to persist last-known position (mirrors the existing Sign
  `ToTreeAttributes`/`SendBlockEntityPacket` persistence pattern); an extension of the existing
  `OnStormTick`-style heartbeat for the proximity scan.
- Assets: a new crafting recipe JSON (knife + parchment + reed → 8 Task Notices) and an item JSON
  referencing the existing placeholder scroll model.
- No new mod dependencies; no changes to `assignment-multi-item-creation` (the existing staging
  slot/row-selection mechanism is reused unchanged for "Send a Notice" mode too).
