## Context

Four Scribe surfaces animate row removal: the editor, Read view, Pin Tab, and the pinned-task HUD.
`extract-animated-task-list` built `ScribeAnimatedList` (a view-agnostic container that diffs an
identity-keyed item set, collapses departed rows, and self-cleans) and adopted it on Read + Pin Tab;
`animate-row-insertion` migrated the editor and shipped the `ScribeSlideIn` entry primitive. This change
migrates the **last** surface — the HUD — so exactly one animation path remains
(`extract-animated-task-list` §6.3, the consolidation).

### The premise correction (why `Delayed` is gone)

This change was originally scoped around wiring the container's stubbed **`Delayed` removal policy** —
a held, faded *ghost* in front of the collapse — believing the HUD "needs" it. That premise was wrong,
and the correction is the core design decision here.

Trace the HUD's actual destructive-completion timeline (`HudScribePins.OnToggleRow` → `OnTick`):

1. Player checks a pin under a destructive policy (Unpin/Delete/UnpinSink). `OnToggleRow` flips
   `optimisticDone`, records a `PendingCompletion` in `pendingCompletions` with an expiry
   `elapsedMs + PinHudWaitMs` (**1500ms**), and rebuilds. **Nothing is sent to the server yet.**
2. During the window the pin **stays live in `MyPins`**: its row renders with a **clickable checkbox**,
   and its text fades 1→0 via `ScribeFadeText` as a countdown preview (`IsFadingOut`).
3. **Undo = unchecking the live row.** `OnToggleRow`'s un-check branch does
   `pendingCompletions.Remove(key)` + clears `optimisticDone` — a true undo, because the packet was
   never sent.
4. On expiry (`OnTick`), the completion is **sent** (`SendCompletion`), the pin is added to
   `awaitingRemoval` (suppressing it from the rendered set so it doesn't flash back to full opacity
   before the server push), and *today* `BeginDeparting` snapshots the row and starts its collapse.
5. The server's removal push drops the pin from `MyPins`; the collapse finishes and `OnDepartingCollapsed`
   retires the entry.

The undo window (steps 1–3) is a **deferred-network-send phase on a LIVE, interactive row** — it is HUD
domain state (a pending packet + optimistic flag), not an animation. A container `Delayed` ghost-hold
(step 4+ made longer) could not carry it: a frozen ghost has no live checkbox, so it can't host the
uncheck-to-undo affordance, and moving undo to "re-add a departed row" would be the very UX change the
user said not to make ("keep the HUD's current fade out before it's culled, allowing for the undo").

The collapse itself (step 4→5) begins the frame the pin leaves the rendered set — which is *exactly*
the container's existing **`Immediate`** policy. So the migration is: **keep steps 1–3 in the HUD, hand
the container an item set that already excludes `awaitingRemoval` pins, and let `Immediate` collapse the
row when it leaves.** No new policy is needed; the `Delayed` enum member + throwing stub are removed.

## Goals / Non-Goals

**Goals:**
- Route the HUD's rows through `ScribeAnimatedList(Immediate)`, deleting `departing` / `BeginDeparting`
  / `ReconcileDeparting` / `CancelDeparting` / `OnDepartingCollapsed` / `needsCollapseCleanup`.
- Preserve the HUD's undo semantics EXACTLY: the deferred-send window (`pendingCompletions`,
  `PinHudWaitMs`), the live-row `ScribeFadeText` countdown fade, and uncheck-to-undo all stay in the HUD.
- HUD rows enter with the shared `ScribeSlideIn` (D6) and order like the Pin Tab (D7).
- Remove the misconceived `Delayed` policy from the container.
- End with one animation path across all four surfaces.

**Non-Goals:**
- Any change to the HUD's undo-window *duration* or fade *feel* — behavior-preserving migration.
- Moving `awaitingRemoval`, `pendingCompletions`, or the fade into the container. These are HUD domain
  state (in-flight network removal, deferred send, live-row countdown); they stay in the HUD and simply
  drive which ids are in the item set (see D3).
