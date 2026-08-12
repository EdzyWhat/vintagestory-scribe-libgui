## Context

Four Scribe surfaces animate row removal: the editor, Read view, Pin Tab, and the pinned-task HUD.
`extract-animated-task-list` built `ScribeAnimatedList` (a view-agnostic container that diffs an
identity-keyed item set, collapses departed rows, and self-cleans) and adopted it on Read + Pin Tab.
`animate-row-insertion` §0 migrates the editor. This change migrates the **last** surface — the HUD
— so exactly one animation path remains (`extract-animated-task-list` §6.3, the consolidation).

The HUD was deliberately left for last because it is the only surface that needs the container's
**`Delayed` removal policy**, which today is a guarded stub:

```csharp
if (policy == ScribeListRemovalPolicy.Delayed)
    throw new NotSupportedException("...HUD fade/undo-window migration is a follow-up...");
```

The HUD's removal is not a plain collapse. When a pin is completed under a destructive policy
(Unpin/Delete/UnpinSink), the HUD:
1. **Sends** the destructive packet, then tracks the pin in `awaitingRemoval` until the server push
   confirms the removal (the pin can still be live in `MyPins` for a frame or two).
2. Holds the row at full height for an **undo window** (`UndoWindowMs`), during which the row's TEXT
   fades to zero opacity (`ScribeFadeText`) as a countdown preview — but the row's height stays full
   so a misclick-rescue is possible ([[hud-undo-window-is-policy-hiding]]: this window exists ONLY
   because the HUD hides the Completion Policy, so a completion may be a silent no-undo delete).
3. **Then** collapses the row's height via `ScribeRowSizeAnimation`, snapshotting it into `departing`
   at the display index it held so it collapses in place.
4. Retires the entry on collapse-complete (`OnDepartingCollapsed`), deferred out of the frame.

A reappearing pin mid-window cancels (`ReconcileDeparting`/`CancelDeparting`). This is a faithful
hand-wired copy of what the container does — plus the hold-and-fade timing the container doesn't yet
implement.

## Goals / Non-Goals

**Goals:**
- Implement the container's `Delayed` policy for real: hold-at-full-height for an undo window
  (optionally fading the row content), then collapse with the same mechanism as `Immediate`.
- Route the HUD through `ScribeAnimatedList(Delayed)`, deleting `departing` / `BeginDeparting` /
  `ReconcileDeparting` / `CancelDeparting` / `OnDepartingCollapsed`.
- Preserve the HUD's undo semantics EXACTLY (window duration, text-fade feel, misclick rescue).
- End with one animation path across all four surfaces.

**Non-Goals:**
- Any change to the HUD's undo-window *duration* or fade *feel* — behavior-preserving migration.
- Moving `awaitingRemoval` (the server-confirmation tracking) into the container. That is HUD
  domain state about an in-flight *network* removal, not an animation concern; it stays in the HUD
  and simply drives which ids are in the item set (see D3).
- Any Core model/persistence/sync change.
- Re-opening the [[hud-undo-window-is-policy-hiding]] decision itself (whether the HUD *should* have
  an undo window). That policy stands; this only changes the mechanism that renders it.

## Decisions

### D1 — `Delayed` = a hold phase in front of the existing collapse, driven by the same registry
The container gets a per-departed-id hold timer. On the build a row departs under `Delayed`, the
container records a hold deadline and renders the row's ghost at **full height** (factor 1) for the
window, then transitions it into the existing `ScribeRowSizeDirection.Collapse` exactly as
`Immediate` does today. The hold is driven by the same host-owned `ScribeAnimationRegistry` /
ticker the collapse uses — no second timing system — so it survives `ForceRebuild`/reconcile like
everything else. The collapse *shape* and cleanup are untouched; only a hold is prepended.

### D2 — The undo-window text fade reuses the host-owned-controller fade primitive from `animate-row-insertion`
The HUD's current fade is `ScribeFadeText` (self-owned controller — it survives reconcile but snaps
on `ForceRebuild`, and it already caused one regression). `animate-row-insertion` introduces a
`ScribeFade` opacity wrapper driven by a registry controller (ForceRebuild+reconcile stable). The
`Delayed` policy uses that primitive for the optional hold-phase fade, so the HUD stops depending on
`ScribeFadeText` for departure fading. (`ScribeFadeText` may still exist for any non-departure use;
audit and remove if it becomes unused.)

### D3 — `awaitingRemoval` stays in the HUD and feeds the container only via the item set
The container's contract is "diff the item set; an id that vanishes departs." The HUD keeps
`awaitingRemoval` (which pins have a destructive packet in flight) and keeps SUPPRESSING those pins
from the item set it hands the container — so the pin "vanishing" from the container's input is what
triggers the `Delayed` departure, exactly as a Pin Tab delete does for `Immediate`. The container
never learns about the network round-trip; it only sees an id leave. The reappear-cancels-departure
path the container already implements subsumes `CancelDeparting`.

### D4 — Sink / UnpinSink remain a reorder, not a departure
Sink moves a still-live pin to the bottom (its id stays in the set), so it is NOT a container
departure — it's the same in-set reordering the HUD already does via `sunkOrder`. This migration
does not route sink through the departure path; only the genuinely-removing policies (Unpin, Delete,
and the unpin half of UnpinSink) produce a `Delayed` departure. Verify the sink overlay/countdown
preview still reads correctly alongside a concurrent `Delayed` departure of a different row.

### D5 — Migrate behind a parity gate, HUD collapse behavior first
Like the editor migration (D0 in `animate-row-insertion`), wire + build-verify the `Delayed` policy
in the container against a test/harness first, then move the HUD and **playtest that undo + fade +
collapse + cancel behave identically to today** before deleting the hand-wired machinery. The
old code stays until the new path is confirmed in-game.

## Risks / Trade-offs

- **Undo-window feel drifts during migration** → D5 parity gate: record the current `UndoWindowMs`
  and fade curve, reproduce them exactly through the policy, A/B in-game before deleting old code.
- **The `awaitingRemoval` ↔ container-diff interaction races** (a pin re-pushed by the server while
  in its hold window) → D3 keeps the existing suppression logic; the container's
  reappear-cancels-departure handles the revive. Explicitly test re-pin during the undo window.
- **`Delayed` timing double-counts with collapse** (hold + collapse controllers fighting) → D1 uses
  one registry and prepends the hold to the *same* controller/sequence rather than a parallel one;
  unit-test the hold-elapsed→begin-collapse transition if any pure logic is extracted.
- **A fourth consumer exposes a container assumption** (the HUD is the only `Delayed` user and has
  the most overlays: sink, +N more, fade) → adopt last, on top of three proven `Immediate`
  consumers, so any container generalization needed is isolated to this change.

## Open Questions

- Does the hold phase belong to `ScribeAnimatedList` (container-level per-id timer) or to a small
  `ScribeRowSizeAnimation` extension (a `holdMs` before the collapse ramp)? Lean container-level
  (D1) so `ScribeRowSizeAnimation` stays a pure one-shot ramp, but confirm when implementing which
  keeps the cancel/revive path simplest.
- Once departure-fading moves to the `ScribeFade` primitive, is `ScribeFadeText` still used anywhere?
  If not, remove it as part of §6.3 consolidation.
- Should the "+N more" affordance and sink overlays be expressed as container items or stay as HUD
  chrome outside the animated list? (Likely HUD chrome — they aren't rows that depart — but confirm
  they compose with the container's render order.)
