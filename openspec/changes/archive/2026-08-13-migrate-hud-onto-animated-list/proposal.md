## Why

The pinned-task **HUD** is the last of the four animating surfaces still hand-wiring its own row
choreography. `extract-animated-task-list` built the reusable `ScribeAnimatedList` container and
adopted it on the Read view and Pin Tab; `animate-row-insertion` migrated the editor onto it too.
After that, the editor, Read view, and Pin Tab all share one animation path — but the HUD still owns a
private copy: `departing` / `BeginDeparting` / `ReconcileDeparting` / `CancelDeparting` /
`OnDepartingCollapsed`, a private collapse registry, and a `needsCollapseCleanup` deferral.

Keeping the HUD bespoke is a real maintenance cost: every collapse/hover/scroll fix has to be made
(and kept in sync) in two places, and it's the surface most prone to regressions (the reconcile
conversion already surfaced a long-standing `ScribeFadeText` undo-fade bug here). This change moves the
HUD's departure collapse onto the container, retiring the duplicated choreography so exactly one
animation path remains.

**Correction to this change's original premise (2026-08-12).** The proposal was written believing the
HUD "needs the container's `Delayed` removal policy" — a held, faded *ghost* before the collapse — and
that wiring that stubbed policy was the headline deliverable. Reading the HUD against the container's
contract disproved that. The HUD's undo window is **not an animation phase; it is a deferred-network-send
phase.** On a destructive completion the pin stays **live in the pin set** for the undo window
(`PinHudWaitMs`, 1500ms): the checkbox stays clickable, `pendingCompletions` holds an *unsent* packet,
and the text fades as a countdown preview. **Undo is unchecking that live row** (`pendingCompletions.Remove`
→ nothing was ever sent). The pin only leaves the set at **send time, after the window** — and *that* is
when today's collapse begins, which is exactly the container's existing **`Immediate`** policy (collapse
the frame an id leaves the set). A container `Delayed` ghost-hold could not even carry the undo, because a
frozen ghost has no live checkbox. So `Delayed` was a design misconception, not an unbuilt feature: the
correct architecture keeps the deferred-send window + live-row fade in the HUD (where the network state
lives) and uses the container only for the post-send collapse — the same `Immediate` path every other
surface uses. The unused `Delayed` enum member + throwing stub are removed as part of this change.

## What Changes

- **Migrate the HUD (`HudScribePins.cs`) onto `ScribeAnimatedList` with the existing `Immediate`
  policy**, deleting its hand-wired `departing` / `DepartingRow` / `BeginDeparting` /
  `ReconcileDeparting` / `CancelDeparting` / `OnDepartingCollapsed` machinery, its private
  `collapseRegistry` departure use, and the `needsCollapseCleanup` deferral. The pin leaving the item
  set (at send-time, tracked by the retained `awaitingRemoval`) is what triggers the collapse — exactly
  today's timing.
- **Keep the HUD's undo semantics exactly** ([[hud-undo-window-is-policy-hiding]]): the deferred-send
  undo window (`pendingCompletions`, `PinHudWaitMs`) and the live-row countdown fade (`ScribeFadeText`)
  stay in the HUD. The undo window exists ONLY because the HUD hides the Completion Policy, so a
  completion can be a silent delete that needs a misclick-rescue window; that behavior is retained
  verbatim — this is a mechanism migration of the *collapse*, not a UX change.
- **Remove the misconceived `Delayed` removal policy** from `ScribeAnimatedList`: delete the enum member
  and the `throw NotSupportedException` guard. No surface needs a ghost-hold; the one candidate's window
  is a deferred-send phase, not an animation hold. (Can be re-added if a genuine ghost-hold surface ever
  appears.)
- **Retire now-dead duplicated primitives** and confirm one choreography path remains across all four
  surfaces (`extract-animated-task-list` §6.3, the final consolidation step).
- **HUD rows ENTER with the same slide-in animation** as the editor / Read / Pin Tab (D6) — a
  newly-pinned row slides in, doesn't snap. Free from routing through the container with entry animation
  enabled.
- **HUD row ORDER follows the Pinned tab** (D7) — align the HUD's ordering with the Pin Tab's
  `OrderedPinsForDisplay()` / Core `ScribePinOrdering` so the two surfaces agree, rather than the HUD
  keeping its own ordering overlay. This is a behavior change; it must not regress the `40be9d31`
  cross-surface Sink agreement.

Non-goals: no change to the HUD's undo-window *duration* or the fact that the live row fades out before
it is culled (that undo behavior is explicitly KEPT — user 2026-08-12); no Core model/persistence/sync
change (view-layer only); no change to the other three surfaces' already-migrated behavior; the
container's `Immediate` behavior stays byte-for-byte unchanged.

## Capabilities

### New Capabilities
- `gui-hud-shared-row-animation`: The pinned-task HUD renders its rows through the shared
  `ScribeAnimatedList` container under the `Immediate` removal policy — collapsing a row when its
  identity leaves the item set (at destructive-completion send-time), entering rows with the shared
  slide-in, and following the Pin Tab's display order — while the HUD keeps its misclick-rescue undo
  window as a live-row deferred-send phase (not a container animation hold). One animation path across
  the editor, Read view, Pin Tab, and HUD.

## Impact

- **Depends on `extract-animated-task-list`** (the container + `Immediate` policy) and
  `animate-row-insertion` (the `ScribeSlideIn` entry primitive) — both MERGED + ARCHIVED on main
  (2026-08-12), so the migration edits the settled code.
- **Affected code (view layer only):**
  - `src/Mod/ScribeAnimatedList.cs` — remove the `Delayed` enum member + throwing guard (dead concept).
  - `src/Mod/HudScribePins.cs` — route rows through `ScribeAnimatedList(Immediate)`; delete
    `departing` / `BeginDeparting` / `ReconcileDeparting` / `CancelDeparting` / `OnDepartingCollapsed` /
    `needsCollapseCleanup`; keep `awaitingRemoval` (server-confirmation suppression), the deferred-send
    undo window (`pendingCompletions` / `PinHudWaitMs`), and the live-row `ScribeFadeText` countdown
    fade; align ordering with the Pin Tab (D7); leave entry animation enabled (D6).
  - `src/Mod/HudPinsContent` (same file) — build `ScribeAnimatedListItem`s with a frozen ghost for the
    departing collapse; drop the `Departing`/`ScribeRowSizeAnimation` hand-wiring in `BuildRow`.
- **Core:** no model/persistence/sync change. `ScribePinOrdering` is reused (not modified) for D7.
- **No new dependencies.** Vanilla `VintagestoryAPI` + the existing harness only.