- Any Core model/persistence/sync change.
- Re-opening the [[hud-undo-window-is-policy-hiding]] decision itself (whether the HUD *should* have an
  undo window). That policy stands; this only changes the mechanism that renders the collapse.

## Decisions

### D1 — Collapse is the container's existing `Immediate` policy, triggered by the pin leaving the set
The HUD hands `ScribeAnimatedList` an item set built from `MyPins` minus `awaitingRemoval` (the pins
whose destructive completion has been sent). When a completion's window expires and the send fires, the
pin enters `awaitingRemoval` and thus leaves the container's item set — the container sees an id vanish
and collapses it via `Immediate`, exactly as a Pin Tab delete does today. The collapse *shape*, the
host-owned `ScribeAnimationRegistry`, the ghost snapshot, the self-clean, and the scroll/hover loops are
all reused unchanged. No hold phase is prepended; the "hold" the old design imagined is the HUD's
pre-send live window, which never enters the container.

### D2 — The undo window + fade stay in the HUD as a live-row deferred-send phase (KEPT verbatim)
User directive (2026-08-12): **keep the HUD's current fade-out-before-cull-with-undo behavior.** That
behavior lives entirely in the pre-send phase, so it stays entirely in the HUD:
- `pendingCompletions` + `PinHudWaitMs` (the deferred-send window) — unchanged.
- `ScribeFadeText` on the **live** row (`IsFadingOut` → text fades 1→0 as the countdown) — unchanged;
  it is NOT a departure fade, so it is not the container's concern. (This also means `ScribeFadeText` is
  still used and is NOT removed — correcting the original design's assumption that departure-fading would
  move to a `ScribeFade` primitive. There is no `ScribeFade`; the row fade was always the live-window
  fade, and it stays where it is.)
- Undo = unchecking the live row (`OnToggleRow`) — unchanged.

The row only becomes a container ghost AFTER the window, when it is already faded to ~0 and its identity
has left the set; the ghost snapshot renders that already-faded row collapsing, exactly as today's
`departing` snapshot does.

### D3 — `awaitingRemoval` stays in the HUD and feeds the container only via the item set
The container's contract is "diff the item set; an id that vanishes departs." The HUD keeps
`awaitingRemoval` (which pins have a destructive packet in flight) and keeps SUPPRESSING those pins from
the item set it hands the container — so the pin "vanishing" from the container's input is what triggers
the `Immediate` departure. The container never learns about the network round-trip; it only sees an id
leave. The container's existing reappear-cancels-departure path (`diff.Revived`) subsumes
`CancelDeparting` for the rare case where a suppressed pin is re-pinned mid-collapse. `ReconcileDeparting`
(re-pin cancels, server-confirmed removal cleanup) collapses to: on each pin push, `awaitingRemoval` is
reconciled against the live set (drop ids the server removed or that were re-pinned) — a small retained
helper, no longer tied to a `departing` map.

### D4 — Sink / UnpinSink remain a reorder, not a departure
Sink moves a still-live pin to the bottom (its id stays in the set), so it is NOT a container departure —
it's the same in-set reordering the HUD already does via `sunkOrder`. Only the genuinely-removing
completions (Unpin, Delete, and the unpin half of UnpinSink), once SENT and in `awaitingRemoval`, leave
the set and collapse. Verify the sink overlay/countdown still reads correctly alongside a concurrent
`Immediate` collapse of a different row.

### D5 — HUD collapse migrated behind a parity gate, old machinery deleted only after in-game confirm
Wire the container route first, then **playtest that undo + fade + collapse + cancel behave identically
to today** before deleting the hand-wired `departing` machinery. The old code stays until the new path is
confirmed in-game. Record the current behavior (1500ms window, `ScribeFadeText` linear fade over
`FadeWindowMs`=1500, 200ms collapse) as the parity baseline.

### D6 — HUD rows ENTER with the same animation as the editor / Read / Pin Tab
A row newly appearing on the HUD (a freshly-pinned task, or one crossing into the capped window because
another collapsed out) slides in exactly as it does in the editor and Read/Pin views — the container's
`animateEntry` (`ScribeSlideIn`) path, not a snap. Routing the HUD through `ScribeAnimatedList` gets this
for free with entry animation left enabled; confirm the `+N more` cap and the sink overlay don't force a
snap on rows crossing the cap boundary, and that the container's first-build/ForceRebuild entry
suppression holds for the HUD.

### D7 — HUD row ORDER follows the Pin Tab's order
User directive: the HUD should follow the ordering of the **Pinned tab**. Today the two DIVERGE: the Pin
Tab uses `OrderedPinsForDisplay()` (raw `MyPins`, or `ScribePinOrdering.ForDisplay` under Sink/UnpinSink
— `ScribeDialogBase.PinTab.cs:132`), while the HUD's `BuildOrderedRows()` (`HudScribePins.cs:852`) applies
its own overlay: a session-durable `sunkOrder` set plus an undo-window ordering fudge (an in-window
completed pin ordered as if not-done). Align the HUD's base ordering with `ScribePinOrdering.ForDisplay`
(reuse the Core rule, don't duplicate), then re-apply only the HUD-specific overlays that must survive:
- **Durable session-sink bottom-hold** (`sunkOrder`, scribe-settings-followups 2.2): KEEP — it is HUD-only
  UX (a settled-sink pin holds the bottom for the session even after an uncheck) and the Pin Tab has no
  equivalent because it has no undo window. It is applied on top of the shared base order.
- **In-undo-window in-place hold** (an in-window completed pin ordered as if not-done): KEEP — it is the
  ordering half of the deferred-send window (the row must not jump to the bottom until the window settles).
  Also HUD-only for the same reason.

Both surviving overlays are strictly HUD-specific consequences of the undo window; the *base* order now
matches the Pin Tab. Verify this does not regress the `40be9d31` cross-surface Sink agreement (confirmed
2026-08-11).

## Risks / Trade-offs

- **Undo-window feel drifts during migration** → D5 parity gate: record the current `PinHudWaitMs` +
  fade curve, keep them in the HUD unchanged, A/B in-game before deleting old code. Because the window
  stays in the HUD (not reimplemented in the container), the risk is low.
- **The `awaitingRemoval` ↔ container-diff interaction races** (a pin re-pushed by the server while
  suppressed) → D3 keeps the existing suppression logic; the container's reappear-cancels-departure
  (`diff.Revived`) handles the revive. Explicitly test re-pin during a collapse.
- **The ghost snapshot must render the already-faded row** → the frozen ghost captures the row as it
  looked while live (text at ~0 opacity post-fade), so the collapse closes an already-empty row exactly
  as `departing` does today. Build the ghost to match (a zero-opacity-text frozen row), not a
  full-opacity one, or the text would flash back before collapsing.
- **Ordering overlays interact with the container's slot math** (D7's in-window hold changes an id's slot)
  → the container diffs by identity, not slot, and splices ghosts at the slot the row left; an in-set
  reorder is just a new render order the container tracks. Verify a sink-during-collapse doesn't mis-slot
  a concurrent ghost.

## Open Questions

- Should the "+N more" affordance and sink overlays be expressed as container items or stay as HUD
  chrome outside the animated list? (Likely HUD chrome — they aren't rows that depart — but confirm they
  compose with the container's render order, same as the Pin Tab's policy-picker header sits outside its
  animated list.)
- Does the ghost need the checkbox at all, or just the faded text + row shape? Today's `departing`
  snapshot keeps the checkbox visible during collapse; match that in the frozen ghost unless the collapse
  reads better without it (decide in-game under D5).
